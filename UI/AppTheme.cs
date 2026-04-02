using System.Drawing;

namespace HistorianSyncTool.UI
{
    /// <summary>
    /// Central design token registry. All colors, fonts and spacing come from here.
    /// </summary>
    public static class AppTheme
    {
        // ── Colors ─────────────────────────────────────────────────────────────────
        public static readonly Color Navy         = Color.FromArgb(30,  58,  95);   // #1E3A5F
        public static readonly Color NavyHover    = Color.FromArgb(22,  44,  72);   // darker navy
        public static readonly Color NavyLight    = Color.FromArgb(232, 240, 250);  // #E8F0FA
        public static readonly Color Background   = Color.FromArgb(245, 246, 250);  // #F5F6FA
        public static readonly Color Surface      = Color.White;
        public static readonly Color Border       = Color.FromArgb(221, 226, 232);  // #DDE2E8
        public static readonly Color TextPrimary  = Color.FromArgb(44,  62,  80);   // #2C3E50
        public static readonly Color TextSecondary= Color.FromArgb(127, 140, 141);  // #7F8C8D
        public static readonly Color Success      = Color.FromArgb(39,  174, 96);   // #27AE60
        public static readonly Color Danger       = Color.FromArgb(231, 76,  60);   // #E74C3C
        public static readonly Color Warning      = Color.FromArgb(243, 156, 18);   // #F39C12
        public static readonly Color RowAlt       = Color.FromArgb(248, 249, 253);  // alternate grid row

        // ── Section header ─────────────────────────────────────────────────────────
        public static readonly Color SectionHeaderBg   = Navy;
        public static readonly Color SectionHeaderFg   = Color.White;
        public const int SectionHeaderHeight = 28;

        // ── Fonts ──────────────────────────────────────────────────────────────────
        public static readonly Font Default      = new Font("Segoe UI", 9f,   FontStyle.Regular);
        public static readonly Font Bold         = new Font("Segoe UI", 9f,   FontStyle.Bold);
        public static readonly Font Small        = new Font("Segoe UI", 8f,   FontStyle.Regular);
        public static readonly Font AppTitle     = new Font("Segoe UI", 12f,  FontStyle.Bold);
        public static readonly Font SectionLabel = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        public static readonly Font Mono         = new Font("Consolas",  8.5f, FontStyle.Regular);

        // ── Sizing ─────────────────────────────────────────────────────────────────
        public const int ControlHeight   = 26;
        public const int ButtonHeight    = 30;
        public const int SectionPad      = 10;
        public const int LeftPanelWidth  = 285;
        public const int RightPanelWidth = 390;
        public const int StatusBarHeight = 34;

        // ── Grid style helper ──────────────────────────────────────────────────────
        public static void StyleGrid(System.Windows.Forms.DataGridView grid)
        {
            grid.BackgroundColor        = Surface;
            grid.BorderStyle            = System.Windows.Forms.BorderStyle.None;
            grid.CellBorderStyle        = System.Windows.Forms.DataGridViewCellBorderStyle.SingleHorizontal;
            grid.GridColor              = Border;
            grid.RowHeadersVisible      = false;
            grid.AllowUserToAddRows     = false;
            grid.AllowUserToDeleteRows  = false;
            grid.AllowUserToResizeRows  = false;
            grid.ReadOnly               = true;
            grid.SelectionMode          = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            grid.AutoSizeColumnsMode    = System.Windows.Forms.DataGridViewAutoSizeColumnsMode.Fill;
            grid.Font                   = Default;

            grid.ColumnHeadersDefaultCellStyle.BackColor   = Navy;
            grid.ColumnHeadersDefaultCellStyle.ForeColor   = Color.White;
            grid.ColumnHeadersDefaultCellStyle.Font        = SectionLabel;
            grid.ColumnHeadersDefaultCellStyle.Padding     = new System.Windows.Forms.Padding(6, 0, 0, 0);
            grid.ColumnHeadersHeight                        = 30;
            grid.ColumnHeadersHeightSizeMode               = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            grid.EnableHeadersVisualStyles                  = false;

            grid.DefaultCellStyle.BackColor                = Surface;
            grid.DefaultCellStyle.ForeColor                = TextPrimary;
            grid.DefaultCellStyle.SelectionBackColor       = NavyLight;
            grid.DefaultCellStyle.SelectionForeColor       = Navy;
            grid.DefaultCellStyle.Padding                  = new System.Windows.Forms.Padding(6, 0, 0, 0);

            grid.AlternatingRowsDefaultCellStyle.BackColor = RowAlt;
        }
    }
}
