using System;
using System.Collections.Generic;

namespace HistorianSyncTool.Models
{
    public class GapWindow
    {
        public DateTime Start { get; set; }
        public DateTime End { get; set; }
        public TimeSpan Duration => End - Start;
        public List<GapBatch> Batches { get; set; } = new List<GapBatch>();
    }
}
