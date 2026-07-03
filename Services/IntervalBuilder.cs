using HistorianSyncTool.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HistorianSyncTool.Services
{
    /// <summary>
    /// Turns lists of sample timestamps / gap batches into merged time intervals for
    /// the timeline display. Pure logic — no Proficy or UI dependencies, fully testable.
    /// </summary>
    public static class IntervalBuilder
    {
        /// <summary>
        /// Merges sorted timestamps into ranges: consecutive points closer than
        /// <paramref name="mergeGap"/> belong to the same range. Each range is widened
        /// to at least <paramref name="minWidth"/> so it stays visible/hoverable.
        /// Returns the ranges together with how many points each one contains.
        /// </summary>
        public static List<(TimeRange Range, int Count)> MergePoints(
            List<DateTime> sortedPoints, TimeSpan mergeGap, TimeSpan minWidth)
        {
            var result = new List<(TimeRange, int)>();
            if (sortedPoints == null || sortedPoints.Count == 0) return result;

            DateTime start = sortedPoints[0];
            DateTime last  = sortedPoints[0];
            int count = 1;

            for (int i = 1; i < sortedPoints.Count; i++)
            {
                if (sortedPoints[i] - last <= mergeGap)
                {
                    last = sortedPoints[i];
                    count++;
                }
                else
                {
                    result.Add((MakeRange(start, last, minWidth), count));
                    start = sortedPoints[i];
                    last  = sortedPoints[i];
                    count = 1;
                }
            }
            result.Add((MakeRange(start, last, minWidth), count));
            return result;
        }

        private static TimeRange MakeRange(DateTime start, DateTime last, TimeSpan minWidth)
        {
            DateTime end = last;
            if (end - start < minWidth) end = start + minWidth;
            return new TimeRange(start, end);
        }

        /// <summary>
        /// Median distance between consecutive sorted timestamps. Zero when fewer than 2.
        /// </summary>
        public static TimeSpan MedianInterval(List<DateTime> sortedPoints)
        {
            if (sortedPoints == null || sortedPoints.Count < 2) return TimeSpan.Zero;
            var deltas = new List<long>(sortedPoints.Count - 1);
            for (int i = 1; i < sortedPoints.Count; i++)
                deltas.Add((sortedPoints[i] - sortedPoints[i - 1]).Ticks);
            deltas.Sort();
            int mid = deltas.Count / 2;
            long ticks = deltas.Count % 2 == 0
                ? (deltas[mid - 1] + deltas[mid]) / 2
                : deltas[mid];
            return TimeSpan.FromTicks(ticks);
        }

        /// <summary>
        /// p-th percentile (0..1) of the distances between consecutive sorted timestamps.
        /// Robust cadence estimate for deadband/irregular tags where the median collapses
        /// (e.g. median 6 min but normal quiet periods of an hour). The index is capped
        /// below the largest delta so a single real outage in a sparse window can never
        /// masquerade as "cadence". Zero when fewer than 2 points.
        /// </summary>
        public static TimeSpan PercentileInterval(List<DateTime> sortedPoints, double pct)
        {
            if (sortedPoints == null || sortedPoints.Count < 2) return TimeSpan.Zero;
            var deltas = new List<long>(sortedPoints.Count - 1);
            for (int i = 1; i < sortedPoints.Count; i++)
                deltas.Add((sortedPoints[i] - sortedPoints[i - 1]).Ticks);
            deltas.Sort();
            int idx = Math.Min((int)(pct * deltas.Count), deltas.Count - 2);
            if (idx < 0) idx = 0;
            return TimeSpan.FromTicks(deltas[idx]);
        }

        /// <summary>
        /// Merges sample timestamps into "data present" ranges. When
        /// <paramref name="mergeGap"/> is given (normally the tag's per-tag gap threshold)
        /// it defines what still counts as one covered run — pass the same value the gap
        /// detector uses so green rendering and gap detection agree. Without it, falls
        /// back to 3× the median cadence.
        /// </summary>
        public static List<TimeRange> CoverageIntervals(
            List<DateTime> sortedTimes, TimeSpan? mergeGap = null)
        {
            if (sortedTimes == null || sortedTimes.Count == 0) return new List<TimeRange>();
            TimeSpan gap = mergeGap ?? TimeSpan.FromTicks(
                Math.Max(MedianInterval(sortedTimes).Ticks * 3, TimeSpan.FromSeconds(10).Ticks));
            return MergePoints(sortedTimes, gap, TimeSpan.FromSeconds(1))
                .Select(m => m.Range)
                .ToList();
        }

        /// <summary>The uncovered rest of [from,to): everything not inside <paramref name="covered"/>.</summary>
        public static List<TimeRange> Complement(DateTime from, DateTime to, List<TimeRange> covered)
        {
            var gaps = new List<TimeRange>();
            DateTime cursor = from;
            if (covered != null)
            {
                foreach (var c in covered)
                {
                    DateTime s = c.Start < from ? from : c.Start;
                    DateTime e = c.End > to ? to : c.End;
                    if (e <= cursor) continue;
                    if (s > cursor) gaps.Add(new TimeRange(cursor, s));
                    if (e > cursor) cursor = e;
                }
            }
            if (cursor < to) gaps.Add(new TimeRange(cursor, to));
            return gaps;
        }

        /// <summary>Overlap of two sorted, non-overlapping range lists (two-pointer sweep).</summary>
        public static List<TimeRange> Intersect(List<TimeRange> a, List<TimeRange> b)
        {
            var result = new List<TimeRange>();
            if (a == null || b == null) return result;
            int i = 0, j = 0;
            while (i < a.Count && j < b.Count)
            {
                DateTime s = a[i].Start > b[j].Start ? a[i].Start : b[j].Start;
                DateTime e = a[i].End < b[j].End ? a[i].End : b[j].End;
                if (s < e) result.Add(new TimeRange(s, e));
                if (a[i].End < b[j].End) i++; else j++;
            }
            return result;
        }

        /// <summary>
        /// Merged runs of timestamps present on one side but missing on the other at
        /// whole-second resolution (the backfill diff's own comparison). Runs are split
        /// when the spacing exceeds 3× the source's median cadence, so a run never
        /// bridges an area where the target actually has data.
        /// </summary>
        public static List<CopyableSegment> BuildCopyableSegments(
            List<DateTime> hasTimes, HashSet<long> lackSecondTicks,
            bool toSecondary, string description = null)
        {
            var missing = hasTimes
                .Where(t => !lackSecondTicks.Contains(SampleFilter.ToSecondTicks(t)))
                .ToList();
            if (missing.Count == 0) return new List<CopyableSegment>();

            TimeSpan median = MedianInterval(hasTimes);
            TimeSpan mergeGap = TimeSpan.FromTicks(
                Math.Max(median.Ticks * 3, TimeSpan.FromSeconds(10).Ticks));

            return MergePoints(missing, mergeGap, TimeSpan.FromSeconds(1))
                .Select(m => new CopyableSegment
                {
                    Range = m.Range,
                    ToSecondary = toSecondary,
                    SampleCount = m.Count,
                    Description = description
                })
                .ToList();
        }

        /// <summary>
        /// Splits a gap window into contiguous fillable (opposite server has data) and
        /// unfillable segments by merging consecutive batches with the same CanBackfill.
        /// </summary>
        public static void SplitByFeasibility(
            GapWindow gap, List<TimeRange> fillable, List<TimeRange> unfillable)
        {
            if (gap == null) return;
            if (gap.Batches == null || gap.Batches.Count == 0)
            {
                unfillable.Add(new TimeRange(gap.Start, gap.End));
                return;
            }

            DateTime segStart = gap.Batches[0].Start;
            bool segFillable  = gap.Batches[0].CanBackfill;
            DateTime segEnd   = gap.Batches[0].End;

            for (int i = 1; i < gap.Batches.Count; i++)
            {
                var b = gap.Batches[i];
                if (b.CanBackfill == segFillable && b.Start <= segEnd)
                {
                    segEnd = b.End;
                }
                else
                {
                    (segFillable ? fillable : unfillable).Add(new TimeRange(segStart, segEnd));
                    segStart    = b.Start;
                    segEnd      = b.End;
                    segFillable = b.CanBackfill;
                }
            }
            (segFillable ? fillable : unfillable).Add(new TimeRange(segStart, segEnd));
        }
    }
}
