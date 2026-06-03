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

        // ── Auto-analyze debounce: re-runs gap analysis ~500ms after date changes ──
        private System.Windows.Forms.Timer _gapAutoAnalyzeTimer;
        private bool  _suppressAutoAnalyze;

        // ── Unattended scheduler (Phase 7) ─────────────────────────────────────────
        private ScheduleService _schedule;

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
                btnAnalyzeGaps, btnBackfillPreview, btnHistory
            };

            // Debounced auto-analyze on date change
            _gapAutoAnalyzeTimer = new System.Windows.Forms.Timer { Interval = 500 };
            _gapAutoAnalyzeTimer.Tick += GapAutoAnalyzeTimer_Tick;
            dtpStart.ValueChanged += GapInputChanged;
            dtpEnd.ValueChanged   += GapInputChanged;

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

        private void GapInputChanged(object sender, EventArgs e)
        {
            if (_suppressAutoAnalyze || _isBusy) return;
            if (!_connections.IsPrimaryConnected && !_connections.IsSecondaryConnected) return;
            _gapAutoAnalyzeTimer.Stop();
            _gapAutoAnalyzeTimer.Start();
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
            AppTheme.StyleGrid(gridGaps);
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
            _suppressAutoAnalyze = true;
            try
            {
                var s = Settings.Default;
                txtPrimary.Text   = s.PrimaryHostname;
                txtSecondary.Text = s.SecondaryHostname;
                txtTagnameFilter.Text = s.TagnameFilter;

                dtpStart.Value = s.StartDate > DateTime.MinValue
                    ? s.StartDate : DateTime.Now.AddMonths(-1);
                dtpEnd.Value = s.EndDate > DateTime.MinValue && s.EndDate > s.StartDate
                    ? s.EndDate : DateTime.Now;

                // Gap analysis always uses HistSync — no radio buttons
            }
            finally { _suppressAutoAnalyze = false; }
        }

        private void SaveSettings()
        {
            var s = Settings.Default;
            s.PrimaryHostname      = txtPrimary.Text.Trim();
            s.SecondaryHostname    = txtSecondary.Text.Trim();
            s.TagnameFilter        = txtTagnameFilter.Text.Trim();
            s.StartDate            = dtpStart.Value;
            s.EndDate              = dtpEnd.Value;
            s.Save();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            SaveSettings();
            try { _cts?.Cancel(); } catch { }
            try { _cts?.Dispose(); } catch { }
            try { _gapAutoAnalyzeTimer?.Stop(); _gapAutoAnalyzeTimer?.Dispose(); } catch { }
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
            Log(message);
        }

        private void SetBusy(bool busy, string operationLabel = "")
        {
            if (InvokeRequired) { Invoke((Action)(() => SetBusy(busy, operationLabel))); return; }
            _isBusy = busy;
            progressOp.Visible  = busy;
            btnCancel.Visible   = busy;
            btnStop.Visible     = busy;
            progressOp.Style    = busy ? ProgressBarStyle.Marquee : ProgressBarStyle.Blocks;
            if (!busy) { progressOp.Style = ProgressBarStyle.Blocks; progressOp.Value = 0; }

            foreach (var btn in _actionButtons)
                btn.Enabled = !busy;
        }

        private void SetProgress(int current, int total)
        {
            if (InvokeRequired) { Invoke((Action)(() => SetProgress(current, total))); return; }
            if (total <= 0) return;
            progressOp.Style = ProgressBarStyle.Blocks;
            progressOp.Maximum = total;
            progressOp.Value = Math.Min(current, total);
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

        private void CancelCurrentOperation()
        {
            _cts?.Cancel();
            SetBusy(false);
            SetStatus("Operation cancelled.");
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
            lblGridPrimaryTag.Text = $"{txtPrimary.Text.Trim()} — {cboPrimary.Text}";
            await ReadPrimaryData();
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
            lblGridSecondaryTag.Text = $"{txtSecondary.Text.Trim()} — {cboSecondary.Text}";
            await ReadSecondaryData();
            _gapAutoAnalyzeTimer?.Stop();
            _gapAutoAnalyzeTimer?.Start();
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
            GapAnalysisResult gapResult, string sourceLabel, string targetLabel)
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
                dtpStart.Value, dtpEnd.Value, _data))
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
            var tags = ShowTagSelectionDialog(_lastSecondaryResult, "Primary", "Secondary");
            if (tags == null) return;

            await ExecuteBackfill(_lastSecondaryResult, _connections.Primary,
                _connections.Secondary, "Primary", "Secondary", tags);

            await AutoRefreshAfterBackfill();
        }

        private async void btnCopyToPrimary_Click(object sender, EventArgs e)
        {
            var tags = ShowTagSelectionDialog(_lastPrimaryResult, "Secondary", "Primary");
            if (tags == null) return;

            await ExecuteBackfill(_lastPrimaryResult, _connections.Secondary,
                _connections.Primary, "Secondary", "Primary", tags);

            await AutoRefreshAfterBackfill();
        }

        private async void btnBackfillPreview_Click(object sender, EventArgs e)
        {
            if (!_connections.IsPrimaryConnected || !_connections.IsSecondaryConnected)
            { SetStatus("Both servers must be connected for backfill.", true); return; }

            DateTime from = dtpStart.Value, to = dtpEnd.Value;
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

                        // Direct-comparison diff at whole-second resolution (Historian's
                        // storage precision). Comparing exact ticks would never match a
                        // sub-second source sample to the second it gets stored as, so the
                        // same samples would be "missing" and re-copied on every run.
                        var tgtTicks = new HashSet<long>(tgtData.Select(s => SampleFilter.ToSecondTicks(s.Time)));
                        var missing = srcData
                            .Where(s => !tgtTicks.Contains(SampleFilter.ToSecondTicks(s.Time)))
                            .OrderBy(s => s.Time)
                            .ToList();

                        if (missing.Count == 0)
                        {
                            Invoke((Action)(() =>
                                Log($"  {tag}: already in sync ({srcData.Count} source, {tgtData.Count} target).")));
                            continue;
                        }

                        // Group missing samples into batches by the configured bucket duration.
                        // Each batch contains samples within `batchSize` of the batch-start
                        // timestamp; batch-start resets when the next sample falls outside.
                        var batches = SampleBucketer.GroupByBucket(missing, batchSize);
                        int totalBatches = batches.Count;

                        Invoke((Action)(() =>
                            Log($"  {tag}: {missing.Count} missing sample(s) → {totalBatches} batch(es)")));

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
                report.CompletedAt = DateTime.Now;
                report.Errors.Add("Operation cancelled by user.");
                LogRunReport(report);
                SetStatus("Backfill cancelled — completed tags are preserved.");
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
            if (writtenTicks.Count > 0)
            {
                try
                {
                    var journal = new BackfillJournalEntry
                    {
                        Id          = BackfillJournalService.NewId(),
                        RunLocal    = report.StartedAt,
                        Mode        = unattended ? "Scheduled" : "Manual",
                        SourceLabel = sourceLabel,
                        SourceHost  = sourceHost,
                        TargetLabel = targetLabel,
                        TargetHost  = targetHost
                    };
                    foreach (var kv in writtenTicks)
                        journal.Tags.Add(new BackfillJournalTag { TagName = kv.Key, Ticks = kv.Value.ToArray() });
                    BackfillJournalService.Save(journal);
                }
                catch (Exception ex) { Log($"Journal save error: {ex.Message}"); }
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
                }, token);

                if (priOnPrimary != null)
                {
                    _lastPrimaryResult = _gapAnalysis.Analyze(priOnPrimary, priHost, from, to);
                    _lastPrimaryResult.TagName = priTag;
                }
                if (secOnSecondary != null)
                {
                    _lastSecondaryResult = _gapAnalysis.Analyze(secOnSecondary, secHost, from, to);
                    _lastSecondaryResult.TagName = secTag;
                }

                if (_lastPrimaryResult != null && priOnSecondary != null)
                    _gapAnalysis.MarkBackfillFeasibility(_lastPrimaryResult, priOnSecondary);
                if (_lastSecondaryResult != null && secOnPrimary != null)
                    _gapAnalysis.MarkBackfillFeasibility(_lastSecondaryResult, secOnPrimary);

                // Cross-server diff for the selected tag(s) — reuses the samples already
                // fetched above (no extra reads). Drives the right-panel diff table.
                var diffRows = new List<DiffSummaryRow>();
                var d1 = BuildDiffRow(priTag, "Primary → Secondary", true,  priOnPrimary,   priOnSecondary);
                if (d1 != null) diffRows.Add(d1);
                var d2 = BuildDiffRow(priTag, "Secondary → Primary", false, priOnSecondary, priOnPrimary);
                if (d2 != null) diffRows.Add(d2);
                if (secTag != priTag)
                {
                    var d3 = BuildDiffRow(secTag, "Primary → Secondary", true,  secOnPrimary,   secOnSecondary);
                    if (d3 != null) diffRows.Add(d3);
                    var d4 = BuildDiffRow(secTag, "Secondary → Primary", false, secOnSecondary, secOnPrimary);
                    if (d4 != null) diffRows.Add(d4);
                }
                _lastDiffRows = diffRows;

                UpdateGapAnalysisUI(from, to);

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
                var samples = _data.ReadRaw(conn, tag, from);
                return samples
                    .Where(s => s.Time >= from && s.Time <= to)
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

        private void UpdateGapAnalysisUI(DateTime from, DateTime to)
        {
            string priHost = txtPrimary.Text.Trim();
            string secHost = txtSecondary.Text.Trim();

            RenderCoverage(_lastPrimaryResult, barPrimary, lblPrimaryTagName,
                "Primary", priHost, from, to);
            RenderCoverage(_lastSecondaryResult, barSecondary, lblSecondaryTagName,
                "Secondary", secHost, from, to);

            PopulateDiffGrid();

            int toSecondary = _lastDiffRows.Where(r =>  r.ToSecondary).Sum(r => r.Count);
            int toPrimary   = _lastDiffRows.Where(r => !r.ToSecondary).Sum(r => r.Count);
            if (toSecondary == 0 && toPrimary == 0)
            {
                lblGapSummary.Text      = "In sync — both servers hold the same\nsamples for the selected tag(s).";
                lblGapSummary.ForeColor = AppTheme.Success;
            }
            else
            {
                lblGapSummary.Text      = $"Primary → Secondary:  {toSecondary:N0} missing\n" +
                                          $"Secondary → Primary:  {toPrimary:N0} missing";
                lblGapSummary.ForeColor = AppTheme.Danger;
            }
        }

        /// <summary>
        /// Renders one server's coverage bar + label. Handles three cases:
        /// (1) result has data → show bar with gaps, (2) analyzed but empty data → fully red 0%,
        /// (3) not analyzed yet → neutral "not analyzed" label.
        /// </summary>
        private void RenderCoverage(GapAnalysisResult result, CoverageBar bar, Label label,
            string sideLabel, string host, DateTime from, DateTime to)
        {
            if (result != null && result.HasData)
            {
                string tag = result.TagName ?? "(tag)";
                bar.TooltipLabel = $"{host} · {tag}";
                bar.SetData(from, to, result.Gaps, result.CoverageRatio);
                label.Text = $"{sideLabel}: {host}  ·  {tag}";
            }
            else if (result != null)
            {
                // Analyzed, but zero samples in the range → render as "fully missing" (red 0%)
                string tag = result.TagName ?? "(tag)";
                var wholeSpan = new List<GapWindow>
                {
                    new GapWindow { Start = from, End = to }
                };
                bar.TooltipLabel = $"{host} · {tag} · no data";
                bar.SetData(from, to, wholeSpan, 0.0);
                label.Text = string.IsNullOrWhiteSpace(host)
                    ? $"{sideLabel} — not connected"
                    : $"{sideLabel}: {host}  ·  {tag}  ·  no data";
            }
            else
            {
                bar.Clear();
                label.Text = string.IsNullOrWhiteSpace(host)
                    ? $"{sideLabel} — not analyzed"
                    : $"{sideLabel}: {host}  ·  not analyzed";
            }
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
        /// Counts whole-second timestamps present on <paramref name="hasSide"/> but absent on
        /// <paramref name="lacksSide"/> (the cross-server diff, matching backfill's resolution).
        /// Returns null if either list is missing (server not connected) or nothing is missing.
        /// </summary>
        private static DiffSummaryRow BuildDiffRow(string tag, string direction, bool toSecondary,
            List<DateTime> hasSide, List<DateTime> lacksSide)
        {
            if (hasSide == null || lacksSide == null) return null;
            var lackTicks = new HashSet<long>(lacksSide.Select(SampleFilter.ToSecondTicks));
            int count = 0;
            DateTime? first = null, last = null;
            foreach (var t in hasSide)
            {
                if (lackTicks.Contains(SampleFilter.ToSecondTicks(t))) continue;
                count++;
                if (first == null || t < first) first = t;
                if (last  == null || t > last)  last  = t;
            }
            if (count == 0) return null;
            return new DiffSummaryRow
            {
                Tag = tag, Direction = direction, ToSecondary = toSecondary,
                Count = count, First = first, Last = last
            };
        }

        private void PopulateDiffGrid()
        {
            gridGaps.Rows.Clear();
            foreach (var r in _lastDiffRows)
            {
                string shortDir = r.ToSecondary ? "→ Secondary" : "→ Primary";
                string range = (r.First.HasValue && r.Last.HasValue)
                    ? $"{r.First.Value:MM-dd HH:mm} → {r.Last.Value:MM-dd HH:mm}"
                    : "—";
                int rowIdx = gridGaps.Rows.Add(r.Tag, shortDir, r.Count.ToString("N0"), range);
                var row = gridGaps.Rows[rowIdx];
                row.DefaultCellStyle.BackColor = r.ToSecondary ? AppTheme.RowAlt : AppTheme.RowAltWarm;
                // Columns are narrow in the right panel — full text on hover.
                row.Cells["Tag"].ToolTipText = r.Tag;
                row.Cells["Direction"].ToolTipText = r.Direction;   // e.g. "Primary → Secondary"
                row.Cells["Range"].ToolTipText = (r.First.HasValue && r.Last.HasValue)
                    ? $"{r.First.Value:yyyy-MM-dd HH:mm:ss} → {r.Last.Value:yyyy-MM-dd HH:mm:ss}"
                    : "—";
            }
        }

        private static string FormatDuration(TimeSpan ts)
        {
            if (ts.TotalDays >= 1)
                return $"{(int)ts.TotalDays}d {ts.Hours}h {ts.Minutes}m";
            if (ts.TotalHours >= 1)
                return $"{(int)ts.TotalHours}h {ts.Minutes}m";
            return $"{ts.Minutes}m {ts.Seconds}s";
        }

        // ── Stop / Cancel ──────────────────────────────────────────────────────────
        private void btnStop_Click(object sender, EventArgs e)   => CancelCurrentOperation();
        private void btnCancel_Click(object sender, EventArgs e) => CancelCurrentOperation();

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
