using System.Drawing;
using System.Windows.Forms;

namespace HistorianSyncTool.UI.Controls
{
    /// <summary>
    /// Navy bar used as a visual section divider with white label text.
    /// Set Dock = DockStyle.Top after adding to parent.
    /// </summary>
    public class SectionHeader : Label
    {
        public SectionHeader()
        {
            AutoSize    = false;
            Height      = AppTheme.SectionHeaderHeight;
            BackColor   = AppTheme.Navy;
            ForeColor   = Color.White;
            Font        = AppTheme.SectionLabel;
            TextAlign   = ContentAlignment.MiddleLeft;
            Padding     = new Padding(10, 0, 0, 0);
        }
    }
}
