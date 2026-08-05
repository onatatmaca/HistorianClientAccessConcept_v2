using HistorianSyncTool.Models;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace HistorianSyncTool.UI.Controls
{
    /// <summary>
    /// Two server tracks (Primary above, Secondary below) on ONE shared time axis,
    /// with a "copy candidates" strip between them highlighting exactly where one
    /// server has samples the other lacks. Replaces the two separate CoverageBars,
    /// which forced the user to compare positions by eye.
    ///
    ///   ┌ PRIMARY · host · tag ─────────────────────────── 96 % ┐
    ///   │ ██████████████░░░░(red)░░████████▒▒(gray)▒▒█████████  │  ← track
    ///   │            ▲▲▲▲▲ (amber copy candidates) ▲▲▲          │  ← strip
    ///   │ ████████████████████░░░░░░██████████████████████████  │  ← track
    ///   └ SECONDARY · host · tag ──────────────────────── 91 % ┘
    ///     |02.06    |04.06    |06.06    |08.06    |10.06          ← axis
    ///
    /// Red   = missing here, the OTHER server has it  → copyable (click to zoom)
    /// Gray  = missing on BOTH servers                → nothing to copy
    /// Blue underline = written by this tool's backfill (journal, non-reverted)
    /// </summary>
    public class GapTimeline : Control
    {
        private const int MinSegPixelWidth = 3;

        // ── Input data ─────────────────────────────────────────────────────────────
        private DateTime _from, _to;
        private bool _hasRange;
        private TimelineTrackData _top;
        private TimelineTrackData _bottom;
        private List<CopyableSegment> _copyable = new List<CopyableSegment>();
        private string _stripNote;      // e.g. "different tags selected — copy strip hidden"
        private string _emptyMessage = "";

        /// <summary>Compact mode (preview dialogs): thinner rows, no legend.</summary>
        public bool Compact { get; set; }

        /// <summary>False disables click-to-zoom (preview dialogs).</summary>
        public bool AllowZoom { get; set; } = true;

        /// <summary>User clicked a segment — asks the owner to zoom the date range.</summary>
        public event Action<DateTime, DateTime> ZoomRequested;

        // ── Hit testing / hover ─────────────────────────────────────────────────────
        private struct Hit
        {
            public Rectangle Rect;
            public string Tip;
            public TimeRange Range;
            public bool Zoomable;
        }
        private readonly List<Hit> _hits = new List<Hit>();
        private int _hoverIndex = -2;           // -2 none set, -1 background, >=0 hit index
        private int _mouseX = -1;
        private readonly ToolTip _tooltip;

        public GapTimeline()
        {
            DoubleBuffered = true;
            BackColor = Color.White;
            _tooltip = new ToolTip
            {
                AutoPopDelay = 12000,
                InitialDelay = 150,
                ReshowDelay = 80,
                ShowAlways = true
            };
        }

        // ── Public API ──────────────────────────────────────────────────────────────

        public void SetData(DateTime from, DateTime to,
            TimelineTrackData top, TimelineTrackData bottom,
            List<CopyableSegment> copyable, string stripNote = null)
        {
            _from = from;
            _to = to;
            _hasRange = to > from;
            _top = top;
            _bottom = bottom;
            _copyable = copyable ?? new List<CopyableSegment>();
            _stripNote = stripNote;
            _hoverIndex = -2;
            Invalidate();
        }

        /// <summary>Placeholder shown before anything has been analysed. Re-assigned when
        /// the UI language changes.</summary>
        public void SetEmptyMessage(string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            _emptyMessage = message;
            Invalidate();
        }

        public void Clear(string message = null)
        {
            _hasRange = false;
            _top = null;
            _bottom = null;
            _copyable = new List<CopyableSegment>();
            if (message != null) _emptyMessage = message;
            _tooltip.Hide(this);
            _hoverIndex = -2;
            Invalidate();
        }

        // ── Layout ──────────────────────────────────────────────────────────────────

        private int LabelH  => Compact ? 15 : 17;
        private int StripH  => Compact ? 12 : 16;
        private int AxisH   => Compact ? 16 : 18;
        private int LegendH => Compact ? 0 : 16;

        private Rectangle TopLabelRect, TopTrackRect, StripRect, BottomTrackRect, BottomLabelRect, AxisRect, LegendRect;

        private void ComputeLayout()
        {
            int w = Width;
            int trackH = Math.Max(18, (Height - 2 * LabelH - StripH - AxisH - LegendH - 2) / 2);
            int y = 0;
            TopLabelRect    = new Rectangle(0, y, w, LabelH);          y += LabelH;
            TopTrackRect    = new Rectangle(0, y, w, trackH);          y += trackH;
            StripRect       = new Rectangle(0, y, w, StripH);          y += StripH;
            BottomTrackRect = new Rectangle(0, y, w, trackH);          y += trackH;
            BottomLabelRect = new Rectangle(0, y, w, LabelH);          y += LabelH;
            AxisRect        = new Rectangle(0, y, w, AxisH);           y += AxisH;
            LegendRect      = new Rectangle(0, y, w, Math.Max(0, LegendH));
        }

        private int XOf(DateTime t)
        {
            long total = (_to - _from).Ticks;
            if (total <= 0) return 0;
            double f = (double)(t - _from).Ticks / total;
            if (f < 0) f = 0;
            if (f > 1) f = 1;
            return (int)Math.Round(f * (Width - 1));
        }

        // ── Painting ────────────────────────────────────────────────────────────────

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.None;
            _hits.Clear();
            ComputeLayout();

            if (!_hasRange || (_top == null && _bottom == null))
            {
                using (var br = new SolidBrush(AppTheme.TextSecondary))
                {
                    var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                    g.DrawString(_emptyMessage, AppTheme.Default, br, ClientRectangle, fmt);
                }
                return;
            }

            string topName = !string.IsNullOrEmpty(_top?.TooltipName) ? _top.TooltipName : "Primary";
            string bottomName = !string.IsNullOrEmpty(_bottom?.TooltipName) ? _bottom.TooltipName : "Secondary";
            DrawTrackLabel(g, _top, "PRIMARY", TopLabelRect);
            DrawTrack(g, _top, topName, TopTrackRect);
            DrawStrip(g);
            DrawTrack(g, _bottom, bottomName, BottomTrackRect);
            DrawTrackLabel(g, _bottom, "SECONDARY", BottomLabelRect);
            DrawAxis(g);
            if (!Compact) DrawLegend(g);

            // Hover crosshair with its time value shown in the axis row
            if (_mouseX >= 0 && _mouseX < Width)
            {
                using (var pen = new Pen(Color.FromArgb(110, AppTheme.Navy)))
                    g.DrawLine(pen, _mouseX, TopTrackRect.Top, _mouseX, BottomTrackRect.Bottom);

                DateTime tAt = _from + TimeSpan.FromTicks(
                    (long)((double)_mouseX / Math.Max(1, Width - 1) * (_to - _from).Ticks));
                string txt = FormatCursorTime(tAt);
                SizeF sz = g.MeasureString(txt, AppTheme.Small);
                float tx = Math.Min(Math.Max(0, _mouseX - sz.Width / 2f), Width - sz.Width);
                using (var bg = new SolidBrush(AppTheme.Navy))
                using (var fg = new SolidBrush(Color.White))
                {
                    var r = new RectangleF(tx - 2, AxisRect.Top, sz.Width + 4, sz.Height);
                    g.FillRectangle(bg, r);
                    g.DrawString(txt, AppTheme.Small, fg, tx, AxisRect.Top);
                }
            }
        }

        private string FormatCursorTime(DateTime t)
        {
            TimeSpan span = _to - _from;
            if (span.TotalHours <= 48) return t.ToString("dd.MM HH:mm:ss");
            if (span.TotalDays <= 32) return t.ToString("dd.MM HH:mm");
            return t.ToString("yyyy-MM-dd HH:mm");
        }

        /// <summary>
        /// One rounding rule for every completeness figure on this control. Only a genuinely
        /// complete period prints "100 %" — a 2 h shortfall in a 13-day window is 0.6 %, and
        /// rounding that away would hide exactly what the screen exists to show.
        /// </summary>
        internal static string FormatCoverage(double ratio)
            => ratio >= 0.9995 ? ratio.ToString("P0") : ratio.ToString("P1");

        private void DrawTrackLabel(Graphics g, TimelineTrackData data, string fallback, Rectangle rc)
        {
            string label = data != null && !string.IsNullOrEmpty(data.Label) ? data.Label : fallback;
            using (var br = new SolidBrush(AppTheme.Navy))
                g.DrawString(label, AppTheme.SectionLabel, br, rc.Left + 2, rc.Top + 1);

            if (data != null && data.CoverageRatio >= 0)
            {
                string pct = FormatCoverage(data.CoverageRatio);
                SizeF sz = g.MeasureString(pct, AppTheme.SectionLabel);
                Color c = data.CoverageRatio >= 0.999 ? AppTheme.Success
                        : data.CoverageRatio >= 0.9 ? AppTheme.TextPrimary
                        : AppTheme.Danger;
                using (var br = new SolidBrush(c))
                    g.DrawString(pct, AppTheme.SectionLabel, br, rc.Right - sz.Width - 2, rc.Top + 1);
            }
        }

        /// <summary>
        /// Paints one track column by column from <see cref="TimelineTrackData.SegmentShare"/>:
        /// green from the bottom in proportion to what this server holds, the rest red (the
        /// other server has those readings) or grey (neither does).
        ///
        /// Column-per-pixel, so cost is the track width and not the number of segments.
        /// </summary>
        private static void DrawProportionalTrack(Graphics g, TimelineTrackData data, Rectangle rc)
        {
            var share = data.SegmentShare;
            int n = share.Length;
            var grey = Color.FromArgb(158, 166, 175);

            using (var green = new SolidBrush(AppTheme.Success))
            using (var red   = new SolidBrush(AppTheme.Danger))
            using (var grey_ = new SolidBrush(grey))
            {
                for (int x = 0; x < rc.Width; x++)
                {
                    // Each pixel column averages the segments it covers, so zooming out cannot
                    // make a bad stretch disappear behind a good one.
                    int s0 = (int)((long)x * n / rc.Width);
                    int s1 = (int)(((long)x + 1) * n / rc.Width);
                    if (s1 <= s0) s1 = s0 + 1;
                    if (s1 > n) s1 = n;

                    double sum = 0; int have = 0, none = 0;
                    for (int s = s0; s < s1; s++)
                    {
                        if (share[s] < 0) { none++; continue; }
                        sum += share[s]; have++;
                    }

                    if (have == 0)
                    {
                        g.FillRectangle(grey_, rc.Left + x, rc.Top, 1, rc.Height);
                        continue;
                    }

                    double f = sum / have;
                    if (f < 0) f = 0; else if (f > 1) f = 1;

                    int greenH = (int)Math.Round(f * rc.Height);
                    // A server that holds SOMETHING must never render as an empty column, and
                    // one that is missing something must never render as a full one.
                    if (greenH <= 0 && f > 0)      greenH = 1;
                    if (greenH >= rc.Height && f < 1) greenH = rc.Height - 1;

                    int missH = rc.Height - greenH;
                    if (missH > 0)
                        g.FillRectangle(none > have ? grey_ : red, rc.Left + x, rc.Top, 1, missH);
                    if (greenH > 0)
                        g.FillRectangle(green, rc.Left + x, rc.Top + missH, 1, greenH);
                }
            }
        }

        private void DrawTrack(Graphics g, TimelineTrackData data, string sideName, Rectangle rc)
        {
            if (data == null || data.CoverageRatio < 0)
            {
                using (var br = new SolidBrush(AppTheme.Border))
                    g.FillRectangle(br, rc);
                string txt = data?.EmptyText ?? Loc.T("timeline.notAnalyzed");
                var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                using (var br = new SolidBrush(AppTheme.TextSecondary))
                    g.DrawString(txt, AppTheme.Small, br, rc, fmt);
                return;
            }

            if (data.SegmentShare != null && data.SegmentShare.Length > 0)
            {
                DrawProportionalTrack(g, data, rc);
            }
            else
            {
                // Base: green = data present. Only for tracks that carry no per-segment detail
                // (the compact preview timelines); the gap lists below paint over it.
                using (var br = new SolidBrush(AppTheme.Success))
                    g.FillRectangle(br, rc);
            }

            // Backfilled-by-tool underline band (drawn first so gaps paint over it if they overlap)
            int bandH = Math.Max(4, rc.Height / 5);
            using (var br = new SolidBrush(Color.FromArgb(46, 134, 222)))   // clear blue
            {
                foreach (var b in data.Backfilled)
                {
                    var r = SegRect(b, rc.Bottom - bandH, bandH);
                    if (r.Width <= 0) continue;
                    g.FillRectangle(br, r);
                    _hits.Add(new Hit
                    {
                        Rect = r,
                        Range = b,
                        Zoomable = true,
                        Tip = Loc.T("timeline.tip.restored") + "\n" + RangeText(b) + "\n" +
                              Loc.T("timeline.tip.restoredBody") + ZoomHint()
                    });
                }
            }

            // With per-segment detail the colours are already painted in proportion; these two
            // loops then only register the tooltips and the click-to-zoom targets. Filling here
            // as well would put a solid block back over a partially-served segment — which is
            // exactly how a 100 % track ended up 90 % red.
            bool fillGaps = data.SegmentShare == null || data.SegmentShare.Length == 0;

            // Unfillable gaps: gray — missing on BOTH servers
            using (var br = new SolidBrush(Color.FromArgb(158, 166, 175)))
            {
                foreach (var u in data.UnfillableGaps)
                {
                    var r = SegRect(u, rc.Top, rc.Height);
                    if (r.Width <= 0) continue;
                    if (fillGaps) g.FillRectangle(br, r);
                    _hits.Add(new Hit
                    {
                        Rect = r,
                        Range = u,
                        Zoomable = true,
                        Tip = Loc.F("timeline.tip.noData", sideName) + "\n" + RangeText(u) + "\n" +
                              Loc.T("timeline.tip.noDataBody") + ZoomHint()
                    });
                }
            }

            // Fillable gaps: red — the other server HAS this data
            using (var br = new SolidBrush(AppTheme.Danger))
            {
                foreach (var f in data.FillableGaps)
                {
                    var r = SegRect(f, rc.Top, rc.Height);
                    if (r.Width <= 0) continue;
                    if (fillGaps) g.FillRectangle(br, r);
                    string why = Loc.T(data.FeasibilityKnown
                        ? "timeline.tip.canCopy"
                        : "timeline.tip.unknown");
                    _hits.Add(new Hit
                    {
                        Rect = r,
                        Range = f,
                        Zoomable = true,
                        Tip = Loc.F("timeline.tip.missing", sideName) + "\n" + RangeText(f) + "\n" + why + ZoomHint()
                    });
                }
            }

            using (var pen = new Pen(AppTheme.Border))
                g.DrawRectangle(pen, rc.Left, rc.Top, rc.Width - 1, rc.Height - 1);

            // Centered coverage percentage, like the old bars. SAME rounding as the figure at
            // the right of the label row — printing "100%" here next to "99.8%" there is two
            // numbers for one quantity, which is the confusion this screen keeps being fixed for.
            string pct = FormatCoverage(data.CoverageRatio);
            using (var font = new Font("Segoe UI", Compact ? 8f : 9f, FontStyle.Bold))
            {
                SizeF sz = g.MeasureString(pct, font);
                float x = rc.Left + (rc.Width - sz.Width) / 2f;
                float y = rc.Top + (rc.Height - sz.Height) / 2f;
                using (var sh = new SolidBrush(Color.FromArgb(90, 0, 0, 0)))
                    g.DrawString(pct, font, sh, x + 1, y + 1);
                using (var br = new SolidBrush(Color.White))
                    g.DrawString(pct, font, br, x, y);
            }
        }

        private void DrawStrip(Graphics g)
        {
            var rc = StripRect;
            using (var br = new SolidBrush(AppTheme.Background))
                g.FillRectangle(br, rc);

            if (!string.IsNullOrEmpty(_stripNote))
            {
                var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                using (var br = new SolidBrush(AppTheme.TextSecondary))
                    g.DrawString(_stripNote, AppTheme.Small, br, rc, fmt);
                return;
            }

            using (var br = new SolidBrush(AppTheme.Warning))
            {
                foreach (var seg in _copyable)
                {
                    var r = SegRect(seg.Range, rc.Top + 2, rc.Height - 4);
                    if (r.Width <= 0) continue;
                    g.FillRectangle(br, r);
                    string dir = !string.IsNullOrEmpty(seg.Description)
                        ? seg.Description
                        : seg.ToSecondary
                            ? "Primary has these — Secondary lacks them → copy to SECONDARY"
                            : "Secondary has these — Primary lacks them → copy to PRIMARY";
                    _hits.Add(new Hit
                    {
                        Rect = r,
                        Range = seg.Range,
                        Zoomable = true,
                        Tip = Loc.F("timeline.tip.candidates", seg.SampleCount.ToString("N0"))
                              + "\n" + RangeText(seg.Range) + "\n" + dir + ZoomHint()
                    });
                }
            }
        }

        private void DrawAxis(Graphics g)
        {
            var ticks = BuildTicks();
            using (var grid = new Pen(Color.FromArgb(46, 0, 0, 0)))
            using (var br = new SolidBrush(AppTheme.TextSecondary))
            {
                foreach (var (t, label) in ticks)
                {
                    int x = XOf(t);
                    g.DrawLine(grid, x, TopTrackRect.Top, x, BottomTrackRect.Bottom);
                    g.DrawLine(grid, x, AxisRect.Top, x, AxisRect.Top + 3);
                    SizeF sz = g.MeasureString(label, AppTheme.Small);
                    float tx = x - sz.Width / 2f;
                    if (tx < 0) tx = 0;
                    if (tx + sz.Width > Width) tx = Width - sz.Width;
                    g.DrawString(label, AppTheme.Small, br, tx, AxisRect.Top + 3);
                }
            }
        }

        private void DrawLegend(Graphics g)
        {
            var rc = LegendRect;
            if (rc.Height <= 0) return;
            int x = rc.Left + 2;
            int boxY = rc.Top + (rc.Height - 9) / 2;

            x = LegendItem(g, x, boxY, AppTheme.Success, Loc.T("timeline.legend.data"), rc.Top);
            x = LegendItem(g, x, boxY, AppTheme.Danger, Loc.T("timeline.legend.fillable"), rc.Top);
            x = LegendItem(g, x, boxY, Color.FromArgb(158, 166, 175), Loc.T("timeline.legend.unfillable"), rc.Top);
            x = LegendItem(g, x, boxY, Color.FromArgb(46, 134, 222), Loc.T("timeline.legend.restored"), rc.Top);
            LegendItem(g, x, boxY, AppTheme.Warning, Loc.T("timeline.legend.candidates"), rc.Top);
        }

        private int LegendItem(Graphics g, int x, int boxY, Color color, string text, int textTop)
        {
            using (var br = new SolidBrush(color))
                g.FillRectangle(br, x, boxY, 9, 9);
            using (var br = new SolidBrush(AppTheme.TextSecondary))
                g.DrawString(text, AppTheme.Small, br, x + 12, textTop + 1);
            return x + 12 + (int)g.MeasureString(text, AppTheme.Small).Width + 12;
        }

        private Rectangle SegRect(TimeRange range, int top, int height)
        {
            int x1 = XOf(range.Start);
            int x2 = XOf(range.End);
            int w = Math.Max(x2 - x1, MinSegPixelWidth);
            if (x1 + w > Width) x1 = Math.Max(0, Width - w);
            return new Rectangle(x1, top, w, height);
        }

        private string ZoomHint() =>
            AllowZoom ? Loc.T("timeline.tip.zoom") : "";

        private static string RangeText(TimeRange r) =>
            $"{r.Start:yyyy-MM-dd HH:mm:ss} → {r.End:yyyy-MM-dd HH:mm:ss}  ({FormatDuration(r.Duration)})";

        private static string FormatDuration(TimeSpan ts)
        {
            if (ts.TotalDays >= 1) return $"{(int)ts.TotalDays}d {ts.Hours}h {ts.Minutes}m";
            if (ts.TotalHours >= 1) return $"{(int)ts.TotalHours}h {ts.Minutes}m";
            if (ts.TotalMinutes >= 1) return $"{ts.Minutes}m {ts.Seconds}s";
            return $"{ts.Seconds}s";
        }

        // ── Axis ticks ──────────────────────────────────────────────────────────────

        private List<(DateTime T, string Label)> BuildTicks()
        {
            var result = new List<(DateTime, string)>();
            TimeSpan span = _to - _from;
            if (span.Ticks <= 0) return result;

            int targetCount = Math.Max(3, Width / 110);

            // Month/year stepping can't be a fixed TimeSpan — handle separately.
            if (span.TotalDays > 80)
            {
                int months = Math.Max(1, (int)Math.Round(span.TotalDays / 30.4 / targetCount));
                if (months > 12) months = 12 * Math.Max(1, months / 12);
                var t = new DateTime(_from.Year, _from.Month, 1);
                if (t < _from) t = t.AddMonths(1);
                string fmt = months >= 12 ? "yyyy" : "MMM yy";
                while (t <= _to)
                {
                    result.Add((t, t.ToString(fmt)));
                    t = t.AddMonths(months);
                }
                return result;
            }

            TimeSpan[] steps =
            {
                TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15),
                TimeSpan.FromMinutes(30), TimeSpan.FromHours(1), TimeSpan.FromHours(3),
                TimeSpan.FromHours(6), TimeSpan.FromHours(12), TimeSpan.FromDays(1),
                TimeSpan.FromDays(2), TimeSpan.FromDays(7), TimeSpan.FromDays(14)
            };
            TimeSpan step = steps[steps.Length - 1];
            foreach (var s in steps)
            {
                if (span.Ticks / s.Ticks <= targetCount) { step = s; break; }
            }

            long stepTicks = step.Ticks;
            long first = ((_from.Ticks + stepTicks - 1) / stepTicks) * stepTicks;
            for (long ticks = first; ticks <= _to.Ticks; ticks += stepTicks)
            {
                var t = new DateTime(ticks);
                string label;
                if (step.TotalDays >= 1) label = t.ToString("dd.MM");
                else if (t.TimeOfDay == TimeSpan.Zero) label = t.ToString("dd.MM");
                else if (span.TotalDays > 2) label = t.ToString("dd.MM HH:mm");
                else label = t.ToString("HH:mm");
                result.Add((t, label));
            }
            return result;
        }

        // ── Mouse interaction ───────────────────────────────────────────────────────

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!_hasRange) return;

            _mouseX = e.X;

            int hit = -1;
            // Later-added hits (red gaps, strip) are visually on top — search from the end.
            for (int i = _hits.Count - 1; i >= 0; i--)
            {
                if (_hits[i].Rect.Contains(e.Location)) { hit = i; break; }
            }

            Cursor = (hit >= 0 && _hits[hit].Zoomable && AllowZoom) ? Cursors.Hand : Cursors.Default;

            if (hit != _hoverIndex)
            {
                _hoverIndex = hit;
                if (hit >= 0)
                    _tooltip.Show(_hits[hit].Tip, this, e.X + 14, e.Y + 18, 12000);
                else
                    _tooltip.Hide(this);
            }
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _mouseX = -1;
            _hoverIndex = -2;
            _tooltip.Hide(this);
            Invalidate();
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (!AllowZoom || !_hasRange || e.Button != MouseButtons.Left) return;

            for (int i = _hits.Count - 1; i >= 0; i--)
            {
                if (!_hits[i].Rect.Contains(e.Location) || !_hits[i].Zoomable) continue;
                var r = _hits[i].Range;
                // Pad by 10% each side (at least 30s) so the zoomed gap has context.
                long pad = Math.Max(r.Duration.Ticks / 10, TimeSpan.FromSeconds(30).Ticks);
                ZoomRequested?.Invoke(
                    new DateTime(Math.Max(DateTime.MinValue.Ticks + pad, r.Start.Ticks) - pad),
                    new DateTime(Math.Min(DateTime.MaxValue.Ticks - pad, r.End.Ticks) + pad));
                return;
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _tooltip?.Dispose();
            base.Dispose(disposing);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            using (var br = new SolidBrush(BackColor))
                e.Graphics.FillRectangle(br, ClientRectangle);
        }
    }
}
