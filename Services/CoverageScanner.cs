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

        public double MainCoverage   => Fraction(Main);
        public double MirrorCoverage => Fraction(Mirror);

        /// <summary>
        /// Estimated readings the mirror is missing (buckets where only the main server has
        /// data). Zero when the point does not exist on the mirror at all — a restore could
        /// not write them, and reporting a number we cannot deliver would be a lie.
        /// </summary>
        public int EstMissingOnMirror => MissingEntirely ? 0 : OneSided(Main, Mirror);

        /// <summary>Estimated readings the main server is missing.</summary>
        public int EstMissingOnMain => MissingEntirely ? 0 : OneSided(Mirror, Main);

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

        private static double Fraction(int[] counts)
        {
            if (counts == null || counts.Length == 0) return -1;
            int with = 0;
            for (int i = 0; i < counts.Length; i++) if (counts[i] > 0) with++;
            return (double)with / counts.Length;
        }

        private static int OneSided(int[] have, int[] lack)
        {
            if (have == null || lack == null) return 0;
            int n = Math.Min(have.Length, lack.Length), sum = 0;
            for (int i = 0; i < n; i++) if (have[i] > 0 && lack[i] == 0) sum += have[i];
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
            int done = 0;

            for (int i = 0; i < tags.Count; i += TagsPerQuery)
            {
                token.ThrowIfCancellationRequested();

                // Budget check BEFORE starting a chunk: the remaining points stay marked
                // "not scanned" and the UI says so — never silently truncated.
                if (budget > TimeSpan.Zero && clock.Elapsed >= budget)
                {
                    scan.Truncated = true;
                    break;
                }

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
