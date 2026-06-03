using HistorianSyncTool.Models;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace HistorianSyncTool.UI.Controls
{
    /// <summary>
    /// Visualizes data coverage over a time range.
    /// Green = covered, Red = gap. Percentage label centered.
    /// Hovering a gap segment shows a tooltip with the time range + duration.
    /// </summary>
    public class CoverageBar : Control
    {
        private const int MinGapPixelWidth = 3;

        private DateTime _rangeStart;
        private DateTime _rangeEnd;
        private readonly List<(Rectangle Rect, DateTime Start, DateTime End)> _gapRects =
            new List<(Rectangle, DateTime, DateTime)>();
        private double _coverageRatio = -1; // -1 = no data yet

        // Tag/server label used in tooltip text. e.g. "Primary · HistSync"
        public string TooltipLabel { get; set; } = "";

        private readonly ToolTip _tooltip;
        private int _lastHoverIndex = -2; // -2 = not set, -1 = background, >=0 = gap index

        public CoverageBar()
        {
            Height         = 28;
            DoubleBuffered = true;
            BackColor      = AppTheme.Border;

            _tooltip = new ToolTip
            {
                AutoPopDelay = 10000,
                InitialDelay = 200,
                ReshowDelay  = 100,
                ShowAlways   = true
            };
        }

        /// <summary>Populate with live gap analysis data.</summary>
        public void SetData(DateTime rangeStart, DateTime rangeEnd,
                            List<GapWindow> gaps, double coverageRatio)
        {
            _rangeStart    = rangeStart;
            _rangeEnd      = rangeEnd;
            _coverageRatio = coverageRatio;
            _gapRects.Clear();
            if (gaps != null)
            {
                // Store start/end; rects computed during OnPaint
                foreach (var g in gaps)
                    _gapRects.Add((Rectangle.Empty, g.Start, g.End));
            }
            _lastHoverIndex = -2;
            Invalidate();
        }

        public void Clear()
        {
            _gapRects.Clear();
            _coverageRatio  = -1;
            _lastHoverIndex = -2;
            _tooltip.Hide(this);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g  = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var rc = new Rectangle(0, 0, Width - 1, Height - 1);

            if (_coverageRatio < 0)
            {
                using (var brush = new SolidBrush(AppTheme.Border))
                    g.FillRectangle(brush, rc);
                return;
            }

            // Green background = covered
            using (var brush = new SolidBrush(AppTheme.Success))
                g.FillRectangle(brush, rc);

            long totalTicks = (_rangeEnd - _rangeStart).Ticks;
            if (totalTicks > 0 && _gapRects.Count > 0)
            {
                using (var gapBrush = new SolidBrush(AppTheme.Danger))
                {
                    for (int i = 0; i < _gapRects.Count; i++)
                    {
                        var item = _gapRects[i];
                        float xF = (float)Math.Max(0, (item.Start - _rangeStart).Ticks) / totalTicks * Width;
                        float wF = (float)(item.End - item.Start).Ticks / totalTicks * Width;
                        int x = (int)Math.Floor(xF);
                        int w = (int)Math.Ceiling(wF);
                        if (w < MinGapPixelWidth) w = MinGapPixelWidth;
                        if (x + w > Width) x = Math.Max(0, Width - w);

                        var segRect = new Rectangle(x, 0, w, Height);
                        g.FillRectangle(gapBrush, segRect);

                        // Store the computed rect for hit testing
                        _gapRects[i] = (segRect, item.Start, item.End);
                    }
                }
            }

            // Border
            using (var pen = new Pen(AppTheme.Border, 1f))
                g.DrawRectangle(pen, rc);

            // Percentage label
            DrawCenteredLabel(g, $"{_coverageRatio:P0}", Color.White);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_coverageRatio < 0) return;

            int hitIndex = -1; // -1 = not a gap (green area)
            for (int i = 0; i < _gapRects.Count; i++)
            {
                if (_gapRects[i].Rect.Contains(e.Location))
                {
                    hitIndex = i;
                    break;
                }
            }

            if (hitIndex == _lastHoverIndex) return;
            _lastHoverIndex = hitIndex;

            if (hitIndex >= 0)
            {
                var seg = _gapRects[hitIndex];
                string lbl = string.IsNullOrEmpty(TooltipLabel) ? "Gap" : TooltipLabel;
                string text =
                    $"{lbl}\n" +
                    $"{seg.Start:yyyy-MM-dd HH:mm} → {seg.End:yyyy-MM-dd HH:mm}\n" +
                    $"Duration: {FormatDuration(seg.End - seg.Start)}";
                _tooltip.Show(text, this, e.X + 14, e.Y + 18, 10000);
            }
            else
            {
                // Green area — brief coverage summary
                string lbl = string.IsNullOrEmpty(TooltipLabel) ? "Covered" : TooltipLabel + " — covered";
                _tooltip.Show($"{lbl}\nCoverage: {_coverageRatio:P1}",
                    this, e.X + 14, e.Y + 18, 5000);
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _tooltip.Hide(this);
            _lastHoverIndex = -2;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _tooltip?.Dispose();
            base.Dispose(disposing);
        }

        private void DrawCenteredLabel(Graphics g, string text, Color color)
        {
            using (var font   = new Font("Segoe UI", 8.5f, FontStyle.Bold))
            using (var brush  = new SolidBrush(color))
            using (var shadow = new SolidBrush(Color.FromArgb(80, 0, 0, 0)))
            {
                SizeF size = g.MeasureString(text, font);
                float x = (Width  - size.Width)  / 2f;
                float y = (Height - size.Height) / 2f;
                g.DrawString(text, font, shadow, x + 1, y + 1);
                g.DrawString(text, font, brush,  x,     y);
            }
        }

        private static string FormatDuration(TimeSpan ts)
        {
            if (ts.TotalDays >= 1)
                return $"{(int)ts.TotalDays}d {ts.Hours}h {ts.Minutes}m";
            if (ts.TotalHours >= 1)
                return $"{(int)ts.TotalHours}h {ts.Minutes}m";
            if (ts.TotalMinutes >= 1)
                return $"{ts.Minutes}m {ts.Seconds}s";
            return $"{ts.Seconds}s";
        }

        protected override void OnPaintBackground(PaintEventArgs e) { /* handled in OnPaint */ }
    }
}
