using HistorianSyncTool.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HistorianSyncTool.Services
{
    /// <summary>
    /// Pure gap detection and batch planning. No UI or API dependencies.
    /// </summary>
    public class GapAnalysisService
    {
        private readonly TimeSpan _batchSize;
        private const double GapThresholdMultiplier = 1.5;

        public GapAnalysisService(TimeSpan? batchSize = null)
        {
            _batchSize = batchSize ?? TimeSpan.FromMinutes(10);
        }

        /// <summary>
        /// Analyze a sorted list of sample timestamps, detect gaps, and split them into batches.
        /// </summary>
        public GapAnalysisResult Analyze(List<DateTime> sampleTimes, string serverLabel)
        {
            var result = new GapAnalysisResult { ServerLabel = serverLabel };

            if (sampleTimes == null || sampleTimes.Count < 2)
            {
                result.HasData = sampleTimes != null && sampleTimes.Count > 0;
                result.SampleTimes = sampleTimes ?? new List<DateTime>();
                result.CoverageRatio = result.HasData ? 1.0 : 0.0;
                return result;
            }

            result.HasData = true;
            result.TotalSamples = sampleTimes.Count;
            result.FirstTimestamp = sampleTimes.First();
            result.LastTimestamp = sampleTimes.Last();
            result.SampleTimes = sampleTimes;

            // Compute all consecutive deltas
            var deltas = new List<TimeSpan>();
            for (int i = 1; i < sampleTimes.Count; i++)
                deltas.Add(sampleTimes[i] - sampleTimes[i - 1]);

            result.ExpectedInterval = Median(deltas);
            TimeSpan gapThreshold = TimeSpan.FromTicks((long)(result.ExpectedInterval.Ticks * GapThresholdMultiplier));

            // Detect gaps
            TimeSpan totalMissing = TimeSpan.Zero;
            for (int i = 1; i < sampleTimes.Count; i++)
            {
                TimeSpan delta = sampleTimes[i] - sampleTimes[i - 1];
                if (delta > gapThreshold)
                {
                    var window = new GapWindow
                    {
                        Start = sampleTimes[i - 1],
                        End   = sampleTimes[i]
                    };
                    window.Batches = PlanBatches(window.Start, window.End);
                    result.Gaps.Add(window);

                    TimeSpan gapDuration = delta - result.ExpectedInterval;
                    totalMissing += gapDuration;
                    if (gapDuration > result.LargestGapDuration)
                        result.LargestGapDuration = gapDuration;
                }
            }

            result.TotalMissingDuration = totalMissing;

            TimeSpan totalSpan = result.LastTimestamp - result.FirstTimestamp;
            result.CoverageRatio = totalSpan.Ticks > 0
                ? Math.Max(0, (totalSpan - totalMissing).Ticks / (double)totalSpan.Ticks)
                : 1.0;

            return result;
        }

        /// <summary>
        /// Mark each batch CanBackfill = true if the opposing server has at least one sample
        /// in the half-open interval [batch.Start, batch.End).
        /// </summary>
        public void MarkBackfillFeasibility(GapAnalysisResult result, List<DateTime> otherServerSamples)
        {
            if (result?.Gaps == null || otherServerSamples == null) return;

            var sorted = new SortedSet<DateTime>(otherServerSamples);
            foreach (var gap in result.Gaps)
                foreach (var batch in gap.Batches)
                    batch.CanBackfill = sorted.Any(t => t >= batch.Start && t < batch.End);
        }

        // ── Private Helpers ────────────────────────────────────────────────────────

        private List<GapBatch> PlanBatches(DateTime start, DateTime end)
        {
            var batches = new List<GapBatch>();
            DateTime cursor = start;
            while (cursor < end)
            {
                DateTime batchEnd = cursor + _batchSize < end ? cursor + _batchSize : end;
                batches.Add(new GapBatch { Start = cursor, End = batchEnd });
                cursor = batchEnd;
            }
            return batches;
        }

        private static TimeSpan Median(List<TimeSpan> spans)
        {
            if (spans.Count == 0) return TimeSpan.Zero;
            var sorted = spans.OrderBy(s => s.Ticks).ToList();
            int mid = sorted.Count / 2;
            return sorted.Count % 2 == 0
                ? TimeSpan.FromTicks((sorted[mid - 1].Ticks + sorted[mid].Ticks) / 2)
                : sorted[mid];
        }
    }
}
