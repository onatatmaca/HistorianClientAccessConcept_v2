using System;

namespace HistorianSyncTool.Models
{
    public class GapBatch
    {
        public DateTime Start { get; set; }
        public DateTime End { get; set; }
        public TimeSpan Duration => End - Start;
        public bool CanBackfill { get; set; }
        public bool Verified { get; set; }
        public int SamplesWritten { get; set; }
    }
}
