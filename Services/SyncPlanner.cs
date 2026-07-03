using HistorianSyncTool.Models;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;

namespace HistorianSyncTool.Services
{
    /// <summary>
    /// The planned copy set for one direction of one tag, plus the statistics that
    /// justify it (shown in the UI so the rule is never a black box).
    /// </summary>
    public class SyncPlan
    {
        /// <summary>Fraction of source samples whose whole second exists on the target.</summary>
        public double MatchRate { get; set; }

        /// <summary>
        /// True when both servers hold the same timestamp stream (same-source data such
        /// as HistSync or samples this tool wrote). Exact-second diff is used then —
        /// it catches isolated missing samples that outage detection cannot.
        /// </summary>
        public bool StreamsAligned { get; set; }

        /// <summary>True when the exact-second diff was used (aligned streams or too little data for stats).</summary>
        public bool UsedExactDiff { get; set; }

        /// <summary>The tag's cadence: p90 of the source side's sample intervals.</summary>
        public TimeSpan Cadence { get; set; }

        /// <summary>Silence on the target longer than this counts as an outage.</summary>
        public TimeSpan OutageThreshold { get; set; }

        /// <summary>Detected outage windows on the target (independent-stream mode only).</summary>
        public List<TimeRange> TargetOutages { get; set; } = new List<TimeRange>();

        /// <summary>Source sample times the backfill should write (sorted).</summary>
        public List<DateTime> ToCopy { get; set; } = new List<DateTime>();
    }

    /// <summary>
    /// Decides what a backfill would copy for one tag+direction. Pure logic, no UI or
    /// Proficy dependencies — the ONE implementation shared by the backfill itself, the
    /// preview dialogs, the timeline's copy-candidate strip and the missing-data table,
    /// so every surface shows the same numbers.
    ///
    /// Two modes, chosen automatically per tag:
    ///  - ALIGNED (same-source data): both servers carry the same timestamps → copy every
    ///    source sample whose whole second is absent on the target (catches isolated misses).
    ///  - INDEPENDENT (redundant collectors sampling on their own clocks): timestamps never
    ///    align, so the exact diff reports thousands of phantom "missing" samples that are
    ///    really the same values a few seconds apart (measured on real Genthin data:
    ///    33 376 of 47 474 flagged, 98 genuinely missing). Here only real OUTAGES on the
    ///    target — silence longer than the tag's own cadence allows — are filled.
    /// </summary>
    public static class SyncPlanner
    {
        /// <summary>Exact-second match rate at/above which streams count as aligned.</summary>
        public const double AlignedMatchRateThreshold = 0.90;

        /// <summary>Below this many samples per side interval statistics are meaningless.</summary>
        public const int MinSamplesForStats = 20;

        /// <summary>Shared gap-rule settings (floor + multiplier) from app.config.</summary>
        public static (TimeSpan Floor, double Multiplier) ReadConfig()
        {
            int seconds; double mult;
            TimeSpan floor = int.TryParse(
                    ConfigurationManager.AppSettings["MinimumGapSeconds"], out seconds) && seconds > 0
                ? TimeSpan.FromSeconds(seconds)
                : TimeSpan.FromSeconds(120);
            if (!double.TryParse(ConfigurationManager.AppSettings["GapThresholdMultiplier"],
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out mult) || mult <= 0)
                mult = 2.0;
            return (floor, mult);
        }

        /// <summary>Plan using the app.config gap-rule settings.</summary>
        public static SyncPlan PlanWithConfig(
            List<DateTime> sourceTimes, List<DateTime> targetTimes,
            DateTime evalFrom, DateTime evalTo)
        {
            var cfg = ReadConfig();
            return Plan(sourceTimes, targetTimes, evalFrom, evalTo, cfg.Floor, cfg.Multiplier);
        }

        /// <summary>
        /// The per-tag gap rule for a sample stream: silence longer than
        /// max(p90(intervals) × multiplier, floor) counts as missing data.
        /// </summary>
        public static TimeSpan GapRule(List<DateTime> sortedTimes, TimeSpan floor, double multiplier)
        {
            TimeSpan p90 = IntervalBuilder.PercentileInterval(sortedTimes, 0.90);
            return TimeSpan.FromTicks(Math.Max((long)(p90.Ticks * multiplier), floor.Ticks));
        }

        /// <summary>
        /// Merged runs of a plan's copy set — feeds the amber strip on the main timeline
        /// and the preview dialogs' mini timelines, so what's drawn IS what gets copied.
        /// </summary>
        public static List<CopyableSegment> ToSegments(
            SyncPlan plan, bool toSecondary, string description = null)
        {
            if (plan == null || plan.ToCopy.Count == 0) return new List<CopyableSegment>();
            TimeSpan mergeGap = plan.OutageThreshold > TimeSpan.Zero
                ? plan.OutageThreshold : TimeSpan.FromMinutes(10);
            return IntervalBuilder.MergePoints(plan.ToCopy, mergeGap, TimeSpan.FromSeconds(1))
                .Select(m => new CopyableSegment
                {
                    Range = m.Range,
                    ToSecondary = toSecondary,
                    SampleCount = m.Count,
                    Description = description
                })
                .ToList();
        }

        public static SyncPlan Plan(
            List<DateTime> sourceTimes, List<DateTime> targetTimes,
            DateTime evalFrom, DateTime evalTo,
            TimeSpan minGapFloor, double thresholdMultiplier)
        {
            var plan = new SyncPlan();
            sourceTimes = sourceTimes ?? new List<DateTime>();
            targetTimes = targetTimes ?? new List<DateTime>();

            var src = sourceTimes.Where(t => t >= evalFrom && t <= evalTo).OrderBy(t => t).ToList();
            var tgt = targetTimes.Where(t => t >= evalFrom && t <= evalTo).OrderBy(t => t).ToList();
            if (src.Count == 0) return plan;

            var tgtSeconds = new HashSet<long>(tgt.Select(SampleFilter.ToSecondTicks));

            int matched = src.Count(t => tgtSeconds.Contains(SampleFilter.ToSecondTicks(t)));
            plan.MatchRate = (double)matched / src.Count;
            plan.StreamsAligned = plan.MatchRate >= AlignedMatchRateThreshold;

            plan.Cadence = IntervalBuilder.PercentileInterval(src, 0.90);
            plan.OutageThreshold = GapRule(src, minGapFloor, thresholdMultiplier);

            // Exact diff when the streams demonstrably align, or when either side is too
            // small to derive honest statistics (bounded worst case, and an empty target
            // means everything is one big outage anyway — both modes agree there).
            plan.UsedExactDiff = plan.StreamsAligned
                || src.Count < MinSamplesForStats
                || tgt.Count < MinSamplesForStats;

            if (plan.UsedExactDiff)
            {
                plan.ToCopy = src
                    .Where(t => !tgtSeconds.Contains(SampleFilter.ToSecondTicks(t)))
                    .ToList();
                return plan;
            }

            // ── Independent streams: fill only real target outages ────────────────────
            plan.TargetOutages = DetectOutages(tgt, evalFrom, evalTo, plan.OutageThreshold);
            if (plan.TargetOutages.Count == 0) return plan;

            // Source samples inside an outage window whose second the target lacks
            // (the second-check keeps re-runs idempotent after a partial fill).
            int oi = 0;
            foreach (var t in src)
            {
                while (oi < plan.TargetOutages.Count && plan.TargetOutages[oi].End < t) oi++;
                if (oi >= plan.TargetOutages.Count) break;
                var o = plan.TargetOutages[oi];
                if (t >= o.Start && t <= o.End &&
                    !tgtSeconds.Contains(SampleFilter.ToSecondTicks(t)))
                {
                    plan.ToCopy.Add(t);
                }
            }
            return plan;
        }

        /// <summary>
        /// Stretches of [evalFrom, evalTo] where <paramref name="sortedTimes"/> is silent
        /// for longer than <paramref name="threshold"/> — leading, internal and trailing.
        /// </summary>
        public static List<TimeRange> DetectOutages(
            List<DateTime> sortedTimes, DateTime evalFrom, DateTime evalTo, TimeSpan threshold)
        {
            var outages = new List<TimeRange>();
            if (sortedTimes == null || sortedTimes.Count == 0)
            {
                if (evalTo > evalFrom) outages.Add(new TimeRange(evalFrom, evalTo));
                return outages;
            }

            if (sortedTimes[0] - evalFrom > threshold)
                outages.Add(new TimeRange(evalFrom, sortedTimes[0]));

            for (int i = 1; i < sortedTimes.Count; i++)
            {
                if (sortedTimes[i] - sortedTimes[i - 1] > threshold)
                    outages.Add(new TimeRange(sortedTimes[i - 1], sortedTimes[i]));
            }

            if (evalTo - sortedTimes[sortedTimes.Count - 1] > threshold)
                outages.Add(new TimeRange(sortedTimes[sortedTimes.Count - 1], evalTo));

            return outages;
        }
    }
}
