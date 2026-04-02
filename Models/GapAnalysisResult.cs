using System;
using System.Collections.Generic;

namespace HistorianSyncTool.Models
{
    public class GapAnalysisResult
    {
        public bool HasData { get; set; }
        public bool HasGaps => Gaps != null && Gaps.Count > 0;
        public int TotalSamples { get; set; }
        public DateTime FirstTimestamp { get; set; }
        public DateTime LastTimestamp { get; set; }
        public TimeSpan ExpectedInterval { get; set; }
        public TimeSpan LargestGapDuration { get; set; }
        public TimeSpan TotalMissingDuration { get; set; }

        /// <summary>0.0 – 1.0. Percentage of the time range covered by actual samples.</summary>
        public double CoverageRatio { get; set; }

        public List<DateTime> SampleTimes { get; set; } = new List<DateTime>();
        public List<GapWindow> Gaps { get; set; } = new List<GapWindow>();

        public string ServerLabel { get; set; }
    }
}
