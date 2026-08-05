using HistorianSyncTool.Services;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace HistorianSyncTool.UI.Controls
{
    /// <summary>
    /// Every measurement point, one row each, both servers drawn on the same time axis:
    ///
    ///   STAT6.TEMP_05_GAA_SCALE                      ~1,284 readings to restore  ›
    ///   main   ██████████████████░░░░░░░████████████████████████  98 %
    ///   mirror ██████████████████████████████▒▒▒▒███████████████  94 %
    ///
    ///   green = data present · red = missing here, the other server has it ·
    ///   grey = missing on both (nothing to copy)
    ///
    /// Scroll to see the rest; click a row to open that point.
    ///
    /// ONE owner-drawn control rather than a control per row: with ~78 points (and plants have
    /// far more) a panel full of child controls crawls on x86, and only the visible rows are
    /// painted here. Derived from Panel purely for its real scrollbar and mouse-wheel handling.
    /// </summary>
    public class TagOverviewList : Panel
    {
        private const int RowHeight   = 62;
        private const int NameWidth   = 250;
        private const int RightWidth  = 200;
        private const int BarHeight   = 13;

        private List<PointCoverage> _all = new List<PointCoverage>();
        private List<PointCoverage> _shown = new List<PointCoverage>();
        private string _filter = "";
        private int _hoverIndex = -1;
        private readonly ToolTip _tip;

        /// <summary>Shown centred when there is nothing to list.</summary>
        public string EmptyMessage = "";

        /// <summary>Raised when a row is clicked — the owner opens that measurement point.</summary>
        public event Action<string> PointActivated;

        public TagOverviewList()
        {
            DoubleBuffered = true;
            BackColor      = Color.White;
            AutoScroll     = true;
            _tip = new ToolTip { AutoPopDelay = 12000, InitialDelay = 250, ReshowDelay = 80 };
        }

        public int ShownCount => _shown.Count;

        public void SetData(IEnumerable<PointCoverage> points)
        {
            _all = (points ?? Enumerable.Empty<PointCoverage>()).ToList();
            ApplyFilter();
        }

        public void Clear(string message = null)
        {
            _all = new List<PointCoverage>();
            if (message != null) EmptyMessage = message;
            ApplyFilter();
        }

        /// <summary>Client-side filter — never triggers a re-scan.</summary>
        public void SetFilter(string text)
        {
            _filter = (text ?? "").Trim();
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            _shown = string.IsNullOrEmpty(_filter)
                ? new List<PointCoverage>(_all)
                : _all.Where(p => p.Tag != null &&
                        p.Tag.IndexOf(_filter, StringComparison.OrdinalIgnoreCase) >= 0).ToList();

            AutoScrollMinSize = new Size(0, _shown.Count * RowHeight);
            _hoverIndex = -1;
            Invalidate();
        }

        // ── Painting ───────────────────────────────────────────────────────────────

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.None;

            if (_shown.Count == 0)
            {
                using (var br = new SolidBrush(AppTheme.TextSecondary))
                {
                    var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                    g.DrawString(EmptyMessage, AppTheme.Default, br, ClientRectangle, fmt);
                }
                return;
            }

            int scroll = -AutoScrollPosition.Y;
            int firstRow = Math.Max(0, scroll / RowHeight);
            int lastRow  = Math.Min(_shown.Count - 1, (scroll + Height) / RowHeight);

            for (int i = firstRow; i <= lastRow; i++)
                DrawRow(g, _shown[i], i, new Rectangle(0, i * RowHeight - scroll, Width, RowHeight));
        }

        private void DrawRow(Graphics g, PointCoverage p, int index, Rectangle rc)
        {
            if (index == _hoverIndex)
                using (var br = new SolidBrush(AppTheme.NavyLight))
                    g.FillRectangle(br, rc);

            using (var pen = new Pen(AppTheme.Border))
                g.DrawLine(pen, rc.Left + 8, rc.Bottom - 1, rc.Right - 8, rc.Bottom - 1);

            // Point name
            using (var br = new SolidBrush(AppTheme.TextPrimary))
                g.DrawString(p.Tag, AppTheme.Bold, br,
                    new RectangleF(rc.Left + 10, rc.Top + 6, NameWidth - 14, 18));

            // Right-hand verdict
            string verdict;
            Color verdictColor;
            if (!p.Scanned)                    { verdict = Loc.T("ov.notScanned");  verdictColor = AppTheme.TextSecondary; }
            else if (p.Error != null)          { verdict = Loc.T("ov.failed");      verdictColor = AppTheme.Danger; }
            // A point configured on only one server is a different problem from a data gap:
            // restoring cannot fix it, so it gets its own wording and its own colour.
            else if (!p.OnMirror)              { verdict = Loc.T("ov.notOnMirror"); verdictColor = AppTheme.Warning; }
            else if (!p.OnMain)                { verdict = Loc.T("ov.notOnMain");   verdictColor = AppTheme.Warning; }
            else if (p.InSync)                 { verdict = Loc.T("ov.inSync");      verdictColor = AppTheme.Success; }
            else
            {
                int total = p.EstMissingOnMirror + p.EstMissingOnMain;
                verdict = Loc.F("ov.toRestore", total.ToString("N0"));
                verdictColor = AppTheme.Danger;
            }
            using (var br = new SolidBrush(verdictColor))
            {
                var fmt = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter };
                fmt.FormatFlags |= StringFormatFlags.NoWrap;
                g.DrawString(verdict, AppTheme.Bold, br,
                    new RectangleF(rc.Right - RightWidth - 26, rc.Top + 5, RightWidth, 18), fmt);
            }

            // Chevron — this row opens
            using (var br = new SolidBrush(AppTheme.TextSecondary))
                g.DrawString("›", new Font("Segoe UI", 12f, FontStyle.Bold), br, rc.Right - 22, rc.Top + 12);

            int barLeft  = rc.Left + 10;
            int barWidth = rc.Width - 20 - 44;   // room for the trailing percentages
            DrawBar(g, p.Main,   p.Mirror, barLeft, rc.Top + 28, barWidth, p.MainCoverage,
                    Loc.T("ov.main"), p.OnMain);
            DrawBar(g, p.Mirror, p.Main,   barLeft, rc.Top + 28 + BarHeight + 3, barWidth, p.MirrorCoverage,
                    Loc.T("ov.mirror"), p.OnMirror);
        }

        /// <summary>
        /// One server's bar. Colours follow the timeline exactly: green = data, red = missing
        /// here while the other server HAS it, grey = missing on both (nothing to copy).
        /// </summary>
        private void DrawBar(Graphics g, int[] mine, int[] other, int left, int top, int width,
                             double coverage, string sideLabel, bool configured = true)
        {
            var rect = new Rectangle(left + 46, top, Math.Max(10, width - 46 - 46), BarHeight);

            using (var br = new SolidBrush(AppTheme.TextSecondary))
                g.DrawString(sideLabel, AppTheme.Small, br, left, top - 1);

            // The point is not configured on this server: hatched, not "empty". An empty bar
            // would read as "has no data right now", which is a very different problem.
            if (!configured)
            {
                using (var hatch = new HatchBrush(HatchStyle.WideUpwardDiagonal,
                        Color.FromArgb(120, AppTheme.Warning), AppTheme.Background))
                    g.FillRectangle(hatch, rect);
                using (var pen = new Pen(AppTheme.Border))
                    g.DrawRectangle(pen, rect.Left, rect.Top, rect.Width - 1, rect.Height - 1);
                string note = Loc.T("ov.notConfigured");
                SizeF ns = g.MeasureString(note, AppTheme.Small);
                var nbox = new RectangleF(rect.Left + (rect.Width - ns.Width) / 2f - 4,
                                          rect.Top + (rect.Height - ns.Height) / 2f,
                                          ns.Width + 8, ns.Height);
                using (var bg = new SolidBrush(Color.FromArgb(235, Color.White)))
                    g.FillRectangle(bg, nbox);      // hatching behind text makes it unreadable
                using (var br = new SolidBrush(AppTheme.TextPrimary))
                    g.DrawString(note, AppTheme.Small, br, nbox.X + 4, nbox.Y);
                return;
            }

            if (mine == null || mine.Length == 0)
            {
                using (var br = new SolidBrush(AppTheme.Border))
                    g.FillRectangle(br, rect);
                return;
            }

            // Proportional, not all-or-nothing. "Segment holds >= 1 reading -> solid green" made
            // every row look identical: measured live over a year (segments ~15 h wide), all 600
            // segments held at least one reading on both servers for essentially every point, so
            // every bar was solid green at 100 %. Each segment is now green in proportion to the
            // share of readings this server has of whatever the better-served one has, which is
            // the same number printed beside the bar.
            var share = PointCoverage.SegmentShare(mine, other);
            int n = share.Length;
            var grey = Color.FromArgb(158, 166, 175);
            using (var brGreen = new SolidBrush(AppTheme.Success))
            using (var brRed   = new SolidBrush(AppTheme.Danger))
            using (var brGrey  = new SolidBrush(grey))
            {
                for (int i = 0; i < n; i++)
                {
                    int x1 = rect.Left + (int)((long)i * rect.Width / n);
                    int x2 = rect.Left + (int)((long)(i + 1) * rect.Width / n);
                    if (x2 <= x1) x2 = x1 + 1;
                    int w = x2 - x1;

                    if (share[i] < 0)   // neither server recorded anything here
                    {
                        g.FillRectangle(brGrey, x1, rect.Top, w, rect.Height);
                        continue;
                    }

                    int greenH = (int)Math.Round(share[i] * rect.Height);
                    // Holding something must never look empty; missing something must never
                    // look full — at this bar height a rounding step is ~8 % of the segment.
                    if (greenH <= 0 && share[i] > 0)          greenH = 1;
                    if (greenH >= rect.Height && share[i] < 1) greenH = rect.Height - 1;

                    int missH = rect.Height - greenH;
                    if (missH > 0)  g.FillRectangle(brRed,   x1, rect.Top, w, missH);
                    if (greenH > 0) g.FillRectangle(brGreen, x1, rect.Top + missH, w, greenH);
                }
            }

            using (var pen = new Pen(AppTheme.Border))
                g.DrawRectangle(pen, rect.Left, rect.Top, rect.Width - 1, rect.Height - 1);

            if (coverage >= 0)
            {
                // A 2 h gap in a 13-day window is 0.6 % — rounding that to "100 %" would hide
                // exactly the thing this screen exists to show. Only a genuinely complete
                // period prints 100 %.
                string pct = coverage >= 0.9995 ? coverage.ToString("P0") : coverage.ToString("P1");
                using (var br = new SolidBrush(coverage >= 0.999 ? AppTheme.Success
                                             : coverage >= 0.9   ? AppTheme.TextPrimary
                                                                 : AppTheme.Danger))
                    g.DrawString(pct, AppTheme.Small, br, rect.Right + 5, top - 1);
            }
        }

        // ── Interaction ────────────────────────────────────────────────────────────

        private int RowAt(Point p)
        {
            int idx = (p.Y - AutoScrollPosition.Y) / RowHeight;
            return (idx >= 0 && idx < _shown.Count) ? idx : -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int idx = RowAt(e.Location);
            if (idx == _hoverIndex) return;

            _hoverIndex = idx;
            Cursor = idx >= 0 ? Cursors.Hand : Cursors.Default;
            if (idx >= 0) _tip.SetToolTip(this, BuildTip(_shown[idx]));
            else _tip.SetToolTip(this, "");
            Invalidate();
        }

        private static string BuildTip(PointCoverage p)
        {
            if (!p.Scanned) return p.Tag + "\n" + Loc.T("ov.notScannedTip");
            if (p.Error != null) return p.Tag + "\n" + p.Error;
            if (!p.OnMirror) return p.Tag + "\n" + Loc.T("ov.notOnMirrorTip");
            if (!p.OnMain)   return p.Tag + "\n" + Loc.T("ov.notOnMainTip");

            string s = p.Tag + "\n"
                     + Loc.F("ov.tipCoverage", p.MainCoverage.ToString("P1"), p.MirrorCoverage.ToString("P1"));
            if (p.EstMissingOnMirror > 0)
                s += "\n" + Loc.F("ov.tipMissingMirror", p.EstMissingOnMirror.ToString("N0"));
            if (p.EstMissingOnMain > 0)
                s += "\n" + Loc.F("ov.tipMissingMain", p.EstMissingOnMain.ToString("N0"));
            if (p.EstMissingOnMirror > 0 || p.EstMissingOnMain > 0)
                s += "\n" + Loc.T("ov.tipEstimate");
            s += "\n" + Loc.T("ov.tipOpen");
            return s;
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hoverIndex = -1;
            Invalidate();
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button != MouseButtons.Left) return;
            int idx = RowAt(e.Location);
            if (idx < 0) return;
            var handler = PointActivated;
            if (handler != null) handler(_shown[idx].Tag);
        }

        protected override void OnScroll(ScrollEventArgs se)
        {
            base.OnScroll(se);
            Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            Invalidate();
        }

        protected override void OnResize(EventArgs eventargs)
        {
            base.OnResize(eventargs);
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _tip != null) _tip.Dispose();
            base.Dispose(disposing);
        }
    }
}
