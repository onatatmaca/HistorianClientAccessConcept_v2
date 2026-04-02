using System.Drawing;
using System.Windows.Forms;

namespace HistorianSyncTool.UI.Controls
{
    public enum FlatButtonStyle { Primary, Secondary, Danger, Success }

    /// <summary>
    /// Flat button with hover effect. Style is set via the <see cref="ButtonStyle"/> property.
    /// </summary>
    public class FlatButton : Button
    {
        private FlatButtonStyle _style = FlatButtonStyle.Primary;
        private Color _baseBack;
        private Color _hoverBack;
        private Color _baseFore;

        public FlatButtonStyle ButtonStyle
        {
            get => _style;
            set { _style = value; ApplyStyle(); Invalidate(); }
        }

        public FlatButton()
        {
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            FlatAppearance.MouseOverBackColor = Color.Transparent;
            FlatAppearance.MouseDownBackColor = Color.Transparent;
            Height  = AppTheme.ButtonHeight;
            Font    = AppTheme.SectionLabel;
            Cursor  = Cursors.Hand;
            UseVisualStyleBackColor = false;
            ApplyStyle();
        }

        private void ApplyStyle()
        {
            switch (_style)
            {
                case FlatButtonStyle.Secondary:
                    _baseBack = AppTheme.NavyLight;
                    _hoverBack = Color.FromArgb(210, 228, 248);
                    _baseFore  = AppTheme.Navy;
                    break;
                case FlatButtonStyle.Danger:
                    _baseBack = AppTheme.Danger;
                    _hoverBack = Color.FromArgb(200, 60, 48);
                    _baseFore  = Color.White;
                    break;
                case FlatButtonStyle.Success:
                    _baseBack = AppTheme.Success;
                    _hoverBack = Color.FromArgb(30, 150, 80);
                    _baseFore  = Color.White;
                    break;
                default: // Primary
                    _baseBack = AppTheme.Navy;
                    _hoverBack = AppTheme.NavyHover;
                    _baseFore  = Color.White;
                    break;
            }
            BackColor = _baseBack;
            ForeColor = _baseFore;
        }

        protected override void OnMouseEnter(System.EventArgs e)
        {
            base.OnMouseEnter(e);
            BackColor = _hoverBack;
        }

        protected override void OnMouseLeave(System.EventArgs e)
        {
            base.OnMouseLeave(e);
            BackColor = _baseBack;
        }

        protected override void OnEnabledChanged(System.EventArgs e)
        {
            base.OnEnabledChanged(e);
            BackColor = Enabled ? _baseBack : AppTheme.Border;
            ForeColor = Enabled ? _baseFore : AppTheme.TextSecondary;
        }
    }
}
