using System.Drawing;
using System.Windows.Forms;

namespace HistorianSyncTool.UI.Controls
{
    public enum FlatButtonStyle { Primary, Secondary, Danger, Success, Warning, Info }

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
            AutoEllipsis = true;      // last resort: never clip a caption mid-word in silence
            ApplyStyle();
        }

        // ── Captions must never be silently cut ────────────────────────────────────
        // A plain Button clips overflowing text with no sign that anything is missing, so
        // "← Copy to main server" reads as "← Copy to main" and the German equivalents lose
        // whole words. That already cost us a review round on the table captions. Here the
        // font shrinks a little to make the caption fit; only if that is not enough does
        // AutoEllipsis kick in, which at least SHOWS that something was dropped.

        private const float MinFontSize = 7.25f;
        private Font _designFont;      // the size the caption would like to be
        private Font _fitFont;         // the shrunk copy in use, ours to dispose
        private bool _fitting;

        protected override void Dispose(bool disposing)
        {
            if (disposing && _fitFont != null) { _fitFont.Dispose(); _fitFont = null; }
            base.Dispose(disposing);
        }

        protected override void OnTextChanged(System.EventArgs e) { base.OnTextChanged(e); FitCaption(); }
        protected override void OnSizeChanged(System.EventArgs e) { base.OnSizeChanged(e); FitCaption(); }

        protected override void OnFontChanged(System.EventArgs e)
        {
            base.OnFontChanged(e);
            if (_fitting) return;      // our own shrink, not a new design font
            _designFont = Font;
            FitCaption();
        }

        private void FitCaption()
        {
            if (_fitting || Width <= 8 || string.IsNullOrEmpty(Text)) return;
            if (_designFont == null) _designFont = Font;

            // "&&" is how a literal ampersand is escaped in a button caption; measure what
            // is actually painted, not the escape.
            string shown = Text.Replace("&&", "&");
            int avail = Width - 10;

            // Find the size that fits without allocating one Font per step.
            float size = _designFont.Size;
            using (var probe = new Font(_designFont.FontFamily, size, _designFont.Style))
            {
                var f = probe;
                while (TextRenderer.MeasureText(shown, f).Width > avail && size > MinFontSize)
                {
                    size -= 0.5f;
                    if (!ReferenceEquals(f, probe)) f.Dispose();
                    f = new Font(_designFont.FontFamily, size, _designFont.Style);
                }
                if (!ReferenceEquals(f, probe)) f.Dispose();
            }

            _fitting = true;
            try
            {
                var old = _fitFont;
                if (size >= _designFont.Size) { _fitFont = null; Font = _designFont; }
                else
                {
                    _fitFont = new Font(_designFont.FontFamily, size, _designFont.Style);
                    Font = _fitFont;
                }
                if (old != null) old.Dispose();
            }
            finally { _fitting = false; }
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
                case FlatButtonStyle.Warning:
                    _baseBack = AppTheme.Warning;
                    _hoverBack = Color.FromArgb(220, 138, 10);
                    _baseFore  = Color.White;
                    break;
                case FlatButtonStyle.Success:
                    _baseBack = AppTheme.Success;
                    _hoverBack = Color.FromArgb(30, 150, 80);
                    _baseFore  = Color.White;
                    break;
                case FlatButtonStyle.Info:
                    _baseBack = AppTheme.Teal;
                    _hoverBack = AppTheme.TealHover;
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
