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
        public void Missing_CountsOnlyBucketsTheOtherSideLacksEntirely()
        {
            // Mirror is empty in buckets 1 and 3; the main server holds 7 + 9 readings there.
            var p = Make(new[] { 4, 7, 4, 9 }, new[] { 2, 0, 2, 0 });
            Assert.AreEqual(16, p.EstMissingOnMirror);
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
            var p = Make(new[] { 5, 0, 5 }, new[] { 0, 6, 5 });
            Assert.AreEqual(5, p.EstMissingOnMirror);
            Assert.AreEqual(6, p.EstMissingOnMain);
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
        public void OneSided_OutranksEveryDataGap()
        {
            var oneSided = Make(new[] { 1, 1 }, null, onMirror: false);
            var bigGap = Make(new[] { 1000000, 1000000 }, new[] { 0, 0 });
            Assert.IsTrue(oneSided.Severity > bigGap.Severity,
                "a point missing from a server is a configuration problem — restoring cannot fix it");
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
            var p = Make(new[] { 1, 0, 1, 0, 1 }, new[] { 0, 0, 1 });
            Assert.AreEqual(1, p.EstMissingOnMirror);   // only bucket 0 of the shared prefix
            Assert.AreEqual(0, p.EstMissingOnMain);
        }
    }
}
