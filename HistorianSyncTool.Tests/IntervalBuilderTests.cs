using HistorianSyncTool.Models;
using HistorianSyncTool.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HistorianSyncTool.Tests
{
    [TestClass]
    public class IntervalBuilderTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 5, 20, 12, 0, 0);
        private static DateTime T(int seconds) => T0.AddSeconds(seconds);

        // ── MergePoints ────────────────────────────────────────────────────────────

        [TestMethod]
        public void MergePoints_EmptyOrNull_ReturnsEmpty()
        {
            Assert.AreEqual(0, IntervalBuilder.MergePoints(null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(1)).Count);
            Assert.AreEqual(0, IntervalBuilder.MergePoints(new List<DateTime>(), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(1)).Count);
        }

        [TestMethod]
        public void MergePoints_PointsWithinMergeGap_BecomeOneRange()
        {
            var pts = new List<DateTime> { T(0), T(5), T(10), T(14) };
            var merged = IntervalBuilder.MergePoints(pts, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(1));

            Assert.AreEqual(1, merged.Count);
            Assert.AreEqual(T(0), merged[0].Range.Start);
            Assert.AreEqual(T(14), merged[0].Range.End);
            Assert.AreEqual(4, merged[0].Count);
        }

        [TestMethod]
        public void MergePoints_GapBeyondMergeGap_SplitsRanges()
        {
            var pts = new List<DateTime> { T(0), T(5), T(100), T(105) };
            var merged = IntervalBuilder.MergePoints(pts, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(1));

            Assert.AreEqual(2, merged.Count);
            Assert.AreEqual(2, merged[0].Count);
            Assert.AreEqual(2, merged[1].Count);
            Assert.AreEqual(T(100), merged[1].Range.Start);
        }

        [TestMethod]
        public void MergePoints_SinglePoint_WidenedToMinWidth()
        {
            var pts = new List<DateTime> { T(0) };
            var merged = IntervalBuilder.MergePoints(pts, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(2));

            Assert.AreEqual(1, merged.Count);
            Assert.AreEqual(TimeSpan.FromSeconds(2), merged[0].Range.Duration);
        }

        // ── MedianInterval ─────────────────────────────────────────────────────────

        [TestMethod]
        public void MedianInterval_FewerThanTwoPoints_Zero()
        {
            Assert.AreEqual(TimeSpan.Zero, IntervalBuilder.MedianInterval(new List<DateTime>()));
            Assert.AreEqual(TimeSpan.Zero, IntervalBuilder.MedianInterval(new List<DateTime> { T(0) }));
        }

        [TestMethod]
        public void MedianInterval_IgnoresOutliers()
        {
            // deltas: 10, 10, 10, 3600 → median 10
            var pts = new List<DateTime> { T(0), T(10), T(20), T(30), T(3630) };
            Assert.AreEqual(TimeSpan.FromSeconds(10), IntervalBuilder.MedianInterval(pts));
        }

        // ── Complement ─────────────────────────────────────────────────────────────

        [TestMethod]
        public void Complement_NoCoverage_WholeRangeIsGap()
        {
            var gaps = IntervalBuilder.Complement(T(0), T(100), new List<TimeRange>());
            Assert.AreEqual(1, gaps.Count);
            Assert.AreEqual(T(0), gaps[0].Start);
            Assert.AreEqual(T(100), gaps[0].End);
        }

        [TestMethod]
        public void Complement_MiddleCoverage_YieldsLeadingAndTrailingGaps()
        {
            var covered = new List<TimeRange> { new TimeRange(T(40), T(60)) };
            var gaps = IntervalBuilder.Complement(T(0), T(100), covered);

            Assert.AreEqual(2, gaps.Count);
            Assert.AreEqual(T(0), gaps[0].Start);
            Assert.AreEqual(T(40), gaps[0].End);
            Assert.AreEqual(T(60), gaps[1].Start);
            Assert.AreEqual(T(100), gaps[1].End);
        }

        [TestMethod]
        public void Complement_CoverageBeyondWindow_IsClipped()
        {
            var covered = new List<TimeRange> { new TimeRange(T(-50), T(150)) };
            var gaps = IntervalBuilder.Complement(T(0), T(100), covered);
            Assert.AreEqual(0, gaps.Count);
        }

        [TestMethod]
        public void Complement_FullCoverage_NoGaps()
        {
            var covered = new List<TimeRange> { new TimeRange(T(0), T(100)) };
            Assert.AreEqual(0, IntervalBuilder.Complement(T(0), T(100), covered).Count);
        }

        // ── Intersect ──────────────────────────────────────────────────────────────

        [TestMethod]
        public void Intersect_OverlappingRanges_ReturnsOverlap()
        {
            var a = new List<TimeRange> { new TimeRange(T(0), T(50)) };
            var b = new List<TimeRange> { new TimeRange(T(30), T(80)) };
            var x = IntervalBuilder.Intersect(a, b);

            Assert.AreEqual(1, x.Count);
            Assert.AreEqual(T(30), x[0].Start);
            Assert.AreEqual(T(50), x[0].End);
        }

        [TestMethod]
        public void Intersect_DisjointRanges_Empty()
        {
            var a = new List<TimeRange> { new TimeRange(T(0), T(10)) };
            var b = new List<TimeRange> { new TimeRange(T(20), T(30)) };
            Assert.AreEqual(0, IntervalBuilder.Intersect(a, b).Count);
        }

        [TestMethod]
        public void Intersect_MultipleSegments_SweepsCorrectly()
        {
            var a = new List<TimeRange>
            {
                new TimeRange(T(0), T(20)), new TimeRange(T(40), T(60)), new TimeRange(T(80), T(100))
            };
            var b = new List<TimeRange> { new TimeRange(T(10), T(90)) };
            var x = IntervalBuilder.Intersect(a, b);

            Assert.AreEqual(3, x.Count);
            Assert.AreEqual(T(10), x[0].Start);
            Assert.AreEqual(T(20), x[0].End);
            Assert.AreEqual(T(40), x[1].Start);
            Assert.AreEqual(T(60), x[1].End);
            Assert.AreEqual(T(80), x[2].Start);
            Assert.AreEqual(T(90), x[2].End);
        }

        // ── CoverageIntervals ──────────────────────────────────────────────────────

        [TestMethod]
        public void CoverageIntervals_SteadyCadence_OneInterval()
        {
            var pts = Enumerable.Range(0, 60).Select(i => T(i * 10)).ToList(); // 10s cadence
            var cov = IntervalBuilder.CoverageIntervals(pts);
            Assert.AreEqual(1, cov.Count);
        }

        [TestMethod]
        public void CoverageIntervals_LongPause_SplitsIntervals()
        {
            var pts = new List<DateTime>();
            for (int i = 0; i < 10; i++) pts.Add(T(i * 10));          // block 1: 0..90s
            for (int i = 0; i < 10; i++) pts.Add(T(3600 + i * 10));   // block 2: 1h later
            var cov = IntervalBuilder.CoverageIntervals(pts);
            Assert.AreEqual(2, cov.Count);
        }

        // ── BuildCopyableSegments ──────────────────────────────────────────────────

        [TestMethod]
        public void BuildCopyableSegments_TargetHasEverything_Empty()
        {
            var has = new List<DateTime> { T(0), T(10), T(20) };
            var lack = new HashSet<long>(has.Select(SampleFilter.ToSecondTicks));
            Assert.AreEqual(0, IntervalBuilder.BuildCopyableSegments(has, lack, true).Count);
        }

        [TestMethod]
        public void BuildCopyableSegments_SubSecondSourceMatchesStoredSecond()
        {
            // Source sample at 12:00:00.700 must match a target sample stored at 12:00:00
            var has = new List<DateTime> { T(0).AddMilliseconds(700) };
            var lack = new HashSet<long> { SampleFilter.ToSecondTicks(T(0)) };
            Assert.AreEqual(0, IntervalBuilder.BuildCopyableSegments(has, lack, true).Count);
        }

        [TestMethod]
        public void BuildCopyableSegments_MissingRun_CountedAndMerged()
        {
            // 10s cadence source; target lacks 5 consecutive samples
            var has = Enumerable.Range(0, 20).Select(i => T(i * 10)).ToList();
            var lack = new HashSet<long>(has
                .Where((t, i) => i < 5 || i >= 10)   // target has all except samples 5..9
                .Select(SampleFilter.ToSecondTicks));

            var segs = IntervalBuilder.BuildCopyableSegments(has, lack, toSecondary: true);
            Assert.AreEqual(1, segs.Count);
            Assert.AreEqual(5, segs[0].SampleCount);
            Assert.IsTrue(segs[0].ToSecondary);
            Assert.AreEqual(T(50), segs[0].Range.Start);
        }

        // ── SplitByFeasibility ─────────────────────────────────────────────────────

        [TestMethod]
        public void SplitByFeasibility_MergesConsecutiveBatchesOfSameKind()
        {
            var gap = new GapWindow { Start = T(0), End = T(400) };
            gap.Batches = new List<GapBatch>
            {
                new GapBatch { Start = T(0),   End = T(100), CanBackfill = true },
                new GapBatch { Start = T(100), End = T(200), CanBackfill = true },
                new GapBatch { Start = T(200), End = T(300), CanBackfill = false },
                new GapBatch { Start = T(300), End = T(400), CanBackfill = true }
            };

            var fill = new List<TimeRange>();
            var unfill = new List<TimeRange>();
            IntervalBuilder.SplitByFeasibility(gap, fill, unfill);

            Assert.AreEqual(2, fill.Count);
            Assert.AreEqual(1, unfill.Count);
            Assert.AreEqual(T(0), fill[0].Start);
            Assert.AreEqual(T(200), fill[0].End);
            Assert.AreEqual(T(200), unfill[0].Start);
            Assert.AreEqual(T(300), unfill[0].End);
            Assert.AreEqual(T(300), fill[1].Start);
        }

        [TestMethod]
        public void SplitByFeasibility_NoBatches_WholeGapUnfillable()
        {
            var gap = new GapWindow { Start = T(0), End = T(100), Batches = new List<GapBatch>() };
            var fill = new List<TimeRange>();
            var unfill = new List<TimeRange>();
            IntervalBuilder.SplitByFeasibility(gap, fill, unfill);

            Assert.AreEqual(0, fill.Count);
            Assert.AreEqual(1, unfill.Count);
        }
    }
}
