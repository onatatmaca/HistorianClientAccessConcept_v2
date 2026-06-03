using HistorianSyncTool.Services;
using HistorianSyncTool.UI;
using HistorianSyncTool.UI.Controls;
using Proficy.Historian.ClientAccess.API;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace HistorianSyncTool.Forms
{
    /// <summary>
    /// Combined, bidirectional backfill preview. Shows TWO lists side by side:
    ///   left  = samples Primary has that Secondary lacks  (copy Primary → Secondary)
    ///   right = samples Secondary has that Primary lacks   (copy Secondary → Primary)
    /// Each shared tag is read once per server; both diffs are computed from the same
    /// pair at whole-second resolution (<see cref="SampleFilter.ToSecondTicks"/>) so the
    /// "Will copy" counts match exactly what ExecuteBackfill will write. The user ticks
    /// tags independently per side; tags already in sync in a direction are not listed.
    /// </summary>
    public class BidirectionalBackfillDialog : Form
    {
        private readonly ServerConnection _primaryConn;
        private readonly ServerConnection _secondaryConn;
        private readonly List<string> _sharedTags;
        private readonly DateTime _rangeStart;
        private readonly DateTime _rangeEnd;
        private readonly HistorianDataService _dataService;

        private readonly Label _lblSummary;
        private readonly ProgressBar _progress;
        private readonly Label _lblProgress;
        private readonly DataGridView _gridP2S;
        private readonly DataGridView _gridS2P;
        private readonly FlatButton _btnStart;
        private readonly FlatButton _btnCancel;
        private readonly SplitContainer _split;

        private CancellationTokenSource _cts;
        private int _p2sRows;
        private int _s2pRows;

        public List<string> SelectedPrimaryToSecondary { get; private set; } = new List<string>();
        public List<string> SelectedSecondaryToPrimary { get; private set; } = new List<string>();

        public BidirectionalBackfillDialog(
            ServerConnection primaryConn,
            ServerConnection secondaryConn,
            List<string> sharedTags,
            DateTime rangeStart,
            DateTime rangeEnd,
            HistorianDataService dataService)
        {
            _primaryConn   = primaryConn;
            _secondaryConn = secondaryConn;
            _sharedTags    = sharedTags ?? new List<string>();
            _rangeStart    = rangeStart;
            _rangeEnd      = rangeEnd;
            _dataService   = dataService;

            // ── Form ──────────────────────────────────────────────────────────────
            Text            = "Preview & Backfill — both directions";
            Size            = new Size(940, 580);
            MinimumSize     = new Size(720, 440);
            StartPosition   = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox     = true;
            MinimizeBox     = false;
            BackColor       = AppTheme.Background;
            Font            = AppTheme.Default;

            // ── Summary + progress ─────────────────────────────────────────────────
            _lblSummary = new Label
            {
                Text = $"Comparing both servers across {_rangeStart:MM-dd HH:mm} → {_rangeEnd:MM-dd HH:mm}\n" +
                       $"{_sharedTags.Count} shared tag(s). Each list shows what one server is missing from the other.",
                Dock = DockStyle.Top, Height = 46,
                Padding = new Padding(16, 12, 16, 4),
                Font = AppTheme.Default, ForeColor = AppTheme.TextPrimary, AutoSize = false
            };
            _lblProgress = new Label
            {
                Text = "Comparing…", Dock = DockStyle.Top, Height = 18,
                Padding = new Padding(16, 0, 16, 0),
                Font = AppTheme.Small, ForeColor = AppTheme.TextSecondary
            };
            _progress = new ProgressBar { Dock = DockStyle.Top, Height = 6 };

            // ── Two side-by-side grids ─────────────────────────────────────────────
            _gridP2S = MakeSideGrid();
            _gridS2P = MakeSideGrid();

            _split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterWidth = 6,
                BackColor = AppTheme.Background
            };
            _split.Panel1.Controls.Add(BuildSide(_gridP2S, "Primary → Secondary",
                "Primary has these — copy to Secondary", AppTheme.RowAlt));
            _split.Panel2.Controls.Add(BuildSide(_gridS2P, "Secondary → Primary",
                "Secondary has these — copy to Primary", AppTheme.RowAltWarm));

            // ── Bottom buttons ─────────────────────────────────────────────────────
            var pnlButtons = new Panel
            {
                Dock = DockStyle.Bottom, Height = 52,
                Padding = new Padding(16, 10, 16, 10), BackColor = AppTheme.Surface
            };
            pnlButtons.Paint += (s, e) =>
                e.Graphics.DrawLine(new Pen(AppTheme.Border), 0, 0, pnlButtons.Width, 0);

            _btnCancel = new FlatButton
            {
                Text = "Cancel", ButtonStyle = FlatButtonStyle.Secondary,
                Dock = DockStyle.Right, Width = 90
            };
            _btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };

            _btnStart = new FlatButton
            {
                Text = "Start Backfill", ButtonStyle = FlatButtonStyle.Info,
                Dock = DockStyle.Right, Width = 140, Enabled = false
            };
            _btnStart.Click += BtnStart_Click;

            pnlButtons.Controls.Add(_btnStart);
            pnlButtons.Controls.Add(_btnCancel);

            // Add in reverse dock order
            Controls.Add(_split);        // Fill
            Controls.Add(_progress);     // Top
            Controls.Add(_lblProgress);  // Top
            Controls.Add(_lblSummary);   // Top
            Controls.Add(pnlButtons);    // Bottom

            AcceptButton = _btnStart;
            CancelButton = _btnCancel;

            Load        += Dialog_Load;
            FormClosing += (s, e) => _cts?.Cancel();
        }

        private DataGridView MakeSideGrid()
        {
            var g = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                RowHeadersVisible = false,
                BackgroundColor = AppTheme.Surface,
                BorderStyle = BorderStyle.FixedSingle,
                CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
                GridColor = AppTheme.Border,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                EnableHeadersVisualStyles = false,
                ColumnHeadersHeight = 28,
                RowTemplate = { Height = 24 }
            };
            g.ColumnHeadersDefaultCellStyle.BackColor = AppTheme.Navy;
            g.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
            g.ColumnHeadersDefaultCellStyle.Font      = AppTheme.SectionLabel;
            g.AlternatingRowsDefaultCellStyle.BackColor = AppTheme.RowAlt;

            g.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "", Name = "Sel", FillWeight = 8 });
            g.Columns.Add(new DataGridViewTextBoxColumn  { HeaderText = "Tag",       Name = "Tag",     FillWeight = 42, ReadOnly = true });
            g.Columns.Add(new DataGridViewTextBoxColumn  { HeaderText = "Will copy", Name = "Missing", FillWeight = 16, ReadOnly = true, DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight } });
            g.Columns.Add(new DataGridViewTextBoxColumn  { HeaderText = "Range",     Name = "Range",   FillWeight = 34, ReadOnly = true });
            return g;
        }

        /// <summary>One side = title bar + All/None toolbar + grid, all in a panel.</summary>
        private Panel BuildSide(DataGridView grid, string title, string subtitle, Color accent)
        {
            var panel = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.Surface };

            var toolbar = new Panel { Dock = DockStyle.Top, Height = 30, BackColor = AppTheme.Surface };
            var btnAll = new FlatButton { Text = "All",  ButtonStyle = FlatButtonStyle.Secondary, Left = 8,  Top = 2, Width = 56, Height = 24 };
            var btnNone = new FlatButton { Text = "None", ButtonStyle = FlatButtonStyle.Secondary, Left = 68, Top = 2, Width = 56, Height = 24 };
            btnAll.Click  += (s, e) => SetAllChecked(grid, true);
            btnNone.Click += (s, e) => SetAllChecked(grid, false);
            toolbar.Controls.Add(btnAll);
            toolbar.Controls.Add(btnNone);

            var header = new Label
            {
                Text = $"{title}\n{subtitle}",
                Dock = DockStyle.Top, Height = 38,
                Padding = new Padding(10, 4, 8, 2),
                Font = AppTheme.SectionLabel, ForeColor = AppTheme.Navy,
                BackColor = accent, AutoSize = false
            };

            panel.Controls.Add(grid);     // Fill (add first)
            panel.Controls.Add(toolbar);  // Top
            panel.Controls.Add(header);   // Top
            return panel;
        }

        private void Dialog_Load(object sender, EventArgs e)
        {
            try { _split.SplitterDistance = Math.Max(120, _split.Width / 2); } catch { }

            if (_dataService == null || _primaryConn == null || _secondaryConn == null || _sharedTags.Count == 0)
            {
                _progress.Visible = false;
                _lblProgress.Text = _sharedTags.Count == 0
                    ? "No tags exist on both servers."
                    : "Cannot compare — missing connection.";
                _lblProgress.ForeColor = AppTheme.Danger;
                _btnStart.Enabled = false;
                return;
            }

            _cts = new CancellationTokenSource();
            _progress.Style = ProgressBarStyle.Continuous;
            _progress.Minimum = 0;
            _progress.Maximum = _sharedTags.Count;
            _progress.Value = 0;

            Task.Run(() => LoadDiffs(_cts.Token));
        }

        private void LoadDiffs(CancellationToken token)
        {
            for (int i = 0; i < _sharedTags.Count; i++)
            {
                if (token.IsCancellationRequested) return;
                string tag = _sharedTags[i];

                int p2s = 0, s2p = 0;
                DateTime? p2sFirst = null, p2sLast = null, s2pFirst = null, s2pLast = null;

                try
                {
                    var priData = _dataService.ReadRawInRange(_primaryConn, tag, _rangeStart, _rangeEnd);
                    List<(DateTime Time, float Value, double Quality)> secData;
                    try { secData = _dataService.ReadRawInRange(_secondaryConn, tag, _rangeStart, _rangeEnd); }
                    catch { secData = new List<(DateTime, float, double)>(); }

                    var priTicks = new HashSet<long>(priData.Select(s => SampleFilter.ToSecondTicks(s.Time)));
                    var secTicks = new HashSet<long>(secData.Select(s => SampleFilter.ToSecondTicks(s.Time)));

                    foreach (var s in priData)
                        if (!secTicks.Contains(SampleFilter.ToSecondTicks(s.Time)))
                        {
                            p2s++;
                            if (p2sFirst == null || s.Time < p2sFirst) p2sFirst = s.Time;
                            if (p2sLast  == null || s.Time > p2sLast)  p2sLast  = s.Time;
                        }
                    foreach (var s in secData)
                        if (!priTicks.Contains(SampleFilter.ToSecondTicks(s.Time)))
                        {
                            s2p++;
                            if (s2pFirst == null || s.Time < s2pFirst) s2pFirst = s.Time;
                            if (s2pLast  == null || s.Time > s2pLast)  s2pLast  = s.Time;
                        }
                }
                catch { /* tag failed on source read — skip; backfill will report any real errors */ }

                if (token.IsCancellationRequested) return;
                int idx = i;
                int cp2s = p2s, cs2p = s2p;
                DateTime? pf = p2sFirst, pl = p2sLast, sf = s2pFirst, sl = s2pLast;
                BeginInvoke((Action)(() =>
                {
                    if (cp2s > 0) { _gridP2S.Rows.Add(true, tag, cp2s.ToString("N0"), RangeText(pf, pl)); _p2sRows++; }
                    if (cs2p > 0) { _gridS2P.Rows.Add(true, tag, cs2p.ToString("N0"), RangeText(sf, sl)); _s2pRows++; }
                    _progress.Value = Math.Min(idx + 1, _progress.Maximum);
                    _lblProgress.Text = $"Comparing… {idx + 1}/{_sharedTags.Count}";
                }));
            }

            BeginInvoke((Action)FinishLoading);
        }

        private void FinishLoading()
        {
            _progress.Visible = false;
            if (_p2sRows == 0 && _s2pRows == 0)
            {
                _lblProgress.Text = "In sync — nothing to copy in either direction.";
                _lblProgress.ForeColor = AppTheme.Success;
                _btnStart.Enabled = false;
            }
            else
            {
                _lblProgress.Text = $"Ready — {_p2sRows} tag(s) to copy → Secondary, {_s2pRows} tag(s) → Primary.";
                _lblProgress.ForeColor = AppTheme.Success;
                _btnStart.Enabled = true;
            }
        }

        private static string RangeText(DateTime? a, DateTime? b) =>
            (a.HasValue && b.HasValue) ? $"{a.Value:MM-dd HH:mm} → {b.Value:MM-dd HH:mm}" : "—";

        private static void SetAllChecked(DataGridView grid, bool state)
        {
            for (int i = 0; i < grid.Rows.Count; i++)
                grid.Rows[i].Cells["Sel"].Value = state;
        }

        private static List<string> CollectChecked(DataGridView grid)
        {
            var list = new List<string>();
            for (int i = 0; i < grid.Rows.Count; i++)
            {
                var chk = grid.Rows[i].Cells["Sel"].Value;
                if (chk is bool b && b)
                    list.Add((string)grid.Rows[i].Cells["Tag"].Value);
            }
            return list;
        }

        private void BtnStart_Click(object sender, EventArgs e)
        {
            // Commit any in-progress checkbox edit before reading values
            _gridP2S.EndEdit();
            _gridS2P.EndEdit();

            var p2s = CollectChecked(_gridP2S);
            var s2p = CollectChecked(_gridS2P);
            if (p2s.Count == 0 && s2p.Count == 0)
            {
                MessageBox.Show(this, "Select at least one tag in either direction.",
                    "No Tags Selected", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            SelectedPrimaryToSecondary = p2s;
            SelectedSecondaryToPrimary = s2p;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
