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
        public void Coverage_IsTheShareOfEverythingRecordedAnywhere()
        {
            // best per segment = 5, 1, 3, 4 = 13.  main has 12, mirror has 4.
            var p = Make(new[] { 5, 0, 3, 4 }, new[] { 1, 1, 1, 1 });
            Assert.AreEqual(12.0 / 13.0, p.MainCoverage, 1e-9);
            Assert.AreEqual(4.0 / 13.0, p.MirrorCoverage, 1e-9);
        }

        [TestMethod]
        public void Coverage_DoesNotSaturateWhenEverySegmentIsTouched()
        {
            // THE regression this measure exists for. Every segment holds at least one reading
            // on both servers, which the old "segments touched" measure reported as 100 %/100 %
            // for essentially every point on a year-long window - so every row in the list
            // looked identical and a track could be labelled 100 % while painted mostly red.
            var p = Make(new[] { 100, 100, 100, 100 }, new[] { 100, 1, 100, 100 });
            Assert.AreEqual(1.0, p.MainCoverage, 1e-9);
            Assert.AreEqual(301.0 / 400.0, p.MirrorCoverage, 1e-9);
            Assert.IsTrue(p.MirrorCoverage < 0.8, "a server missing a quarter of the readings must not read as ~100 %");
        }

        [TestMethod]
        public void Coverage_IsZeroNotNegativeWhenNeitherServerRecordedAnything()
        {
            var p = Make(new[] { 0, 0 }, new[] { 0, 0 });
            Assert.AreEqual(0.0, p.MainCoverage, 1e-9);
        }

        [TestMethod]
        public void Coverage_IsNegativeWhenNothingWasMeasured()
        {
            var p = Make(null, new[] { 1, 1 });
            Assert.IsTrue(p.MainCoverage < 0, "unmeasured must be distinguishable from 0 %");
        }

        // ── Total outage on one server (Phase 13 audit) ───────────────────────────

        [TestMethod]
        public void TotalOutage_CountsEveryReadingTheOtherServerHolds()
        {
            // The worst case this tool exists to find: the point IS configured on both, but the
            // mirror recorded nothing at all in this window. It used to score 0 to restore —
            // the spacing rule needs a cadence and a silent server has none, and the shortfall
            // rule bailed out too — so the row was painted green as "no difference found" and
            // sorted last, while opening it offered to restore every reading.
            var p = Make(new[] { 10, 12, 9, 11 }, new[] { 0, 0, 0, 0 });

            Assert.AreEqual(42, p.EstMissingOnMirror, "every reading the main server holds is missing there");
            Assert.AreEqual(0, p.EstMissingOnMain);
            Assert.IsFalse(p.InSync, "a total outage must never read as in sync");
            Assert.AreEqual(0, p.SortRank, "it must lead the list, not sit at the bottom");
        }

        [TestMethod]
        public void TotalOutage_NotClaimedWhenThePointIsNotConfiguredThere()
        {
            // Same counts, but the point does not exist on the mirror. A restore cannot create
            // a measurement point, so promising 42 readings would be a number we cannot deliver.
            var p = Make(new[] { 10, 12, 9, 11 }, new[] { 0, 0, 0, 0 }, onMirror: false);

            Assert.AreEqual(0, p.EstMissingOnMirror);
            Assert.IsTrue(p.MissingEntirely);
        }

        [TestMethod]
        public void TotalOutage_SilenceOnBothServersIsNobodysFault()
        {
            // A plant-wide silence is not a restorable difference in either direction.
            var p = Make(new[] { 0, 0, 0 }, new[] { 0, 0, 0 });

            Assert.AreEqual(0, p.EstMissingOnMirror);
            Assert.AreEqual(0, p.EstMissingOnMain);
        }

        // ── Per-segment share (what the bars and the timeline paint) ──────────────

        [TestMethod]
        public void SegmentShare_IsTheFractionOfTheBetterServedServer()
        {
            var share = PointCoverage.SegmentShare(new[] { 5, 0, 2 }, new[] { 10, 0, 1 });
            Assert.AreEqual(0.5, share[0], 1e-9);
            Assert.AreEqual(-1.0, share[1], 1e-9, "neither server recorded here - grey, not this server's failure");
            Assert.AreEqual(1.0, share[2], 1e-9, "holding the most of anyone is complete");
        }

        [TestMethod]
        public void SegmentShare_IsZeroWhereOnlyTheOtherServerHasReadings()
        {
            var share = PointCoverage.SegmentShare(new[] { 0 }, new[] { 7 });
            Assert.AreEqual(0.0, share[0], 1e-9, "0 % must be paintable as fully missing, never as green");
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
