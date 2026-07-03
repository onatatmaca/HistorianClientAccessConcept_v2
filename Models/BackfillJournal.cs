using System;
using System.Collections.Generic;
using System.Linq;
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

    /// <summary>Per-tag row of a persisted run report (mirrors TagBackfillResult).</summary>
    [DataContract]
    public class JournalTagReport
    {
        [DataMember] public string TagName { get; set; }
        [DataMember] public int BatchesAttempted { get; set; }
        [DataMember] public int BatchesSucceeded { get; set; }
        [DataMember] public int BatchesFailed { get; set; }
        [DataMember] public int SamplesWritten { get; set; }
        [DataMember] public List<string> Errors { get; set; }
    }

    /// <summary>
    /// One direction's full run report, persisted with the journal so Backfill History
    /// can reopen the EXACT results dialog later (incl. batches, errors and timing).
    /// Times are stored as raw local ticks — a default DateTime would crash
    /// DataContractJsonSerializer in UTC+ timezones (see RevertedLocal below).
    /// </summary>
    [DataContract]
    public class JournalDirectionReport
    {
        [DataMember] public string SourceServer { get; set; }
        [DataMember] public string TargetServer { get; set; }
        [DataMember] public string DirectionLabel { get; set; }
        [DataMember] public int GapsFound { get; set; }
        [DataMember] public long StartedTicks { get; set; }
        [DataMember] public long CompletedTicks { get; set; }
        [DataMember] public List<string> Errors { get; set; }
        [DataMember] public List<JournalTagReport> Tags { get; set; }

        public static JournalDirectionReport From(SyncRunReport r) => new JournalDirectionReport
        {
            SourceServer   = r.SourceServer,
            TargetServer   = r.TargetServer,
            DirectionLabel = r.DirectionLabel,
            GapsFound      = r.GapsFound,
            StartedTicks   = r.StartedAt.Ticks,
            CompletedTicks = r.CompletedAt.Ticks,
            Errors         = new List<string>(r.Errors ?? new List<string>()),
            Tags = (r.TagResults ?? new List<TagBackfillResult>()).Select(t => new JournalTagReport
            {
                TagName          = t.TagName,
                BatchesAttempted = t.BatchesAttempted,
                BatchesSucceeded = t.BatchesSucceeded,
                BatchesFailed    = t.BatchesFailed,
                SamplesWritten   = t.SamplesWritten,
                Errors           = new List<string>(t.Errors ?? new List<string>())
            }).ToList()
        };

        public SyncRunReport ToReport()
        {
            var r = new SyncRunReport
            {
                SourceServer   = SourceServer,
                TargetServer   = TargetServer,
                DirectionLabel = DirectionLabel,
                GapsFound      = GapsFound,
                StartedAt      = new DateTime(StartedTicks),
                CompletedAt    = new DateTime(CompletedTicks),
                Errors         = new List<string>(Errors ?? new List<string>())
            };
            foreach (var t in Tags ?? new List<JournalTagReport>())
                r.TagResults.Add(new TagBackfillResult
                {
                    TagName          = t.TagName,
                    BatchesAttempted = t.BatchesAttempted,
                    BatchesSucceeded = t.BatchesSucceeded,
                    BatchesFailed    = t.BatchesFailed,
                    SamplesWritten   = t.SamplesWritten,
                    Errors           = new List<string>(t.Errors ?? new List<string>())
                });
            return r;
        }
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
        // Nullable for the same serializer reason as RevertedLocal below, and so journal
        // files written before this field existed still load (they show "—" as duration).
        [DataMember] public DateTime? CompletedLocal { get; set; }
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

        /// <summary>
        /// The COMPLETE report of the run this entry belongs to — for a bidirectional
        /// Preview&amp;Backfill both directions are stored on BOTH entries, so clicking
        /// either in Backfill History reopens the exact combined results dialog the
        /// user saw at the end of the run. Null on entries from older versions
        /// (history falls back to the samples-only reconstruction).
        /// </summary>
        [DataMember] public List<JournalDirectionReport> ReportDirections { get; set; }

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
