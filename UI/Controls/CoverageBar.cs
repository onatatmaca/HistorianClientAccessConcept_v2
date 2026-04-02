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
    /// Green = covered, Red = gap. Percentage label is drawn in the center.
    /// </summary>
    public class CoverageBar : Control
    {
        private DateTime _rangeStart;
        private DateTime _rangeEnd;
        private List<(DateTime Start, DateTime End)> _gaps = new List<(DateTime, DateTime)>();
        private double _coverageRatio = -1; // -1 = no data yet

        public CoverageBar()
        {
            Height          = 28;
            DoubleBuffered  = true;
            BackColor       = AppTheme.Border;
        }

        /// <summary>Populate with live gap analysis data.</summary>
        public void SetData(DateTime rangeStart, DateTime rangeEnd,
                            List<GapWindow> gaps, double coverageRatio)
        {
            _rangeStart     = rangeStart;
            _rangeEnd       = rangeEnd;
            _coverageRatio  = coverageRatio;
            _gaps           = new List<(DateTime, DateTime)>();
            if (gaps != null)
                foreach (var g in gaps)
                    _gaps.Add((g.Start, g.End));
            Invalidate();
        }

        public void Clear()
        {
            _gaps.Clear();
            _coverageRatio = -1;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g   = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var rc  = new Rectangle(0, 0, Width - 1, Height - 1);

            if (_coverageRatio < 0)
            {
                // No data state — gray with "No data" label
                using (var brush = new SolidBrush(AppTheme.Border))
                    g.FillRectangle(brush, rc);
                DrawCenteredLabel(g, "No data", AppTheme.TextSecondary);
                return;
            }

            // Green background = covered
            using (var brush = new SolidBrush(AppTheme.Success))
                g.FillRectangle(brush, rc);

            long totalTicks = (_rangeEnd - _rangeStart).Ticks;
            if (totalTicks > 0)
            {
                // Red segments = gaps
                using (var gapBrush = new SolidBrush(AppTheme.Danger))
                {
                    foreach (var gap in _gaps)
                    {
                        float x = (float)Math.Max(0, (gap.Start - _rangeStart).Ticks) / totalTicks * Width;
                        float w = (float)(gap.End - gap.Start).Ticks / totalTicks * Width;
                        if (w < 1) w = 1;
                        g.FillRectangle(gapBrush, x, 0, w, Height);
                    }
                }
            }

            // Border
            using (var pen = new Pen(AppTheme.Border, 1f))
                g.DrawRectangle(pen, rc);

            // Percentage label
            DrawCenteredLabel(g, $"{_coverageRatio:P0}", Color.White);
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

        protected override void OnPaintBackground(PaintEventArgs e) { /* handled in OnPaint */ }
    }
}
