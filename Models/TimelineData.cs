using System;
using System.Collections.Generic;

namespace HistorianSyncTool.Models
{
    /// <summary>A half-open [Start, End) span of time on the timeline.</summary>
    public struct TimeRange
    {
        public DateTime Start;
        public DateTime End;

        public TimeRange(DateTime start, DateTime end)
        {
            Start = start;
            End = end;
        }

        public TimeSpan Duration => End - Start;
    }

    /// <summary>
    /// A merged run of samples that exist on one server but not the other —
    /// what a backfill in that direction would copy.
    /// </summary>
    public class CopyableSegment
    {
        public TimeRange Range;
        public bool ToSecondary;     // true = Primary has these, Secondary lacks them
        public int SampleCount;

        /// <summary>Optional tooltip direction text; when empty the timeline uses the
        /// standard Primary/Secondary wording based on ToSecondary.</summary>
        public string Description;
    }

    /// <summary>
    /// Everything the GapTimeline control needs to draw one server's track.
    /// CoverageRatio &lt; 0 means "nothing analyzed yet" (track renders neutral).
    /// </summary>
    public class TimelineTrackData
    {
        public string Label = "";

        /// <summary>Name used inside tooltips ("Primary", "Target (Secondary)", …).
        /// Defaults to Primary/Secondary by track position when empty.</summary>
        public string TooltipName;

        public double CoverageRatio = -1;
        public bool HasData;

        /// <summary>True when the opposite server's data was available to check
        /// which gaps it could fill — affects tooltip wording only.</summary>
        public bool FeasibilityKnown;

        /// <summary>Gaps the OTHER server has data for → red, copyable.</summary>
        public List<TimeRange> FillableGaps = new List<TimeRange>();

        /// <summary>Gaps neither server has data for → gray, nothing to copy.</summary>
        public List<TimeRange> UnfillableGaps = new List<TimeRange>();

        /// <summary>Ranges this tool wrote via backfill (journal, non-reverted).</summary>
        public List<TimeRange> Backfilled = new List<TimeRange>();

        /// <summary>Shown centered when CoverageRatio &lt; 0 (e.g. "not connected").</summary>
        public string EmptyText = "not analyzed";

        /// <summary>
        /// Per-segment share of the better-served server, 0..1, left to right; -1 where NEITHER
        /// server recorded anything. When present the track is painted from this — each segment
        /// green in proportion to what it holds, the remainder red (the other server has it) or
        /// grey (-1, nothing to copy).
        ///
        /// This exists because the old "fill green, then paint gaps over it" could not tell the
        /// truth at long ranges. A segment is hours wide there, so it was green if it held ONE
        /// reading and red if it lacked ONE — and at a one-year zoom both are nearly always
        /// true. That produced a track labelled 100 % and painted 90 % red, and a list where
        /// every point looked identical. Proportional painting makes the label and the picture
        /// the same quantity by construction, so "0 % but green" cannot happen.
        /// </summary>
        public double[] SegmentShare;
    }
}
