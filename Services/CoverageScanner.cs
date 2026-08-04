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

        /// <summary>False when the scan ran out of its time budget before reaching this point.</summary>
        public bool Scanned;

        /// <summary>Set when this point could not be read (message shown on the row).</summary>
        public string Error;

        public int Buckets => Main != null ? Main.Length : (Mirror != null ? Mirror.Length : 0);

        public double MainCoverage   => Fraction(Main);
        public double MirrorCoverage => Fraction(Mirror);

        /// <summary>Estimated readings the mirror is missing (buckets where only the main server has data).</summary>
        public int EstMissingOnMirror => OneSided(Main, Mirror);

        /// <summary>Estimated readings the main server is missing.</summary>
        public int EstMissingOnMain => OneSided(Mirror, Main);

        public bool InSync => Scanned && Error == null
                              && EstMissingOnMirror == 0 && EstMissingOnMain == 0;

        /// <summary>Worst first: most estimated missing readings at the top.</summary>
        public int Severity => EstMissingOnMirror + EstMissingOnMain;

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

        public static CoverageScan Scan(
            HistorianDataService data,
            ServerConnection main, ServerConnection mirror,
            IList<string> tags, DateTime from, DateTime to,
            int buckets, TimeSpan budget, CancellationToken token,
            Action<int, int> progress = null)
        {
            var scan = new CoverageScan { From = from, To = to, Buckets = buckets };
            if (tags == null || tags.Count == 0 || to <= from) return scan;

            var byTag = new Dictionary<string, PointCoverage>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in tags)
            {
                var pc = new PointCoverage { Tag = t };
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
                Dictionary<string, int[]> mainCounts = null, mirrorCounts = null;
                string error = null;

                try
                {
                    if (main   != null) mainCounts   = data.ReadBucketCounts(main,   chunk, from, to, buckets);
                    token.ThrowIfCancellationRequested();
                    if (mirror != null) mirrorCounts = data.ReadBucketCounts(mirror, chunk, from, to, buckets);
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

            // Worst first — the problem finds the user instead of the user hunting for it.
            scan.Points = scan.Points
                .OrderByDescending(p => p.Severity)
                .ThenBy(p => p.Tag, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return scan;
        }
    }
}
