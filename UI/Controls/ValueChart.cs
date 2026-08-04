using HistorianSyncTool.Models;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace HistorianSyncTool.UI.Controls
{
    /// <summary>
    /// The measured values of one point from BOTH servers on one axis, with the periods each
    /// server is missing shaded behind the curves:
    ///
    ///   °C
    ///   25 ┤        ╱╲        ╱╲     ── main server (solid)
    ///      │   ╱╲╱╲╱  ╲╱╲╱╲╱   ╲     ── mirror (dashed)
    ///   20 ┤╱╲╱      ░░░░░       ╲    ░░ missing on the mirror
    ///      └──────────────────────────
    ///       01.07                14.07
    ///
    /// Answers the question the coverage timeline cannot: do the two servers actually agree on
    /// the values, and what was the process doing where one of them lost data?
    ///
    /// Drawn from the SAME samples the tables below are showing, decimated to a min/max envelope
    /// per pixel column — so the curve and the table can never disagree, and a window holding
    /// millions of readings still paints in one pass. Custom-drawn to match GapTimeline rather
    /// than pulling in the charting assembly for one control.
    /// </summary>
    public class ValueChart : Control
    {
        private DateTime _from, _to;
        private bool _hasRange;

        private List<(DateTime Time, float Value, double Quality)> _main;
        private List<(DateTime Time, float Value, double Quality)> _mirror;
        private List<TimeRange> _mainMissing   = new List<TimeRange>();
        private List<TimeRange> _mirrorMissing = new List<TimeRange>();
        private string _mainLabel = "", _mirrorLabel = "";
        private string _emptyMessage = "";

        private int _mouseX = -1;

        private static readonly Color MainColor   = Color.FromArgb(30, 58, 95);     // navy
        private static readonly Color MirrorColor = Color.FromArgb(22, 160, 133);   // teal

        public ValueChart()
        {
            DoubleBuffered = true;
            BackColor = Color.White;
        }

        public void SetEmptyMessage(string message)
        {
            _emptyMessage = message ?? "";
            Invalidate();
        }

        public void SetData(DateTime from, DateTime to,
            List<(DateTime Time, float Value, double Quality)> main,
            List<(DateTime Time, float Value, double Quality)> mirror,
            List<TimeRange> mainMissing, List<TimeRange> mirrorMissing,
            string mainLabel, string mirrorLabel)
        {
            _from = from; _to = to;
            _hasRange = to > from;
            _main = main; _mirror = mirror;
            _mainMissing   = mainMissing   ?? new List<TimeRange>();
            _mirrorMissing = mirrorMissing ?? new List<TimeRange>();
            _mainLabel = mainLabel ?? ""; _mirrorLabel = mirrorLabel ?? "";
            Invalidate();
        }

        public void Clear()
        {
            _main = null; _mirror = null;
            _mainMissing = new List<TimeRange>();
            _mirrorMissing = new List<TimeRange>();
            _hasRange = false;
            Invalidate();
        }

        // ── Layout ─────────────────────────────────────────────────────────────────

        // No left gutter: the completeness timeline directly above spans the full control
        // width, and the two are only comparable if the same x means the same instant in both.
        // The value labels are therefore drawn INSIDE the plot.
        private const int LeftAxis = 0;
        private const int TopPad   = 16;
        private const int BottomAxis = 16;

        private Rectangle PlotRect =>
            new Rectangle(LeftAxis, TopPad,
                          Math.Max(10, Width - LeftAxis - 8),
                          Math.Max(10, Height - TopPad - BottomAxis));

        private int XOf(DateTime t)
        {
            long total = (_to - _from).Ticks;
            if (total <= 0) return PlotRect.Left;
            double f = (double)(t - _from).Ticks / total;
            if (f < 0) f = 0; if (f > 1) f = 1;
            return PlotRect.Left + (int)Math.Round(f * (PlotRect.Width - 1));
        }

        // ── Painting ───────────────────────────────────────────────────────────────

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var plot = PlotRect;

            bool empty = !_hasRange
                || ((_main == null || _main.Count == 0) && (_mirror == null || _mirror.Count == 0));
            if (empty)
            {
                using (var br = new SolidBrush(AppTheme.TextSecondary))
                {
                    var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                    g.DrawString(_emptyMessage, AppTheme.Default, br, ClientRectangle, fmt);
                }
                return;
            }

            float min, max;
            ComputeRange(out min, out max);

            // Missing periods behind everything, one tint per server.
            ShadeMissing(g, _mainMissing,   Color.FromArgb(70, AppTheme.Danger), plot, true);
            ShadeMissing(g, _mirrorMissing, Color.FromArgb(70, AppTheme.Danger), plot, false);

            DrawYAxis(g, plot, min, max);

            g.SmoothingMode = SmoothingMode.AntiAlias;
            // Main first, mirror dashed ON TOP: on a healthy pair the two curves are almost
            // identical, and whichever is drawn second is the only one you can see. Dashes over
            // a solid line show both.
            DrawSeries(g, _main,   MainColor,   plot, min, max, false);
            DrawSeries(g, _mirror, MirrorColor, plot, min, max, true);
            g.SmoothingMode = SmoothingMode.None;

            DrawLegend(g, plot);
            DrawCursor(g, plot, min, max);

            using (var pen = new Pen(AppTheme.Border))
                g.DrawRectangle(pen, plot.Left, plot.Top, plot.Width - 1, plot.Height - 1);
        }

        private void ComputeRange(out float min, out float max)
        {
            min = float.MaxValue; max = float.MinValue;
            foreach (var list in new[] { _main, _mirror })
            {
                if (list == null) continue;
                foreach (var s in list)
                {
                    if (s.Value < min) min = s.Value;
                    if (s.Value > max) max = s.Value;
                }
            }
            if (min > max) { min = 0; max = 1; }
            if (Math.Abs(max - min) < 1e-6f) { min -= 1; max += 1; }   // flat line still needs a band
            float pad = (max - min) * 0.08f;
            min -= pad; max += pad;
        }

        private void ShadeMissing(Graphics g, List<TimeRange> ranges, Color color, Rectangle plot, bool topHalf)
        {
            if (ranges == null || ranges.Count == 0) return;
            int h = plot.Height / 2;
            int y = topHalf ? plot.Top : plot.Top + h;
            using (var br = new SolidBrush(color))
                foreach (var r in ranges)
                {
                    int x1 = XOf(r.Start), x2 = XOf(r.End);
                    int w = Math.Max(2, x2 - x1);
                    g.FillRectangle(br, x1, y, w, h);
                }
        }

        private void DrawYAxis(Graphics g, Rectangle plot, float min, float max)
        {
            const int steps = 4;
            using (var grid = new Pen(Color.FromArgb(30, 0, 0, 0)))
            using (var br = new SolidBrush(AppTheme.TextSecondary))
            {
                for (int i = 0; i <= steps; i++)
                {
                    float v = min + (max - min) * i / steps;
                    int y = plot.Bottom - 1 - (int)((v - min) / (max - min) * (plot.Height - 2));
                    g.DrawLine(grid, plot.Left, y, plot.Right, y);
                    if (i == 0) continue;   // the bottom label would sit on the frame
                    string label = Math.Abs(v) >= 1000 ? v.ToString("N0") : v.ToString("G4");
                    SizeF sz = g.MeasureString(label, AppTheme.Small);
                    var box = new RectangleF(plot.Left + 3, y - sz.Height / 2, sz.Width + 2, sz.Height);
                    using (var bg = new SolidBrush(Color.FromArgb(205, Color.White)))
                        g.FillRectangle(bg, box);   // keep labels readable over the curve
                    g.DrawString(label, AppTheme.Small, br, box.X + 1, box.Y);
                }
            }
        }

        /// <summary>
        /// One server's curve, decimated to a min/max envelope per pixel column: a column with
        /// several readings is drawn as the vertical span it covers, so spikes survive the
        /// decimation instead of being sampled away.
        /// </summary>
        private void DrawSeries(Graphics g, List<(DateTime Time, float Value, double Quality)> data,
            Color color, Rectangle plot, float min, float max, bool dashed)
        {
            if (data == null || data.Count == 0) return;

            int w = plot.Width;
            var loY = new int[w]; var hiY = new int[w]; var has = new bool[w];
            long total = (_to - _from).Ticks;
            if (total <= 0) return;

            foreach (var s in data)
            {
                double f = (double)(s.Time - _from).Ticks / total;
                if (f < 0 || f > 1) continue;
                int col = (int)(f * (w - 1));
                if (col < 0 || col >= w) continue;
                int y = plot.Bottom - 1 - (int)((s.Value - min) / (max - min) * (plot.Height - 2));
                if (!has[col]) { has[col] = true; loY[col] = hiY[col] = y; }
                else { if (y < loY[col]) loY[col] = y; if (y > hiY[col]) hiY[col] = y; }
            }

            using (var pen = new Pen(color, 1.4f))
            {
                if (dashed) { pen.DashStyle = DashStyle.Dash; pen.DashPattern = new float[] { 4f, 3f }; }

                int prevCol = -1, prevY = 0;
                for (int c = 0; c < w; c++)
                {
                    if (!has[c]) continue;
                    int x = plot.Left + c;
                    if (hiY[c] != loY[c]) g.DrawLine(pen, x, loY[c], x, hiY[c]);   // the column's span
                    // Join to the previous column only across a small gap: bridging a real
                    // outage would draw a line through data that does not exist.
                    if (prevCol >= 0 && c - prevCol <= 3) g.DrawLine(pen, plot.Left + prevCol, prevY, x, loY[c]);
                    prevCol = c; prevY = hiY[c];
                }
            }
        }

        private void DrawLegend(Graphics g, Rectangle plot)
        {
            int x = plot.Left + 60, y = plot.Top + 2;
            using (var br = new SolidBrush(MainColor))
            using (var pen = new Pen(MainColor, 2f))
            {
                g.DrawLine(pen, x, y + 6, x + 16, y + 6);
                g.DrawString(_mainLabel, AppTheme.Small, br, x + 20, y);
                x += 24 + (int)g.MeasureString(_mainLabel, AppTheme.Small).Width + 14;
            }
            using (var br = new SolidBrush(MirrorColor))
            using (var pen = new Pen(MirrorColor, 2f) { DashStyle = DashStyle.Dash })
            {
                g.DrawLine(pen, x, y + 6, x + 16, y + 6);
                g.DrawString(_mirrorLabel, AppTheme.Small, br, x + 20, y);
            }
        }

        private void DrawCursor(Graphics g, Rectangle plot, float min, float max)
        {
            if (_mouseX < plot.Left || _mouseX > plot.Right) return;

            using (var pen = new Pen(Color.FromArgb(110, AppTheme.Navy)))
                g.DrawLine(pen, _mouseX, plot.Top, _mouseX, plot.Bottom);

            DateTime at = _from + TimeSpan.FromTicks(
                (long)((double)(_mouseX - plot.Left) / Math.Max(1, plot.Width - 1) * (_to - _from).Ticks));

            string txt = at.ToString("dd.MM HH:mm");
            string vMain   = ValueAt(_main, at);
            string vMirror = ValueAt(_mirror, at);
            if (vMain != null)   txt += "   " + _mainLabel + " " + vMain;
            if (vMirror != null) txt += "   " + _mirrorLabel + " " + vMirror;

            SizeF sz = g.MeasureString(txt, AppTheme.Small);
            float tx = Math.Min(Math.Max(plot.Left, _mouseX - sz.Width / 2f), plot.Right - sz.Width);
            using (var bg = new SolidBrush(AppTheme.Navy))
            using (var fg = new SolidBrush(Color.White))
            {
                g.FillRectangle(bg, tx - 3, plot.Bottom + 1, sz.Width + 6, sz.Height);
                g.DrawString(txt, AppTheme.Small, fg, tx, plot.Bottom + 1);
            }
        }

        /// <summary>Nearest reading to <paramref name="at"/>, or null when that server has
        /// nothing close — the readout must not invent a value inside an outage.</summary>
        private string ValueAt(List<(DateTime Time, float Value, double Quality)> data, DateTime at)
        {
            if (data == null || data.Count == 0) return null;

            int lo = 0, hi = data.Count - 1;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (data[mid].Time < at) lo = mid + 1; else hi = mid;
            }
            var best = data[lo];
            if (lo > 0 && Math.Abs((data[lo - 1].Time - at).Ticks) < Math.Abs((best.Time - at).Ticks))
                best = data[lo - 1];

            // "Close" scales with the window: one pixel column's worth of time.
            long tolerance = Math.Max(TimeSpan.TicksPerSecond,
                (_to - _from).Ticks / Math.Max(1, PlotRect.Width) * 3);
            if (Math.Abs((best.Time - at).Ticks) > tolerance) return null;
            return best.Value.ToString("G6");
        }

        // ── Interaction ────────────────────────────────────────────────────────────

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            _mouseX = e.X;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _mouseX = -1;
            Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Invalidate();
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            using (var br = new SolidBrush(BackColor))
                e.Graphics.FillRectangle(br, ClientRectangle);
        }
    }
}
