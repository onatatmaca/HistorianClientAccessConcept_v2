using HistorianSyncTool.Models;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace HistorianSyncTool.UI.Controls
{
    /// <summary>
    /// The measured values of one point, one plot per server, stacked on a shared time axis —
    /// deliberately the same shape as the completeness bars above it:
    ///
    ///   main    ┤   ╱╲╱╲╱╲    ░░░░   ╱╲╱╲        ░░ = missing on this server
    ///   mirror  ┤   ╱╲╱╲╱╲           ╱╲╱╲
    ///           └─────────────────────────────
    ///
    /// One plot each rather than two curves in one box: on a healthy pair the curves sit on top
    /// of each other, so overlaying them hides exactly the thing the screen exists to show.
    /// The value scale is SHARED between the two plots, so a difference in level is visible at
    /// a glance instead of being hidden by independent auto-scaling.
    ///
    /// Drawn from the SAME samples the tables below show, decimated to a min/max envelope per
    /// pixel column, so the curve and the table can never disagree and a window holding
    /// millions of readings still paints in one pass.
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

        /// <summary>Larger fonts and taller rows for the enlarged window.</summary>
        public bool Large { get; set; }

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

        /// <summary>Everything needed to clone this chart into the enlarged window.</summary>
        public void CopyTo(ValueChart other)
        {
            if (other == null) return;
            other.SetEmptyMessage(_emptyMessage);
            other.SetData(_from, _to, _main, _mirror, _mainMissing, _mirrorMissing,
                          _mainLabel, _mirrorLabel);
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
        // No left gutter: the completeness timeline above spans the full control width and the
        // two are only comparable if the same x means the same instant. Value labels are drawn
        // INSIDE each plot.

        private int LabelH => Large ? 18 : 14;
        private int AxisH  => Large ? 26 : 15;   // Large also has to fit the date labels
        private int GapH   => 4;

        private void LayoutPlots(out Rectangle top, out Rectangle bottom, out Rectangle axis)
        {
            int w = Math.Max(10, Width - 8);
            int usable = Height - AxisH - 2 * LabelH - GapH;
            int plotH = Math.Max(18, usable / 2);
            int y = 0;
            y += LabelH;
            top = new Rectangle(0, y, w, plotH);
            y += plotH + GapH;
            y += LabelH;
            bottom = new Rectangle(0, y, w, plotH);
            y += plotH;
            axis = new Rectangle(0, y, w, AxisH);
        }

        private int XOf(DateTime t, Rectangle plot)
        {
            long total = (_to - _from).Ticks;
            if (total <= 0) return plot.Left;
            double f = (double)(t - _from).Ticks / total;
            if (f < 0) f = 0; if (f > 1) f = 1;
            return plot.Left + (int)Math.Round(f * (plot.Width - 1));
        }

        // ── Painting ───────────────────────────────────────────────────────────────

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;

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

            Rectangle top, bottom, axis;
            LayoutPlots(out top, out bottom, out axis);

            float min, max;
            ComputeRange(out min, out max);   // shared scale: level differences stay visible

            DrawPlot(g, top,    _main,   _mainMissing,   MainColor,   _mainLabel,   min, max, false);
            DrawPlot(g, bottom, _mirror, _mirrorMissing, MirrorColor, _mirrorLabel, min, max, true);

            // On the main screen the completeness timeline sits directly above on the SAME
            // time span and carries the dates — a second axis here would just repeat it. The
            // enlarged window has no timeline, so without this it shows curves against no
            // time reference at all.
            if (Large) DrawTimeAxis(g, axis);

            DrawCursor(g, top, bottom, axis, min, max);
        }

        private void DrawPlot(Graphics g, Rectangle plot,
            List<(DateTime Time, float Value, double Quality)> data, List<TimeRange> missing,
            Color color, string label, float min, float max, bool dashed)
        {
            var labelFont = Large ? AppTheme.Default : AppTheme.Small;

            using (var br = new SolidBrush(color))
                g.DrawString(label, labelFont, br, plot.Left + 2, plot.Top - LabelH);

            // Missing periods behind the curve, across the full height of THIS server's plot.
            using (var br = new SolidBrush(Color.FromArgb(60, AppTheme.Danger)))
                foreach (var r in missing ?? new List<TimeRange>())
                {
                    int x1 = XOf(r.Start, plot), x2 = XOf(r.End, plot);
                    g.FillRectangle(br, x1, plot.Top, Math.Max(2, x2 - x1), plot.Height);
                }

            DrawGrid(g, plot, min, max, labelFont);

            g.SmoothingMode = SmoothingMode.AntiAlias;
            DrawSeries(g, data, color, plot, min, max, dashed);
            g.SmoothingMode = SmoothingMode.None;

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
            if (Math.Abs(max - min) < 1e-6f) { min -= 1; max += 1; }   // a flat line still needs a band
            float pad = (max - min) * 0.08f;
            min -= pad; max += pad;
        }

        /// <summary>
        /// Date/time labels under the lower plot. The format follows the span, so a two-hour
        /// window does not print the same date six times and a two-year window does not print
        /// six identical times.
        /// </summary>
        private void DrawTimeAxis(Graphics g, Rectangle axis)
        {
            TimeSpan span = _to - _from;
            if (span <= TimeSpan.Zero) return;

            string fmt = span.TotalDays   > 200 ? "MMM yyyy"
                       : span.TotalDays   > 3   ? "dd.MM"
                       : span.TotalHours  > 6   ? "dd.MM HH:mm"
                       :                          "HH:mm";

            int ticks = Math.Max(2, Math.Min(8, axis.Width / 130));
            using (var br  = new SolidBrush(AppTheme.TextSecondary))
            using (var pen = new Pen(Color.FromArgb(40, 0, 0, 0)))
            {
                for (int i = 0; i <= ticks; i++)
                {
                    DateTime t = _from + TimeSpan.FromTicks(span.Ticks * i / ticks);
                    int x = axis.Left + (int)Math.Round((double)i / ticks * (axis.Width - 1));
                    g.DrawLine(pen, x, axis.Top, x, axis.Top + 3);

                    string s = t.ToString(fmt);
                    float tw = g.MeasureString(s, AppTheme.Small).Width;
                    // First and last labels are pulled inside so they cannot be clipped.
                    float tx = i == 0 ? axis.Left
                             : i == ticks ? axis.Right - tw
                             : x - tw / 2;
                    g.DrawString(s, AppTheme.Small, br, tx, axis.Top + 4);
                }
            }
        }

        /// <summary>Horizontal grid with the value at each line, drawn inside the plot on a
        /// small opaque backing so the numbers stay readable over the curve.</summary>
        private void DrawGrid(Graphics g, Rectangle plot, float min, float max, Font font)
        {
            int steps = Large ? 4 : 2;   // the small chart has room for very few
            using (var grid = new Pen(Color.FromArgb(28, 0, 0, 0)))
            using (var br = new SolidBrush(AppTheme.TextSecondary))
            {
                for (int i = 0; i <= steps; i++)
                {
                    float v = min + (max - min) * i / steps;
                    int y = plot.Bottom - 1 - (int)((v - min) / (max - min) * (plot.Height - 2));
                    g.DrawLine(grid, plot.Left, y, plot.Right, y);
                    if (i == 0 || i == steps) continue;   // top/bottom sit on the frame

                    string label = Math.Abs(v) >= 1000 ? v.ToString("N0") : v.ToString("G4");
                    SizeF sz = g.MeasureString(label, font);
                    var box = new RectangleF(plot.Left + 3, y - sz.Height / 2, sz.Width + 3, sz.Height);
                    using (var bg = new SolidBrush(Color.FromArgb(215, Color.White)))
                        g.FillRectangle(bg, box);
                    g.DrawString(label, font, br, box.X + 1, box.Y);
                }
            }
        }

        /// <summary>
        /// One server's curve, decimated to a min/max envelope per pixel column: a column with
        /// several readings is drawn as the vertical span it covers, so spikes survive instead
        /// of being sampled away.
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

            using (var pen = new Pen(color, Large ? 1.6f : 1.3f))
            {
                if (dashed) { pen.DashStyle = DashStyle.Dash; pen.DashPattern = new float[] { 4f, 3f }; }
                int prevCol = -1, prevY = 0;
                for (int c = 0; c < w; c++)
                {
                    if (!has[c]) continue;
                    int x = plot.Left + c;
                    if (hiY[c] != loY[c]) g.DrawLine(pen, x, loY[c], x, hiY[c]);
                    // Join only across a small gap: bridging a real outage would draw a line
                    // through data that does not exist.
                    if (prevCol >= 0 && c - prevCol <= 3) g.DrawLine(pen, plot.Left + prevCol, prevY, x, loY[c]);
                    prevCol = c; prevY = hiY[c];
                }
            }
        }

        private void DrawCursor(Graphics g, Rectangle top, Rectangle bottom, Rectangle axis,
            float min, float max)
        {
            if (_mouseX < top.Left || _mouseX > top.Right) return;

            using (var pen = new Pen(Color.FromArgb(110, AppTheme.Navy)))
                g.DrawLine(pen, _mouseX, top.Top, _mouseX, bottom.Bottom);

            DateTime at = _from + TimeSpan.FromTicks(
                (long)((double)(_mouseX - top.Left) / Math.Max(1, top.Width - 1) * (_to - _from).Ticks));

            string vMain   = ValueAt(_main, at, top);
            string vMirror = ValueAt(_mirror, at, top);
            string txt = at.ToString("dd.MM HH:mm");
            txt += "   " + _mainLabel   + " " + (vMain   ?? "—");
            txt += "   " + _mirrorLabel + " " + (vMirror ?? "—");

            var font = Large ? AppTheme.Default : AppTheme.Small;
            SizeF sz = g.MeasureString(txt, font);
            float tx = Math.Min(Math.Max(top.Left, _mouseX - sz.Width / 2f), top.Right - sz.Width);
            using (var bg = new SolidBrush(AppTheme.Navy))
            using (var fg = new SolidBrush(Color.White))
            {
                g.FillRectangle(bg, tx - 3, axis.Top, sz.Width + 6, sz.Height);
                g.DrawString(txt, font, fg, tx, axis.Top);
            }
        }

        /// <summary>Nearest reading to <paramref name="at"/>, or null when that server has
        /// nothing close — the readout must not invent a value inside an outage.</summary>
        private string ValueAt(List<(DateTime Time, float Value, double Quality)> data, DateTime at,
            Rectangle plot)
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

            long tolerance = Math.Max(TimeSpan.TicksPerSecond,
                (_to - _from).Ticks / Math.Max(1, plot.Width) * 3);
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
