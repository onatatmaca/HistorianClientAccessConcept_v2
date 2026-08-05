using HistorianSyncTool.UI;
using HistorianSyncTool.UI.Controls;
using System;
using System.Drawing;
using System.Windows.Forms;

namespace HistorianSyncTool.Forms
{
    /// <summary>
    /// The value chart at full size, in its own window.
    ///
    /// The chart on the main screen has to share the height with the completeness bars and the
    /// two tables, which leaves it too short to read precisely. This shows the same data with
    /// room to breathe — resizable, and closed with the button or Escape.
    ///
    /// It receives a COPY of the data, so it never reads from the servers itself and cannot be
    /// affected by anything the main window does afterwards.
    /// </summary>
    public class ChartDialog : Form
    {
        private readonly ValueChart _chart;

        public ChartDialog(ValueChart source, string pointName)
        {
            Text            = Loc.T("chart.enlargedTitle") + (string.IsNullOrWhiteSpace(pointName) ? "" : " — " + pointName);
            StartPosition   = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox     = false;
            ShowInTaskbar   = false;
            BackColor       = AppTheme.Surface;
            Font            = AppTheme.Default;
            MinimumSize     = new Size(700, 380);

            // Big by default, but never larger than the screen it opens on.
            var wa = Screen.PrimaryScreen.WorkingArea;
            Size = new Size(Math.Min(1400, (int)(wa.Width * 0.9)),
                            Math.Min(760, (int)(wa.Height * 0.85)));

            var header = new SectionHeader { Text = Loc.T("hdr.values"), Dock = DockStyle.Top };

            _chart = new ValueChart { Dock = DockStyle.Fill, Large = true };
            source?.CopyTo(_chart);

            var wrap = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.Surface, Padding = new Padding(12, 8, 12, 4) };
            wrap.Controls.Add(_chart);

            var buttons = new Panel { Dock = DockStyle.Bottom, Height = 46, BackColor = AppTheme.Background, Padding = new Padding(10, 8, 10, 8) };
            var btnClose = new FlatButton
            {
                Text = Loc.T("dlg.close"), ButtonStyle = FlatButtonStyle.Secondary,
                Dock = DockStyle.Right, Width = 120
            };
            btnClose.Click += (s, e) => Close();
            buttons.Controls.Add(btnClose);

            Controls.Add(wrap);
            Controls.Add(buttons);
            Controls.Add(header);

            CancelButton = btnClose;   // Escape closes
            KeyPreview = true;
        }
    }
}
