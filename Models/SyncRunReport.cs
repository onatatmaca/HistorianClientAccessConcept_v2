using System;
using System.Collections.Generic;
using System.Linq;

namespace HistorianSyncTool.Models
{
    public class TagBackfillResult
    {
        public string TagName { get; set; }
        public int BatchesAttempted { get; set; }
        public int BatchesSucceeded { get; set; }
        public int BatchesFailed { get; set; }
        public int SamplesWritten { get; set; }
        public List<string> Errors { get; set; } = new List<string>();
    }

    public class SyncRunReport
    {
        public DateTime StartedAt { get; set; }
        public DateTime CompletedAt { get; set; }
        public string SourceServer { get; set; }
        public string TargetServer { get; set; }

        /// <summary>Short direction tag (e.g. "P→S") used when several runs share one report dialog.</summary>
        public string DirectionLabel { get; set; }

        public int GapsFound { get; set; }

        public List<TagBackfillResult> TagResults { get; set; } = new List<TagBackfillResult>();

        // Aggregate properties across all tags
        public int BatchesAttempted  => TagResults.Sum(t => t.BatchesAttempted);
        public int BatchesSucceeded  => TagResults.Sum(t => t.BatchesSucceeded);
        public int BatchesFailed     => TagResults.Sum(t => t.BatchesFailed);
        public int SamplesWritten    => TagResults.Sum(t => t.SamplesWritten);

        public List<string> Errors { get; set; } = new List<string>();

        public TimeSpan Duration => CompletedAt - StartedAt;
    }
}
