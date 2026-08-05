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
        /// A timestamp as whole seconds since the epoch **in UTC** — the identity every diff,
        /// verify and journal entry is keyed on.
        ///
        /// Whole seconds because that is the precision Historian stores at: a sub-second source
        /// timestamp (12:54:30.123) has to match the second it becomes (12:54:30), or the diff
        /// never matches what it just wrote and re-copies it forever.
        ///
        /// UTC because the LOCAL clock is not a unique name for an instant. On the autumn
        /// change-over the local hour repeats, so two readings a real hour apart produced the
        /// SAME key — proven on a W. Europe machine: localTicksEqual, dateTimeEquals and
        /// hashEqual were all true for 00:30 and 01:30 UTC. A mirror outage confined to that
        /// repeated hour therefore looked already-present in every HashSet lookup here, so the
        /// planner reported "in sync" and the hour was never restored, on every run, forever.
        ///
        /// Kind matters: values from HistorianDataService are Local and carry .NET's
        /// ambiguous-time flag (set by ToLocalTime), so ToUniversalTime recovers the exact
        /// instant. A value already tagged Utc is used as-is — which is what the backfill
        /// journal passes, so journal ticks on disk are unchanged and legacy entries still
        /// revert identically.
        /// </summary>
        public static long ToSecondTicks(DateTime t)
        {
            long ticks = t.Kind == DateTimeKind.Utc ? t.Ticks : t.ToUniversalTime().Ticks;
            return ticks - (ticks % TimeSpan.TicksPerSecond);
        }

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
