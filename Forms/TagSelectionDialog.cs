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

        // ── Per-tag mini timeline (source vs target coverage + what will be copied) ──
        private readonly GapTimeline _tagTimeline;
        private readonly Label _lblTimelineTag;
        private readonly Dictionary<string, TagPreview> _previews =
            new Dictionary<string, TagPreview>(StringComparer.OrdinalIgnoreCase);

        private sealed class TagPreview
        {
            public double SrcCoverage, TgtCoverage;
            public int SrcCount, TgtCount;
            public List<TimeRange> SrcFill, SrcUnfill, TgtFill, TgtUnfill;
            public List<CopyableSegment> Copyable;
        }

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
            Size            = new Size(820, 700);
            MinimumSize     = new Size(660, 560);
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

            // ── Per-tag timeline (bottom): source vs target coverage for the selected
            //    tag on one shared axis; amber = exactly what this backfill would copy.
            var pnlTagTimeline = new Panel
            {
                Dock = DockStyle.Bottom, Height = 158,
                BackColor = AppTheme.Surface, Padding = new Padding(12, 2, 12, 6)
            };
            pnlTagTimeline.Paint += (s, e) =>
                e.Graphics.DrawLine(new Pen(AppTheme.Border), 0, 0, pnlTagTimeline.Width, 0);

            _lblTimelineTag = new Label
            {
                Text = "TAG TIMELINE — select a tag above",
                Dock = DockStyle.Top, Height = 22,
                Font = AppTheme.SectionLabel, ForeColor = AppTheme.Navy,
                TextAlign = ContentAlignment.MiddleLeft
            };
            _tagTimeline = new GapTimeline
            {
                Dock = DockStyle.Fill,
                Compact = true,
                AllowZoom = false
            };
            _tagTimeline.Clear("Select a tag above to see where its data sits on both servers.");
            pnlTagTimeline.Controls.Add(_tagTimeline);     // Fill
            pnlTagTimeline.Controls.Add(_lblTimelineTag);  // Top

            // ── Layout (add in reverse dock order) ────────────────────────────────
            Controls.Add(_grid);           // Fill — added first
            Controls.Add(_progress);       // Top
            Controls.Add(_lblProgress);    // Top
            Controls.Add(_lblSummary);     // Top
            Controls.Add(pnlTagTimeline);  // Bottom (inner)
            Controls.Add(pnlButtons);      // Bottom (outermost)

            _grid.SelectionChanged += (s, e) => ShowPreviewForSelection();

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
                TagPreview preview = null;

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
                    if (tgtSamples == null) tgtSamples = new List<(DateTime, float, double)>();

                    // SyncPlanner is the same planner ExecuteBackfill uses, so the
                    // preview's "Will copy" count is exactly what a backfill would write
                    // (aligned streams → exact whole-second diff; independent collector
                    // streams → only real target outages, no phantom duplicates).
                    var plan = SyncPlanner.PlanWithConfig(
                        srcSamples.Select(s => s.Time).ToList(),
                        tgtSamples.Select(s => s.Time).ToList(),
                        _rangeStart, _rangeEnd);
                    missingCount = plan.ToCopy.Count;
                    if (plan.ToCopy.Count > 0)
                    {
                        writeFirst = plan.ToCopy[0];
                        writeLast  = plan.ToCopy[plan.ToCopy.Count - 1];
                    }

                    preview = BuildPreview(srcSamples, tgtSamples, plan);
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
                TagPreview pv = preview;
                BeginInvoke((Action)(() =>
                {
                    if (pv != null) _previews[tag] = pv;
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

        /// <summary>
        /// Distills one tag's sample lists into merged intervals for the mini timeline
        /// (same semantics as the main SYNC TIMELINE: red = other side has it, gray =
        /// missing on both, amber = exactly what this backfill would copy).
        /// </summary>
        private TagPreview BuildPreview(
            List<(DateTime Time, float Value, double Quality)> srcSamples,
            List<(DateTime Time, float Value, double Quality)> tgtSamples,
            SyncPlan plan)
        {
            var srcTimes = srcSamples.Select(s => s.Time).ToList();
            var tgtTimes = tgtSamples.Select(s => s.Time).ToList();

            // Per-side gap rule (same statistic the gap detector uses) so the green
            // rendering agrees with what actually counts as missing data for this tag.
            var cfg = SyncPlanner.ReadConfig();
            var srcCov  = IntervalBuilder.CoverageIntervals(srcTimes,
                SyncPlanner.GapRule(srcTimes, cfg.Floor, cfg.Multiplier));
            var tgtCov  = IntervalBuilder.CoverageIntervals(tgtTimes,
                SyncPlanner.GapRule(tgtTimes, cfg.Floor, cfg.Multiplier));
            var srcGaps = IntervalBuilder.Complement(_rangeStart, _rangeEnd, srcCov);
            var tgtGaps = IntervalBuilder.Complement(_rangeStart, _rangeEnd, tgtCov);

            double total = Math.Max(1, (_rangeEnd - _rangeStart).Ticks);
            var preview = new TagPreview
            {
                SrcCount    = srcTimes.Count,
                TgtCount    = tgtTimes.Count,
                SrcCoverage = 1.0 - srcGaps.Sum(gp => (double)gp.Duration.Ticks) / total,
                TgtCoverage = 1.0 - tgtGaps.Sum(gp => (double)gp.Duration.Ticks) / total,
                SrcFill     = IntervalBuilder.Intersect(srcGaps, tgtCov),
                SrcUnfill   = IntervalBuilder.Intersect(srcGaps, tgtGaps),
                TgtFill     = IntervalBuilder.Intersect(tgtGaps, srcCov),
                TgtUnfill   = IntervalBuilder.Intersect(tgtGaps, srcGaps),
                Copyable    = SyncPlanner.ToSegments(
                    plan,
                    toSecondary: _targetLabel == "Secondary",
                    description: $"This backfill would copy these sample(s) from {_sourceLabel} to {_targetLabel}")
            };
            return preview;
        }

        /// <summary>Shows the mini timeline for the tag selected in the grid.</summary>
        private void ShowPreviewForSelection()
        {
            if (_grid.SelectedRows.Count == 0) return;
            string tag = _grid.SelectedRows[0].Cells["Tag"].Value as string;
            if (string.IsNullOrEmpty(tag)) return;

            _lblTimelineTag.Text = $"TAG TIMELINE — {tag}";

            TagPreview pv;
            if (!_previews.TryGetValue(tag, out pv))
            {
                _tagTimeline.Clear("still comparing this tag…");
                return;
            }

            var top = new TimelineTrackData
            {
                Label            = $"SOURCE — {_sourceLabel} · {pv.SrcCount:N0} samples",
                TooltipName      = $"Source ({_sourceLabel})",
                CoverageRatio    = pv.SrcCoverage,
                HasData          = pv.SrcCount > 0,
                FeasibilityKnown = true,
                FillableGaps     = pv.SrcFill,
                UnfillableGaps   = pv.SrcUnfill
            };
            var bottom = new TimelineTrackData
            {
                Label            = $"TARGET — {_targetLabel} · {pv.TgtCount:N0} samples",
                TooltipName      = $"Target ({_targetLabel})",
                CoverageRatio    = pv.TgtCoverage,
                HasData          = pv.TgtCount > 0,
                FeasibilityKnown = true,
                FillableGaps     = pv.TgtFill,
                UnfillableGaps   = pv.TgtUnfill
            };
            _tagTimeline.SetData(_rangeStart, _rangeEnd, top, bottom, pv.Copyable);
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
