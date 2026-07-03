using HistorianSyncTool.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HistorianSyncTool.Tests
{
    [TestClass]
    public class SyncPlannerTests
    {
        private static readonly DateTime T0 = new DateTime(2025, 1, 1, 0, 0, 0);
        private static readonly TimeSpan Floor = TimeSpan.FromSeconds(120);
        private const double Mult = 2.0;

        private static List<DateTime> Every(DateTime start, TimeSpan step, int count) =>
            Enumerable.Range(0, count).Select(i => start + TimeSpan.FromTicks(step.Ticks * i)).ToList();

        // ── Aligned streams (same-source data) → exact-second diff ────────────────

        [TestMethod]
        public void Plan_AlignedStreams_UsesExactDiff_CatchesIsolatedMiss()
        {
            // Identical 10s streams except target lacks ONE sample in the middle.
            var src = Every(T0, TimeSpan.FromSeconds(10), 100);
            var tgt = new List<DateTime>(src);
            tgt.RemoveAt(50);

            var plan = SyncPlanner.Plan(src, tgt, T0, T0.AddSeconds(1000), Floor, Mult);

            Assert.IsTrue(plan.StreamsAligned, "99% match rate should count as aligned.");
            Assert.IsTrue(plan.UsedExactDiff);
            Assert.AreEqual(1, plan.ToCopy.Count, "The single missing sample must be caught.");
            Assert.AreEqual(src[50], plan.ToCopy[0]);
        }

        [TestMethod]
        public void Plan_AlignedStreams_InSync_NothingToCopy()
        {
            var src = Every(T0, TimeSpan.FromSeconds(5), 200);
            var plan = SyncPlanner.Plan(src, new List<DateTime>(src), T0, T0.AddSeconds(1000), Floor, Mult);

            Assert.IsTrue(plan.StreamsAligned);
            Assert.AreEqual(0, plan.ToCopy.Count);
        }

        [TestMethod]
        public void Plan_SubSecondSource_MatchesStoredSecond()
        {
            // Source sample at 12:00:00.400 must match target's stored 12:00:00.
            var src = Every(T0.AddMilliseconds(400), TimeSpan.FromSeconds(10), 50);
            var tgt = Every(T0, TimeSpan.FromSeconds(10), 50);

            var plan = SyncPlanner.Plan(src, tgt, T0, T0.AddSeconds(600), Floor, Mult);

            Assert.IsTrue(plan.StreamsAligned, "Whole-second matching must ignore sub-second parts.");
            Assert.AreEqual(0, plan.ToCopy.Count);
        }

        // ── Independent streams (redundant collectors) → outage fill only ─────────

        [TestMethod]
        public void Plan_IndependentStreams_NoOutage_CopiesNothing()
        {
            // Two collectors sampling every 10s, offset by 4s: 0% exact matches but
            // both healthy. The old exact diff would copy EVERYTHING (100% phantom).
            var src = Every(T0, TimeSpan.FromSeconds(10), 360);
            var tgt = Every(T0.AddSeconds(4), TimeSpan.FromSeconds(10), 360);

            var plan = SyncPlanner.Plan(src, tgt, T0, T0.AddSeconds(3600), Floor, Mult);

            Assert.IsFalse(plan.StreamsAligned);
            Assert.IsFalse(plan.UsedExactDiff);
            Assert.AreEqual(0, plan.TargetOutages.Count, "Healthy offset streams have no outages.");
            Assert.AreEqual(0, plan.ToCopy.Count, "No phantom copies for offset duplicates.");
        }

        [TestMethod]
        public void Plan_IndependentStreams_RealOutage_FilledFromSource()
        {
            // Source: continuous 10s samples for 2h. Target: same cadence offset 3s,
            // but silent from minute 30 to minute 60 (a real outage).
            var src = Every(T0, TimeSpan.FromSeconds(10), 720);
            var tgt = Every(T0.AddSeconds(3), TimeSpan.FromSeconds(10), 180) // 0-30min
                .Concat(Every(T0.AddMinutes(60).AddSeconds(3), TimeSpan.FromSeconds(10), 360)) // 60-120min
                .ToList();

            var plan = SyncPlanner.Plan(src, tgt, T0, T0.AddHours(2), Floor, Mult);

            Assert.IsFalse(plan.UsedExactDiff);
            Assert.AreEqual(1, plan.TargetOutages.Count, "Exactly one outage window expected.");
            // ~30 minutes of 10s source samples ≈ 180 samples (edges shared with target seconds)
            Assert.IsTrue(plan.ToCopy.Count >= 175 && plan.ToCopy.Count <= 182,
                $"Expected ~180 samples to copy, got {plan.ToCopy.Count}.");
            Assert.IsTrue(plan.ToCopy.All(t => t >= T0.AddMinutes(29) && t <= T0.AddMinutes(61)),
                "All copied samples must lie inside the outage window.");
        }

        [TestMethod]
        public void Plan_EmptyTarget_WholeWindowIsOutage_CopiesEverything()
        {
            var src = Every(T0, TimeSpan.FromMinutes(1), 60);
            var plan = SyncPlanner.Plan(src, new List<DateTime>(), T0, T0.AddHours(1), Floor, Mult);

            Assert.AreEqual(src.Count, plan.ToCopy.Count, "Empty target: copy all source samples.");
        }

        [TestMethod]
        public void Plan_EmptySource_NothingToCopy()
        {
            var plan = SyncPlanner.Plan(new List<DateTime>(), Every(T0, TimeSpan.FromSeconds(10), 50),
                T0, T0.AddHours(1), Floor, Mult);
            Assert.AreEqual(0, plan.ToCopy.Count);
        }

        [TestMethod]
        public void Plan_TinyData_FallsBackToExactDiff()
        {
            // Below MinSamplesForStats no honest cadence exists — exact diff is the
            // bounded-harm fallback.
            var src = Every(T0, TimeSpan.FromMinutes(5), 5);
            var tgt = Every(T0.AddSeconds(7), TimeSpan.FromMinutes(5), 5);

            var plan = SyncPlanner.Plan(src, tgt, T0, T0.AddHours(1), Floor, Mult);

            Assert.IsTrue(plan.UsedExactDiff);
            Assert.AreEqual(5, plan.ToCopy.Count);
        }

        [TestMethod]
        public void Plan_Rerun_AfterFill_IsIdempotent()
        {
            // After a backfill the target holds the copied seconds — a re-run must copy 0.
            var src = Every(T0, TimeSpan.FromSeconds(10), 720);
            var tgtBefore = Every(T0.AddSeconds(3), TimeSpan.FromSeconds(10), 180)
                .Concat(Every(T0.AddMinutes(60).AddSeconds(3), TimeSpan.FromSeconds(10), 360))
                .ToList();

            var first = SyncPlanner.Plan(src, tgtBefore, T0, T0.AddHours(2), Floor, Mult);
            Assert.IsTrue(first.ToCopy.Count > 0);

            var tgtAfter = tgtBefore.Concat(first.ToCopy).OrderBy(t => t).ToList();
            var second = SyncPlanner.Plan(src, tgtAfter, T0, T0.AddHours(2), Floor, Mult);

            Assert.AreEqual(0, second.ToCopy.Count, "Re-running after the fill must copy nothing.");
        }

        // ── DetectOutages ──────────────────────────────────────────────────────────

        [TestMethod]
        public void DetectOutages_LeadingInternalTrailing()
        {
            var threshold = TimeSpan.FromMinutes(5);
            var times = Every(T0.AddMinutes(10), TimeSpan.FromMinutes(1), 10)   // 10-19
                .Concat(Every(T0.AddMinutes(40), TimeSpan.FromMinutes(1), 11))  // 40-50
                .ToList();

            var outages = SyncPlanner.DetectOutages(times, T0, T0.AddMinutes(60), threshold);

            Assert.AreEqual(3, outages.Count);
            Assert.AreEqual(T0, outages[0].Start);                       // leading
            Assert.AreEqual(T0.AddMinutes(10), outages[0].End);
            Assert.AreEqual(T0.AddMinutes(19), outages[1].Start);        // internal
            Assert.AreEqual(T0.AddMinutes(40), outages[1].End);
            Assert.AreEqual(T0.AddMinutes(50), outages[2].Start);        // trailing
            Assert.AreEqual(T0.AddMinutes(60), outages[2].End);
        }

        [TestMethod]
        public void DetectOutages_EmptyTimes_WholeWindow()
        {
            var outages = SyncPlanner.DetectOutages(new List<DateTime>(), T0, T0.AddHours(1),
                TimeSpan.FromMinutes(5));
            Assert.AreEqual(1, outages.Count);
            Assert.AreEqual(T0, outages[0].Start);
            Assert.AreEqual(T0.AddHours(1), outages[0].End);
        }

        // ── GapRule (per-tag threshold) ────────────────────────────────────────────

        [TestMethod]
        public void GapRule_DeadbandTag_UsesTailNotMedian()
        {
            // 6-min median cadence with regular 30-60 min quiet periods (the real
            // TEMP_02_WS shape). Median×1.5 would flag every quiet period; the p90
            // rule must exceed the normal quiet duration.
            var times = new List<DateTime>();
            var t = T0;
            var rnd = new Random(42);
            for (int i = 0; i < 300; i++)
            {
                times.Add(t);
                // 80% short intervals (~6min), 20% long quiet (~30-60min)
                t = t.AddMinutes(rnd.NextDouble() < 0.8 ? 6 : 30 + rnd.NextDouble() * 30);
            }

            var rule = SyncPlanner.GapRule(times, Floor, Mult);

            Assert.IsTrue(rule >= TimeSpan.FromMinutes(60),
                $"Rule must exceed the normal quiet periods, got {rule}.");
        }

        [TestMethod]
        public void GapRule_RegularTag_FloorApplies()
        {
            var times = Every(T0, TimeSpan.FromSeconds(5), 500);
            var rule = SyncPlanner.GapRule(times, Floor, Mult);
            Assert.AreEqual(Floor, rule, "Regular fast tag: floor dominates (2×5s < 120s).");
        }
    }
}
