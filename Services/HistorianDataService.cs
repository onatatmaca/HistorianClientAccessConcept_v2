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

        public virtual List<Tag> BrowseTags(ServerConnection conn, string tagnameMask)
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

        public virtual bool TagExists(ServerConnection conn, string tagName)
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

        public virtual List<(DateTime Time, float Value, double Quality)> ReadInterpolated(
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

        public virtual List<(DateTime Time, float Value, double Quality)> ReadRaw(
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
        public virtual List<(DateTime Time, float Value, double Quality)> ReadRawInRange(
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

        /// <summary>
        /// Per-bucket RAW SAMPLE COUNTS for many tags in one round-trip — the cheap read that
        /// makes an all-points overview possible.
        ///
        /// The window is split into <paramref name="buckets"/> equal intervals and the server
        /// returns how many raw samples fall in each (<c>CalculationModeType.Count</c>), instead
        /// of shipping millions of timestamps we would only count anyway. Result: tag → counts,
        /// one entry per bucket.
        ///
        /// This is an ESTIMATE surface only. A bucket with at least one sample counts as
        /// "has data" regardless of the tag's cadence, so it can never be the basis for a write —
        /// <see cref="SyncPlanner"/> stays the only thing that decides what a restore copies.
        ///
        /// Buckets are filled by the returned TIMESTAMP, not by index: it must not matter
        /// whether the server returns exactly <paramref name="buckets"/> intervals.
        /// </summary>
        public virtual Dictionary<string, int[]> ReadBucketCounts(
            ServerConnection conn, IList<string> tagNames, DateTime from, DateTime to, int buckets)
        {
            var result = new Dictionary<string, int[]>(StringComparer.OrdinalIgnoreCase);
            if (tagNames == null || tagNames.Count == 0 || buckets < 1) return result;

            DateTime fromUtc = ToApi(from), toUtc = ToApi(to);
            long span = (toUtc - fromUtc).Ticks;
            if (span <= 0) return result;
            long bucketTicks = Math.Max(1, span / buckets);

            foreach (var t in tagNames) result[t] = new int[buckets];

            RetryHelper.Retry<object>(() =>
            {
                DataQueryParams query = new CalculatedQuery(
                    DataCriteria.CalculationModeType.Count, tagNames.ToArray())
                {
                    Fields = DataFields.Time | DataFields.Value
                };
                query.Criteria.Start           = fromUtc;
                query.Criteria.End             = toUtc;
                query.Criteria.NumberOfSamples = (uint)buckets;

                ItemErrors errors;
                Proficy.Historian.ClientAccess.API.DataSet set;
                bool more = conn.IData.Query(ref query, out set, out errors);
                ThrowOnItemErrors(errors, string.Join(",", tagNames));
                while (more)   // drain: never abandon a paged query (leaks a server-side cursor)
                {
                    Proficy.Historian.ClientAccess.API.DataSet page;
                    more = conn.IData.Query(ref query, out page, out errors);
                    ThrowOnItemErrors(errors, string.Join(",", tagNames));
                    if (page != null) set.AddRange(page);
                }
                if (set == null) return null;

                foreach (var tag in tagNames)
                {
                    if (!set.ContainsKey(tag)) continue;
                    var samples = set[tag];
                    int[] counts = result[tag];
                    int n = samples.Count();

                    // The server returns one value per interval, in order. When we get exactly
                    // the number we asked for, position IS the bucket — that is the definitive
                    // mapping and it sidesteps the stamping question entirely.
                    //
                    // Mapping by timestamp instead was WRONG and measured so (2026-08-04,
                    // STAT6.BHKW_01_GAS.F_CV, 7 days / 200 buckets): the total matched the raw
                    // read exactly but 42 buckets were off by one position, because each
                    // interval is stamped at its END, not its start. On a steady cadence that
                    // is invisible in most buckets — but at the edges it reports an empty
                    // bucket where the data starts, which the overview then reads as an outage
                    // on one server and adds to the estimate.
                    bool byPosition = (n == buckets);

                    for (int i = 0; i < n; i++)
                    {
                        object v = samples.GetValue(i);
                        if (v == null) continue;
                        double c;
                        if (!double.TryParse(Convert.ToString(v,
                                System.Globalization.CultureInfo.InvariantCulture),
                                System.Globalization.NumberStyles.Any,
                                System.Globalization.CultureInfo.InvariantCulture, out c))
                            continue;
                        if (c <= 0) continue;

                        int idx;
                        if (byPosition)
                        {
                            idx = i;
                        }
                        else
                        {
                            // Fallback (short/long response): treat the stamp as the interval
                            // END, so a value stamped at from+step belongs to bucket 0.
                            long offset = samples.GetTime(i).Ticks - fromUtc.Ticks - 1;
                            idx = (int)(offset / bucketTicks);
                        }
                        if (idx < 0) idx = 0;
                        if (idx >= buckets) idx = buckets - 1;
                        counts[idx] += (int)Math.Round(c);
                    }
                }
                return null;
            }, _maxRetries);

            return result;
        }

        // ── Data Writes ────────────────────────────────────────────────────────────

        public virtual List<string> WriteFloatSamples(
            ServerConnection conn, string tagName,
            List<DateTime> times, List<float> values)
        {
            return RetryHelper.Retry(() =>
            {
                Proficy.Historian.ClientAccess.API.DataSet set = new Proficy.Historian.ClientAccess.API.DataSet();
                set[tagName] = new DataSamples<float>
                {
                    // ToApi, like every other method that crosses into the API. This was the
                    // ONE write/delete path that passed app-frame times straight through, so
                    // any future caller would have written 1 h early in winter / 2 h in
                    // summer. It has no caller today, which is exactly why it could sit here
                    // looking correct.
                    Times = times.Select(ToApi).ToArray(),
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

        public virtual List<string> WriteFloatSamplesWithQuality(
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

        public virtual (int Expected, int Actual) VerifyWrite(
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
        public virtual List<string> DeleteSamples(ServerConnection conn, string tagName, IList<DateTime> times)
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
