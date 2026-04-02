using System;
using System.Drawing;
using System.Windows.Forms;

namespace HistorianSyncTool.UI.Controls
{
    /// <summary>
    /// A panel with a navy header bar that can be toggled to collapse/expand its content.
    /// Use Dock = DockStyle.Bottom and set ExpandedHeight to desired open height.
    /// Access child controls via the <see cref="Content"/> panel.
    /// </summary>
    public class CollapsiblePanel : Panel
    {
        private const int HeaderH = AppTheme.SectionHeaderHeight;

        private readonly Panel  _header;
        private readonly Label  _lblTitle;
        private readonly Button _btnToggle;
        private bool            _expanded   = true;
        private int             _expandedH  = 160;

        public Panel Content { get; }

        public bool IsExpanded
        {
            get => _expanded;
            set
            {
                _expanded       = value;
                Content.Visible = value;
                _btnToggle.Text = value ? "▼" : "▲";
                Height          = value ? _expandedH : HeaderH;
            }
        }

        public int ExpandedHeight
        {
            get => _expandedH;
            set { _expandedH = Math.Max(HeaderH + 20, value); if (_expanded) Height = _expandedH; }
        }

        public string Title
        {
            get => _lblTitle.Text;
            set => _lblTitle.Text = value;
        }

        public CollapsiblePanel()
        {
            BackColor = AppTheme.Surface;

            _lblTitle = new Label
            {
                Text        = "LOG",
                ForeColor   = Color.White,
                Font        = AppTheme.SectionLabel,
                AutoSize    = false,
                Dock        = DockStyle.Fill,
                TextAlign   = ContentAlignment.MiddleLeft,
                Padding     = new Padding(10, 0, 0, 0)
            };

            _btnToggle = new Button
            {
                Text        = "▼",
                ForeColor   = Color.White,
                BackColor   = Color.Transparent,
                FlatStyle   = FlatStyle.Flat,
                Dock        = DockStyle.Right,
                Width       = 32,
                Cursor      = Cursors.Hand,
                Font        = new Font("Segoe UI", 8f),
                TabStop     = false
            };
            _btnToggle.FlatAppearance.BorderSize          = 0;
            _btnToggle.FlatAppearance.MouseOverBackColor  = Color.FromArgb(30, 255, 255, 255);
            _btnToggle.Click += (s, e) => IsExpanded = !_expanded;

            _header = new Panel
            {
                Dock        = DockStyle.Top,
                Height      = HeaderH,
                BackColor   = AppTheme.Navy
            };
            // Right control added before Fill so it docks to right correctly
            _header.Controls.Add(_lblTitle);
            _header.Controls.Add(_btnToggle);

            Content = new Panel
            {
                Dock        = DockStyle.Fill,
                BackColor   = AppTheme.Surface,
                Padding     = new Padding(6)
            };

            // Add Content first (Fill), then Header (Top) — WinForms processes last→first
            Controls.Add(Content);
            Controls.Add(_header);

            Height = _expandedH;
        }
    }
}
