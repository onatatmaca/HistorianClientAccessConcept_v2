using Proficy.Historian.ClientAccess.API;
using System;
using System.Collections.Generic;
using System.Threading;

namespace HistorianSyncTool.Services
{
    /// <summary>
    /// All Historian API data and tag operations. No UI dependencies.
    /// </summary>
    public class HistorianDataService
    {
        private readonly int _maxRetries;
        private readonly int[] _retryDelaysMs = { 500, 1000, 2000 };

        public HistorianDataService(int maxRetries = 3)
        {
            _maxRetries = maxRetries;
        }

        // ── Tag Queries ────────────────────────────────────────────────────────────

        public List<Tag> BrowseTags(ServerConnection conn, string tagnameMask)
        {
            return Retry(() =>
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
            });
        }

        public bool TagExists(ServerConnection conn, string tagName)
        {
            return Retry(() =>
            {
                TagQueryParams query = new TagQueryParams { PageSize = 1 };
                query.Criteria.TagnameMask = tagName;
                List<Tag> result;
                conn.ITags.Query(ref query, out result);
                return result != null && result.Count > 0;
            });
        }

        // ── Data Reads ─────────────────────────────────────────────────────────────

        /// <summary>Returns up to <paramref name="count"/> interpolated samples over the given window.</summary>
        public List<(DateTime Time, float Value, double Quality)> ReadInterpolated(
            ServerConnection conn, string tagName, DateTime from, DateTime to, int count)
        {
            return Retry(() =>
            {
                DataQueryParams query = new InterpolatedQuery(from, to, (uint)count, tagName)
                {
                    Fields = DataFields.Time | DataFields.Value | DataFields.Quality
                };
                ItemErrors errors;
                Proficy.Historian.ClientAccess.API.DataSet set = new Proficy.Historian.ClientAccess.API.DataSet();
                conn.IData.Query(ref query, out set, out errors);
                return ExtractFloatSamples(set, tagName);
            });
        }

        /// <summary>Returns all raw samples from <paramref name="from"/> onward.</summary>
        public List<(DateTime Time, float Value, double Quality)> ReadRaw(
            ServerConnection conn, string tagName, DateTime from)
        {
            return Retry(() =>
            {
                DataQueryParams query = new RawByTimeQuery(from, tagName)
                {
                    Fields = DataFields.Time | DataFields.Value | DataFields.Quality
                };
                ItemErrors errors;
                Proficy.Historian.ClientAccess.API.DataSet all = new Proficy.Historian.ClientAccess.API.DataSet();
                Proficy.Historian.ClientAccess.API.DataSet page = new Proficy.Historian.ClientAccess.API.DataSet();
                while (conn.IData.Query(ref query, out page, out errors))
                    all.AddRange(page);
                all.AddRange(page);
                return ExtractFloatSamples(all, tagName);
            });
        }

        /// <summary>Returns raw samples in [start, end) — half-open interval.</summary>
        public List<(DateTime Time, float Value)> ReadRawInRange(
            ServerConnection conn, string tagName, DateTime start, DateTime end)
        {
            return Retry(() =>
            {
                DataQueryParams query = new RawByTimeQuery(start, tagName)
                {
                    Fields = DataFields.Time | DataFields.Value
                };
                ItemErrors errors;
                Proficy.Historian.ClientAccess.API.DataSet all = new Proficy.Historian.ClientAccess.API.DataSet();
                Proficy.Historian.ClientAccess.API.DataSet page = new Proficy.Historian.ClientAccess.API.DataSet();
                while (conn.IData.Query(ref query, out page, out errors))
                    all.AddRange(page);
                all.AddRange(page);

                var result = new List<(DateTime, float)>();
                for (int i = 0; i < all.TotalSamples; i++)
                {
                    DateTime t = all[tagName].GetTime(i);
                    if (t >= end) break;
                    if (t < start) continue;
                    object raw = all[tagName].GetValue(i);
                    if (raw == null) continue;
                    float v;
                    if (float.TryParse(raw.ToString(), out v))
                        result.Add((t, v));
                }
                return result;
            });
        }

        // ── Data Writes ────────────────────────────────────────────────────────────

        /// <summary>Writes float samples to the target tag. Returns any error messages.</summary>
        public List<string> WriteFloatSamples(
            ServerConnection conn, string tagName,
            List<DateTime> times, List<float> values)
        {
            return Retry(() =>
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
            });
        }

        // ── Helpers ────────────────────────────────────────────────────────────────

        private List<(DateTime Time, float Value, double Quality)> ExtractFloatSamples(
            Proficy.Historian.ClientAccess.API.DataSet set, string tagName)
        {
            var list = new List<(DateTime, float, double)>();
            for (int i = 0; i < set.TotalSamples; i++)
            {
                object raw = set[tagName].GetValue(i);
                if (raw == null) continue;
                float v;
                if (!float.TryParse(raw.ToString(), out v)) continue;
                list.Add((set[tagName].GetTime(i), v, set[tagName].GetQuality(i).PercentGood()));
            }
            return list;
        }

        private T Retry<T>(Func<T> action)
        {
            Exception last = null;
            for (int attempt = 0; attempt < _maxRetries; attempt++)
            {
                try { return action(); }
                catch (Exception ex)
                {
                    last = ex;
                    if (attempt < _retryDelaysMs.Length)
                        Thread.Sleep(_retryDelaysMs[attempt]);
                }
            }
            throw new InvalidOperationException($"Operation failed after {_maxRetries} attempts: {last?.Message}", last);
        }
    }
}
