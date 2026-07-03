using HistorianSyncTool.Models;
using HistorianSyncTool.Properties;
using HistorianSyncTool.Services;
using HistorianSyncTool.UI;
using HistorianSyncTool.UI.Controls;
using Proficy.Historian.ClientAccess.API;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace HistorianSyncTool.Forms
{
    public partial class MainForm : Form
    {
        // ── Services ───────────────────────────────────────────────────────────────
        private readonly HistorianConnectionService _connections = new HistorianConnectionService();
        private readonly HistorianDataService       _data;
        private readonly GapAnalysisService         _gapAnalysis = new GapAnalysisService();

        // ── Gap Analysis state ─────────────────────────────────────────────────────
        private GapAnalysisResult _lastPrimaryResult;
        private GapAnalysisResult _lastSecondaryResult;

        // ── Cancellation ───────────────────────────────────────────────────────────
        private CancellationTokenSource _cts;
        private bool _isBusy;

        // ── Action buttons to disable during long ops ──────────────────────────────
        private List<Control> _actionButtons;

        // ── Virtual mode backing data ──────────────────────────────────────────────
        private List<GridRow> _primaryRows   = new List<GridRow>();
        private List<GridRow> _secondaryRows = new List<GridRow>();

        // ── Compare state ──────────────────────────────────────────────────────────
        private bool _isCompareMode;
        private List<(DateTime Time, float Value, double Quality)> _rawPrimarySamples;
        private List<(DateTime Time, float Value, double Quality)> _rawSecondarySamples;

        // ── Scroll sync ────────────────────────────────────────────────────────────
        private bool _scrollSyncEnabled;
        private bool _isSyncing;

        // ── Auto-read suppression (prevents reads during DataSource assignment) ──
        private bool _suppressAutoRead;

        // ── Auto-analyze debounce: re-runs gap analysis ~500ms after a TAG change.
        //    Date/time edits & the quick presets do NOT auto-run — a new time range is
        //    analyzed only when the user clicks "Analyze Gaps" (or click-to-zoom). ──
        private System.Windows.Forms.Timer _gapAutoAnalyzeTimer;

        // ── Unattended scheduler (Phase 7) ─────────────────────────────────────────
        private ScheduleService _schedule;

        // ── Modal progress dialog (Phase 9) ────────────────────────────────────────
        // One Cancel button, one progress surface, and the main window is blocked
        // while an operation runs. The dialog only appears if the operation outlives
        // a short delay, so quick actions don't flash a window.
        private ProgressDialog _progressDlg;
        private System.Windows.Forms.Timer _progressShowTimer;
        private string _pendingOpTitle = "Working…";
        private bool _suppressOpDialog;   // true during scheduled/headless runs

        // ── Tag link: primary tag ↔ secondary tag (Phase 9) ────────────────────────
        private bool _tagLinkEnabled = true;
        private bool _isLinkPropagating;

        // ── Timeline zoom history (Phase 9) ────────────────────────────────────────
        private readonly Stack<(DateTime From, DateTime To)> _zoomStack =
            new Stack<(DateTime From, DateTime To)>();

        // ── Live edge: exclude the trailing N seconds from every backfill diff.
        // On live servers the collectors are still writing there, so evaluating up to
        // "now" reports in-flight samples as missing on every run — an endless backfill.
        private static readonly TimeSpan LiveEdgeGrace = ReadLiveEdgeGrace();

        private static TimeSpan ReadLiveEdgeGrace()
        {
            int seconds;
            string cfg = ConfigurationManager.AppSettings["LiveEdgeGraceSeconds"];
            return TimeSpan.FromSeconds(
                int.TryParse(cfg, out seconds) && seconds >= 0 ? seconds : 120);
        }

        // ── Last-browsed tag names per server (for the scheduler's tag multiselect) ──
        private string[] _browsedPrimaryTags   = new string[0];
        private string[] _browsedSecondaryTags = new string[0];

        // ── GridRow model ──────────────────────────────────────────────────────────
        private class GridRow
        {
            public DateTime RawTime;
            public string Timestamp;
            public string Value;
            public string Quality;
            public bool IsSpacer;
            public bool IsExtra;
            public bool IsMismatch;
        }

        // ── Colors ─────────────────────────────────────────────────────────────────
        private static readonly Color ColorSpacer   = Color.FromArgb(245, 245, 245);
        private static readonly Color ColorExtra    = Color.FromArgb(232, 255, 232);
        private static readonly Color ColorMismatch = Color.FromArgb(255, 248, 220);

        // ── Constructor ────────────────────────────────────────────────────────────
        public MainForm()
        {
            InitializeComponent();

            int maxRetries;
            string retryStr = ConfigurationManager.AppSettings["MaxRetryAttempts"];
            maxRetries = int.TryParse(retryStr, out maxRetries) ? maxRetries : 3;
            _data = new HistorianDataService(maxRetries);

            ApplyTheme();
            SetupVirtualMode();
            LoadSettings();
            UpdateConnectionStatus();
            UpdateTitleBar();

            _actionButtons = new List<Control>
            {
                btnConnect, btnBrowseTags, btnGetStats,
                btnReadPrimary, btnReadSecondary, btnCompare,
                btnCopyToPrimary, btnCopyToSecondary,
                btnAnalyzeGaps, btnBackfillPreview, btnHistory,
                btnTagLink
            };

            // Debounced auto-analyze after a TAG change only. Date/time edits and the
            // quick-select presets deliberately do NOT auto-run — analysis for a new
            // time range runs only when the user clicks "Analyze Gaps" (boss request
            // 2026-07: editing day/month/hour used to fire a run on every field change).
            _gapAutoAnalyzeTimer = new System.Windows.Forms.Timer { Interval = 500 };
            _gapAutoAnalyzeTimer.Tick += GapAutoAnalyzeTimer_Tick;

            // Modal progress dialog: appears only when an operation runs longer than this
            _progressShowTimer = new System.Windows.Forms.Timer { Interval = 400 };
            _progressShowTimer.Tick += ProgressShowTimer_Tick;

            // Timeline interactivity: click a gap → zoom the date range to it
            timeline.ZoomRequested += async (zoomFrom, zoomTo) => await ZoomTo(zoomFrom, zoomTo);
            gridGaps.CellClick += gridGaps_CellClick;

            // Tag link (same tag on both servers) — persisted preference
            _tagLinkEnabled = Settings.Default.TagLinkEnabled;
            UpdateTagLinkVisual();

            // Unattended scheduler — applies persisted settings, wires status indicator
            _schedule = new ScheduleService(RunScheduledBackfillAsync);
            _schedule.StatusChanged += (s, e2) => UpdateScheduleStatusLabel();
            ApplyScheduleSettings();
            UpdateScheduleStatusLabel();

            // If "run on startup" is enabled and we land already connected (rare on cold
            // start but possible after reconnect), trigger a one-shot run after the form
            // has a chance to load. Otherwise it waits for the user to Connect.
            if (Settings.Default.ScheduleEnabled && Settings.Default.ScheduleRunOnStartup)
                BeginInvoke((Action)(async () => { await TryRunScheduledOnStartup(); }));
        }

        private async Task TryRunScheduledOnStartup()
        {
            await Task.Delay(2000); // let the form finish painting before we kick a run
            if (_isBusy) return;
            if (!_connections.IsPrimaryConnected || !_connections.IsSecondaryConnected) return;
            try { await _schedule.TriggerNowAsync(); }
            catch (Exception ex) { ScheduleLogger.Append($"Startup-run failed: {ex.Message}"); }
        }

        private async void GapAutoAnalyzeTimer_Tick(object sender, EventArgs e)
        {
            _gapAutoAnalyzeTimer.Stop();
            if (_isBusy) return;
            DateTime from = dtpStart.Value;
            DateTime to   = dtpEnd.Value;
            if (from >= to) return;
            await RunGapAnalysis(from, to);
        }

        // ── Startup / Shutdown ─────────────────────────────────────────────────────
        private void ApplyTheme()
        {
            AppTheme.StyleGrid(gridPrimary);
            AppTheme.StyleGrid(gridSecondary);
            // gridGaps is fully styled in SetupGapGrid — StyleGrid here would reset
            // its taller wrapped header (ColumnHeadersHeight) back to the default.
        }

        private void SetupVirtualMode()
        {
            // Primary grid
            gridPrimary.VirtualMode = true;
            gridPrimary.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Timestamp", Name = "Timestamp", FillWeight = 40 });
            gridPrimary.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Value",     Name = "Value",     FillWeight = 35 });
            gridPrimary.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Quality",   Name = "Quality",   FillWeight = 25 });
            gridPrimary.CellValueNeeded += gridPrimary_CellValueNeeded;
            gridPrimary.RowPrePaint     += grid_RowPrePaint;
            gridPrimary.Scroll          += gridPrimary_Scroll;
            gridPrimary.SelectionChanged += gridPrimary_SelectionChanged;
            gridPrimary.ReadOnly = true;
            gridPrimary.AllowUserToAddRows = false;
            gridPrimary.AutoGenerateColumns = false;

            // Secondary grid
            gridSecondary.VirtualMode = true;
            gridSecondary.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Timestamp", Name = "Timestamp", FillWeight = 40 });
            gridSecondary.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Value",     Name = "Value",     FillWeight = 35 });
            gridSecondary.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Quality",   Name = "Quality",   FillWeight = 25 });
            gridSecondary.CellValueNeeded += gridSecondary_CellValueNeeded;
            gridSecondary.RowPrePaint     += grid_RowPrePaint;
            gridSecondary.Scroll          += gridSecondary_Scroll;
            gridSecondary.SelectionChanged += gridSecondary_SelectionChanged;
            gridSecondary.ReadOnly = true;
            gridSecondary.AllowUserToAddRows = false;
            gridSecondary.AutoGenerateColumns = false;
        }

        // ── Virtual mode events ────────────────────────────────────────────────────
        private void gridPrimary_CellValueNeeded(object sender, DataGridViewCellValueEventArgs e)
        {
            if (e.RowIndex >= _primaryRows.Count) return;
            var row = _primaryRows[e.RowIndex];
            switch (e.ColumnIndex)
            {
                case 0: e.Value = row.Timestamp; break;
                case 1: e.Value = row.Value;     break;
                case 2: e.Value = row.Quality;   break;
            }
        }

        private void gridSecondary_CellValueNeeded(object sender, DataGridViewCellValueEventArgs e)
        {
            if (e.RowIndex >= _secondaryRows.Count) return;
            var row = _secondaryRows[e.RowIndex];
            switch (e.ColumnIndex)
            {
                case 0: e.Value = row.Timestamp; break;
                case 1: e.Value = row.Value;     break;
                case 2: e.Value = row.Quality;   break;
            }
        }

        private void grid_RowPrePaint(object sender, DataGridViewRowPrePaintEventArgs e)
        {
            var grid = (DataGridView)sender;
            var rows = grid == gridPrimary ? _primaryRows : _secondaryRows;
            if (e.RowIndex >= rows.Count) return;
            var row = rows[e.RowIndex];

            Color bg;
            if (row.IsSpacer)        bg = ColorSpacer;
            else if (row.IsExtra)    bg = ColorExtra;
            else if (row.IsMismatch) bg = ColorMismatch;
            else return;

            grid.Rows[e.RowIndex].DefaultCellStyle.BackColor = bg;
            if (row.IsSpacer)
                grid.Rows[e.RowIndex].DefaultCellStyle.ForeColor = AppTheme.TextSecondary;
        }

        private void UpdateGridRowCount(DataGridView grid, int count)
        {
            grid.RowCount = 0;
            grid.RowCount = count;
            grid.Invalidate();
        }

        // ── Scroll sync ────────────────────────────────────────────────────────────
        private void gridPrimary_Scroll(object sender, ScrollEventArgs e)
        {
            if (!_scrollSyncEnabled || _isSyncing) return;
            _isSyncing = true;
            try
            {
                if (gridPrimary.FirstDisplayedScrollingRowIndex >= 0 && gridSecondary.RowCount > 0)
                    gridSecondary.FirstDisplayedScrollingRowIndex =
                        Math.Min(gridPrimary.FirstDisplayedScrollingRowIndex, gridSecondary.RowCount - 1);
            }
            finally { _isSyncing = false; }
        }

        private void gridSecondary_Scroll(object sender, ScrollEventArgs e)
        {
            if (!_scrollSyncEnabled || _isSyncing) return;
            _isSyncing = true;
            try
            {
                if (gridSecondary.FirstDisplayedScrollingRowIndex >= 0 && gridPrimary.RowCount > 0)
                    gridPrimary.FirstDisplayedScrollingRowIndex =
                        Math.Min(gridSecondary.FirstDisplayedScrollingRowIndex, gridPrimary.RowCount - 1);
            }
            finally { _isSyncing = false; }
        }

        private void gridPrimary_SelectionChanged(object sender, EventArgs e)
        {
            if (!_scrollSyncEnabled || _isSyncing) return;
            _isSyncing = true;
            try
            {
                if (gridPrimary.CurrentRow != null && gridSecondary.RowCount > gridPrimary.CurrentRow.Index)
                    gridSecondary.CurrentCell = gridSecondary[0, gridPrimary.CurrentRow.Index];
            }
            finally { _isSyncing = false; }
        }

        private void gridSecondary_SelectionChanged(object sender, EventArgs e)
        {
            if (!_scrollSyncEnabled || _isSyncing) return;
            _isSyncing = true;
            try
            {
                if (gridSecondary.CurrentRow != null && gridPrimary.RowCount > gridSecondary.CurrentRow.Index)
                    gridPrimary.CurrentCell = gridPrimary[0, gridSecondary.CurrentRow.Index];
            }
            finally { _isSyncing = false; }
        }

        private void btnSyncScroll_Click(object sender, EventArgs e)
        {
            _scrollSyncEnabled = !_scrollSyncEnabled;
            btnSyncScroll.Text = _scrollSyncEnabled ? "Unsync" : "Sync Scroll";
        }

        // ── Settings ───────────────────────────────────────────────────────────────
        private void LoadSettings()
        {
            var s = Settings.Default;
            txtPrimary.Text   = s.PrimaryHostname;
            txtSecondary.Text = s.SecondaryHostname;
            txtTagnameFilter.Text = s.TagnameFilter;

            dtpStart.Value = s.StartDate > DateTime.MinValue
                ? s.StartDate : DateTime.Now.AddMonths(-1);
            dtpEnd.Value = s.EndDate > DateTime.MinValue && s.EndDate > s.StartDate
                ? s.EndDate : DateTime.Now;
        }

        private void SaveSettings()
        {
            var s = Settings.Default;
            s.PrimaryHostname      = txtPrimary.Text.Trim();
            s.SecondaryHostname    = txtSecondary.Text.Trim();
            s.TagnameFilter        = txtTagnameFilter.Text.Trim();
            s.StartDate            = dtpStart.Value;
            s.EndDate              = dtpEnd.Value;
            s.TagLinkEnabled       = _tagLinkEnabled;
            s.Save();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            SaveSettings();
            try { _cts?.Cancel(); } catch { }
            try { _cts?.Dispose(); } catch { }
            try { _gapAutoAnalyzeTimer?.Stop(); _gapAutoAnalyzeTimer?.Dispose(); } catch { }
            try { _progressShowTimer?.Stop(); _progressShowTimer?.Dispose(); } catch { }
            try { _progressDlg?.RequestClose(); } catch { }
            try { _schedule?.Dispose(); } catch { }
            _connections.Dispose();
            base.OnFormClosing(e);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            // After DPI auto-scaling, ensure form fits within the current screen
            var wa = Screen.FromControl(this).WorkingArea;
            if (Width > wa.Width) Width = wa.Width;
            if (Height > wa.Height) Height = wa.Height;
            if (Left + Width > wa.Right) Left = wa.Right - Width;
            if (Top + Height > wa.Bottom) Top = wa.Bottom - Height;
        }

        // ── Title bar ──────────────────────────────────────────────────────────────
        private void UpdateTitleBar()
        {
            string pri = string.IsNullOrWhiteSpace(txtPrimary.Text) ? "—" : txtPrimary.Text.Trim();
            string sec = string.IsNullOrWhiteSpace(txtSecondary.Text) ? "—" : txtSecondary.Text.Trim();
            bool bothConnected = _connections.IsPrimaryConnected && _connections.IsSecondaryConnected;
            Text = bothConnected
                ? $"Historian Sync Tool  —  {pri}  ↔  {sec}"
                : "Historian Sync Tool";
        }

        // ── Status helpers ─────────────────────────────────────────────────────────
        private void SetStatus(string message, bool isError = false)
        {
            if (InvokeRequired) { Invoke((Action)(() => SetStatus(message, isError))); return; }
            lblStatus.Text      = message;
            lblStatus.ForeColor = isError ? AppTheme.Danger : AppTheme.TextPrimary;
            _progressDlg?.UpdateDetail(message);
            Log(message);
        }

        private void SetBusy(bool busy, string operationLabel = "")
        {
            if (InvokeRequired) { Invoke((Action)(() => SetBusy(busy, operationLabel))); return; }
            _isBusy = busy;

            foreach (var btn in _actionButtons)
                btn.Enabled = !busy;

            if (busy)
            {
                _pendingOpTitle = string.IsNullOrWhiteSpace(operationLabel) ? "Working…" : operationLabel;
                if (!_suppressOpDialog)
                {
                    _progressShowTimer.Stop();
                    _progressShowTimer.Start();   // dialog appears only if the op outlives the delay
                }
            }
            else
            {
                _progressShowTimer.Stop();
                _progressDlg?.RequestClose();     // unwinds the nested ShowDialog pump
            }
        }

        /// <summary>
        /// Delayed-show tick: the operation is still running after the grace period, so
        /// bring up the modal progress dialog. ShowDialog pumps messages, which keeps the
        /// running operation's async continuations and worker Invokes flowing; SetBusy(false)
        /// closes the dialog and control returns here.
        /// </summary>
        private void ProgressShowTimer_Tick(object sender, EventArgs e)
        {
            _progressShowTimer.Stop();
            if (!_isBusy || _suppressOpDialog || _progressDlg != null) return;

            using (var dlg = new ProgressDialog(_pendingOpTitle))
            {
                _progressDlg = dlg;
                dlg.CancelRequested += (s2, e2) => { try { _cts?.Cancel(); } catch { } };
                try { dlg.ShowDialog(this); }
                finally { _progressDlg = null; }
            }
        }

        private void SetProgress(int current, int total)
        {
            if (InvokeRequired) { Invoke((Action)(() => SetProgress(current, total))); return; }
            if (total <= 0) return;
            _progressDlg?.UpdateStep(current, total, $"Batch {current} / {total}");
        }

        private void SetPhaseProgress(int current, int total, string label)
        {
            if (InvokeRequired) { Invoke((Action)(() => SetPhaseProgress(current, total, label))); return; }
            _progressDlg?.UpdatePhase(current, total, label);
        }

        private void Log(string message)
        {
            if (InvokeRequired) { Invoke((Action)(() => Log(message))); return; }
            txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}]  {message}{Environment.NewLine}");
            txtLog.SelectionStart = txtLog.TextLength;
            txtLog.ScrollToCaret();
        }

        private void UpdateConnectionStatus()
        {
            if (InvokeRequired) { Invoke((Action)UpdateConnectionStatus); return; }

            bool pri = _connections.IsPrimaryConnected;
            bool sec = _connections.IsSecondaryConnected;

            lblPrimaryStatus.Text      = pri ? "Connected" : "Not connected";
            lblPrimaryStatus.ForeColor = pri ? AppTheme.Success : AppTheme.TextSecondary;
            txtPrimary.BackColor       = pri ? Color.FromArgb(240, 255, 245) : SystemColors.Window;

            lblSecondaryStatus.Text      = sec ? "Connected" : (string.IsNullOrWhiteSpace(txtSecondary.Text) ? "—" : "Not connected");
            lblSecondaryStatus.ForeColor = sec ? AppTheme.Success : AppTheme.TextSecondary;
            txtSecondary.BackColor       = sec ? Color.FromArgb(240, 255, 245) : SystemColors.Window;

            dotStatus.State = (pri || sec) ? ConnectionState.Connected : ConnectionState.Disconnected;
            UpdateTitleBar();
        }

        private void SetConnecting()
        {
            if (InvokeRequired) { Invoke((Action)SetConnecting); return; }
            lblPrimaryStatus.Text      = "Connecting…";
            lblPrimaryStatus.ForeColor = AppTheme.Warning;
            lblSecondaryStatus.Text    = string.IsNullOrWhiteSpace(txtSecondary.Text) ? "—" : "Connecting…";
            lblSecondaryStatus.ForeColor = AppTheme.Warning;
            dotStatus.State = ConnectionState.Connecting;
        }

        private void SetConnectionError()
        {
            if (InvokeRequired) { Invoke((Action)SetConnectionError); return; }
            lblPrimaryStatus.Text      = "Connection failed";
            lblPrimaryStatus.ForeColor = AppTheme.Danger;
            txtPrimary.BackColor       = SystemColors.Window;
            lblSecondaryStatus.Text    = string.IsNullOrWhiteSpace(txtSecondary.Text) ? "—" : "Connection failed";
            lblSecondaryStatus.ForeColor = AppTheme.Danger;
            txtSecondary.BackColor     = SystemColors.Window;
            dotStatus.State = ConnectionState.Error;
        }

        /// <summary>
        /// Disposes the previous CancellationTokenSource (if any) and allocates a new one.
        /// Replaces bare `_cts = new CancellationTokenSource()` assignments to avoid per-op leaks.
        /// </summary>
        private void ResetCts()
        {
            try { _cts?.Dispose(); } catch { }
            _cts = new CancellationTokenSource();
        }

        // ── Grid data helpers ──────────────────────────────────────────────────────
        private List<GridRow> SamplesToGridRows(List<(DateTime Time, float Value, double Quality)> samples)
        {
            return samples.Select(s => new GridRow
            {
                RawTime   = s.Time,
                Timestamp = s.Time.ToString("yyyy-MM-dd HH:mm:ss"),
                Value     = s.Value.ToString("G6"),
                Quality   = $"{s.Quality:F1}%"
            }).ToList();
        }

        private void ExitCompareMode()
        {
            if (!_isCompareMode) return;
            _isCompareMode = false;
            btnCompare.Text = "Compare";
        }

        // ── Alignment algorithm ────────────────────────────────────────────────────
        private void AlignGridData(
            List<(DateTime Time, float Value, double Quality)> priSamples,
            List<(DateTime Time, float Value, double Quality)> secSamples)
        {
            var priAligned = new List<GridRow>();
            var secAligned = new List<GridRow>();

            // Compute tolerance from median interval of the larger set
            var larger = priSamples.Count >= secSamples.Count ? priSamples : secSamples;
            TimeSpan tolerance = TimeSpan.Zero;
            if (larger.Count >= 2)
            {
                var deltas = new List<long>();
                for (int k = 1; k < larger.Count; k++)
                    deltas.Add((larger[k].Time - larger[k - 1].Time).Ticks);
                deltas.Sort();
                long medianTicks = deltas[deltas.Count / 2];
                tolerance = TimeSpan.FromTicks(medianTicks / 2);
            }
            if (tolerance.Ticks <= 0)
                tolerance = TimeSpan.FromSeconds(5);

            int i = 0, j = 0;
            while (i < priSamples.Count && j < secSamples.Count)
            {
                var pTime = priSamples[i].Time;
                var sTime = secSamples[j].Time;
                TimeSpan diff = pTime - sTime;

                if (diff.Duration() <= tolerance)
                {
                    // Matched
                    bool mismatch = Math.Abs(priSamples[i].Value - secSamples[j].Value) > 0.001f;
                    priAligned.Add(new GridRow
                    {
                        RawTime   = pTime,
                        Timestamp = pTime.ToString("yyyy-MM-dd HH:mm:ss"),
                        Value     = priSamples[i].Value.ToString("G6"),
                        Quality   = $"{priSamples[i].Quality:F1}%",
                        IsMismatch = mismatch
                    });
                    secAligned.Add(new GridRow
                    {
                        RawTime   = sTime,
                        Timestamp = sTime.ToString("yyyy-MM-dd HH:mm:ss"),
                        Value     = secSamples[j].Value.ToString("G6"),
                        Quality   = $"{secSamples[j].Quality:F1}%",
                        IsMismatch = mismatch
                    });
                    i++; j++;
                }
                else if (pTime < sTime)
                {
                    // Primary-only
                    priAligned.Add(new GridRow
                    {
                        RawTime   = pTime,
                        Timestamp = pTime.ToString("yyyy-MM-dd HH:mm:ss"),
                        Value     = priSamples[i].Value.ToString("G6"),
                        Quality   = $"{priSamples[i].Quality:F1}%",
                        IsExtra   = true
                    });
                    secAligned.Add(new GridRow
                    {
                        RawTime   = pTime,
                        Timestamp = pTime.ToString("yyyy-MM-dd HH:mm:ss"),
                        IsSpacer  = true
                    });
                    i++;
                }
                else
                {
                    // Secondary-only
                    priAligned.Add(new GridRow
                    {
                        RawTime   = sTime,
                        Timestamp = sTime.ToString("yyyy-MM-dd HH:mm:ss"),
                        IsSpacer  = true
                    });
                    secAligned.Add(new GridRow
                    {
                        RawTime   = sTime,
                        Timestamp = sTime.ToString("yyyy-MM-dd HH:mm:ss"),
                        Value     = secSamples[j].Value.ToString("G6"),
                        Quality   = $"{secSamples[j].Quality:F1}%",
                        IsExtra   = true
                    });
                    j++;
                }
            }

            // Remaining primary
            while (i < priSamples.Count)
            {
                var pTime = priSamples[i].Time;
                priAligned.Add(new GridRow
                {
                    RawTime = pTime, Timestamp = pTime.ToString("yyyy-MM-dd HH:mm:ss"),
                    Value = priSamples[i].Value.ToString("G6"), Quality = $"{priSamples[i].Quality:F1}%",
                    IsExtra = true
                });
                secAligned.Add(new GridRow
                {
                    RawTime = pTime, Timestamp = pTime.ToString("yyyy-MM-dd HH:mm:ss"),
                    IsSpacer = true
                });
                i++;
            }

            // Remaining secondary
            while (j < secSamples.Count)
            {
                var sTime = secSamples[j].Time;
                priAligned.Add(new GridRow
                {
                    RawTime = sTime, Timestamp = sTime.ToString("yyyy-MM-dd HH:mm:ss"),
                    IsSpacer = true
                });
                secAligned.Add(new GridRow
                {
                    RawTime = sTime, Timestamp = sTime.ToString("yyyy-MM-dd HH:mm:ss"),
                    Value = secSamples[j].Value.ToString("G6"), Quality = $"{secSamples[j].Quality:F1}%",
                    IsExtra = true
                });
                j++;
            }

            _primaryRows   = priAligned;
            _secondaryRows = secAligned;
        }

        // ── Connection ─────────────────────────────────────────────────────────────
        private async void btnConnect_Click(object sender, EventArgs e)
        {
            string pri = txtPrimary.Text.Trim();
            string sec = txtSecondary.Text.Trim();
            if (string.IsNullOrWhiteSpace(pri)) { SetStatus("Enter primary server hostname.", true); return; }

            SetBusy(true, "Connecting...");
            SetStatus("Connecting to servers…");
            SetConnecting();

            try
            {
                ResetCts();
                await Task.Run(() =>
                {
                    _connections.ConnectPrimary(pri);
                    if (!string.IsNullOrWhiteSpace(sec))
                        _connections.ConnectSecondary(sec);
                }, _cts.Token);

                UpdateConnectionStatus();
                SetStatus($"Connected to {pri}" + (string.IsNullOrWhiteSpace(sec) ? "." : $" and {sec}."));
            }
            catch (OperationCanceledException)
            {
                UpdateConnectionStatus();
                SetStatus("Connection cancelled.");
            }
            catch (Exception ex)
            {
                SetConnectionError();
                SetStatus($"Connection failed: {ex.Message}", true);
            }
            finally { SetBusy(false); }
        }

        // ── Browse Tags ────────────────────────────────────────────────────────────
        private async void btnBrowseTags_Click(object sender, EventArgs e)
        {
            if (!_connections.IsPrimaryConnected && !_connections.IsSecondaryConnected)
            { SetStatus("Connect to a server first.", true); return; }

            SetBusy(true);
            SetStatus("Browsing tags…");
            string mask = string.IsNullOrWhiteSpace(txtTagnameFilter.Text) ? "*" : txtTagnameFilter.Text.Trim();
            bool priConn = _connections.IsPrimaryConnected;
            bool secConn = _connections.IsSecondaryConnected;

            try
            {
                ResetCts();
                var token = _cts.Token;

                Tag[] priTags = null;
                Tag[] secTags = null;

                await Task.Run(() =>
                {
                    token.ThrowIfCancellationRequested();
                    if (priConn)
                        priTags = _data.BrowseTags(_connections.Primary, mask).ToArray();
                    token.ThrowIfCancellationRequested();
                    if (secConn)
                        secTags = _data.BrowseTags(_connections.Secondary, mask).ToArray();
                }, token);

                _suppressAutoRead = true;
                try
                {
                    if (priTags != null)
                    {
                        cboPrimary.DataSource    = priTags;
                        cboPrimary.DisplayMember = "Name";
                        cboPrimary.ValueMember   = "Name";
                        SetAutoComplete(cboPrimary, priTags);
                    }
                    if (secTags != null)
                    {
                        cboSecondary.DataSource    = secTags;
                        cboSecondary.DisplayMember = "Name";
                        cboSecondary.ValueMember   = "Name";
                        SetAutoComplete(cboSecondary, secTags);
                    }
                }
                finally { _suppressAutoRead = false; }

                // Remember names so the scheduler dialog can offer a tag multiselect.
                if (priTags != null) _browsedPrimaryTags   = priTags.Select(t => t.Name).ToArray();
                if (secTags != null) _browsedSecondaryTags = secTags.Select(t => t.Name).ToArray();

                int p = priTags?.Length ?? 0;
                int s = secTags?.Length ?? 0;
                SetStatus($"Tags loaded — Primary: {p}, Secondary: {s}");
            }
            catch (OperationCanceledException) { SetStatus("Browse cancelled."); }
            catch (Exception ex) { SetStatus($"Browse failed: {ex.Message}", true); }
            finally { SetBusy(false); }
        }

        /// <summary>
        /// Builds the type-ahead AutoComplete list for a tag combo from its tag names,
        /// so the user can filter hundreds of tags by typing. Rebuilt on every browse.
        /// </summary>
        private static void SetAutoComplete(ComboBox combo, Tag[] tags)
        {
            var src = new AutoCompleteStringCollection();
            src.AddRange(tags.Select(t => t.Name).ToArray());
            combo.AutoCompleteCustomSource = src;
        }

        // ── Get Stats ──────────────────────────────────────────────────────────────
        private async void btnGetStats_Click(object sender, EventArgs e)
        {
            if (!_connections.IsPrimaryConnected && !_connections.IsSecondaryConnected)
            { SetStatus("Connect to a server first.", true); return; }

            SetBusy(true);
            SetStatus("Fetching server stats…");
            string mask = string.IsNullOrWhiteSpace(txtTagnameFilter.Text) ? "*" : txtTagnameFilter.Text.Trim();
            string priHost = txtPrimary.Text.Trim();
            string secHost = txtSecondary.Text.Trim();
            bool priConn = _connections.IsPrimaryConnected;
            bool secConn = _connections.IsSecondaryConnected;

            try
            {
                ResetCts();
                var token = _cts.Token;
                int priCount = 0, secCount = 0;

                await Task.Run(() =>
                {
                    token.ThrowIfCancellationRequested();
                    if (priConn)
                        priCount = _data.BrowseTags(_connections.Primary, mask).Count;
                    token.ThrowIfCancellationRequested();
                    if (secConn)
                        secCount = _data.BrowseTags(_connections.Secondary, mask).Count;
                }, token);

                if (priConn)
                    Log($"Primary  ({priHost}): {priCount} float tag(s) matching '{mask}'");
                if (secConn)
                    Log($"Secondary ({secHost}): {secCount} float tag(s) matching '{mask}'");

                SetStatus("Server stats loaded — see Activity Log.");
            }
            catch (OperationCanceledException) { SetStatus("Stats cancelled."); }
            catch (Exception ex) { SetStatus($"Stats failed: {ex.Message}", true); }
            finally { SetBusy(false); }
        }

        // ── Read Data ──────────────────────────────────────────────────────────────
        private async void btnReadPrimary_Click(object sender, EventArgs e)
        {
            if (!_connections.IsPrimaryConnected) { SetStatus("Primary not connected.", true); return; }
            if (string.IsNullOrWhiteSpace(cboPrimary.Text)) { SetStatus("Select a primary tag.", true); return; }
            await ReadPrimaryData();
        }

        private async void cboPrimary_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_suppressAutoRead || _isBusy) return;
            if (!_connections.IsPrimaryConnected) return;
            if (string.IsNullOrWhiteSpace(cboPrimary.Text)) return;

            // Linked mode: auto-select the identical tag on the secondary side too
            bool mirrored = _tagLinkEnabled && !_isLinkPropagating
                && TryMirrorTagSelection(cboPrimary, cboSecondary);

            lblGridPrimaryTag.Text = $"{txtPrimary.Text.Trim()} — {cboPrimary.Text}";
            await ReadPrimaryData();

            if (mirrored && _connections.IsSecondaryConnected
                && !string.IsNullOrWhiteSpace(cboSecondary.Text))
            {
                lblGridSecondaryTag.Text = $"{txtSecondary.Text.Trim()} — {cboSecondary.Text}";
                await ReadSecondaryData();
            }

            // Tag change → re-analyze gaps (per-tag coverage) after a short debounce
            _gapAutoAnalyzeTimer?.Stop();
            _gapAutoAnalyzeTimer?.Start();
        }

        private async Task ReadPrimaryData()
        {
            DateTime from = dtpStart.Value;
            DateTime to   = dtpEnd.Value;
            if (from >= to) { SetStatus("Start date must be before end date.", true); return; }

            string tag     = cboPrimary.Text;
            string priHost = txtPrimary.Text.Trim();

            SetBusy(true);
            SetStatus("Reading primary data…");
            ExitCompareMode();

            try
            {
                ResetCts();
                var samples = await Task.Run(
                    () => _data.ReadRawInRange(_connections.Primary, tag, from, to),
                    _cts.Token);

                _rawPrimarySamples = samples;
                _primaryRows = SamplesToGridRows(samples);
                UpdateGridRowCount(gridPrimary, _primaryRows.Count);
                lblGridPrimaryTag.Text = $"{priHost} — {tag}";
                SetStatus($"Primary: {samples.Count} raw samples read for '{tag}'.");
            }
            catch (OperationCanceledException) { SetStatus("Read cancelled."); }
            catch (Exception ex) { SetStatus($"Read failed: {ex.Message}", true); }
            finally { SetBusy(false); }
        }

        private async void btnReadSecondary_Click(object sender, EventArgs e)
        {
            if (!_connections.IsSecondaryConnected) { SetStatus("Secondary not connected.", true); return; }
            if (string.IsNullOrWhiteSpace(cboSecondary.Text)) { SetStatus("Select a secondary tag.", true); return; }
            await ReadSecondaryData();
        }

        private async void cboSecondary_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_suppressAutoRead || _isBusy) return;
            if (!_connections.IsSecondaryConnected) return;
            if (string.IsNullOrWhiteSpace(cboSecondary.Text)) return;

            bool mirrored = _tagLinkEnabled && !_isLinkPropagating
                && TryMirrorTagSelection(cboSecondary, cboPrimary);

            lblGridSecondaryTag.Text = $"{txtSecondary.Text.Trim()} — {cboSecondary.Text}";
            await ReadSecondaryData();

            if (mirrored && _connections.IsPrimaryConnected
                && !string.IsNullOrWhiteSpace(cboPrimary.Text))
            {
                lblGridPrimaryTag.Text = $"{txtPrimary.Text.Trim()} — {cboPrimary.Text}";
                await ReadPrimaryData();
            }

            _gapAutoAnalyzeTimer?.Stop();
            _gapAutoAnalyzeTimer?.Start();
        }

        /// <summary>
        /// When tag-link is on, mirrors the tag just picked on one side to the other
        /// side's combo (if that tag exists there). The change is made with events
        /// suppressed — the caller decides what to read afterwards, so the flow stays
        /// one predictable sequence. Returns true when the other combo changed.
        /// </summary>
        private bool TryMirrorTagSelection(ComboBox changed, ComboBox other)
        {
            string name = changed.Text;
            if (string.IsNullOrWhiteSpace(name)) return false;
            if (string.Equals(other.Text, name, StringComparison.OrdinalIgnoreCase)) return false;

            int match = -1;
            for (int i = 0; i < other.Items.Count; i++)
            {
                var t = other.Items[i] as Tag;
                string itemName = t != null ? t.Name : other.Items[i]?.ToString();
                if (string.Equals(itemName, name, StringComparison.OrdinalIgnoreCase))
                { match = i; break; }
            }
            if (match < 0)
            {
                if (other.Items.Count > 0)
                    SetStatus($"'{name}' does not exist on the other server — tags not linked for this selection.");
                return false;
            }

            _isLinkPropagating = true;
            _suppressAutoRead = true;
            try { other.SelectedIndex = match; }
            finally { _suppressAutoRead = false; _isLinkPropagating = false; }
            return true;
        }

        private void btnTagLink_Click(object sender, EventArgs e)
        {
            if (_isBusy) return;
            _tagLinkEnabled = !_tagLinkEnabled;
            Settings.Default.TagLinkEnabled = _tagLinkEnabled;
            Settings.Default.Save();
            UpdateTagLinkVisual();

            // Turning the link ON re-aligns the secondary tag immediately so the UI
            // state matches what the button promises.
            if (_tagLinkEnabled && !string.IsNullOrWhiteSpace(cboPrimary.Text)
                && TryMirrorTagSelection(cboPrimary, cboSecondary)
                && _connections.IsSecondaryConnected)
            {
                lblGridSecondaryTag.Text = $"{txtSecondary.Text.Trim()} — {cboSecondary.Text}";
                var _ = ReadSecondaryThenReanalyze();
            }
        }

        private async Task ReadSecondaryThenReanalyze()
        {
            await ReadSecondaryData();
            _gapAutoAnalyzeTimer?.Stop();
            _gapAutoAnalyzeTimer?.Start();
        }

        private void UpdateTagLinkVisual()
        {
            btnTagLink.Text = _tagLinkEnabled
                ? "⇄  Linked — same tag on both servers"
                : "✕  Not linked — tags chosen independently";
            btnTagLink.BackColor = _tagLinkEnabled ? AppTheme.NavyLight : AppTheme.Background;
            btnTagLink.ForeColor = _tagLinkEnabled ? AppTheme.Navy : AppTheme.TextSecondary;
        }

        private async Task ReadSecondaryData()
        {
            DateTime from = dtpStart.Value;
            DateTime to   = dtpEnd.Value;
            if (from >= to) { SetStatus("Start date must be before end date.", true); return; }

            string tag     = cboSecondary.Text;
            string secHost = txtSecondary.Text.Trim();

            SetBusy(true);
            SetStatus("Reading secondary data…");
            ExitCompareMode();

            try
            {
                ResetCts();
                var samples = await Task.Run(
                    () => _data.ReadRawInRange(_connections.Secondary, tag, from, to),
                    _cts.Token);

                _rawSecondarySamples = samples;
                _secondaryRows = SamplesToGridRows(samples);
                UpdateGridRowCount(gridSecondary, _secondaryRows.Count);
                lblGridSecondaryTag.Text = $"{secHost} — {tag}";
                SetStatus($"Secondary: {samples.Count} raw samples read for '{tag}'.");
            }
            catch (OperationCanceledException) { SetStatus("Read cancelled."); }
            catch (Exception ex) { SetStatus($"Read failed: {ex.Message}", true); }
            finally { SetBusy(false); }
        }

        // ── Compare ────────────────────────────────────────────────────────────────
        private async void btnCompare_Click(object sender, EventArgs e)
        {
            // Toggle off
            if (_isCompareMode)
            {
                _isCompareMode = false;
                btnCompare.Text = "Compare";
                if (_rawPrimarySamples != null)
                {
                    _primaryRows = SamplesToGridRows(_rawPrimarySamples);
                    UpdateGridRowCount(gridPrimary, _primaryRows.Count);
                }
                if (_rawSecondarySamples != null)
                {
                    _secondaryRows = SamplesToGridRows(_rawSecondarySamples);
                    UpdateGridRowCount(gridSecondary, _secondaryRows.Count);
                }
                SetStatus("Switched to raw view.");
                return;
            }

            // Need both servers
            if (!_connections.IsPrimaryConnected || !_connections.IsSecondaryConnected)
            { SetStatus("Connect to both servers first.", true); return; }

            string priTag = cboPrimary.Text;
            string secTag = cboSecondary.Text;
            if (string.IsNullOrWhiteSpace(priTag) || string.IsNullOrWhiteSpace(secTag))
            { SetStatus("Select tags on both servers first.", true); return; }

            DateTime from = dtpStart.Value;
            DateTime to   = dtpEnd.Value;
            if (from >= to) { SetStatus("Start date must be before end date.", true); return; }

            string priHost = txtPrimary.Text.Trim();
            string secHost = txtSecondary.Text.Trim();

            SetBusy(true);
            SetStatus("Reading and comparing…");

            try
            {
                ResetCts();
                var token = _cts.Token;

                List<(DateTime Time, float Value, double Quality)> priSamples = null;
                List<(DateTime Time, float Value, double Quality)> secSamples = null;

                await Task.Run(() =>
                {
                    token.ThrowIfCancellationRequested();
                    priSamples = _data.ReadRawInRange(_connections.Primary, priTag, from, to);
                    token.ThrowIfCancellationRequested();
                    secSamples = _data.ReadRawInRange(_connections.Secondary, secTag, from, to);
                }, token);

                _rawPrimarySamples   = priSamples;
                _rawSecondarySamples = secSamples;

                // Run alignment
                AlignGridData(priSamples, secSamples);

                UpdateGridRowCount(gridPrimary,   _primaryRows.Count);
                UpdateGridRowCount(gridSecondary, _secondaryRows.Count);

                lblGridPrimaryTag.Text   = $"{priHost} — {priTag}";
                lblGridSecondaryTag.Text = $"{secHost} — {secTag}";

                _isCompareMode = true;
                btnCompare.Text = "Raw View";

                // Summary
                int matched   = _primaryRows.Count(r => !r.IsSpacer && !r.IsExtra);
                int priOnly   = _primaryRows.Count(r => r.IsExtra);
                int secOnly   = _secondaryRows.Count(r => r.IsExtra);
                int mismatches = _primaryRows.Count(r => r.IsMismatch);
                SetStatus($"Compare: Pri {priSamples.Count} | Sec {secSamples.Count} | Matched {matched} | Pri-only {priOnly} | Sec-only {secOnly} | Mismatches {mismatches}");
            }
            catch (OperationCanceledException) { SetStatus("Compare cancelled."); }
            catch (Exception ex) { SetStatus($"Compare failed: {ex.Message}", true); }
            finally { SetBusy(false); }
        }

        // ── Copy / Backfill ────────────────────────────────────────────────────────

        /// <summary>Shows tag selection dialog and returns chosen tags, or null if cancelled.</summary>
        private List<string> ShowTagSelectionDialog(
            GapAnalysisResult gapResult, string sourceLabel, string targetLabel,
            DateTime evalFrom, DateTime evalTo)
        {
            if (!_connections.IsPrimaryConnected || !_connections.IsSecondaryConnected)
            {
                SetStatus("Both servers must be connected for backfill.", true);
                return null;
            }

            // Gap windows are display-only. The dialog's per-tag direct diff decides what is
            // actually copyable, so we no longer gate on HasGaps — isolated missing samples
            // below the gap-detection floor still get caught and offered.
            int gapCount   = gapResult?.Gaps.Count ?? 0;
            int batchCount = gapResult?.Gaps.Sum(g => g.Batches.Count(b => b.CanBackfill)) ?? 0;

            var sharedTags = TryGetSharedTags();
            if (sharedTags == null) return null;

            var allBackfillBatches = gapResult?.Gaps
                .SelectMany(g => g.Batches)
                .Where(b => b.CanBackfill)
                .ToList() ?? new List<GapBatch>();

            // Source connection is the OPPOSITE of the target (we're filling `targetLabel`'s gaps).
            var sourceConn = targetLabel == "Secondary" ? _connections.Primary : _connections.Secondary;
            var targetConn = targetLabel == "Secondary" ? _connections.Secondary : _connections.Primary;

            using (var dlg = new TagSelectionDialog(sourceLabel, targetLabel,
                gapCount, batchCount, sharedTags,
                sourceConn, targetConn, allBackfillBatches,
                evalFrom, evalTo, _data))
            {
                if (dlg.ShowDialog(this) != System.Windows.Forms.DialogResult.OK)
                    return null;
                return dlg.SelectedTags;
            }
        }

        /// <summary>
        /// Browses tags that exist on BOTH servers (intersection, sorted). Returns null and
        /// sets the status bar on failure or when there is no overlap. Runs synchronously on
        /// the UI thread (browse is quick); per-tag diffing happens off-thread in the dialogs.
        /// </summary>
        private List<string> TryGetSharedTags()
        {
            SetStatus("Loading shared tags…");
            try
            {
                var priTags = _data.BrowseTags(_connections.Primary, "*").Select(t => t.Name).ToList();
                var secTags = _data.BrowseTags(_connections.Secondary, "*").Select(t => t.Name).ToList();
                var shared  = priTags.Intersect(secTags).OrderBy(n => n).ToList();
                if (shared.Count == 0)
                {
                    SetStatus("No tags found on both servers.", true);
                    return null;
                }
                SetStatus($"{shared.Count} tag(s) exist on both servers.");
                return shared;
            }
            catch (Exception ex)
            {
                SetStatus($"Failed to load tags: {ex.Message}", true);
                return null;
            }
        }

        private async void btnCopyToSecondary_Click(object sender, EventArgs e)
        {
            // Same live-edge clamp as the backfill itself, so the dialog's "Will copy"
            // numbers match exactly what ExecuteBackfill will write.
            DateTime from = dtpStart.Value;
            DateTime to   = ClampLiveEdge(dtpEnd.Value);
            if (from >= to) { SetStatus("Invalid evaluation range.", true); return; }

            var tags = ShowTagSelectionDialog(_lastSecondaryResult, "Primary", "Secondary", from, to);
            if (tags == null) return;

            await ExecuteBackfill(_lastSecondaryResult, _connections.Primary,
                _connections.Secondary, "Primary", "Secondary", tags,
                evalFromOverride: from, evalToOverride: to);

            await AutoRefreshAfterBackfill();
        }

        private async void btnCopyToPrimary_Click(object sender, EventArgs e)
        {
            DateTime from = dtpStart.Value;
            DateTime to   = ClampLiveEdge(dtpEnd.Value);
            if (from >= to) { SetStatus("Invalid evaluation range.", true); return; }

            var tags = ShowTagSelectionDialog(_lastPrimaryResult, "Secondary", "Primary", from, to);
            if (tags == null) return;

            await ExecuteBackfill(_lastPrimaryResult, _connections.Secondary,
                _connections.Primary, "Secondary", "Primary", tags,
                evalFromOverride: from, evalToOverride: to);

            await AutoRefreshAfterBackfill();
        }

        /// <summary>Caps an evaluation end at now − LiveEdgeGrace (see field comment).</summary>
        private static DateTime ClampLiveEdge(DateTime evalTo)
        {
            DateTime limit = DateTime.Now - LiveEdgeGrace;
            return evalTo > limit ? limit : evalTo;
        }

        private async void btnBackfillPreview_Click(object sender, EventArgs e)
        {
            if (!_connections.IsPrimaryConnected || !_connections.IsSecondaryConnected)
            { SetStatus("Both servers must be connected for backfill.", true); return; }

            DateTime from = dtpStart.Value;
            DateTime to   = ClampLiveEdge(dtpEnd.Value);
            if (from >= to) { SetStatus("Invalid evaluation range.", true); return; }

            var sharedTags = TryGetSharedTags();
            if (sharedTags == null) return;

            // One window, both directions. The dialog computes the real per-tag diff inside,
            // so it always opens (shows "in sync" if there's nothing) — no more empty dialog.
            List<string> p2s, s2p;
            using (var dlg = new BidirectionalBackfillDialog(
                _connections.Primary, _connections.Secondary, sharedTags, from, to, _data))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                p2s = dlg.SelectedPrimaryToSecondary;
                s2p = dlg.SelectedSecondaryToPrimary;
            }

            // Run each chosen direction (suppressing its own report) and gather the results.
            var reports = new List<SyncRunReport>();
            if (p2s != null && p2s.Count > 0)
            {
                var r = await ExecuteBackfill(null, _connections.Primary, _connections.Secondary,
                    "Primary", "Secondary", p2s, evalFromOverride: from, evalToOverride: to,
                    showReport: false);
                if (r != null) reports.Add(r);
            }
            if (s2p != null && s2p.Count > 0)
            {
                var r = await ExecuteBackfill(null, _connections.Secondary, _connections.Primary,
                    "Secondary", "Primary", s2p, evalFromOverride: from, evalToOverride: to,
                    showReport: false);
                if (r != null) reports.Add(r);
            }

            // Persist the COMBINED report on every journal entry of this run, so clicking
            // either entry in Backfill History reopens the exact same both-directions
            // dialog later (each entry initially stored only its own direction).
            if (reports.Count > 1)
            {
                try
                {
                    var combined = reports.Select(JournalDirectionReport.From).ToList();
                    var all = BackfillJournalService.LoadAll();
                    foreach (var rep in reports)
                    {
                        if (rep.JournalId == null) continue;
                        var entry = all.FirstOrDefault(en => en.Id == rep.JournalId);
                        if (entry == null) continue;
                        entry.ReportDirections = combined;
                        BackfillJournalService.Save(entry);
                    }
                }
                catch (Exception ex) { Log($"Combined-report journal update failed: {ex.Message}"); }
            }

            // Single combined report covering both directions.
            if (reports.Count > 0)
            {
                try { using (var rep = new SyncReportDialog(reports)) rep.ShowDialog(this); }
                catch (Exception ex) { Log($"Report dialog error: {ex.Message}"); }
            }

            await AutoRefreshAfterBackfill();
        }

        /// <summary>
        /// Backfill via DIRECT timestamp comparison: read source + target samples for the full
        /// evaluation range, find timestamps present on source but missing on target, and copy
        /// only those. Catches isolated missing samples that interval-based gap detection
        /// misses (anything below the MinimumGapSeconds floor). Groups missing samples into
        /// batches by the configured batch duration for progress tracking and per-batch verify.
        /// </summary>
        private async Task<SyncRunReport> ExecuteBackfill(
            GapAnalysisResult gapResult,
            ServerConnection sourceConn,
            ServerConnection targetConn,
            string sourceLabel,
            string targetLabel,
            List<string> tagsToBackfill,
            DateTime? evalFromOverride = null,
            DateTime? evalToOverride = null,
            bool unattended = false,
            bool showReport = true)
        {
            if (sourceConn == null || targetConn == null) return null;
            if (tagsToBackfill == null || tagsToBackfill.Count == 0) return null;

            // Capture evaluation range up-front (avoid UI access from the worker).
            // Unattended runs pass explicit overrides — they aren't driven by the date pickers.
            DateTime evalFrom = evalFromOverride ?? dtpStart.Value;
            DateTime evalTo   = evalToOverride   ?? dtpEnd.Value;

            // Live-edge guard: never diff/copy the trailing grace window. On live servers
            // the collectors are still writing there, so samples merely in flight would be
            // reported "missing" — and every run would find something new to copy, forever.
            DateTime liveLimit = DateTime.Now - LiveEdgeGrace;
            if (evalTo > liveLimit)
            {
                evalTo = liveLimit;
                Log($"Evaluation end moved to {evalTo:yyyy-MM-dd HH:mm:ss} — the last " +
                    $"{LiveEdgeGrace.TotalSeconds:F0}s are excluded (collectors may still be writing).");
            }
            if (evalFrom >= evalTo) { SetStatus("Invalid evaluation range.", true); return null; }

            TimeSpan batchSize = _gapAnalysis.BatchSize;

            int totalTags = tagsToBackfill.Count;

            // Hostnames captured up-front (worker must not touch UI/services state).
            // TargetHost is stored in the journal so a later revert can match the
            // connection to delete from.
            string sourceHost = sourceLabel == "Primary" ? _connections.PrimaryHostname : _connections.SecondaryHostname;
            string targetHost = targetLabel == "Primary" ? _connections.PrimaryHostname : _connections.SecondaryHostname;

            // Per-tag record of exactly which timestamps we successfully wrote, so the
            // run can be reverted later (delete exactly these). Populated in the worker.
            var writtenTicks = new Dictionary<string, List<long>>();

            SetBusy(true, $"Backfilling {targetLabel}…");
            var report = new SyncRunReport
            {
                StartedAt    = DateTime.Now,
                SourceServer = sourceLabel,
                TargetServer = targetLabel,
                DirectionLabel = $"{(string.IsNullOrEmpty(sourceLabel) ? "?" : sourceLabel.Substring(0, 1))}" +
                                 $"→{(string.IsNullOrEmpty(targetLabel) ? "?" : targetLabel.Substring(0, 1))}",
                GapsFound    = gapResult?.Gaps.Count ?? 0
            };

            bool wasCancelled = false;
            try
            {
                ResetCts();
                var token = _cts.Token;

                await Task.Run(() =>
                {
                    for (int tagIdx = 0; tagIdx < totalTags; tagIdx++)
                    {
                        token.ThrowIfCancellationRequested();
                        string tag = tagsToBackfill[tagIdx];
                        var tagResult = new TagBackfillResult { TagName = tag };
                        report.TagResults.Add(tagResult);

                        Invoke((Action)(() =>
                        {
                            Log($"── Tag {tagIdx + 1}/{totalTags}: {tag} — comparing servers ──");
                            SetStatus($"Tag {tagIdx + 1}/{totalTags}: {tag} — comparing…");
                            SetPhaseProgress(tagIdx + 1, totalTags, $"Tag {tagIdx + 1} / {totalTags} — {tag}");
                        }));

                        // Read both servers for the full eval range
                        List<(DateTime Time, float Value, double Quality)> srcData;
                        List<(DateTime Time, float Value, double Quality)> tgtData;
                        try
                        {
                            srcData = _data.ReadRawInRange(sourceConn, tag, evalFrom, evalTo);
                            tgtData = _data.ReadRawInRange(targetConn, tag, evalFrom, evalTo);
                        }
                        catch (Exception ex)
                        {
                            tagResult.Errors.Add($"Read failed: {ex.Message}");
                            Invoke((Action)(() => Log($"  {tag}: read failed — {ex.Message}")));
                            continue;
                        }
                        token.ThrowIfCancellationRequested();

                        // Plan what to copy (Phase 10) — SyncPlanner decides per tag:
                        // aligned streams (same-source data) → exact whole-second diff,
                        // which catches isolated missing samples; independently collected
                        // streams (redundant collectors on their own clocks) → only real
                        // target OUTAGES are filled. The old always-exact diff reported
                        // thousands of phantom "missing" samples on real plant data (same
                        // values logged seconds apart) and would have permanently
                        // interleaved both collectors' streams into the archive.
                        var plan = SyncPlanner.Plan(
                            srcData.Select(s => s.Time).ToList(),
                            tgtData.Select(s => s.Time).ToList(),
                            evalFrom, evalTo,
                            _gapAnalysis.MinGapDuration, _gapAnalysis.ThresholdMultiplier);
                        var copySet = new HashSet<DateTime>(plan.ToCopy);
                        var missing = srcData
                            .Where(s => copySet.Contains(s.Time))
                            .OrderBy(s => s.Time)
                            .ToList();

                        if (missing.Count == 0)
                        {
                            Invoke((Action)(() =>
                                Log($"  {tag}: in sync ({srcData.Count} source, {tgtData.Count} target samples" +
                                    (plan.UsedExactDiff ? ")." :
                                     $"; outage rule {FormatDuration(plan.OutageThreshold)} — no target outages)."))));
                            continue;
                        }

                        // Group missing samples into batches by the configured bucket duration.
                        // Each batch contains samples within `batchSize` of the batch-start
                        // timestamp; batch-start resets when the next sample falls outside.
                        var batches = SampleBucketer.GroupByBucket(missing, batchSize);
                        int totalBatches = batches.Count;

                        string planMode = plan.UsedExactDiff
                            ? "aligned streams — exact diff"
                            : $"{plan.TargetOutages.Count} target outage(s), gap rule {FormatDuration(plan.OutageThreshold)}";
                        Invoke((Action)(() =>
                            Log($"  {tag}: {missing.Count} sample(s) to copy ({planMode}) → {totalBatches} batch(es)")));

                        int batchIdx = 0;
                        foreach (var batchSamples in batches)
                        {
                            token.ThrowIfCancellationRequested();
                            batchIdx++;
                            tagResult.BatchesAttempted++;

                            try
                            {
                                var times = batchSamples.Select(s => s.Time).ToArray();
                                var values = batchSamples.Select(s => s.Value).ToArray();
                                var qualities = batchSamples.Select(s =>
                                    s.Quality >= 100.0 ? DataQuality.Good :
                                    s.Quality > 0      ? DataQuality.Uncertain :
                                                         DataQuality.Bad).ToArray();

                                var errors = _data.WriteFloatSamplesWithQuality(
                                    targetConn, tag, times, values, qualities);

                                bool writeOk = errors.Count == 0;
                                if (!writeOk)
                                {
                                    foreach (var err in errors)
                                        tagResult.Errors.Add($"Batch {batchIdx}: {err}");
                                }

                                // Verify honestly: re-read the batch range and confirm each
                                // written sample is actually present at its whole-second slot.
                                // (The old count-based ±1s check passed whenever any nearby
                                // sample existed, so it falsely reported success for writes
                                // that never landed — e.g. dropped by archive compression —
                                // which let the same gap be "backfilled" forever.)
                                bool verifyOk = true;
                                int confirmed = 0;
                                if (writeOk)
                                {
                                    DateTime vStart = times[0].AddSeconds(-1);
                                    DateTime vEnd   = times[times.Length - 1].AddSeconds(1);
                                    var reread = _data.ReadRawInRange(targetConn, tag, vStart, vEnd);
                                    var storedSecs = new HashSet<long>(
                                        reread.Select(s => SampleFilter.ToSecondTicks(s.Time)));
                                    confirmed = times.Count(t => storedSecs.Contains(SampleFilter.ToSecondTicks(t)));
                                    if (confirmed < times.Length)
                                    {
                                        verifyOk = false;
                                        tagResult.Errors.Add(
                                            $"Batch {batchIdx}: only {confirmed}/{times.Length} sample(s) landed " +
                                            "(target may reject/compress these) — not counted as written");
                                    }
                                }

                                if (writeOk && verifyOk)
                                {
                                    tagResult.BatchesSucceeded++;
                                    tagResult.SamplesWritten += batchSamples.Count;

                                    // Journal the whole-second timestamps actually stored, so a
                                    // later revert deletes exactly what Historian holds.
                                    List<long> ticks;
                                    if (!writtenTicks.TryGetValue(tag, out ticks))
                                    {
                                        ticks = new List<long>();
                                        writtenTicks[tag] = ticks;
                                    }
                                    foreach (var bs in batchSamples)
                                        ticks.Add(SampleFilter.ToSecondTicks(bs.Time));
                                }
                                else
                                {
                                    tagResult.BatchesFailed++;
                                }
                            }
                            catch (Exception ex)
                            {
                                tagResult.BatchesFailed++;
                                tagResult.Errors.Add($"Batch {batchIdx}: {ex.Message}");
                            }

                            int currentBatchIdx = batchIdx;
                            int currentTotal    = totalBatches;
                            int currentTagIdx   = tagIdx;
                            Invoke((Action)(() =>
                            {
                                SetStatus($"Tag {currentTagIdx + 1}/{totalTags}: {tag} — Batch {currentBatchIdx}/{currentTotal}");
                                SetProgress(currentBatchIdx, currentTotal);
                            }));
                        }

                        Invoke((Action)(() =>
                            Log($"  {tag}: {tagResult.BatchesSucceeded}/{tagResult.BatchesAttempted} batches, {tagResult.SamplesWritten} samples written")));
                    }
                }, token);

                report.CompletedAt = DateTime.Now;
                LogRunReport(report);
                SetStatus($"Backfill complete: {report.BatchesSucceeded}/{report.BatchesAttempted} batches across {totalTags} tag(s), {report.SamplesWritten} samples written.");
            }
            catch (OperationCanceledException)
            {
                wasCancelled = true;
                report.CompletedAt = DateTime.Now;
                report.Errors.Add("Operation cancelled by user.");
                LogRunReport(report);
                SetStatus("Backfill cancelled.");
            }
            catch (Exception ex)
            {
                report.CompletedAt = DateTime.Now;
                report.Errors.Add($"Fatal: {ex.Message}");
                LogRunReport(report);
                SetStatus($"Backfill failed: {ex.Message}", true);
            }
            finally { SetBusy(false); }

            // Journal whatever was actually written (even on cancel/partial failure) so
            // it can be reverted later. Saved regardless of attended/unattended mode.
            BackfillJournalEntry journal = null;
            if (writtenTicks.Count > 0)
            {
                try
                {
                    journal = new BackfillJournalEntry
                    {
                        Id             = BackfillJournalService.NewId(),
                        RunLocal       = report.StartedAt,
                        CompletedLocal = report.CompletedAt,
                        Mode           = unattended ? "Scheduled" : "Manual",
                        SourceLabel = sourceLabel,
                        SourceHost  = sourceHost,
                        TargetLabel = targetLabel,
                        TargetHost  = targetHost
                    };
                    foreach (var kv in writtenTicks)
                        journal.Tags.Add(new BackfillJournalTag { TagName = kv.Key, Ticks = kv.Value.ToArray() });
                    // Full report snapshot → Backfill History can reopen the exact results
                    // dialog later. Bidirectional runs overwrite this with the combined
                    // both-directions list right after the second direction finishes.
                    journal.ReportDirections = new List<JournalDirectionReport>
                        { JournalDirectionReport.From(report) };
                    BackfillJournalService.Save(journal);
                    report.JournalId = journal.Id;
                }
                catch (Exception ex) { Log($"Journal save error: {ex.Message}"); journal = null; }
            }

            // Cancelled mid-run with data already copied → let the user decide right away
            // whether to keep it or roll it back (the journal knows exactly what was written).
            if (wasCancelled && !unattended && journal != null)
            {
                var keep = MessageBox.Show(this,
                    $"The backfill was cancelled.\n\n" +
                    $"{journal.TotalSamples:N0} sample(s) had already been copied to {targetLabel} " +
                    $"({targetHost}) before the stop.\n\n" +
                    "Keep the copied data?\n\n" +
                    "Yes  –  keep it (you can still revert later via Backfill History)\n" +
                    "No   –  revert now: delete exactly those samples again",
                    "Keep the data copied so far?",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button1);
                if (keep == DialogResult.No)
                {
                    try { await RevertBackfill(journal); }
                    catch (Exception ex) { Log($"Revert after cancel failed: {ex.Message}"); }
                }
            }

            if (!unattended && showReport)
            {
                // Show detailed summary dialog (modal). User can export CSV/TXT from there.
                try
                {
                    using (var dlg = new SyncReportDialog(report))
                        dlg.ShowDialog(this);
                }
                catch (Exception ex) { Log($"Report dialog error: {ex.Message}"); }
            }
            else if (unattended)
            {
                // Scheduled run — append a one-line summary to the rolling file log so
                // the user can audit unattended activity without opening modal UI.
                TimeSpan dur = (report.CompletedAt - report.StartedAt);
                ScheduleLogger.Append(
                    $"{sourceLabel}→{targetLabel}  " +
                    $"tags={tagsToBackfill.Count}  " +
                    $"batches={report.BatchesSucceeded}/{report.BatchesAttempted}  " +
                    $"samples={report.SamplesWritten}  " +
                    $"duration={dur.TotalSeconds:F1}s" +
                    (report.Errors.Count > 0 ? $"  errors={report.Errors.Count}" : ""));
            }

            return report;
        }

        // ── Backfill History / Revert ───────────────────────────────────────────────

        private async void btnHistory_Click(object sender, EventArgs e)
        {
            var entries = BackfillJournalService.LoadAll();
            using (var dlg = new BackfillHistoryDialog(entries))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                if (string.IsNullOrEmpty(dlg.RevertEntryId)) return;

                var entry = entries.FirstOrDefault(x => x.Id == dlg.RevertEntryId);
                if (entry != null) await RevertBackfill(entry);
            }
        }

        /// <summary>
        /// Undo a backfill run by deleting exactly the timestamps it wrote (recorded in
        /// the journal). The target server must currently be connected — matched by the
        /// hostname stored in the journal entry. The entry is marked reverted only on a
        /// fully clean pass; on errors it stays Active so the user can safely retry
        /// (re-deleting already-deleted samples is harmless).
        /// </summary>
        private async Task RevertBackfill(BackfillJournalEntry entry)
        {
            if (entry == null || entry.Reverted) return;

            // Resolve the target connection by matching the recorded hostname.
            ServerConnection targetConn = null;
            if (_connections.IsPrimaryConnected &&
                string.Equals(_connections.PrimaryHostname, entry.TargetHost, StringComparison.OrdinalIgnoreCase))
                targetConn = _connections.Primary;
            else if (_connections.IsSecondaryConnected &&
                string.Equals(_connections.SecondaryHostname, entry.TargetHost, StringComparison.OrdinalIgnoreCase))
                targetConn = _connections.Secondary;

            if (targetConn == null)
            {
                SetStatus($"Connect to {entry.TargetHost} before reverting that run.", true);
                return;
            }

            SetBusy(true, "Reverting backfill…");
            int totalDeleted = 0, errorCount = 0;
            try
            {
                ResetCts();
                var token = _cts.Token;
                var tags = entry.Tags ?? new List<BackfillJournalTag>();

                await Task.Run(() =>
                {
                    for (int i = 0; i < tags.Count; i++)
                    {
                        token.ThrowIfCancellationRequested();
                        var t = tags[i];
                        if (t.Ticks == null || t.Ticks.Length == 0) continue;
                        var times = t.Ticks.Select(tk => new DateTime(tk)).ToList();

                        int idx = i;
                        Invoke((Action)(() =>
                        {
                            Log($"Revert {idx + 1}/{tags.Count}: {t.TagName} — deleting {times.Count} sample(s)");
                            SetStatus($"Reverting {idx + 1}/{tags.Count}: {t.TagName}…");
                            SetProgress(idx + 1, tags.Count);
                        }));

                        var errors = _data.DeleteSamples(targetConn, t.TagName, times);
                        if (errors.Count > 0)
                        {
                            errorCount += errors.Count;
                            foreach (var err in errors)
                                Invoke((Action)(() => Log($"  revert error [{t.TagName}]: {err}")));
                        }
                        else
                        {
                            totalDeleted += times.Count;
                        }
                    }
                }, token);

                if (errorCount == 0)
                {
                    entry.Reverted = true;
                    entry.RevertedLocal = DateTime.Now;
                    BackfillJournalService.Save(entry);
                    SetStatus($"Revert complete — deleted {totalDeleted} sample(s) from {entry.TargetLabel}.");
                }
                else
                {
                    SetStatus($"Revert finished with {errorCount} error(s); {totalDeleted} sample(s) deleted. Run kept Active for retry.", true);
                }
            }
            catch (OperationCanceledException) { SetStatus("Revert cancelled — partial deletion may have occurred."); }
            catch (Exception ex) { SetStatus($"Revert failed: {ex.Message}", true); }
            finally { SetBusy(false); }

            // Reflect the deletion in the coverage bars and loaded grids.
            try { await AutoRefreshAfterBackfill(); } catch { }
        }

        // ── Scheduled Backfill (Phase 7) ───────────────────────────────────────────

        private void ApplyScheduleSettings()
        {
            var s = Settings.Default;
            _schedule.Configure(
                enabled:         s.ScheduleEnabled,
                intervalMinutes: s.ScheduleIntervalMinutes,
                lastRunUtc:      s.ScheduleLastRunUtc);
        }

        private void UpdateScheduleStatusLabel()
        {
            if (InvokeRequired) { Invoke((Action)UpdateScheduleStatusLabel); return; }

            if (_schedule == null || !_schedule.Enabled)
            {
                lblSchedule.Text      = "Schedule: off";
                lblSchedule.ForeColor = AppTheme.TextSecondary;
                return;
            }

            if (_schedule.RunInProgress)
            {
                lblSchedule.Text      = "Schedule: running…";
                lblSchedule.ForeColor = AppTheme.Teal;
                return;
            }

            var next = _schedule.NextRunLocal;
            if (next == DateTime.MaxValue)
            {
                lblSchedule.Text      = "Schedule: pending";
                lblSchedule.ForeColor = AppTheme.TextSecondary;
                return;
            }

            // If next-run is today, show only HH:mm. Otherwise show MM-dd HH:mm.
            string nextText = next.Date == DateTime.Today
                ? next.ToString("HH:mm")
                : next.ToString("MM-dd HH:mm");
            lblSchedule.Text      = $"Next run: {nextText}";
            lblSchedule.ForeColor = AppTheme.Navy;
        }

        private async void lblSchedule_Click(object sender, EventArgs e)
        {
            // Shared-tag intersection from the last browse, so the dialog can offer a
            // manual multiselect. Empty if the user hasn't browsed yet.
            var priSet = new HashSet<string>(_browsedPrimaryTags, StringComparer.OrdinalIgnoreCase);
            var shared = _browsedSecondaryTags.Where(t => priSet.Contains(t)).OrderBy(t => t).ToList();

            using (var dlg = new SchedulerSettingsDialog(
                _schedule.NextRunLocal, _schedule.LastRunUtc, shared))
            {
                var result = dlg.ShowDialog(this);
                if (result != DialogResult.OK) return;

                ApplyScheduleSettings();
                UpdateScheduleStatusLabel();

                if (dlg.RunNowRequested)
                {
                    if (!_connections.IsPrimaryConnected || !_connections.IsSecondaryConnected)
                    {
                        SetStatus("Connect both servers before triggering a scheduled run.", true);
                        return;
                    }
                    try { await _schedule.TriggerNowAsync(); }
                    catch (Exception ex)
                    { SetStatus($"Scheduled run failed: {ex.Message}", true); }
                }
            }
        }

        /// <summary>
        /// Headless backfill driven by the persisted scheduler settings. Computes a
        /// rolling evaluation window (now - <c>ScheduleEvalWindowHours</c>), browses the
        /// tag intersection on both servers, applies the configured tag-name mask, and
        /// runs <see cref="ExecuteBackfill"/> in unattended mode for each configured
        /// direction. Writes a one-line audit entry per direction via ScheduleLogger.
        /// </summary>
        private async Task RunScheduledBackfillAsync()
        {
            // Headless: a scheduled run (and its auto-refresh) must never pop the modal
            // progress dialog in the user's face — the status bar + file log cover it.
            _suppressOpDialog = true;
            try { await RunScheduledBackfillCoreAsync(); }
            finally { _suppressOpDialog = false; }
        }

        private async Task RunScheduledBackfillCoreAsync()
        {
            if (!_connections.IsPrimaryConnected || !_connections.IsSecondaryConnected)
            {
                ScheduleLogger.Append("Skipped — both servers must be connected.");
                Settings.Default.ScheduleLastRunUtc = DateTime.UtcNow;
                Settings.Default.Save();
                return;
            }
            if (_isBusy)
            {
                ScheduleLogger.Append("Skipped — a manual operation is currently in progress.");
                return;
            }

            var s = Settings.Default;
            DateTime evalTo   = DateTime.Now;
            DateTime evalFrom = evalTo.AddHours(-Math.Max(1, s.ScheduleEvalWindowHours));

            // Tag selection: either an explicit hand-picked list, or a filter mask.
            bool useList = s.ScheduleUseTagList && !string.IsNullOrWhiteSpace(s.ScheduleTagList);
            string mask  = useList ? "*"
                                   : (string.IsNullOrWhiteSpace(s.ScheduleTagFilter) ? "*" : s.ScheduleTagFilter);

            ScheduleLogger.Append(
                $"=== Scheduled run started — window {evalFrom:yyyy-MM-dd HH:mm} → {evalTo:yyyy-MM-dd HH:mm}, " +
                $"direction={s.ScheduleDirection}, " +
                (useList ? "tags=explicit list" : $"filter={mask}") + " ===");

            // Tag intersection from BOTH servers (under the mask), then — if an explicit
            // list is configured — narrowed to the hand-picked tags that actually exist.
            List<string> sharedTags;
            try
            {
                var pri = await Task.Run(() => _data.BrowseTags(_connections.Primary,   mask));
                var sec = await Task.Run(() => _data.BrowseTags(_connections.Secondary, mask));
                var priSet = new HashSet<string>(pri.Select(t => t.Name), StringComparer.OrdinalIgnoreCase);
                sharedTags = sec.Select(t => t.Name)
                                .Where(n => priSet.Contains(n))
                                .OrderBy(n => n)
                                .ToList();

                if (useList)
                {
                    var wanted = new HashSet<string>(
                        s.ScheduleTagList.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(t => t.Trim()),
                        StringComparer.OrdinalIgnoreCase);
                    sharedTags = sharedTags.Where(t => wanted.Contains(t)).ToList();
                }
            }
            catch (Exception ex)
            {
                ScheduleLogger.Append($"Failed to enumerate shared tags: {ex.Message}");
                Settings.Default.ScheduleLastRunUtc = DateTime.UtcNow;
                Settings.Default.Save();
                return;
            }

            if (sharedTags.Count == 0)
            {
                ScheduleLogger.Append(useList
                    ? "None of the selected tags exist on both servers — nothing to do."
                    : "No shared tags matched the filter — nothing to do.");
                Settings.Default.ScheduleLastRunUtc = DateTime.UtcNow;
                Settings.Default.Save();
                return;
            }

            ScheduleLogger.Append($"Shared tags: {sharedTags.Count}");

            bool doP2S = s.ScheduleDirection == "PrimaryToSecondary" || s.ScheduleDirection == "Both";
            bool doS2P = s.ScheduleDirection == "SecondaryToPrimary" || s.ScheduleDirection == "Both";

            if (doP2S)
            {
                await ExecuteBackfill(
                    gapResult: null,
                    sourceConn: _connections.Primary,
                    targetConn: _connections.Secondary,
                    sourceLabel: "Primary",
                    targetLabel: "Secondary",
                    tagsToBackfill: sharedTags,
                    evalFromOverride: evalFrom,
                    evalToOverride:   evalTo,
                    unattended: true);
            }
            if (doS2P)
            {
                await ExecuteBackfill(
                    gapResult: null,
                    sourceConn: _connections.Secondary,
                    targetConn: _connections.Primary,
                    sourceLabel: "Secondary",
                    targetLabel: "Primary",
                    tagsToBackfill: sharedTags,
                    evalFromOverride: evalFrom,
                    evalToOverride:   evalTo,
                    unattended: true);
            }

            ScheduleLogger.Append("=== Scheduled run completed ===");

            // Refresh the on-screen gap analysis so the user sees the updated coverage
            // the next time they look at the app. Cheap operation; if it fails, the next
            // manual action will retry.
            try { await AutoRefreshAfterBackfill(); }
            catch (Exception ex) { ScheduleLogger.Append($"Auto-refresh failed: {ex.Message}"); }

            // Persist last-run so the next scheduled tick computes the next-run instant.
            Settings.Default.ScheduleLastRunUtc = DateTime.UtcNow;
            Settings.Default.Save();
        }

        private void LogRunReport(SyncRunReport report)
        {
            Log("─── Sync Run Report ───────────────────────────");
            Log($"  {report.SourceServer} \u2192 {report.TargetServer}  |  {report.TagResults.Count} tag(s)");
            Log($"  Duration: {report.Duration.TotalSeconds:F1}s");
            Log($"  Gaps: {report.GapsFound}  |  Batches: {report.BatchesAttempted} attempted, {report.BatchesSucceeded} succeeded, {report.BatchesFailed} failed");
            Log($"  Samples written: {report.SamplesWritten}");

            foreach (var tr in report.TagResults)
            {
                if (tr.Errors.Count > 0)
                {
                    Log($"  Tag '{tr.TagName}': {tr.Errors.Count} error(s):");
                    foreach (var err in tr.Errors)
                        Log($"    - {err}");
                }
            }

            if (report.Errors.Count > 0)
            {
                Log($"  Global errors ({report.Errors.Count}):");
                foreach (var err in report.Errors)
                    Log($"    - {err}");
            }
            Log("────────────────────────────────────────────────");
        }

        // ── Gap Analysis ───────────────────────────────────────────────────────────
        private async void btnAnalyzeGaps_Click(object sender, EventArgs e)
        {
            if (!_connections.IsPrimaryConnected && !_connections.IsSecondaryConnected)
            { SetStatus("Connect to at least one server first.", true); return; }

            DateTime from = dtpStart.Value;
            DateTime to   = dtpEnd.Value;
            if (from >= to) { SetStatus("Start date must be before end date.", true); return; }

            await RunGapAnalysis(from, to);
            // Explicit analyze click → also refresh any loaded data tables for the new range
            await RefreshLoadedGrids();
        }

        /// <summary>
        /// If the primary/secondary data grids had data loaded (and their server is still
        /// connected + a tag is selected), re-read them. Used after an explicit Analyze Gaps
        /// click and after backfill. Debounced auto-analyze does NOT call this.
        /// </summary>
        private async Task RefreshLoadedGrids()
        {
            if (_isCompareMode) ExitCompareMode();

            bool priHadData = _primaryRows   != null && _primaryRows.Count   > 0;
            bool secHadData = _secondaryRows != null && _secondaryRows.Count > 0;

            if (priHadData && _connections.IsPrimaryConnected
                && !string.IsNullOrWhiteSpace(cboPrimary.Text))
            {
                await ReadPrimaryData();
            }
            if (secHadData && _connections.IsSecondaryConnected
                && !string.IsNullOrWhiteSpace(cboSecondary.Text))
            {
                await ReadSecondaryData();
            }
        }

        /// <summary>
        /// Core gap analysis logic. Uses the SELECTED tag per side (cboPrimary / cboSecondary),
        /// falling back to the configured HistSync tag if a combo is empty.
        /// Backfill feasibility for each side's gaps is checked against the OPPOSITE server
        /// using the SAME tag (so you can only backfill what actually exists on the source).
        /// </summary>
        private async Task RunGapAnalysis(DateTime from, DateTime to)
        {
            bool hasPrimary   = _connections.IsPrimaryConnected;
            bool hasSecondary = _connections.IsSecondaryConnected;

            string fallback = Settings.Default.SyncTagName;
            if (string.IsNullOrWhiteSpace(fallback)) fallback = "HistSync";

            // Per-side tag: user selection if present, else HistSync fallback
            string priTag = string.IsNullOrWhiteSpace(cboPrimary.Text)   ? fallback : cboPrimary.Text;
            string secTag = string.IsNullOrWhiteSpace(cboSecondary.Text) ? fallback : cboSecondary.Text;

            // Capture UI values before Task.Run
            string priHost = txtPrimary.Text.Trim();
            string secHost = txtSecondary.Text.Trim();

            SetBusy(true);
            SetStatus(priTag == secTag
                ? $"Analyzing gaps for '{priTag}'…"
                : $"Analyzing gaps — Primary '{priTag}', Secondary '{secTag}'…");
            _lastPrimaryResult   = null;
            _lastSecondaryResult = null;

            try
            {
                ResetCts();
                var token = _cts.Token;

                // priTag samples on primary (own), priTag samples on secondary (for primary-gap feasibility)
                // secTag samples on secondary (own), secTag samples on primary (for secondary-gap feasibility)
                List<DateTime> priOnPrimary   = null;
                List<DateTime> priOnSecondary = null;
                List<DateTime> secOnSecondary = null;
                List<DateTime> secOnPrimary   = null;

                GapAnalysisResult priResult = null, secResult = null;
                List<DiffSummaryRow> diffRows = null;
                List<CopyableSegment> copyable = null;
                string stripNote = null;
                var priFill = new List<TimeRange>(); var priUnfill = new List<TimeRange>();
                var secFill = new List<TimeRange>(); var secUnfill = new List<TimeRange>();
                List<TimeRange> priBack = null, secBack = null;
                bool priFeasKnown = false, secFeasKnown = false;

                await Task.Run(() =>
                {
                    token.ThrowIfCancellationRequested();
                    if (hasPrimary)
                        priOnPrimary = SafeReadTimes(_connections.Primary, priTag, from, to);

                    token.ThrowIfCancellationRequested();
                    if (hasSecondary)
                        secOnSecondary = SafeReadTimes(_connections.Secondary, secTag, from, to);

                    token.ThrowIfCancellationRequested();
                    // Feasibility samples: fetch opposite-server same-tag data unless already available
                    if (priTag == secTag)
                    {
                        priOnSecondary = secOnSecondary;
                        secOnPrimary   = priOnPrimary;
                    }
                    else
                    {
                        if (hasSecondary)
                            priOnSecondary = SafeReadTimes(_connections.Secondary, priTag, from, to);
                        if (hasPrimary)
                            secOnPrimary = SafeReadTimes(_connections.Primary, secTag, from, to);
                    }

                    token.ThrowIfCancellationRequested();

                    // Analysis + all timeline preparation stays on this worker thread —
                    // with real plant archives these lists hold millions of timestamps.
                    if (priOnPrimary != null)
                    {
                        priResult = _gapAnalysis.Analyze(priOnPrimary, priHost, from, to);
                        priResult.TagName = priTag;
                    }
                    if (secOnSecondary != null)
                    {
                        secResult = _gapAnalysis.Analyze(secOnSecondary, secHost, from, to);
                        secResult.TagName = secTag;
                    }

                    priFeasKnown = priOnSecondary != null;
                    secFeasKnown = secOnPrimary   != null;
                    if (priResult != null && priOnSecondary != null)
                        _gapAnalysis.MarkBackfillFeasibility(priResult, priOnSecondary);
                    if (secResult != null && secOnPrimary != null)
                        _gapAnalysis.MarkBackfillFeasibility(secResult, secOnPrimary);

                    // Cross-server sync plan for the selected tag(s) — the SAME SyncPlanner
                    // the backfill uses, so the table, the amber strip and an actual
                    // backfill all report identical numbers. Reuses the fetched samples.
                    diffRows = new List<DiffSummaryRow>();
                    TimeSpan floor = _gapAnalysis.MinGapDuration;
                    double   mult  = _gapAnalysis.ThresholdMultiplier;
                    SyncPlan planToSec = null, planToPri = null;
                    if (priOnPrimary != null && priOnSecondary != null)
                    {
                        planToSec = SyncPlanner.Plan(priOnPrimary, priOnSecondary, from, to, floor, mult);
                        planToPri = SyncPlanner.Plan(priOnSecondary, priOnPrimary, from, to, floor, mult);
                        AddPlanRow(diffRows, priTag, true,  planToSec);
                        AddPlanRow(diffRows, priTag, false, planToPri);
                    }
                    if (secTag != priTag && secOnPrimary != null && secOnSecondary != null)
                    {
                        AddPlanRow(diffRows, secTag, true,
                            SyncPlanner.Plan(secOnPrimary, secOnSecondary, from, to, floor, mult));
                        AddPlanRow(diffRows, secTag, false,
                            SyncPlanner.Plan(secOnSecondary, secOnPrimary, from, to, floor, mult));
                    }

                    // Timeline tracks: split each gap window into red (the other server
                    // HAS this data → copyable) and gray (missing on both → unfillable)
                    // segments, using the per-batch feasibility marks.
                    if (priResult != null)
                        foreach (var gapW in priResult.Gaps)
                            IntervalBuilder.SplitByFeasibility(gapW, priFill, priUnfill);
                    if (secResult != null)
                        foreach (var gapW in secResult.Gaps)
                            IntervalBuilder.SplitByFeasibility(gapW, secFill, secUnfill);
                    // Feasibility unknown (other side not read) → don't claim "missing on
                    // both"; show plain missing (red) instead.
                    if (!priFeasKnown) { priFill.AddRange(priUnfill); priUnfill.Clear(); }
                    if (!secFeasKnown) { secFill.AddRange(secUnfill); secUnfill.Clear(); }

                    // Copy-candidates strip (only meaningful when both sides show one tag):
                    // exactly the samples the planner would copy — nothing phantom.
                    copyable = new List<CopyableSegment>();
                    if (priTag == secTag && planToSec != null)
                    {
                        copyable.AddRange(SegmentsFromPlan(planToSec, toSecondary: true));
                        copyable.AddRange(SegmentsFromPlan(planToPri, toSecondary: false));
                        if (!planToSec.UsedExactDiff || !planToPri.UsedExactDiff)
                            stripNote = "outage-fill mode — collectors log independently " +
                                        $"(timestamps match {planToSec.MatchRate:P0}); only real outages are copied";
                    }
                    else if (priTag != secTag)
                        stripNote = "different tags selected — copy strip hidden (see table)";
                    else
                        stripNote = "connect both servers to see copy candidates";

                    // Blue "backfilled by this tool" bands from the revert journal
                    priBack = LoadBackfilledRanges(priHost, priTag, from, to);
                    secBack = LoadBackfilledRanges(secHost, secTag, from, to);
                }, token);

                _lastPrimaryResult   = priResult;
                _lastSecondaryResult = secResult;
                _lastDiffRows        = diffRows ?? new List<DiffSummaryRow>();

                var priTrack = BuildTrack(priResult, "PRIMARY", priHost, priFeasKnown, priFill, priUnfill, priBack);
                var secTrack = BuildTrack(secResult, "SECONDARY", secHost, secFeasKnown, secFill, secUnfill, secBack);
                UpdateGapAnalysisUI(from, to, priTrack, secTrack, copyable, stripNote);

                int totalGaps = (_lastPrimaryResult?.Gaps.Count ?? 0)
                              + (_lastSecondaryResult?.Gaps.Count ?? 0);
                SetStatus($"Gap analysis complete — {totalGaps} gap(s) found.");
            }
            catch (OperationCanceledException) { SetStatus("Analysis cancelled."); }
            catch (Exception ex) { SetStatus($"Analysis failed: {ex.Message}", true); }
            finally { SetBusy(false); }
        }

        /// <summary>Reads raw sample times for a tag; returns empty list on failure (e.g. tag missing).</summary>
        private List<DateTime> SafeReadTimes(
            Proficy.Historian.ClientAccess.API.ServerConnection conn,
            string tag, DateTime from, DateTime to)
        {
            try
            {
                // ReadRawInRange stops paging at `to` — the old ReadRaw(from) variant kept
                // reading to the end of the archive, which crawled on real plant data
                // whenever the evaluation window ended before the archive did.
                var samples = _data.ReadRawInRange(conn, tag, from, to);
                return samples
                    .Select(s => s.Time)
                    .OrderBy(t => t)
                    .ToList();
            }
            catch { return new List<DateTime>(); }
        }

        /// <summary>
        /// After backfill, re-run gap analysis, then re-read data grids (if they had data loaded)
        /// so the user sees the freshly-written samples immediately.
        /// </summary>
        private async Task AutoRefreshAfterBackfill()
        {
            DateTime from = dtpStart.Value;
            DateTime to   = dtpEnd.Value;
            if (from >= to) return;

            Log("Auto-refreshing gap analysis after backfill…");
            await RunGapAnalysis(from, to);
            await RefreshLoadedGrids();
        }

        private void UpdateGapAnalysisUI(DateTime from, DateTime to,
            TimelineTrackData priTrack, TimelineTrackData secTrack,
            List<CopyableSegment> copyable, string stripNote)
        {
            timeline.SetData(from, to, priTrack, secTrack, copyable, stripNote);
            lnkZoomOut.Visible = _zoomStack.Count > 0;

            PopulateDiffGrid();

            int toSecondary = _lastDiffRows.Where(r =>  r.ToSecondary).Sum(r => r.Count);
            int toPrimary   = _lastDiffRows.Where(r => !r.ToSecondary).Sum(r => r.Count);
            if (toSecondary == 0 && toPrimary == 0)
            {
                lblGapSummary.Text      = "In sync — a backfill would copy\nnothing for the selected tag(s).";
                lblGapSummary.ForeColor = AppTheme.Success;
            }
            else
            {
                lblGapSummary.Text      = $"Backfill would copy {toSecondary:N0} sample(s) → Secondary\n" +
                                          $"and {toPrimary:N0} sample(s) → Primary";
                lblGapSummary.ForeColor = AppTheme.Danger;
            }
        }

        /// <summary>Packs one server's analysis into the data the timeline track needs.</summary>
        private TimelineTrackData BuildTrack(GapAnalysisResult result, string sideLabel, string host,
            bool feasibilityKnown, List<TimeRange> fillable, List<TimeRange> unfillable,
            List<TimeRange> backfilled)
        {
            if (result == null)
            {
                return new TimelineTrackData
                {
                    Label = string.IsNullOrWhiteSpace(host) ? sideLabel : $"{sideLabel} · {host}",
                    CoverageRatio = -1,
                    EmptyText = string.IsNullOrWhiteSpace(host) ? "not connected" : "not analyzed"
                };
            }

            string tag = result.TagName ?? "(tag)";
            // The gap rule is shown right on the track so "why is/isn't this red" is
            // never a black box (derived from THIS tag's own sampling cadence).
            string rule = result.HasData && result.GapThreshold > TimeSpan.Zero
                ? $"   ·   gap rule: silence > {FormatDuration(result.GapThreshold)}"
                : "";
            return new TimelineTrackData
            {
                Label            = $"{sideLabel} · {host} · {tag}" + (result.HasData ? rule : "  (no data)"),
                CoverageRatio    = result.HasData ? result.CoverageRatio : 0.0,
                HasData          = result.HasData,
                FeasibilityKnown = feasibilityKnown,
                FillableGaps     = fillable   ?? new List<TimeRange>(),
                UnfillableGaps   = unfillable ?? new List<TimeRange>(),
                Backfilled       = backfilled ?? new List<TimeRange>()
            };
        }

        /// <summary>
        /// Merged runs of the samples a <see cref="SyncPlanner"/> plan would copy.
        /// Drives the amber copy-candidates strip — by construction the strip shows
        /// exactly what a backfill would write, nothing phantom.
        /// </summary>
        private static List<CopyableSegment> SegmentsFromPlan(SyncPlan plan, bool toSecondary)
            => SyncPlanner.ToSegments(plan, toSecondary);

        /// <summary>
        /// Merged time ranges this tool has written to <paramref name="host"/> for
        /// <paramref name="tag"/> (non-reverted journal entries, clipped to the window).
        /// Shown as the blue "backfilled by this tool" band; reverted runs disappear again.
        /// </summary>
        private List<TimeRange> LoadBackfilledRanges(string host, string tag, DateTime from, DateTime to)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(host)) return new List<TimeRange>();

                var ticks = new List<DateTime>();
                foreach (var entry in BackfillJournalService.LoadAll())
                {
                    if (entry.Reverted || entry.Tags == null) continue;
                    if (!string.Equals(entry.TargetHost, host, StringComparison.OrdinalIgnoreCase)) continue;
                    foreach (var t in entry.Tags)
                    {
                        if (t.Ticks == null) continue;
                        if (!string.Equals(t.TagName, tag, StringComparison.OrdinalIgnoreCase)) continue;
                        foreach (var tk in t.Ticks)
                        {
                            var dt = new DateTime(tk);
                            if (dt >= from && dt <= to) ticks.Add(dt);
                        }
                    }
                }
                if (ticks.Count == 0) return new List<TimeRange>();

                ticks.Sort();
                TimeSpan median = IntervalBuilder.MedianInterval(ticks);
                TimeSpan mergeGap = TimeSpan.FromTicks(
                    Math.Max(median.Ticks * 3, _gapAnalysis.BatchSize.Ticks));
                return IntervalBuilder.MergePoints(ticks, mergeGap, TimeSpan.FromSeconds(1))
                    .Select(m => m.Range)
                    .ToList();
            }
            catch { return new List<TimeRange>(); }
        }

        // Cross-server diff rows for the selected tag(s), computed in RunGapAnalysis from the
        // samples already fetched for coverage analysis (no extra reads). Drives gridGaps.
        private sealed class DiffSummaryRow
        {
            public string Tag;
            public string Direction;   // "Primary → Secondary" / "Secondary → Primary"
            public bool ToSecondary;   // true = Primary has, Secondary lacks (copy → Secondary)
            public int Count;
            public DateTime? First;
            public DateTime? Last;
        }
        private List<DiffSummaryRow> _lastDiffRows = new List<DiffSummaryRow>();

        /// <summary>
        /// Adds a table row for one direction's sync plan (skipped when nothing to copy).
        /// The counts come from <see cref="SyncPlanner"/> — the same numbers a backfill
        /// would actually write.
        /// </summary>
        private static void AddPlanRow(List<DiffSummaryRow> rows, string tag, bool toSecondary, SyncPlan plan)
        {
            if (plan == null || plan.ToCopy.Count == 0) return;
            rows.Add(new DiffSummaryRow
            {
                Tag         = tag,
                Direction   = toSecondary ? "Primary → Secondary" : "Secondary → Primary",
                ToSecondary = toSecondary,
                Count       = plan.ToCopy.Count,
                First       = plan.ToCopy[0],
                Last        = plan.ToCopy[plan.ToCopy.Count - 1]
            });
        }

        private void PopulateDiffGrid()
        {
            gridGaps.Rows.Clear();
            foreach (var r in _lastDiffRows)
            {
                string missingOn = r.ToSecondary ? "Secondary" : "Primary";
                string range = (r.First.HasValue && r.Last.HasValue)
                    ? $"{r.First.Value:MM-dd HH:mm} → {r.Last.Value:MM-dd HH:mm}"
                    : "—";
                int rowIdx = gridGaps.Rows.Add(r.Tag, missingOn, r.Count.ToString("N0"), range);
                var row = gridGaps.Rows[rowIdx];
                row.DefaultCellStyle.BackColor = r.ToSecondary ? AppTheme.RowAlt : AppTheme.RowAltWarm;

                // Columns are narrow in the right panel — a full plain-language sentence
                // on hover explains exactly what the row means.
                string source = r.ToSecondary ? "Primary" : "Secondary";
                string full = $"A backfill would copy {r.Count:N0} sample(s) of '{r.Tag}' from {source} to {missingOn}.";
                if (r.First.HasValue && r.Last.HasValue)
                    full += $"\n{r.First.Value:yyyy-MM-dd HH:mm:ss} → {r.Last.Value:yyyy-MM-dd HH:mm:ss}";
                full += $"\n(Same rule the backfill uses — independent collector streams are not double-copied.)" +
                        $"\nClick the row to zoom the timeline to this period.";
                foreach (DataGridViewCell cell in row.Cells)
                    cell.ToolTipText = full;
            }
            lblDiffHint.Visible = _lastDiffRows.Count > 0;
        }

        private async void gridGaps_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            if (_isBusy) return;
            if (e.RowIndex < 0 || e.RowIndex >= _lastDiffRows.Count) return;
            var r = _lastDiffRows[e.RowIndex];
            if (!r.First.HasValue || !r.Last.HasValue) return;
            long pad = Math.Max((r.Last.Value - r.First.Value).Ticks / 10,
                                TimeSpan.FromSeconds(30).Ticks);
            await ZoomTo(r.First.Value - new TimeSpan(pad), r.Last.Value + new TimeSpan(pad));
        }

        // ── Timeline zoom ──────────────────────────────────────────────────────────

        /// <summary>Remembers the current range, sets the pickers to [from,to], re-analyzes.</summary>
        private async Task ZoomTo(DateTime from, DateTime to)
        {
            if (_isBusy) return;
            from = ClampPickerRange(from);
            to   = ClampPickerRange(to);
            if (to <= from) return;

            _zoomStack.Push((dtpStart.Value, dtpEnd.Value));
            lnkZoomOut.Visible = true;
            await SetRangeAndAnalyze(from, to);
        }

        private async void lnkZoomOut_Click(object sender, EventArgs e)
        {
            if (_isBusy || _zoomStack.Count == 0) return;
            var prev = _zoomStack.Pop();
            lnkZoomOut.Visible = _zoomStack.Count > 0;
            await SetRangeAndAnalyze(prev.From, prev.To);
        }

        private async Task SetRangeAndAnalyze(DateTime from, DateTime to)
        {
            // Zoom/zoom-back sets the pickers then analyzes explicitly. Date-picker
            // ValueChanged no longer auto-runs, so no suppression guard is needed.
            dtpStart.Value = from;
            dtpEnd.Value   = to;
            _gapAutoAnalyzeTimer?.Stop();
            await RunGapAnalysis(from, to);
        }

        private static DateTime ClampPickerRange(DateTime value)
        {
            if (value < DateTimePicker.MinimumDateTime) return DateTimePicker.MinimumDateTime;
            if (value > DateTimePicker.MaximumDateTime) return DateTimePicker.MaximumDateTime;
            return value;
        }

        private static string FormatDuration(TimeSpan ts)
        {
            if (ts.TotalDays >= 1)
                return $"{(int)ts.TotalDays}d {ts.Hours}h {ts.Minutes}m";
            if (ts.TotalHours >= 1)
                return $"{(int)ts.TotalHours}h {ts.Minutes}m";
            return $"{ts.Minutes}m {ts.Seconds}s";
        }

        // ── Log panel ──────────────────────────────────────────────────────────────
        private void btnClearLog_Click(object sender, EventArgs e) => txtLog.Clear();

        private void btnCopyLog_Click(object sender, EventArgs e)
        {
            if (!string.IsNullOrEmpty(txtLog.Text))
                Clipboard.SetText(txtLog.Text);
        }

        // ── Export CSV ─────────────────────────────────────────────────────────────
        private void btnExport_Click(object sender, EventArgs e)
        {
            if (_primaryRows.Count == 0 && _secondaryRows.Count == 0)
            { SetStatus("No data to export — read some data first.", true); return; }

            using (var dlg = new SaveFileDialog())
            {
                dlg.Filter   = "CSV files (*.csv)|*.csv";
                dlg.Title    = "Export Data to CSV";
                dlg.FileName = "historian_export";

                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                string dir      = System.IO.Path.GetDirectoryName(dlg.FileName);
                string baseName = System.IO.Path.GetFileNameWithoutExtension(dlg.FileName);

                try
                {
                    int count = 0;
                    if (_primaryRows.Count > 0)
                    {
                        string path = System.IO.Path.Combine(dir, baseName + "_primary.csv");
                        ExportRowsToCsv(path, _primaryRows);
                        count++;
                        Log($"Exported primary data \u2192 {path}");
                    }
                    if (_secondaryRows.Count > 0)
                    {
                        string path = System.IO.Path.Combine(dir, baseName + "_secondary.csv");
                        ExportRowsToCsv(path, _secondaryRows);
                        count++;
                        Log($"Exported secondary data \u2192 {path}");
                    }
                    SetStatus($"Exported {count} table(s) to CSV.");
                }
                catch (Exception ex)
                {
                    SetStatus($"Export failed: {ex.Message}", true);
                }
            }
        }

        private static void ExportRowsToCsv(string path, List<GridRow> rows)
        {
            using (var sw = new System.IO.StreamWriter(path, false, System.Text.Encoding.UTF8))
            {
                sw.WriteLine("Timestamp,Value,Quality");
                foreach (var row in rows)
                {
                    if (row.IsSpacer) continue;
                    sw.WriteLine($"\"{row.Timestamp}\",\"{row.Value}\",\"{row.Quality}\"");
                }
            }
        }

    }
}
