using System.Collections.Generic;

namespace HistorianSyncTool.Models
{
    public class ServerStats
    {
        public string ServerName { get; set; }
        public double TotalTags { get; set; }
        public long TotalSamples { get; set; }
        public int CollectorCount { get; set; }
        public List<string> CollectorNames { get; set; } = new List<string>();

        public override string ToString() =>
            $"Tags: {TotalTags}  Samples: {TotalSamples}  Collectors: {CollectorCount}";
    }
}
