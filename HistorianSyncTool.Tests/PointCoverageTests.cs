using HistorianSyncTool.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HistorianSyncTool.Tests
{
    /// <summary>
    /// The arithmetic behind every row of the all-points overview.
    ///
    /// These numbers are only ever an ESTIMATE shown to a human — nothing here may reach a
    /// write path — but they still have to be honest: a point that exists on one server only
    /// must never be counted as readings we could restore, because this tool writes samples
    /// and does not create measurement points.
    ///
    /// The orchestration around this (chunking, the time budget, cancellation) is exercised
    /// against the demo server pair by scratchpad/probe-scanner.ps1.
    /// </summary>
    [TestClass]
    public class PointCoverageTests
    {
        private static PointCoverage Make(int[] main, int[] mirror,
            bool onMain = true, bool onMirror = true) =>
            new PointCoverage
            {
                Tag = "STAT6.TEST.F_CV",
                Main = main, Mirror = mirror,
                OnMain = onMain, OnMirror = onMirror,
                Scanned = true
            };

        // ── Coverage fraction ─────────────────────────────────────────────────────

        [TestMethod]
        public void Coverage_CountsBucketsThatHoldAnything()
        {
            var p = Make(new[] { 5, 0, 3, 4 }, new[] { 1, 1, 1, 1 });
            Assert.AreEqual(0.75, p.MainCoverage, 1e-9);
            Assert.AreEqual(1.0, p.MirrorCoverage, 1e-9);
        }

        [TestMethod]
        public void Coverage_IsNegativeWhenNothingWasMeasured()
        {
            var p = Make(null, new[] { 1, 1 });
            Assert.IsTrue(p.MainCoverage < 0, "unmeasured must be distinguishable from 0 %");
        }

        // ── One-sided buckets ─────────────────────────────────────────────────────

        [TestMethod]
        public void Missing_CountsAnOutageRunTheOtherSideLacks()
        {
            // Mirror records steadily, then goes dark for a long run in the middle.
            var main   = new[] { 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5 };
            var mirror = new[] { 5, 5, 5, 5, 5, 5, 0, 0, 0, 0, 0, 0, 0, 0, 5, 5, 5, 5, 5, 5 };
            var p = Make(main, mirror);
            Assert.AreEqual(40, p.EstMissingOnMirror, "the readings inside the mirror's outage");
            Assert.AreEqual(0, p.EstMissingOnMain);
        }

        [TestMethod]
        public void Missing_IgnoresScatteredOneSidedSegments()
        {
            // Two collectors logging about every other segment on their own clocks. Every
            // segment looks one-sided, but nothing is actually missing. Measured live: the
            // naive count claimed 228 readings on a point where SyncPlanner would copy 0.
            var main   = new[] { 2, 0, 2, 0, 2, 0, 2, 0, 2, 0 };
            var mirror = new[] { 0, 2, 0, 2, 0, 2, 0, 2, 0, 2 };
            var p = Make(main, mirror);
            Assert.AreEqual(0, p.EstMissingOnMirror, "alternating cadence is not missing data");
            Assert.AreEqual(0, p.EstMissingOnMain);
        }

        [TestMethod]
        public void Missing_IsZeroWhereBothSidesHoldSomething()
        {
            // Different densities but no empty bucket on either side — an outage-level
            // estimate must not report the density difference as missing data.
            var p = Make(new[] { 10, 10, 10 }, new[] { 1, 1, 1 });
            Assert.AreEqual(0, p.EstMissingOnMirror);
            Assert.AreEqual(0, p.EstMissingOnMain);
            Assert.IsTrue(p.InSync);
        }

        [TestMethod]
        public void Missing_BothDirectionsAreReportedIndependently()
        {
            // Mirror dark in the first half, main dark in the second.
            var main   = new[] { 4, 4, 4, 4, 4, 4, 4, 4, 0, 0, 0, 0, 0, 0, 0, 0 };
            var mirror = new[] { 0, 0, 0, 0, 0, 0, 0, 0, 3, 3, 3, 3, 3, 3, 3, 3 };
            var p = Make(main, mirror);
            Assert.AreEqual(32, p.EstMissingOnMirror);
            Assert.AreEqual(24, p.EstMissingOnMain);
        }

        // ── A point that exists on one server only ────────────────────────────────

        [TestMethod]
        public void OneSided_NeverCountsAsReadingsToRestore()
        {
            // The tool cannot create the point on the mirror, so promising these readings
            // would be a number we can never deliver.
            var p = Make(new[] { 9, 9, 9 }, null, onMirror: false);
            Assert.IsTrue(p.MissingEntirely);
            Assert.AreEqual(0, p.EstMissingOnMirror);
            Assert.AreEqual(0, p.EstMissingOnMain);
        }

        [TestMethod]
        public void OneSided_IsNotInSyncAndNeedsAttention()
        {
            var p = Make(new[] { 9, 9 }, null, onMirror: false);
            Assert.IsFalse(p.InSync);
            Assert.IsTrue(p.NeedsAttention);
        }

        [TestMethod]
        public void OneSided_RanksBelowActionableGapsButAboveHealthy()
        {
            var oneSided = Make(new[] { 1, 1 }, null, onMirror: false);
            var gap      = Make(new[] { 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5 },
                                new[] { 5, 5, 5, 5, 5, 5, 5, 5, 0, 0, 0, 0, 0, 0, 0, 0 });
            var healthy  = Make(new[] { 5, 5 }, new[] { 5, 5 });

            // What the user can act on leads. On the real rig 201 of 273 points exist on one
            // server only, and ranking those first buried every actual gap.
            Assert.IsTrue(gap.SortRank < oneSided.SortRank, "restorable gaps must lead");
            Assert.IsTrue(oneSided.SortRank < healthy.SortRank, "one-sided must stay above healthy");
        }

        [TestMethod]
        public void SortRank_FailedReadsComeBeforeOneSided()
        {
            var failed = Make(null, null);
            failed.Error = "read failed";
            var oneSided = Make(new[] { 1 }, null, onMirror: false);
            Assert.IsTrue(failed.SortRank < oneSided.SortRank);
        }

        [TestMethod]
        public void BucketSpan_IsThePeriodDividedByBuckets()
        {
            var scan = new CoverageScan
            {
                From = new System.DateTime(2026, 1, 1),
                To = new System.DateTime(2026, 1, 2),
                Buckets = 24
            };
            Assert.AreEqual(System.TimeSpan.FromHours(1), scan.BucketSpan,
                "the summary tells the user this — a gap shorter than one segment cannot show up");
        }

        [TestMethod]
        public void OneSided_OnTheMainServerIsDetectedToo()
        {
            var p = Make(null, new[] { 4, 4 }, onMain: false);
            Assert.IsTrue(p.MissingEntirely);
            Assert.AreEqual(0, p.EstMissingOnMain);
        }

        // ── Not scanned / failed ──────────────────────────────────────────────────

        [TestMethod]
        public void NotScanned_IsNeitherInSyncNorNeedingAttention()
        {
            var p = new PointCoverage { Tag = "X", Scanned = false };
            Assert.IsFalse(p.InSync);
            Assert.IsFalse(p.NeedsAttention, "a point we never looked at must not be labelled either way");
            Assert.IsFalse(p.MissingEntirely);
        }

        [TestMethod]
        public void Failed_NeedsAttentionButIsNotInSync()
        {
            var p = Make(null, null);
            p.Error = "read failed";
            Assert.IsFalse(p.InSync);
            Assert.IsTrue(p.NeedsAttention);
            Assert.IsFalse(p.MissingEntirely, "a failed read is not the same as a point that does not exist");
        }

        // ── Mismatched lengths must not throw ─────────────────────────────────────

        [TestMethod]
        public void DifferentBucketCounts_CompareOverTheOverlapOnly()
        {
            // Longer array on one side must not throw or read past the overlap.
            var p = Make(new[] { 1, 1, 1, 1, 1, 1, 1, 1 }, new[] { 1, 0, 0, 0 });
            Assert.AreEqual(0, p.EstMissingOnMain);
            Assert.IsTrue(p.EstMissingOnMirror >= 0);
        }
    }
}
