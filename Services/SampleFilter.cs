using System;
using System.Collections.Generic;

namespace HistorianSyncTool.Services
{
    /// <summary>
    /// Parses raw Historian samples (DateTime + object value + quality) into typed
    /// (DateTime, float, double) tuples, dropping nulls and unparseable entries.
    /// Optionally clips to a half-open [start, end) interval. Assumes chronological
    /// input so the end-boundary check can `break` early.
    /// Pure logic — no Proficy or UI dependencies, fully testable.
    /// </summary>
    public static class SampleFilter
    {
        /// <summary>
        /// Truncates a timestamp to whole-second resolution — the precision Historian
        /// actually stores at. The backfill diff and verify compare at this resolution so
        /// a sub-second source timestamp (e.g. 12:54:30.123) matches the second it gets
        /// stored as (12:54:30). Without this the diff never matches a written sample and
        /// keeps "finding" it missing — re-copying it forever.
        /// </summary>
        public static long ToSecondTicks(DateTime t) =>
            t.Ticks - (t.Ticks % TimeSpan.TicksPerSecond);

        public static List<(DateTime Time, float Value, double Quality)> ParseAndClip(
            IEnumerable<(DateTime Time, object Value, double Quality)> raw,
            DateTime start, DateTime end)
        {
            var result = new List<(DateTime, float, double)>();
            if (raw == null) return result;

            foreach (var s in raw)
            {
                if (s.Time >= end) break;
                if (s.Time < start) continue;
                if (s.Value == null) continue;
                float v;
                if (!float.TryParse(s.Value.ToString(), out v)) continue;
                result.Add((s.Time, v, s.Quality));
            }
            return result;
        }

        public static List<(DateTime Time, float Value, double Quality)> Parse(
            IEnumerable<(DateTime Time, object Value, double Quality)> raw)
        {
            var result = new List<(DateTime, float, double)>();
            if (raw == null) return result;

            foreach (var s in raw)
            {
                if (s.Value == null) continue;
                float v;
                if (!float.TryParse(s.Value.ToString(), out v)) continue;
                result.Add((s.Time, v, s.Quality));
            }
            return result;
        }
    }
}
