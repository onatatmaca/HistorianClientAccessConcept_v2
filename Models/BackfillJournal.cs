using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace HistorianSyncTool.Models
{
    /// <summary>The exact timestamps written for one tag during a backfill run.</summary>
    [DataContract]
    public class BackfillJournalTag
    {
        [DataMember] public string TagName { get; set; }
        /// <summary>DateTime.Ticks of every successfully written + verified sample.</summary>
        [DataMember] public long[] Ticks { get; set; }
    }

    /// <summary>
    /// A persisted record of one backfill run, used to revert it later. Reverting
    /// deletes exactly the timestamps listed here (via IData.Delete), so pre-existing
    /// samples on the target are never affected.
    /// </summary>
    [DataContract]
    public class BackfillJournalEntry
    {
        [DataMember] public string Id { get; set; }
        [DataMember] public DateTime RunLocal { get; set; }
        [DataMember] public string Mode { get; set; }           // "Manual" / "Scheduled"
        [DataMember] public string SourceLabel { get; set; }    // "Primary" / "Secondary"
        [DataMember] public string SourceHost { get; set; }
        [DataMember] public string TargetLabel { get; set; }    // where data was written
        [DataMember] public string TargetHost { get; set; }     // hostname, for reconnect matching
        [DataMember] public bool Reverted { get; set; }
        // Nullable: an un-reverted entry has no revert date. (A default DateTime.MinValue
        // here crashes DataContractJsonSerializer in any timezone ahead of UTC, because
        // converting 0001-01-01 local to UTC underflows DateTime.MinValue.)
        [DataMember] public DateTime? RevertedLocal { get; set; }
        [DataMember] public List<BackfillJournalTag> Tags { get; set; } = new List<BackfillJournalTag>();

        public int TagCount => Tags?.Count ?? 0;

        public int TotalSamples
        {
            get
            {
                int n = 0;
                if (Tags != null)
                    foreach (var t in Tags) n += t.Ticks?.Length ?? 0;
                return n;
            }
        }
    }
}
