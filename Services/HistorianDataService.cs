using Proficy.Historian.ClientAccess.API;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HistorianSyncTool.Services
{
    /// <summary>
    /// All Historian API data and tag operations. No UI dependencies.
    /// Pure-logic pieces (retry policy, sample parsing, range filtering) live in
    /// RetryHelper / SampleFilter and are tested independently.
    /// </summary>
    public class HistorianDataService
    {
        private readonly int _maxRetries;

        public HistorianDataService(int maxRetries = 3)
        {
            _maxRetries = maxRetries;
        }

        // ── Time frame boundary ────────────────────────────────────────────────────
        // The ClientAccess API works in UTC: every timestamp it returns has Kind=Utc, and a
        // Local/Unspecified query start is silently converted local->UTC, shifting the query
        // by the UTC offset (1 h winter / 2 h summer). Proven live 2026-07-16 — see
        // `.claude/rules/historian-api.md`.
        //
        // THE REST OF THE APP WORKS IN LOCAL TIME (date pickers, DateTime.Now, all display),
        // so this service is the ONE place that converts. Keep it that way: mixing the frames
        // silently shifts windows by the UTC offset, because .NET compares raw Ticks and
        // ignores Kind. Every DateTime crossing into or out of the API goes through these.

        /// <summary>Local (or Unspecified-as-local) -> the API's UTC frame.</summary>
        private static DateTime ToApi(DateTime t)
        {
            // ToUniversalTime(): Utc -> unchanged; Local/Unspecified -> converted using the
            // DST offset in effect AT THAT DATE (not today's), which is what we want.
            return DateTime.SpecifyKind(t.ToUniversalTime(), DateTimeKind.Utc);
        }

        /// <summary>The API's UTC frame -> local time for the rest of the app.</summary>
        private static DateTime FromApi(DateTime t)
        {
            // SpecifyKind first: ToLocalTime() on an Unspecified value would be a no-op.
            return DateTime.SpecifyKind(t, DateTimeKind.Utc).ToLocalTime();
        }

        /// <summary>
        /// Never let a failed read look like "no data". Writes and deletes already inspect
        /// ItemErrors; reads used to drop them on the floor and return an empty list, which is
        /// indistinguishable from a genuinely empty range. That is dangerous on the TARGET side:
        /// an empty target read makes SyncPlanner fall back to exact-diff and copy everything,
        /// those writes get journaled, and a later revert would then delete samples the target
        /// held all along. Throwing lets RetryHelper retry and, if it persists, surfaces it.
        /// (Verified live: an empty window and even a nonexistent tag return ItemErrors=0, so
        /// this cannot fire on the normal "no data here" case.)
        /// </summary>
        private static void ThrowOnItemErrors(ItemErrors errors, string tagName)
        {
            if (errors == null || errors.Count == 0) return;
            var msgs = new List<string>();
            foreach (var kv in errors) msgs.Add($"{kv.Key}: {kv.Value}");
            throw new InvalidOperationException(
                $"Historian read failed for '{tagName}': {string.Join("; ", msgs)}");
        }

        // ── Tag Queries ────────────────────────────────────────────────────────────

        public List<Tag> BrowseTags(ServerConnection conn, string tagnameMask)
        {
            return RetryHelper.Retry(() =>
            {
                TagQueryParams query = new TagQueryParams { PageSize = 100 };
                query.Criteria.TagnameMask = string.IsNullOrWhiteSpace(tagnameMask) ? "*" : tagnameMask;
                query.Criteria.DataType = Tag.NativeDataType.Float;

                List<Tag> allTags = new List<Tag>();
                List<Tag> page;
                while (conn.ITags.Query(ref query, out page))
                    allTags.AddRange(page);
                allTags.AddRange(page); // last page is returned after while exits
                return allTags;
            }, _maxRetries);
        }

        public bool TagExists(ServerConnection conn, string tagName)
        {
            return RetryHelper.Retry(() =>
            {
                TagQueryParams query = new TagQueryParams { PageSize = 1 };
                query.Criteria.TagnameMask = tagName;
                List<Tag> result;
                conn.ITags.Query(ref query, out result);
                return result != null && result.Count > 0;
            }, _maxRetries);
        }

        // ── Data Reads ─────────────────────────────────────────────────────────────

        public List<(DateTime Time, float Value, double Quality)> ReadInterpolated(
            ServerConnection conn, string tagName, DateTime from, DateTime to, int count)
        {
            return RetryHelper.Retry(() =>
            {
                DataQueryParams query = new InterpolatedQuery(ToApi(from), ToApi(to), (uint)count, tagName)
                {
                    Fields = DataFields.Time | DataFields.Value | DataFields.Quality
                };
                ItemErrors errors;
                Proficy.Historian.ClientAccess.API.DataSet set = new Proficy.Historian.ClientAccess.API.DataSet();
                conn.IData.Query(ref query, out set, out errors);
                return SampleFilter.Parse(IterateDataSet(set, tagName))
                    .Select(s => (Time: FromApi(s.Time), s.Value, s.Quality)).ToList();
            }, _maxRetries);
        }

        public List<(DateTime Time, float Value, double Quality)> ReadRaw(
            ServerConnection conn, string tagName, DateTime from)
        {
            return RetryHelper.Retry(() =>
            {
                DataQueryParams query = new RawByTimeQuery(ToApi(from), tagName)
                {
                    Fields = DataFields.Time | DataFields.Value | DataFields.Quality
                };
                ItemErrors errors;
                Proficy.Historian.ClientAccess.API.DataSet all = new Proficy.Historian.ClientAccess.API.DataSet();
                Proficy.Historian.ClientAccess.API.DataSet page = new Proficy.Historian.ClientAccess.API.DataSet();
                while (conn.IData.Query(ref query, out page, out errors))
                    all.AddRange(page);
                all.AddRange(page);
                return SampleFilter.Parse(IterateDataSet(all, tagName))
                    .Select(s => (Time: FromApi(s.Time), s.Value, s.Quality)).ToList();
            }, _maxRetries);
        }

        /// <summary>
        /// Raw samples in [start, end], read as bounded RawByNumberQuery chunks so the
        /// query stops at the range end instead of paging to the END OF THE ARCHIVE
        /// (the old RawByTimeQuery loop read years of data for a one-week window on real
        /// plant archives). Chunked queries are also the only safe way to stop early:
        /// abandoning a RawByTime pagination mid-way leaks a server-side cursor until it
        /// expires ("Maximum number of cached items exceeded" — verified live).
        /// </summary>
        public List<(DateTime Time, float Value, double Quality)> ReadRawInRange(
            ServerConnection conn, string tagName, DateTime start, DateTime end)
        {
            const uint chunk = 5000;
            // Work in the API's UTC frame for the whole loop; convert back at the exit.
            DateTime startUtc = ToApi(start), endUtc = ToApi(end);
            return RetryHelper.Retry(() =>
            {
                var all = new List<(DateTime Time, object Value, double Quality)>();
                DateTime cursor = startUtc;   // Kind=Utc => the API sends it as-is (no shift)
                while (cursor <= endUtc)
                {
                    DataQueryParams query = new RawByNumberQuery(cursor, chunk, new[] { tagName })
                    {
                        Fields = DataFields.Time | DataFields.Value | DataFields.Quality,
                        // one server round-trip per chunk — never a dangling continuation
                        PageSize = (int)chunk + 1
                    };
                    ItemErrors errors;
                    Proficy.Historian.ClientAccess.API.DataSet set;
                    bool more = conn.IData.Query(ref query, out set, out errors);
                    ThrowOnItemErrors(errors, tagName);
                    while (more) // drain any unexpected continuation pages of this chunk
                    {
                        Proficy.Historian.ClientAccess.API.DataSet extra;
                        more = conn.IData.Query(ref query, out extra, out errors);
                        ThrowOnItemErrors(errors, tagName);
                        if (extra != null) set.AddRange(extra);
                    }
                    if (set == null || set.TotalSamples == 0) break;

                    int n = 0;
                    DateTime lastTs = cursor;
                    bool pastEnd = false;
                    foreach (var raw in IterateDataSet(set, tagName))
                    {
                        n++;
                        lastTs = raw.Time;
                        if (raw.Time > endUtc) { pastEnd = true; break; }
                        all.Add(raw);
                    }
                    if (pastEnd || n < (int)chunk) break;
                    // AddTicks PRESERVES Kind=Utc. Never use `new DateTime(lastTs.Ticks + 1)`
                    // here: that drops the Kind, so the API would re-convert local->UTC and
                    // every chunk would re-read the previous 1-2 h (duplicate, unordered data).
                    cursor = lastTs.AddTicks(1); // next chunk starts after the last stored tick
                }

                // Clip in the UTC frame (the API can return samples BEFORE the requested start),
                // then hand local time back to the rest of the app.
                var clipped = SampleFilter.ParseAndClip(all, startUtc, endUtc);
                var result = new List<(DateTime Time, float Value, double Quality)>(clipped.Count);
                foreach (var s in clipped) result.Add((FromApi(s.Time), s.Value, s.Quality));
                return result;
            }, _maxRetries);
        }

        // ── Data Writes ────────────────────────────────────────────────────────────

        public List<string> WriteFloatSamples(
            ServerConnection conn, string tagName,
            List<DateTime> times, List<float> values)
        {
            return RetryHelper.Retry(() =>
            {
                Proficy.Historian.ClientAccess.API.DataSet set = new Proficy.Historian.ClientAccess.API.DataSet();
                set[tagName] = new DataSamples<float>
                {
                    Times = times.ToArray(),
                    Values = values.ToArray(),
                    ImplicitQuality = DataQuality.Good
                };
                ItemErrors errors;
                conn.IData.Add(set, false, out errors);

                var messages = new List<string>();
                if (errors != null)
                    foreach (var kv in errors)
                        messages.Add($"Tag {kv.Key}: {kv.Value}");
                return messages;
            }, _maxRetries);
        }

        public List<string> WriteFloatSamplesWithQuality(
            ServerConnection conn, string tagName,
            DateTime[] times, float[] values, DataQuality[] qualities)
        {
            var allMessages = new List<string>();

            var groups = new Dictionary<DataQuality, List<int>>();
            for (int i = 0; i < qualities.Length; i++)
            {
                if (!groups.ContainsKey(qualities[i]))
                    groups[qualities[i]] = new List<int>();
                groups[qualities[i]].Add(i);
            }

            foreach (var grp in groups)
            {
                var msgs = RetryHelper.Retry(() =>
                {
                    var grpTimes  = grp.Value.Select(i => ToApi(times[i])).ToArray();
                    var grpValues = grp.Value.Select(i => values[i]).ToArray();

                    Proficy.Historian.ClientAccess.API.DataSet set = new Proficy.Historian.ClientAccess.API.DataSet();
                    set[tagName] = new DataSamples<float>
                    {
                        Times = grpTimes,
                        Values = grpValues,
                        ImplicitQuality = grp.Key
                    };
                    ItemErrors errors;
                    conn.IData.Add(set, false, out errors);

                    var messages = new List<string>();
                    if (errors != null)
                        foreach (var kv in errors)
                            messages.Add($"Tag {kv.Key}: {kv.Value}");
                    return messages;
                }, _maxRetries);
                allMessages.AddRange(msgs);
            }

            return allMessages;
        }

        public (int Expected, int Actual) VerifyWrite(
            ServerConnection conn, string tagName,
            DateTime start, DateTime end, int expectedCount)
        {
            var samples = ReadRawInRange(conn, tagName, start, end);
            return (expectedCount, samples.Count);
        }

        /// <summary>
        /// Deletes the samples of <paramref name="tagName"/> at the exact
        /// <paramref name="times"/> via <c>IData.Delete</c>. Used by the revert feature
        /// to undo a backfill — only the listed timestamps are removed, so any
        /// pre-existing samples on the server are left untouched. Chunked at 1000 pairs
        /// per call to keep each request bounded. Returns any per-tag error messages.
        /// </summary>
        public List<string> DeleteSamples(ServerConnection conn, string tagName, IList<DateTime> times)
        {
            var messages = new List<string>();
            if (times == null || times.Count == 0) return messages;

            const int chunk = 1000;
            for (int start = 0; start < times.Count; start += chunk)
            {
                int count = Math.Min(chunk, times.Count - start);
                int offset = start;
                var msgs = RetryHelper.Retry(() =>
                {
                    var tagnames = new string[count];
                    var timeArr  = new DateTime[count];
                    for (int i = 0; i < count; i++)
                    {
                        tagnames[i] = tagName;
                        timeArr[i]  = ToApi(times[offset + i]);
                    }
                    ItemErrors errors;
                    conn.IData.Delete(tagnames, timeArr, out errors);

                    var m = new List<string>();
                    if (errors != null)
                        foreach (var kv in errors)
                            m.Add($"Tag {kv.Key}: {kv.Value}");
                    return m;
                }, _maxRetries);
                messages.AddRange(msgs);
            }
            return messages;
        }

        // ── Helpers ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Lazy generator that translates a Proficy DataSet into (DateTime, object, double)
        /// tuples so the filtering / parsing logic in SampleFilter can consume them without
        /// depending on Proficy types.
        /// </summary>
        private static IEnumerable<(DateTime Time, object Value, double Quality)> IterateDataSet(
            Proficy.Historian.ClientAccess.API.DataSet set, string tagName)
        {
            if (set == null || !set.ContainsKey(tagName)) yield break;
            int n = set.TotalSamples;
            for (int i = 0; i < n; i++)
            {
                yield return (
                    set[tagName].GetTime(i),
                    set[tagName].GetValue(i),
                    set[tagName].GetQuality(i).PercentGood()
                );
            }
        }
    }
}
