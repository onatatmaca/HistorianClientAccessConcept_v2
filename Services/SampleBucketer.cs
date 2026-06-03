using System;
using System.Collections.Generic;

namespace HistorianSyncTool.Services
{
    /// <summary>
    /// Groups a sorted list of samples into buckets where consecutive samples within
    /// `bucketSize` of the bucket-start belong together. Bucket-start resets to the
    /// next sample's time when the spacing exceeds bucketSize — so this is not a
    /// fixed-clock bucket. Used by the direct-comparison backfill to chunk missing
    /// samples into per-write batches that match the configured BatchSize.
    /// Pure logic — no Proficy or UI dependencies, fully testable.
    /// </summary>
    public static class SampleBucketer
    {
        public static List<List<(DateTime Time, float Value, double Quality)>> GroupByBucket(
            List<(DateTime Time, float Value, double Quality)> sorted, TimeSpan bucketSize)
        {
            var result = new List<List<(DateTime, float, double)>>();
            if (sorted == null || sorted.Count == 0) return result;

            var current = new List<(DateTime, float, double)> { sorted[0] };
            DateTime bucketStart = sorted[0].Time;

            for (int i = 1; i < sorted.Count; i++)
            {
                if (sorted[i].Time - bucketStart < bucketSize)
                {
                    current.Add(sorted[i]);
                }
                else
                {
                    result.Add(current);
                    current = new List<(DateTime, float, double)> { sorted[i] };
                    bucketStart = sorted[i].Time;
                }
            }

            if (current.Count > 0) result.Add(current);
            return result;
        }
    }
}
