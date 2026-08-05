using Proficy.Historian.ClientAccess.API;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace HistorianSyncTool.Services
{
    /// <summary>What the overview knows about one measurement point.</summary>
    public sealed class PointCoverage
    {
        public string Tag;

        /// <summary>Raw sample counts per bucket, or null when the point was not scanned.</summary>
        public int[] Main;
        public int[] Mirror;

        /// <summary>
        /// Whether the point is configured on each server AT ALL (from the tag browse, not from
        /// the counts — a point can exist and simply hold nothing in the chosen period).
        /// </summary>
        public bool OnMain = true;
        public bool OnMirror = true;

        /// <summary>
        /// The point exists on one server only. Reported, but never counted as readings to
        /// restore: this tool writes samples, it does not create tags — the point has to be
        /// configured on the other server before any data can go there.
        /// </summary>
        public bool MissingEntirely => Scanned && Error == null && (!OnMain || !OnMirror);

        /// <summary>False when the scan ran out of its time budget before reaching this point.</summary>
        public bool Scanned;

        /// <summary>Set when this point could not be read (message shown on the row).</summary>
        public string Error;

        public int Buckets => Main != null ? Main.Length : (Mirror != null ? Mirror.Length : 0);

        public double MainCoverage   => ShareOfBest(Main, Mirror);
        public double MirrorCoverage => ShareOfBest(Mirror, Main);

        /// <summary>
        /// Fraction of segments holding at least one reading. NOT the headline number — it
        /// saturates at 100 % on almost any long window (see <see cref="ShareOfBest"/>) — but
        /// the density gates below still want it, because "is this server recording here at
        /// all?" is a different question from "does it have as much as the other one".
        /// </summary>
        private static double SegmentsTouched(int[] counts)
        {
            if (counts == null || counts.Length == 0) return -1;
            int with = 0;
            for (int i = 0; i < counts.Length; i++) if (counts[i] > 0) with++;
            return (double)with / counts.Length;
        }

        /// <summary>
        /// Estimated readings the mirror is missing (buckets where only the main server has
        /// data). Zero when the point does not exist on the mirror at all — a restore could
        /// not write them, and reporting a number we cannot deliver would be a lie.
        /// </summary>
        public int EstMissingOnMirror => MissingEntirely
            ? 0 : OneSided(Main, Mirror) + Shortfall(Main, Mirror);

        /// <summary>Estimated readings the main server is missing.</summary>
        public int EstMissingOnMain => MissingEntirely
            ? 0 : OneSided(Mirror, Main) + Shortfall(Mirror, Main);

        /// <summary>
        /// True when BOTH servers record in nearly every segment, so the segment counts are
        /// dense enough to compare against each other. Below that, a server's silence in a
        /// segment is just its cadence and count differences are noise.
        ///
        /// Deliberately <see cref="SegmentsTouched"/>, not the headline coverage: this gate asks
        /// "is each server recording throughout the window?", which is what the estimate was
        /// calibrated against (see the measured table in known-issues.md). Swapping in
        /// share-of-best here would silently re-tune the estimate.
        /// </summary>
        private bool DenseEnoughToCompare =>
            SegmentsTouched(Main) >= 0.8 && SegmentsTouched(Mirror) >= 0.8 && SameRecordingRate;

        /// <summary>
        /// True when both servers record at a comparable overall rate. Two servers logging the
        /// same point at genuinely different rates (different deadband, different collector
        /// settings) will differ in every segment without anything being missing, so comparing
        /// their per-segment counts would manufacture a difference. Live, the pair that really
        /// does have missing readings sits at 14,115 vs 14,536 — within 3 %.
        /// </summary>
        private bool SameRecordingRate
        {
            get
            {
                long a = Total(Main), b = Total(Mirror);
                if (a == 0 || b == 0) return false;
                return (double)Math.Min(a, b) / Math.Max(a, b) >= 0.75;
            }
        }

        private static long Total(int[] counts)
        {
            if (counts == null) return 0;
            long sum = 0;
            for (int i = 0; i < counts.Length; i++) sum += counts[i];
            return sum;
        }

        /// <summary>
        /// Readings one side is short BY inside segments where BOTH are recording.
        ///
        /// The outage rule alone cannot see an isolated missing reading — measured live, a point
        /// with 2,915 restorable readings had no segment-level outage at all and would have been
        /// reported as fine. Comparing counts only where both sides are active recovers most of
        /// that (1,817 of 2,915) while staying silent on the sparse points that produced the
        /// false alarms (0, matching what a restore would really copy).
        ///
        /// Still a LOWER BOUND, and still only an estimate: readings can differ inside a segment
        /// without the counts differing at all.
        /// </summary>
        private int Shortfall(int[] have, int[] lack)
        {
            if (have == null || lack == null || !DenseEnoughToCompare) return 0;
            int n = Math.Min(have.Length, lack.Length), sum = 0;
            for (int i = 0; i < n; i++)
                if (have[i] > 0 && lack[i] > 0 && have[i] > lack[i]) sum += have[i] - lack[i];
            return sum;
        }

        public bool InSync => Scanned && Error == null && !MissingEntirely
                              && EstMissingOnMirror == 0 && EstMissingOnMain == 0;

        public bool NeedsAttention => Scanned && (Error != null || MissingEntirely || !InSync);

        /// <summary>Estimated readings a restore would write for this point, both directions.</summary>
        public int Severity => EstMissingOnMirror + EstMissingOnMain;

        /// <summary>
        /// Display group, lowest first. What the user can ACT ON comes first: points with
        /// readings to restore, then read failures, then points configured on one server only,
        /// then unchecked, then healthy.
        ///
        /// One-sided points deliberately do NOT lead. On the test rig 201 of 273 points exist
        /// on one server only (a migration left them there), and putting them first buried
        /// every actual data gap under a wall of them. They are still prominent — second group,
        /// and counted separately in the summary line.
        /// </summary>
        public int SortRank =>
            !Scanned          ? 3 :
            Error != null     ? 1 :
            MissingEntirely   ? 2 :
            Severity > 0      ? 0 :
                                4;

        /// <summary>
        /// How much of everything recorded for this point ANYWHERE this server actually holds.
        ///
        /// The old measure was "fraction of segments holding at least one reading", and it
        /// destroyed the screen at long ranges: measured live on
        /// STAT6.TEMPRL_01_BHKW02_SCALE.F_CV over a year (600 segments of 14.6 h), every single
        /// segment held at least one reading on both servers, so it reported **100.0 % / 100.0 %**
        /// — as it did for nearly every other point, which is why every row in the list looked
        /// identical. The same data under this measure reads **99.5 % / 98.8 %**.
        ///
        /// Per segment the yardstick is the BETTER-served server, not an absolute expectation:
        /// this tool can only ever make one server match the other, so "complete" means "has
        /// what the other one has". That also survives deadband tags, where the recording rate
        /// legitimately varies — both servers see the same deadband, so the ratio stays ~1.
        /// </summary>
        private static double ShareOfBest(int[] mine, int[] other)
        {
            if (mine == null || mine.Length == 0) return -1;
            long sumMine = 0, sumBest = 0;
            int n = other != null ? Math.Min(mine.Length, other.Length) : mine.Length;
            for (int i = 0; i < n; i++)
            {
                int o = other != null ? other[i] : 0;
                sumMine += mine[i];
                sumBest += Math.Max(mine[i], o);
            }
            // Nothing recorded anywhere in this window: neither server is incomplete relative
            // to the other. Reporting 0 % would blame both for a plant-wide silence.
            if (sumBest == 0) return 0.0;
            return (double)sumMine / sumBest;
        }

        /// <summary>
        /// Per-segment share of the better-served server, 0..1, for drawing. A segment where
        /// neither server recorded anything is -1 ("nothing here on either side"), which the
        /// bars render grey rather than as this server's failure.
        /// </summary>
        public static double[] SegmentShare(int[] mine, int[] other)
        {
            if (mine == null || mine.Length == 0) return new double[0];
            int n = mine.Length;
            var share = new double[n];
            for (int i = 0; i < n; i++)
            {
                int o = (other != null && i < other.Length) ? other[i] : 0;
                int best = Math.Max(mine[i], o);
                share[i] = best == 0 ? -1.0 : (double)mine[i] / best;
            }
            return share;
        }

        /// <summary>Segment shares for this point's main server (see <see cref="SegmentShare"/>).</summary>
        public double[] MainShare   => SegmentShare(Main, Mirror);

        /// <summary>Segment shares for this point's mirror.</summary>
        public double[] MirrorShare => SegmentShare(Mirror, Main);

        /// <summary>
        /// Readings one server holds inside a REAL outage on the other — not merely inside a
        /// segment the other happens to have nothing in.
        ///
        /// Counting every one-sided segment fabricates alarms, and by a lot. Measured live
        /// (2026-08-04, 7 days, 17-minute segments): STAT6.TEMP_04_F02_SCALE.F_CV logs about
        /// once an hour on each server, so each reading lands in a different segment and almost
        /// every segment looks one-sided — the naive count claimed 228 readings to restore where
        /// SyncPlanner would copy exactly 0. Three more points behaved the same way (202/0,
        /// 145/0, 266/0). On a 273-point plant that turns into a screen of false alarms.
        ///
        /// So a run of consecutive one-sided segments only counts when it is longer than the
        /// lacking server's OWN typical spacing — i.e. longer than it would go quiet anyway.
        /// Spacing is taken from its own fill ratio, so no extra reads are needed. This stays a
        /// LOWER BOUND: a gap inside a populated segment is still invisible here, which is why
        /// the drill-down recomputes with SyncPlanner.
        /// </summary>
        private static int OneSided(int[] have, int[] lack)
        {
            if (have == null || lack == null) return 0;
            int n = Math.Min(have.Length, lack.Length);
            if (n == 0) return 0;

            int lackFilled = 0;
            for (int i = 0; i < n; i++) if (lack[i] > 0) lackFilled++;
            if (lackFilled == 0) return 0;   // holds nothing here at all — see MissingEntirely

            // How many segments this server normally goes between readings, from its own data.
            double spacing = (double)n / lackFilled;
            int minRun = (int)Math.Ceiling(spacing * 3.0);
            if (minRun < 2) minRun = 2;

            int sum = 0, runStart = -1, runSum = 0;
            for (int i = 0; i <= n; i++)
            {
                bool oneSided = i < n && have[i] > 0 && lack[i] == 0;
                if (oneSided)
                {
                    if (runStart < 0) { runStart = i; runSum = 0; }
                    runSum += have[i];
                }
                else if (runStart >= 0)
                {
                    if (i - runStart >= minRun) sum += runSum;
                    runStart = -1;
                }
            }
            return sum;
        }
    }

    /// <summary>Outcome of one scan pass.</summary>
    public sealed class CoverageScan
    {
        public List<PointCoverage> Points = new List<PointCoverage>();
        public DateTime From, To;
        public int Buckets;

        /// <summary>
        /// How much time one bar segment covers — the resolution of the whole estimate.
        ///
        /// This is not a detail: a gap SHORTER than one segment can disappear entirely,
        /// because the segment still holds readings on both sides. Measured on the real rig,
        /// a 13-day window flagged 251 points and the same servers over 365 days flagged only
        /// 201 — the ~50 real gaps were shorter than a 22-hour segment. The overview must
        /// therefore say what its resolution is, and the exact numbers stay in the drill-down.
        /// </summary>
        public TimeSpan BucketSpan =>
            (Buckets > 0 && To > From)
                ? TimeSpan.FromTicks((To - From).Ticks / Buckets)
                : TimeSpan.Zero;

        /// <summary>True when the time budget ran out and some points were left unscanned.</summary>
        public bool Truncated;
        public double Seconds;

        public int ScannedCount   => Points.Count(p => p.Scanned);
        public int UnscannedCount => Points.Count(p => !p.Scanned);
    }

    /// <summary>
    /// Builds the all-points overview: for every measurement point on both servers, how much of
    /// the chosen period each side actually holds.
    ///
    /// Cost is the whole point. A raw read of ~78 points over a long window means millions of
    /// timestamps; instead each chunk of tags is asked for per-bucket COUNTS in one round-trip
    /// (<see cref="HistorianDataService.ReadBucketCounts"/>).
    ///
    /// The numbers here are ESTIMATES and must be presented as such:
    ///  * a bucket counts as "has data" from a single reading, so coverage reads optimistically
    ///    compared with the per-tag gap rule, and
    ///  * one-sided buckets are an OUTAGE-level estimate. That matches how the planner treats
    ///    independently recording collectors, but it cannot see an isolated missing reading
    ///    inside a populated bucket.
    /// Opening a point re-computes the exact number with <see cref="SyncPlanner"/>, which stays
    /// the only source of truth for anything that gets written.
    /// </summary>
    public static class CoverageScanner
    {
        /// <summary>Tags per query. Small enough to stay responsive, large enough that ~78
        /// points need only a handful of round-trips.</summary>
        public const int TagsPerQuery = 20;

        /// <param name="onMain">Points configured on the main server (from the tag browse).</param>
        /// <param name="onMirror">Points configured on the mirror.</param>
        public static CoverageScan Scan(
            HistorianDataService data,
            ServerConnection main, ServerConnection mirror,
            IList<string> tags, ISet<string> onMain, ISet<string> onMirror,
            DateTime from, DateTime to,
            int buckets, TimeSpan budget, CancellationToken token,
            Action<int, int> progress = null)
        {
            var scan = new CoverageScan { From = from, To = to, Buckets = buckets };
            if (tags == null || tags.Count == 0 || to <= from) return scan;

            var byTag = new Dictionary<string, PointCoverage>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in tags)
            {
                var pc = new PointCoverage
                {
                    Tag = t,
                    OnMain   = onMain   == null || onMain.Contains(t),
                    OnMirror = onMirror == null || onMirror.Contains(t)
                };
                byTag[t] = pc;
                scan.Points.Add(pc);
            }

            var clock = Stopwatch.StartNew();
            var lastChunk = TimeSpan.Zero;   // cost of the previous chunk, to predict the next
            int done = 0;

            for (int i = 0; i < tags.Count; i += TagsPerQuery)
            {
                token.ThrowIfCancellationRequested();

                // Budget check BEFORE starting a chunk — a chunk is atomic, so the only way to
                // honour a limit is to decline to start one. Checking "have I run out?" alone
                // overshoots by a whole chunk (measured: 13.7 s against a 10 s budget on a
                // 1-year window), so also decline when the last chunk's cost says the next one
                // would not fit. The remaining points stay marked "not scanned" and the UI says
                // so — never silently truncated.
                if (budget > TimeSpan.Zero &&
                    (clock.Elapsed >= budget || clock.Elapsed + lastChunk > budget))
                {
                    scan.Truncated = true;
                    break;
                }
                var chunkClock = Stopwatch.StartNew();

                var chunk = tags.Skip(i).Take(TagsPerQuery).ToList();
                // Ask each server only for the points it actually has. Cheaper, and it keeps a
                // point that exists on one side only from muddying the other side's response.
                var chunkMain   = chunk.Where(t => byTag[t].OnMain).ToList();
                var chunkMirror = chunk.Where(t => byTag[t].OnMirror).ToList();

                Dictionary<string, int[]> mainCounts = null, mirrorCounts = null;
                string error = null;

                try
                {
                    if (main   != null && chunkMain.Count   > 0)
                        mainCounts = data.ReadBucketCounts(main, chunkMain, from, to, buckets);
                    token.ThrowIfCancellationRequested();
                    if (mirror != null && chunkMirror.Count > 0)
                        mirrorCounts = data.ReadBucketCounts(mirror, chunkMirror, from, to, buckets);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    // A chunk that fails must not kill the whole overview — the other points
                    // are still worth showing, and the failed rows say what happened.
                    error = ex.Message;
                }

                foreach (var tag in chunk)
                {
                    var pc = byTag[tag];
                    if (error != null)
                    {
                        pc.Error = error;
                        pc.Scanned = true;   // scanned and failed, not "not reached"
                    }
                    else
                    {
                        int[] m, s;
                        if (mainCounts   != null && mainCounts.TryGetValue(tag, out m))   pc.Main   = m;
                        if (mirrorCounts != null && mirrorCounts.TryGetValue(tag, out s)) pc.Mirror = s;
                        pc.Scanned = true;
                    }
                    done++;
                }

                chunkClock.Stop();
                lastChunk = chunkClock.Elapsed;
                if (progress != null) progress(done, tags.Count);
            }

            clock.Stop();
            scan.Seconds = clock.Elapsed.TotalSeconds;

            // Actionable first — the problem finds the user instead of the user hunting for it.
            scan.Points = scan.Points
                .OrderBy(p => p.SortRank)
                .ThenByDescending(p => p.Severity)
                .ThenBy(p => p.Tag, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return scan;
        }
    }
}
