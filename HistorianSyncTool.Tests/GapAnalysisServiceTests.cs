using HistorianSyncTool.Models;
using HistorianSyncTool.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HistorianSyncTool.Tests
{
    [TestClass]
    public class GapAnalysisServiceTests
    {
        private GapAnalysisService _svc;

        [TestInitialize]
        public void Setup()
        {
            _svc = new GapAnalysisService(TimeSpan.FromMinutes(10));
        }

        // ── Null / empty / single-sample edge cases ────────────────────────────────

        [TestMethod]
        public void Analyze_NullInput_ReturnsNoData()
        {
            var result = _svc.Analyze(null, "Test");

            Assert.IsFalse(result.HasData);
            Assert.IsFalse(result.HasGaps);
            Assert.AreEqual(0.0, result.CoverageRatio);
        }

        [TestMethod]
        public void Analyze_EmptyList_ReturnsNoData()
        {
            var result = _svc.Analyze(new List<DateTime>(), "Test");

            Assert.IsFalse(result.HasData);
            Assert.IsFalse(result.HasGaps);
            Assert.AreEqual(0.0, result.CoverageRatio);
        }

        [TestMethod]
        public void Analyze_SingleSample_ReturnsHasDataNoGaps()
        {
            var times = new List<DateTime> { new DateTime(2025, 1, 1, 12, 0, 0) };
            var result = _svc.Analyze(times, "Test");

            Assert.IsTrue(result.HasData);
            Assert.IsFalse(result.HasGaps);
            Assert.AreEqual(1.0, result.CoverageRatio);
            Assert.AreEqual(1, result.TotalSamples);
        }

        // ── Continuous data (no gaps) ──────────────────────────────────────────────

        [TestMethod]
        public void Analyze_ContinuousSamples_NoGapsDetected()
        {
            // 60 samples at 1-minute intervals — no gap exceeds 1.5× median
            var start = new DateTime(2025, 1, 1, 12, 0, 0);
            var times = Enumerable.Range(0, 60)
                .Select(i => start.AddMinutes(i))
                .ToList();

            var result = _svc.Analyze(times, "Server1");

            Assert.IsTrue(result.HasData);
            Assert.IsFalse(result.HasGaps);
            Assert.AreEqual(60, result.TotalSamples);
            Assert.AreEqual(TimeSpan.FromMinutes(1), result.ExpectedInterval);
            Assert.IsTrue(result.CoverageRatio > 0.99);
        }

        // ── Single gap detection ───────────────────────────────────────────────────

        [TestMethod]
        public void Analyze_SingleGap_DetectedCorrectly()
        {
            var start = new DateTime(2025, 1, 1, 12, 0, 0);
            var times = new List<DateTime>();

            // 10 samples at 1-min intervals
            for (int i = 0; i < 10; i++)
                times.Add(start.AddMinutes(i));

            // 30-minute gap (skip minutes 10–39)

            // 10 more samples at 1-min intervals starting at minute 40
            for (int i = 40; i < 50; i++)
                times.Add(start.AddMinutes(i));

            var result = _svc.Analyze(times, "Server1");

            Assert.IsTrue(result.HasGaps);
            Assert.AreEqual(1, result.Gaps.Count);

            var gap = result.Gaps[0];
            Assert.AreEqual(start.AddMinutes(9), gap.Start);
            Assert.AreEqual(start.AddMinutes(40), gap.End);
            Assert.AreEqual(TimeSpan.FromMinutes(31), gap.Duration);
        }

        [TestMethod]
        public void Analyze_SingleGap_CoverageRatioIsReasonable()
        {
            var start = new DateTime(2025, 1, 1, 0, 0, 0);
            var times = new List<DateTime>();

            // 30 min normal, 30 min gap, 30 min normal = ~67% coverage
            for (int i = 0; i < 30; i++) times.Add(start.AddMinutes(i));
            for (int i = 60; i < 90; i++) times.Add(start.AddMinutes(i));

            var result = _svc.Analyze(times, "Test");

            Assert.IsTrue(result.CoverageRatio > 0.5);
            Assert.IsTrue(result.CoverageRatio < 0.9);
        }

        // ── Multiple gaps ──────────────────────────────────────────────────────────

        [TestMethod]
        public void Analyze_MultipleGaps_AllDetected()
        {
            var start = new DateTime(2025, 1, 1, 0, 0, 0);
            var times = new List<DateTime>();

            // Segment 1: minutes 0–9
            for (int i = 0; i < 10; i++) times.Add(start.AddMinutes(i));
            // Gap 1: 10 → 25 (15 min)
            // Segment 2: minutes 25–34
            for (int i = 25; i < 35; i++) times.Add(start.AddMinutes(i));
            // Gap 2: 34 → 50 (16 min)
            // Segment 3: minutes 50–59
            for (int i = 50; i < 60; i++) times.Add(start.AddMinutes(i));

            var result = _svc.Analyze(times, "Test");

            Assert.AreEqual(2, result.Gaps.Count);
        }

        // ── Median interval calculation ────────────────────────────────────────────

        [TestMethod]
        public void Median_OddCount_ReturnsMiddle()
        {
            var spans = new List<TimeSpan>
            {
                TimeSpan.FromSeconds(10),
                TimeSpan.FromSeconds(30),
                TimeSpan.FromSeconds(20)
            };

            var median = GapAnalysisService.Median(spans);

            Assert.AreEqual(TimeSpan.FromSeconds(20), median);
        }

        [TestMethod]
        public void Median_EvenCount_ReturnsAverage()
        {
            var spans = new List<TimeSpan>
            {
                TimeSpan.FromSeconds(10),
                TimeSpan.FromSeconds(20),
                TimeSpan.FromSeconds(30),
                TimeSpan.FromSeconds(40)
            };

            var median = GapAnalysisService.Median(spans);

            Assert.AreEqual(TimeSpan.FromSeconds(25), median);
        }

        [TestMethod]
        public void Median_Empty_ReturnsZero()
        {
            var median = GapAnalysisService.Median(new List<TimeSpan>());
            Assert.AreEqual(TimeSpan.Zero, median);
        }

        // ── Batch planning ─────────────────────────────────────────────────────────

        [TestMethod]
        public void PlanBatches_ExactMultiple_CorrectCount()
        {
            // 30-minute gap with 10-min batch size = 3 batches
            var start = new DateTime(2025, 1, 1, 12, 0, 0);
            var end   = start.AddMinutes(30);

            var batches = _svc.PlanBatches(start, end);

            Assert.AreEqual(3, batches.Count);
            Assert.AreEqual(start, batches[0].Start);
            Assert.AreEqual(start.AddMinutes(10), batches[0].End);
            Assert.AreEqual(start.AddMinutes(10), batches[1].Start);
            Assert.AreEqual(start.AddMinutes(20), batches[1].End);
            Assert.AreEqual(start.AddMinutes(20), batches[2].Start);
            Assert.AreEqual(end, batches[2].End);
        }

        [TestMethod]
        public void PlanBatches_NotExactMultiple_LastBatchIsShorter()
        {
            // 25-minute gap with 10-min batch size = 3 batches (last one is 5 min)
            var start = new DateTime(2025, 1, 1, 12, 0, 0);
            var end   = start.AddMinutes(25);

            var batches = _svc.PlanBatches(start, end);

            Assert.AreEqual(3, batches.Count);
            Assert.AreEqual(TimeSpan.FromMinutes(5), batches[2].Duration);
        }

        [TestMethod]
        public void PlanBatches_SmallerThanBatchSize_SingleBatch()
        {
            var start = new DateTime(2025, 1, 1, 12, 0, 0);
            var end   = start.AddMinutes(5);

            var batches = _svc.PlanBatches(start, end);

            Assert.AreEqual(1, batches.Count);
            Assert.AreEqual(start, batches[0].Start);
            Assert.AreEqual(end, batches[0].End);
        }

        [TestMethod]
        public void PlanBatches_ContiguousBoundaries()
        {
            var start = new DateTime(2025, 1, 1, 12, 0, 0);
            var end   = start.AddMinutes(40);
            var batches = _svc.PlanBatches(start, end);

            for (int i = 1; i < batches.Count; i++)
                Assert.AreEqual(batches[i - 1].End, batches[i].Start,
                    $"Batch {i - 1} end should equal batch {i} start (contiguous)");
        }

        // ── Backfill feasibility ───────────────────────────────────────────────────

        [TestMethod]
        public void MarkBackfillFeasibility_SourceHasData_MarksTrue()
        {
            var start = new DateTime(2025, 1, 1, 12, 0, 0);
            var result = new GapAnalysisResult
            {
                HasData = true,
                Gaps = new List<GapWindow>
                {
                    new GapWindow
                    {
                        Start = start,
                        End = start.AddMinutes(20),
                        Batches = new List<GapBatch>
                        {
                            new GapBatch { Start = start, End = start.AddMinutes(10) },
                            new GapBatch { Start = start.AddMinutes(10), End = start.AddMinutes(20) }
                        }
                    }
                }
            };

            // Other server has data in first batch only
            var otherSamples = new List<DateTime>
            {
                start.AddMinutes(3),
                start.AddMinutes(7)
            };

            _svc.MarkBackfillFeasibility(result, otherSamples);

            Assert.IsTrue(result.Gaps[0].Batches[0].CanBackfill);
            Assert.IsFalse(result.Gaps[0].Batches[1].CanBackfill);
        }

        [TestMethod]
        public void MarkBackfillFeasibility_NullOtherSamples_AllFalse()
        {
            var result = new GapAnalysisResult
            {
                HasData = true,
                Gaps = new List<GapWindow>
                {
                    new GapWindow
                    {
                        Start = DateTime.Now,
                        End = DateTime.Now.AddMinutes(10),
                        Batches = new List<GapBatch>
                        {
                            new GapBatch { Start = DateTime.Now, End = DateTime.Now.AddMinutes(10) }
                        }
                    }
                }
            };

            _svc.MarkBackfillFeasibility(result, null);

            Assert.IsFalse(result.Gaps[0].Batches[0].CanBackfill);
        }

        [TestMethod]
        public void MarkBackfillFeasibility_HalfOpenInterval_BoundaryExcluded()
        {
            // Sample exactly at batch.End should NOT count (half-open [start, end))
            var start = new DateTime(2025, 1, 1, 12, 0, 0);
            var result = new GapAnalysisResult
            {
                HasData = true,
                Gaps = new List<GapWindow>
                {
                    new GapWindow
                    {
                        Start = start,
                        End = start.AddMinutes(10),
                        Batches = new List<GapBatch>
                        {
                            new GapBatch { Start = start, End = start.AddMinutes(10) }
                        }
                    }
                }
            };

            // Only sample is exactly at the end boundary
            var otherSamples = new List<DateTime> { start.AddMinutes(10) };

            _svc.MarkBackfillFeasibility(result, otherSamples);

            Assert.IsFalse(result.Gaps[0].Batches[0].CanBackfill,
                "Sample at exact batch.End should not count (half-open interval).");
        }

        [TestMethod]
        public void MarkBackfillFeasibility_SampleAtStart_Included()
        {
            var start = new DateTime(2025, 1, 1, 12, 0, 0);
            var result = new GapAnalysisResult
            {
                HasData = true,
                Gaps = new List<GapWindow>
                {
                    new GapWindow
                    {
                        Start = start,
                        End = start.AddMinutes(10),
                        Batches = new List<GapBatch>
                        {
                            new GapBatch { Start = start, End = start.AddMinutes(10) }
                        }
                    }
                }
            };

            var otherSamples = new List<DateTime> { start };

            _svc.MarkBackfillFeasibility(result, otherSamples);

            Assert.IsTrue(result.Gaps[0].Batches[0].CanBackfill,
                "Sample at exact batch.Start should count (half-open interval).");
        }

        // ── Sort validation ────────────────────────────────────────────────────────

        [TestMethod]
        public void Analyze_UnsortedInput_SortsAndAnalyzesCorrectly()
        {
            var start = new DateTime(2025, 1, 1, 12, 0, 0);
            var times = new List<DateTime>
            {
                start.AddMinutes(5),
                start.AddMinutes(0),
                start.AddMinutes(3),
                start.AddMinutes(1),
                start.AddMinutes(4),
                start.AddMinutes(2)
            };

            var result = _svc.Analyze(times, "Test");

            Assert.IsTrue(result.HasData);
            Assert.AreEqual(6, result.TotalSamples);
            Assert.AreEqual(start, result.FirstTimestamp);
            Assert.AreEqual(start.AddMinutes(5), result.LastTimestamp);
            Assert.AreEqual(TimeSpan.FromMinutes(1), result.ExpectedInterval);
        }

        // ── Coverage ratio ─────────────────────────────────────────────────────────

        [TestMethod]
        public void Analyze_FullCoverage_RatioIsOne()
        {
            var start = new DateTime(2025, 1, 1, 0, 0, 0);
            var times = Enumerable.Range(0, 100)
                .Select(i => start.AddSeconds(i * 10))
                .ToList();

            var result = _svc.Analyze(times, "Test");

            Assert.AreEqual(1.0, result.CoverageRatio, 0.001);
        }

        [TestMethod]
        public void Analyze_CoverageRatio_NeverNegative()
        {
            // Extreme case: mostly gaps
            var start = new DateTime(2025, 1, 1, 0, 0, 0);
            var times = new List<DateTime>
            {
                start,
                start.AddSeconds(1),
                start.AddHours(10)  // huge gap
            };

            var result = _svc.Analyze(times, "Test");

            Assert.IsTrue(result.CoverageRatio >= 0.0, "Coverage ratio should never be negative.");
        }

        // ── BatchSize from constructor ─────────────────────────────────────────────

        [TestMethod]
        public void Constructor_CustomBatchSize_IsUsed()
        {
            var svc = new GapAnalysisService(TimeSpan.FromMinutes(5));
            Assert.AreEqual(TimeSpan.FromMinutes(5), svc.BatchSize);

            var start = new DateTime(2025, 1, 1, 12, 0, 0);
            var batches = svc.PlanBatches(start, start.AddMinutes(15));
            Assert.AreEqual(3, batches.Count);
        }

        // ── Integration: Analyze → MarkBackfillFeasibility ─────────────────────────

        [TestMethod]
        public void Analyze_ThenMarkFeasibility_EndToEnd()
        {
            var start = new DateTime(2025, 1, 1, 0, 0, 0);

            // Target server: 60 samples, then 30-min gap, then 60 more
            var targetTimes = new List<DateTime>();
            for (int i = 0; i < 60; i++) targetTimes.Add(start.AddMinutes(i));
            for (int i = 90; i < 150; i++) targetTimes.Add(start.AddMinutes(i));

            // Source server: continuous across the gap
            var sourceTimes = Enumerable.Range(0, 150)
                .Select(i => start.AddMinutes(i))
                .ToList();

            var result = _svc.Analyze(targetTimes, "Target");
            _svc.MarkBackfillFeasibility(result, sourceTimes);

            Assert.IsTrue(result.HasGaps);
            Assert.AreEqual(1, result.Gaps.Count);

            // All batches in the gap should be backfillable since source has continuous data
            foreach (var batch in result.Gaps[0].Batches)
                Assert.IsTrue(batch.CanBackfill, $"Batch [{batch.Start:HH:mm}–{batch.End:HH:mm}] should be backfillable.");
        }
    }
}
