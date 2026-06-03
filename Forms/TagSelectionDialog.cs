using HistorianSyncTool.Models;
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
    /// Modal dialog that lets the user pick which tags to backfill.
    /// Shows only tags that exist on BOTH servers (intersection) and computes
    /// per-tag stats (source/target counts, backfillable batches with data,
    /// write range, estimated samples) on a background thread so the user
    /// can make an informed selection.
    /// </summary>
    public class TagSelectionDialog : Form
    {
        private readonly string _sourceLabel;
        private readonly string _targetLabel;
        private readonly int _gapCount;
        private readonly int _backfillableBatches;
        private readonly List<string> _sharedTags;

        private readonly ServerConnection _sourceConn;
        private readonly ServerConnection _targetConn;
        private readonly List<GapBatch> _allBackfillBatches;
        private readonly DateTime _rangeStart;
        private readonly DateTime _rangeEnd;
        private readonly HistorianDataService _dataService;

        private readonly Label _lblSummary;
        private readonly DataGridView _grid;
        private readonly ProgressBar _progress;
        private readonly Label _lblProgress;
        private readonly FlatButton _btnSelectAll;
        private readonly FlatButton _btnSelectNone;
        private readonly FlatButton _btnOk;
        private readonly FlatButton _btnCancel;

        private CancellationTokenSource _cts;

        public List<string> SelectedTags { get; private set; } = new List<string>();

        /// <summary>
        /// Full constructor with per-tag pre-flight stat gathering. If the service/connection
        /// args are null, the dialog degrades gracefully (shows the old summary-only view).
        /// </summary>
        public TagSelectionDialog(
            string sourceLabel,
            string targetLabel,
            int gapCount,
            int backfillableBatches,
            List<string> sharedTags,
            ServerConnection sourceConn = null,
            ServerConnection targetConn = null,
            List<GapBatch> allBackfillBatches = null,
            DateTime rangeStart = default,
            DateTime rangeEnd = default,
            HistorianDataService dataService = null)
        {
            _sourceLabel = sourceLabel;
            _targetLabel = targetLabel;
            _gapCount = gapCount;
            _backfillableBatches = backfillableBatches;
            _sharedTags = sharedTags ?? new List<string>();
            _sourceConn = sourceConn;
            _targetConn = targetConn;
            _allBackfillBatches = allBackfillBatches ?? new List<GapBatch>();
            _rangeStart = rangeStart;
            _rangeEnd = rangeEnd;
            _dataService = dataService;

            // ── Form ──────────────────────────────────────────────────────────────
            Text            = "Select Tags for Backfill";
            Size            = new Size(820, 560);
            MinimumSize     = new Size(660, 420);
            StartPosition   = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox     = false;
            MinimizeBox     = false;
            BackColor       = AppTheme.Background;
            Font            = AppTheme.Default;

            // ── Summary label ─────────────────────────────────────────────────────
            _lblSummary = new Label
            {
                Text      = $"Backfill: {sourceLabel} → {targetLabel}\n" +
                            $"{gapCount} gap window(s) • {backfillableBatches} backfillable batch(es) • " +
                            $"{_sharedTags.Count} shared tag(s)",
                Dock      = DockStyle.Top,
                Height    = 46,
                Padding   = new Padding(16, 12, 16, 4),
                Font      = AppTheme.Default,
                ForeColor = AppTheme.TextPrimary,
                AutoSize  = false
            };

            // ── Progress bar ──────────────────────────────────────────────────────
            _lblProgress = new Label
            {
                Text = "Loading per-tag stats…",
                Dock = DockStyle.Top, Height = 18,
                Padding = new Padding(16, 0, 16, 0),
                Font = AppTheme.Small, ForeColor = AppTheme.TextSecondary
            };
            _progress = new ProgressBar
            {
                Dock = DockStyle.Top,
                Height = 6,
                Margin = new Padding(16, 2, 16, 4)
            };

            // ── DataGridView ──────────────────────────────────────────────────────
            _grid = new DataGridView
            {
                Dock              = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                RowHeadersVisible = false,
                BackgroundColor   = AppTheme.Surface,
                BorderStyle       = BorderStyle.FixedSingle,
                CellBorderStyle   = DataGridViewCellBorderStyle.SingleHorizontal,
                GridColor         = AppTheme.Border,
                SelectionMode     = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect       = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                EnableHeadersVisualStyles = false,
                ColumnHeadersHeight = 30,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                RowTemplate = { Height = 24 }
            };
            _grid.ColumnHeadersDefaultCellStyle.BackColor = AppTheme.Navy;
            _grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
            _grid.ColumnHeadersDefaultCellStyle.Font      = AppTheme.SectionLabel;
            _grid.AlternatingRowsDefaultCellStyle.BackColor = AppTheme.RowAlt;

            _grid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "", Name = "Sel",     FillWeight = 6 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn  { HeaderText = "Tag",           Name = "Tag",       FillWeight = 30, ReadOnly = true });
            _grid.Columns.Add(new DataGridViewTextBoxColumn  { HeaderText = "Source samples", Name = "Src",      FillWeight = 13, ReadOnly = true, DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight } });
            _grid.Columns.Add(new DataGridViewTextBoxColumn  { HeaderText = "Target samples", Name = "Tgt",      FillWeight = 13, ReadOnly = true, DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight } });
            _grid.Columns.Add(new DataGridViewTextBoxColumn  { HeaderText = "Will copy",      Name = "Missing",  FillWeight = 13, ReadOnly = true, DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight } });
            _grid.Columns.Add(new DataGridViewTextBoxColumn  { HeaderText = "Write range",    Name = "Range",    FillWeight = 25, ReadOnly = true });

            foreach (var tag in _sharedTags)
                _grid.Rows.Add(false, tag, "…", "…", "…", "…");

            // ── Buttons ───────────────────────────────────────────────────────────
            var pnlButtons = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 52,
                Padding = new Padding(16, 10, 16, 10),
                BackColor = AppTheme.Surface
            };
            pnlButtons.Paint += (s, e) =>
                e.Graphics.DrawLine(new Pen(AppTheme.Border), 0, 0, pnlButtons.Width, 0);

            _btnSelectAll = new FlatButton
            {
                Text = "Select All", ButtonStyle = FlatButtonStyle.Secondary,
                Left = 16, Top = 10, Width = 100, Height = 28
            };
            _btnSelectAll.Click += (s, e) => SetAllChecked(true);

            _btnSelectNone = new FlatButton
            {
                Text = "Select None", ButtonStyle = FlatButtonStyle.Secondary,
                Left = 120, Top = 10, Width = 100, Height = 28
            };
            _btnSelectNone.Click += (s, e) => SetAllChecked(false);

            _btnCancel = new FlatButton
            {
                Text = "Cancel", ButtonStyle = FlatButtonStyle.Secondary,
                Dock = DockStyle.Right, Width = 90
            };
            _btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };

            _btnOk = new FlatButton
            {
                Text = "Start Backfill", ButtonStyle = FlatButtonStyle.Info,
                Dock = DockStyle.Right, Width = 140, Enabled = false
            };
            _btnOk.Click += BtnOk_Click;

            pnlButtons.Controls.Add(_btnSelectAll);
            pnlButtons.Controls.Add(_btnSelectNone);
            pnlButtons.Controls.Add(_btnOk);
            pnlButtons.Controls.Add(_btnCancel);

            // ── Layout (add in reverse dock order) ────────────────────────────────
            Controls.Add(_grid);         // Fill — added first
            Controls.Add(_progress);     // Top
            Controls.Add(_lblProgress);  // Top
            Controls.Add(_lblSummary);   // Top
            Controls.Add(pnlButtons);    // Bottom

            AcceptButton = _btnOk;
            CancelButton = _btnCancel;

            Load     += TagSelectionDialog_Load;
            FormClosing += (s, e) => _cts?.Cancel();
        }

        private void TagSelectionDialog_Load(object sender, EventArgs e)
        {
            // If we don't have the service/conns, skip stat loading and enable OK immediately
            if (_dataService == null || _sourceConn == null || _targetConn == null || _sharedTags.Count == 0)
            {
                _progress.Visible = false;
                _lblProgress.Visible = false;
                _btnOk.Enabled = true;
                return;
            }

            _cts = new CancellationTokenSource();
            _progress.Style = ProgressBarStyle.Continuous;
            _progress.Minimum = 0;
            _progress.Maximum = _sharedTags.Count;
            _progress.Value = 0;

            Task.Run(() => LoadTagStats(_cts.Token));
        }

        private void LoadTagStats(CancellationToken token)
        {
            for (int i = 0; i < _sharedTags.Count; i++)
            {
                if (token.IsCancellationRequested) return;
                string tag = _sharedTags[i];

                int srcCount = 0, tgtCount = 0;
                int missingCount = 0;
                DateTime? writeFirst = null;
                DateTime? writeLast = null;
                string errorText = null;

                try
                {
                    // Read both servers for the full evaluation period
                    var srcSamples = _dataService.ReadRawInRange(_sourceConn, tag, _rangeStart, _rangeEnd);
                    srcCount = srcSamples.Count;

                    List<(DateTime Time, float Value, double Quality)> tgtSamples = null;
                    try
                    {
                        tgtSamples = _dataService.ReadRawInRange(_targetConn, tag, _rangeStart, _rangeEnd);
                        tgtCount = tgtSamples.Count;
                    }
                    catch { tgtSamples = null; /* leave tgtCount=0 */ }

                    // Direct-comparison diff at whole-second resolution — must match the
                    // backfill's own comparison (SampleFilter.ToSecondTicks) so the preview's
                    // "Will copy" count agrees with what ExecuteBackfill actually copies.
                    var tgtTicks = new HashSet<long>(
                        (tgtSamples ?? new List<(DateTime, float, double)>())
                            .Select(s => SampleFilter.ToSecondTicks(s.Time)));
                    foreach (var s in srcSamples)
                    {
                        if (!tgtTicks.Contains(SampleFilter.ToSecondTicks(s.Time)))
                        {
                            missingCount++;
                            if (writeFirst == null || s.Time < writeFirst) writeFirst = s.Time;
                            if (writeLast  == null || s.Time > writeLast)  writeLast  = s.Time;
                        }
                    }
                }
                catch (Exception ex)
                {
                    errorText = ex.Message;
                }

                // Marshal to UI thread to update the row
                if (token.IsCancellationRequested) return;
                int row = i;
                int mc  = missingCount;
                DateTime? wf = writeFirst, wl = writeLast;
                int sc = srcCount, tc = tgtCount;
                string err = errorText;
                BeginInvoke((Action)(() =>
                {
                    if (row >= _grid.Rows.Count) return;
                    if (err != null)
                    {
                        _grid.Rows[row].Cells["Src"].Value     = "err";
                        _grid.Rows[row].Cells["Tgt"].Value     = "err";
                        _grid.Rows[row].Cells["Missing"].Value = "err";
                        _grid.Rows[row].Cells["Range"].Value   = err.Length > 40 ? err.Substring(0, 40) + "…" : err;
                        _grid.Rows[row].DefaultCellStyle.ForeColor = AppTheme.Danger;
                    }
                    else
                    {
                        _grid.Rows[row].Cells["Src"].Value     = sc.ToString("N0");
                        _grid.Rows[row].Cells["Tgt"].Value     = tc.ToString("N0");
                        _grid.Rows[row].Cells["Missing"].Value = mc.ToString("N0");
                        _grid.Rows[row].Cells["Range"].Value   = (wf.HasValue && wl.HasValue)
                            ? $"{wf.Value:MM-dd HH:mm} → {wl.Value:MM-dd HH:mm}"
                            : "—";

                        // Already-in-sync tags (no missing samples): dim the row
                        if (mc == 0)
                            _grid.Rows[row].DefaultCellStyle.ForeColor = AppTheme.TextSecondary;
                    }
                    _progress.Value = Math.Min(row + 1, _progress.Maximum);
                }));
            }

            // Finished
            BeginInvoke((Action)(() =>
            {
                _progress.Visible = false;
                _lblProgress.Text = "Per-tag stats loaded.";
                _lblProgress.ForeColor = AppTheme.Success;
                _btnOk.Enabled = true;
            }));
        }

        private void SetAllChecked(bool state)
        {
            for (int i = 0; i < _grid.Rows.Count; i++)
                _grid.Rows[i].Cells["Sel"].Value = state;
        }

        private void BtnOk_Click(object sender, EventArgs e)
        {
            var selected = new List<string>();
            for (int i = 0; i < _grid.Rows.Count; i++)
            {
                var chk = _grid.Rows[i].Cells["Sel"].Value;
                if (chk is bool b && b)
                    selected.Add((string)_grid.Rows[i].Cells["Tag"].Value);
            }
            if (selected.Count == 0)
            {
                MessageBox.Show(this, "Select at least one tag.", "No Tags Selected",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            SelectedTags = selected;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
