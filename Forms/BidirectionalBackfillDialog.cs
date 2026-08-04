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

        // ── Per-tag mini timeline ──────────────────────────────────────────────────
        // Interval data is derived while the diff pass already holds each tag's sample
        // lists — only merged ranges are kept (a few dozen structs per tag), never the
        // raw samples, so hundreds of tags stay cheap in a 32-bit process.
        private readonly GapTimeline _tagTimeline;
        private readonly Label _lblTimelineTag;
        private readonly Dictionary<string, TagPreview> _previews =
            new Dictionary<string, TagPreview>(StringComparer.OrdinalIgnoreCase);

        private sealed class TagPreview
        {
            public double PriCoverage, SecCoverage;
            public int PriCount, SecCount;
            public List<TimeRange> PriFill, PriUnfill, SecFill, SecUnfill;
            public List<CopyableSegment> Copyable;
        }

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
            Text            = Loc.T("dlg.previewBoth");
            Size            = new Size(940, 720);
            MinimumSize     = new Size(720, 560);
            StartPosition   = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox     = true;
            MinimizeBox     = false;
            BackColor       = AppTheme.Background;
            Font            = AppTheme.Default;

            // ── Summary + progress ─────────────────────────────────────────────────
            _lblSummary = new Label
            {
                Text = Loc.F("dlg.comparingRange", _rangeStart.ToString("yyyy-MM-dd HH:mm"),
                                 _rangeEnd.ToString("yyyy-MM-dd HH:mm")) + "\n" +
                       Loc.F("msg.sharedCount", _sharedTags.Count),
                Dock = DockStyle.Top, Height = 46,
                Padding = new Padding(16, 12, 16, 4),
                Font = AppTheme.Default, ForeColor = AppTheme.TextPrimary, AutoSize = false
            };
            _lblProgress = new Label
            {
                Text = Loc.T("dlg.comparing"), Dock = DockStyle.Top, Height = 18,
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
            _split.Panel1.Controls.Add(BuildSide(_gridP2S, Loc.T("dlg.sideToMirror"),
                Loc.T("dlg.sideToMirrorHint"), AppTheme.RowAlt));
            _split.Panel2.Controls.Add(BuildSide(_gridS2P, Loc.T("dlg.sideToMain"),
                Loc.T("dlg.sideToMainHint"), AppTheme.RowAltWarm));

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
                Text = Loc.T("dlg.cancel"), ButtonStyle = FlatButtonStyle.Secondary,
                Dock = DockStyle.Right, Width = 90
            };
            _btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };

            _btnStart = new FlatButton
            {
                Text = Loc.T("dlg.start"), ButtonStyle = FlatButtonStyle.Info,
                Dock = DockStyle.Right, Width = 140, Enabled = false
            };
            _btnStart.Click += BtnStart_Click;

            pnlButtons.Controls.Add(_btnStart);
            pnlButtons.Controls.Add(_btnCancel);

            // ── Per-tag timeline (bottom): click a tag row above to see both servers'
            //    coverage for that tag on one shared axis, with copy candidates in amber.
            var pnlTagTimeline = new Panel
            {
                Dock = DockStyle.Bottom, Height = 158,
                BackColor = AppTheme.Surface, Padding = new Padding(12, 2, 12, 6)
            };
            pnlTagTimeline.Paint += (s, e) =>
                e.Graphics.DrawLine(new Pen(AppTheme.Border), 0, 0, pnlTagTimeline.Width, 0);

            _lblTimelineTag = new Label
            {
                Text = Loc.T("dlg.timelineHint"),
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
            _tagTimeline.Clear(Loc.T("dlg.timelineEmpty"));
            pnlTagTimeline.Controls.Add(_tagTimeline);     // Fill
            pnlTagTimeline.Controls.Add(_lblTimelineTag);  // Top

            // Add in reverse dock order (later-added edge panels dock closer to the edge,
            // so the button row stays outermost below the timeline).
            Controls.Add(_split);          // Fill
            Controls.Add(_progress);       // Top
            Controls.Add(_lblProgress);    // Top
            Controls.Add(_lblSummary);     // Top
            Controls.Add(pnlTagTimeline);  // Bottom (inner)
            Controls.Add(pnlButtons);      // Bottom (outermost)

            _gridP2S.SelectionChanged += (s, e) => ShowPreviewForSelection(_gridP2S);
            _gridS2P.SelectionChanged += (s, e) => ShowPreviewForSelection(_gridS2P);

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
            g.Columns.Add(new DataGridViewTextBoxColumn  { HeaderText = Loc.T("dlg.col.willCopy"), Name = "Missing", FillWeight = 16, ReadOnly = true, DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight } });
            g.Columns.Add(new DataGridViewTextBoxColumn  { HeaderText = Loc.T("dlg.col.range"), Name = "Range",   FillWeight = 34, ReadOnly = true });
            return g;
        }

        /// <summary>One side = title bar + All/None toolbar + grid, all in a panel.</summary>
        private Panel BuildSide(DataGridView grid, string title, string subtitle, Color accent)
        {
            var panel = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.Surface };

            var toolbar = new Panel { Dock = DockStyle.Top, Height = 30, BackColor = AppTheme.Surface };
            var btnAll = new FlatButton { Text = Loc.T("dlg.all"),  ButtonStyle = FlatButtonStyle.Secondary, Left = 8,  Top = 2, Width = 56, Height = 24 };
            var btnNone = new FlatButton { Text = Loc.T("dlg.none"), ButtonStyle = FlatButtonStyle.Secondary, Left = 68, Top = 2, Width = 56, Height = 24 };
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
                    ? Loc.T("dlg.noShared")
                    : Loc.T("dlg.noConnection");
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
                TagPreview preview = null;

                try
                {
                    var priData = _dataService.ReadRawInRange(_primaryConn, tag, _rangeStart, _rangeEnd);
                    List<(DateTime Time, float Value, double Quality)> secData;
                    try { secData = _dataService.ReadRawInRange(_secondaryConn, tag, _rangeStart, _rangeEnd); }
                    catch { secData = new List<(DateTime, float, double)>(); }

                    // SyncPlanner = the same planner the backfill itself uses, so the
                    // listed counts are exactly what pressing Start would write.
                    var priTimes = priData.Select(s => s.Time).ToList();
                    var secTimes = secData.Select(s => s.Time).ToList();
                    var planP2S = SyncPlanner.PlanWithConfig(priTimes, secTimes, _rangeStart, _rangeEnd);
                    var planS2P = SyncPlanner.PlanWithConfig(secTimes, priTimes, _rangeStart, _rangeEnd);

                    p2s = planP2S.ToCopy.Count;
                    if (p2s > 0) { p2sFirst = planP2S.ToCopy[0]; p2sLast = planP2S.ToCopy[p2s - 1]; }
                    s2p = planS2P.ToCopy.Count;
                    if (s2p > 0) { s2pFirst = planS2P.ToCopy[0]; s2pLast = planS2P.ToCopy[s2p - 1]; }

                    // Distill the sample lists into interval data for the mini timeline
                    // while we still hold them (they are not kept beyond this iteration).
                    preview = BuildPreview(priData, secData, planP2S, planS2P);
                }
                catch { /* tag failed on source read — skip; backfill will report any real errors */ }

                if (token.IsCancellationRequested) return;
                int idx = i;
                int cp2s = p2s, cs2p = s2p;
                DateTime? pf = p2sFirst, pl = p2sLast, sf = s2pFirst, sl = s2pLast;
                TagPreview pv = preview;
                BeginInvoke((Action)(() =>
                {
                    if (pv != null) _previews[tag] = pv;
                    if (cp2s > 0) { _gridP2S.Rows.Add(true, tag, cp2s.ToString("N0"), RangeText(pf, pl)); _p2sRows++; }
                    if (cs2p > 0) { _gridS2P.Rows.Add(true, tag, cs2p.ToString("N0"), RangeText(sf, sl)); _s2pRows++; }
                    _progress.Value = Math.Min(idx + 1, _progress.Maximum);
                    _lblProgress.Text = Loc.F("dlg.comparingN", idx + 1, _sharedTags.Count);
                }));
            }

            BeginInvoke((Action)FinishLoading);
        }

        private void FinishLoading()
        {
            _progress.Visible = false;
            if (_p2sRows == 0 && _s2pRows == 0)
            {
                _lblProgress.Text = Loc.T("dlg.inSyncBoth");
                _lblProgress.ForeColor = AppTheme.Success;
                _btnStart.Enabled = false;
            }
            else
            {
                _lblProgress.Text = Loc.F("dlg.readyCounts", _p2sRows, _s2pRows);
                _lblProgress.ForeColor = AppTheme.Success;
                _btnStart.Enabled = true;
            }
        }

        private static string RangeText(DateTime? a, DateTime? b) =>
            (a.HasValue && b.HasValue) ? $"{a.Value:MM-dd HH:mm} → {b.Value:MM-dd HH:mm}" : "—";

        /// <summary>
        /// Distills one tag's sample lists into merged intervals for the mini timeline:
        /// coverage per server, red/gray gap split (other-server-has-it vs missing-on-both)
        /// and the amber copy-candidate runs — the same semantics as the main timeline.
        /// </summary>
        private TagPreview BuildPreview(
            List<(DateTime Time, float Value, double Quality)> priData,
            List<(DateTime Time, float Value, double Quality)> secData,
            SyncPlan planP2S, SyncPlan planS2P)
        {
            var priTimes = priData.Select(s => s.Time).ToList();
            var secTimes = secData.Select(s => s.Time).ToList();

            // Per-side gap rule (same statistic the gap detector uses) so green rendering
            // matches what actually counts as missing data for this tag's cadence.
            var cfg = SyncPlanner.ReadConfig();
            var priCov  = IntervalBuilder.CoverageIntervals(priTimes,
                SyncPlanner.GapRule(priTimes, cfg.Floor, cfg.Multiplier));
            var secCov  = IntervalBuilder.CoverageIntervals(secTimes,
                SyncPlanner.GapRule(secTimes, cfg.Floor, cfg.Multiplier));
            var priGaps = IntervalBuilder.Complement(_rangeStart, _rangeEnd, priCov);
            var secGaps = IntervalBuilder.Complement(_rangeStart, _rangeEnd, secCov);

            double total = Math.Max(1, (_rangeEnd - _rangeStart).Ticks);
            var preview = new TagPreview
            {
                PriCount    = priTimes.Count,
                SecCount    = secTimes.Count,
                PriCoverage = 1.0 - priGaps.Sum(gp => (double)gp.Duration.Ticks) / total,
                SecCoverage = 1.0 - secGaps.Sum(gp => (double)gp.Duration.Ticks) / total,
                // red = the other server has data there; gray = nobody does
                PriFill   = IntervalBuilder.Intersect(priGaps, secCov),
                PriUnfill = IntervalBuilder.Intersect(priGaps, secGaps),
                SecFill   = IntervalBuilder.Intersect(secGaps, priCov),
                SecUnfill = IntervalBuilder.Intersect(secGaps, priGaps),
                Copyable  = new List<CopyableSegment>()
            };
            preview.Copyable.AddRange(SyncPlanner.ToSegments(planP2S, toSecondary: true));
            preview.Copyable.AddRange(SyncPlanner.ToSegments(planS2P, toSecondary: false));
            return preview;
        }

        /// <summary>Shows the mini timeline for the tag selected in either grid.</summary>
        private void ShowPreviewForSelection(DataGridView grid)
        {
            if (grid.SelectedRows.Count == 0) return;
            string tag = grid.SelectedRows[0].Cells["Tag"].Value as string;
            if (string.IsNullOrEmpty(tag)) return;

            TagPreview pv;
            if (!_previews.TryGetValue(tag, out pv))
            {
                _lblTimelineTag.Text = $"TAG TIMELINE — {tag}";
                _tagTimeline.Clear("still comparing this tag…");
                return;
            }

            _lblTimelineTag.Text = $"TAG TIMELINE — {tag}";
            var top = new TimelineTrackData
            {
                Label            = Loc.F("dlg.trackMain", pv.PriCount.ToString("N0")),
                CoverageRatio    = pv.PriCoverage,
                HasData          = pv.PriCount > 0,
                FeasibilityKnown = true,
                FillableGaps     = pv.PriFill,
                UnfillableGaps   = pv.PriUnfill
            };
            var bottom = new TimelineTrackData
            {
                Label            = Loc.F("dlg.trackMirror", pv.SecCount.ToString("N0")),
                CoverageRatio    = pv.SecCoverage,
                HasData          = pv.SecCount > 0,
                FeasibilityKnown = true,
                FillableGaps     = pv.SecFill,
                UnfillableGaps   = pv.SecUnfill
            };
            _tagTimeline.SetData(_rangeStart, _rangeEnd, top, bottom, pv.Copyable);
        }

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
                MessageBox.Show(this, Loc.T("dlg.nothingSelected"),
                    Loc.T("dlg.nothingSelectedTitle"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            SelectedPrimaryToSecondary = p2s;
            SelectedSecondaryToPrimary = s2p;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
