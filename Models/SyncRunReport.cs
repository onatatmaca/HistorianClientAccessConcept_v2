using System;
using System.Collections.Generic;

namespace HistorianSyncTool.Models
{
    public class SyncRunReport
    {
        public DateTime StartedAt { get; set; }
        public DateTime CompletedAt { get; set; }
        public string SourceServer { get; set; }
        public string TargetServer { get; set; }
        public string SourceTag { get; set; }
        public string TargetTag { get; set; }

        public int GapsFound { get; set; }
        public int BatchesAttempted { get; set; }
        public int BatchesSucceeded { get; set; }
        public int BatchesFailed { get; set; }
        public int SamplesWritten { get; set; }

        public List<string> Errors { get; set; } = new List<string>();

        public TimeSpan Duration => CompletedAt - StartedAt;
    }
}
