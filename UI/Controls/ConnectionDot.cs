using System.Drawing;
using System.Windows.Forms;

namespace HistorianSyncTool.UI.Controls
{
    public enum ConnectionState { Disconnected, Connecting, Connected, Error }

    /// <summary>Small colored circle indicating server connection status.</summary>
    public class ConnectionDot : Control
    {
        private ConnectionState _state = ConnectionState.Disconnected;

        public ConnectionState State
        {
            get => _state;
            set { _state = value; Invalidate(); ToolTipText = value.ToString(); }
        }

        public string ToolTipText { get; private set; } = "Disconnected";

        public ConnectionDot()
        {
            Size           = new Size(14, 14);
            DoubleBuffered = true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Color fill;
            switch (_state)
            {
                case ConnectionState.Connected:   fill = AppTheme.Success;  break;
                case ConnectionState.Connecting:  fill = AppTheme.Warning;  break;
                case ConnectionState.Error:       fill = AppTheme.Danger;   break;
                default:                          fill = AppTheme.Border;   break;
            }

            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (var brush = new SolidBrush(fill))
                e.Graphics.FillEllipse(brush, 1, 1, Width - 3, Height - 3);

            // Subtle border
            using (var pen = new Pen(Color.FromArgb(60, 0, 0, 0), 1f))
                e.Graphics.DrawEllipse(pen, 1, 1, Width - 3, Height - 3);
        }

        protected override void OnPaintBackground(PaintEventArgs e) { /* transparent */ }
    }
}
