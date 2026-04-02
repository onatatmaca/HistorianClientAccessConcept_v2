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
                btnAnalyzeGaps, btnBackfillPreview, btnWriteData
            };
        }

        // ── Startup / Shutdown ─────────────────────────────────────────────────────
        private void ApplyTheme()
        {
            AppTheme.StyleGrid(gridPrimary);
            AppTheme.StyleGrid(gridSecondary);
            AppTheme.StyleGrid(gridGaps);
            AppTheme.StyleGrid(gridFieldDefs);
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

            // Gap analysis always uses HistSync — no radio buttons
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
            _cts?.Cancel();
            _connections.Dispose();
            base.OnFormClosing(e);
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
                _cts = new CancellationTokenSource();
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
                _cts = new CancellationTokenSource();
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

                if (priTags != null)
                {
                    cboPrimary.DataSource    = priTags;
                    cboPrimary.DisplayMember = "Name";
                    cboPrimary.ValueMember   = "Name";
                }
                if (secTags != null)
                {
                    cboSecondary.DataSource    = secTags;
                    cboSecondary.DisplayMember = "Name";
                    cboSecondary.ValueMember   = "Name";
                }

                int p = priTags?.Length ?? 0;
                int s = secTags?.Length ?? 0;
                SetStatus($"Tags loaded — Primary: {p}, Secondary: {s}");
            }
            catch (OperationCanceledException) { SetStatus("Browse cancelled."); }
            catch (Exception ex) { SetStatus($"Browse failed: {ex.Message}", true); }
            finally { SetBusy(false); }
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
                _cts = new CancellationTokenSource();
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
                _cts = new CancellationTokenSource();
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
                _cts = new CancellationTokenSource();
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
                _cts = new CancellationTokenSource();
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
            if (gapResult == null || !gapResult.HasGaps)
            {
                SetStatus($"No gaps found on {targetLabel} — nothing to backfill.", true);
                return null;
            }
            if (!_connections.IsPrimaryConnected || !_connections.IsSecondaryConnected)
            {
                SetStatus("Both servers must be connected for backfill.", true);
                return null;
            }

            int gapCount = gapResult.Gaps.Count;
            int batchCount = gapResult.Gaps.Sum(g => g.Batches.Count(b => b.CanBackfill));
            if (batchCount == 0)
            {
                SetStatus($"No backfillable batches on {targetLabel}.");
                return null;
            }

            // Fetch tags that exist on BOTH servers
            SetStatus("Loading shared tags…");
            List<string> sharedTags;
            try
            {
                var priTags = _data.BrowseTags(_connections.Primary, "*")
                    .Select(t => t.Name).ToList();
                var secTags = _data.BrowseTags(_connections.Secondary, "*")
                    .Select(t => t.Name).ToList();
                sharedTags = priTags.Intersect(secTags).OrderBy(n => n).ToList();
            }
            catch (Exception ex)
            {
                SetStatus($"Failed to load tags: {ex.Message}", true);
                return null;
            }

            if (sharedTags.Count == 0)
            {
                SetStatus("No tags found on both servers.", true);
                return null;
            }

            using (var dlg = new TagSelectionDialog(sourceLabel, targetLabel,
                gapCount, batchCount, sharedTags))
            {
                if (dlg.ShowDialog(this) != System.Windows.Forms.DialogResult.OK)
                    return null;
                return dlg.SelectedTags;
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
            if (_lastPrimaryResult == null && _lastSecondaryResult == null)
            { SetStatus("Run 'Analyze Gaps' first.", true); return; }

            // Show tag selection for the side that has gaps
            List<string> tags = null;
            if (_lastSecondaryResult != null && _lastSecondaryResult.HasGaps)
            {
                tags = ShowTagSelectionDialog(_lastSecondaryResult, "Primary", "Secondary");
                if (tags == null) return;
            }
            else if (_lastPrimaryResult != null && _lastPrimaryResult.HasGaps)
            {
                tags = ShowTagSelectionDialog(_lastPrimaryResult, "Secondary", "Primary");
                if (tags == null) return;
            }

            // Backfill Secondary gaps (data from Primary → Secondary)
            if (_lastSecondaryResult != null && _lastSecondaryResult.HasGaps
                && _connections.IsPrimaryConnected && _connections.IsSecondaryConnected
                && tags != null)
            {
                await ExecuteBackfill(_lastSecondaryResult, _connections.Primary,
                    _connections.Secondary, "Primary", "Secondary", tags);
            }

            // Backfill Primary gaps (data from Secondary → Primary)
            if (_lastPrimaryResult != null && _lastPrimaryResult.HasGaps
                && _connections.IsPrimaryConnected && _connections.IsSecondaryConnected
                && tags != null)
            {
                await ExecuteBackfill(_lastPrimaryResult, _connections.Secondary,
                    _connections.Primary, "Secondary", "Primary", tags);
            }

            await AutoRefreshAfterBackfill();
        }

        private async Task ExecuteBackfill(
            GapAnalysisResult gapResult,
            ServerConnection sourceConn,
            ServerConnection targetConn,
            string sourceLabel,
            string targetLabel,
            List<string> tagsToBackfill)
        {
            if (gapResult == null || !gapResult.HasGaps) return;
            if (sourceConn == null || targetConn == null) return;

            var allBatches = gapResult.Gaps
                .SelectMany(g => g.Batches)
                .Where(b => b.CanBackfill)
                .ToList();

            if (allBatches.Count == 0) return;

            int totalTags    = tagsToBackfill.Count;
            int totalBatches = allBatches.Count;

            SetBusy(true, $"Backfilling {targetLabel}…");
            var report = new SyncRunReport
            {
                StartedAt    = DateTime.Now,
                SourceServer = sourceLabel,
                TargetServer = targetLabel,
                GapsFound    = gapResult.Gaps.Count
            };

            try
            {
                _cts = new CancellationTokenSource();
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
                            Log($"── Tag {tagIdx + 1}/{totalTags}: {tag} ──");
                            SetStatus($"Tag {tagIdx + 1}/{totalTags}: {tag}");
                        }));

                        int batchIdx = 0;
                        foreach (var batch in allBatches)
                        {
                            token.ThrowIfCancellationRequested();
                            batchIdx++;
                            tagResult.BatchesAttempted++;

                            try
                            {
                                var samples = _data.ReadRawInRange(sourceConn, tag, batch.Start, batch.End);

                                if (samples.Count == 0)
                                    continue; // silently skip — no source data

                                var times     = samples.Select(s => s.Time).ToArray();
                                var values    = samples.Select(s => s.Value).ToArray();
                                var qualities = samples.Select(s =>
                                    s.Quality >= 100.0 ? DataQuality.Good :
                                    s.Quality > 0      ? DataQuality.Uncertain :
                                                         DataQuality.Bad).ToArray();

                                var errors = _data.WriteFloatSamplesWithQuality(
                                    targetConn, tag, times, values, qualities);

                                if (errors.Count > 0)
                                {
                                    foreach (var err in errors)
                                        tagResult.Errors.Add($"Batch {batchIdx}: {err}");
                                }
                                else
                                {
                                    tagResult.BatchesSucceeded++;
                                    tagResult.SamplesWritten += samples.Count;
                                }

                                // Read-after-write verification
                                var verify = _data.VerifyWrite(
                                    targetConn, tag, batch.Start, batch.End, samples.Count);
                                if (verify.Actual < verify.Expected)
                                {
                                    tagResult.Errors.Add(
                                        $"Batch {batchIdx}: verification mismatch — wrote {verify.Expected}, found {verify.Actual}");
                                }
                            }
                            catch (Exception ex)
                            {
                                tagResult.BatchesFailed++;
                                tagResult.Errors.Add($"Batch {batchIdx}: {ex.Message}");
                            }

                            // Progress: combined tag + batch
                            int overallDone = tagIdx * totalBatches + batchIdx;
                            int overallTotal = totalTags * totalBatches;
                            Invoke((Action)(() =>
                            {
                                SetStatus($"Tag {tagIdx + 1}/{totalTags}: {tag} — Batch {batchIdx}/{totalBatches}");
                                SetProgress(overallDone, overallTotal);
                            }));
                        }

                        Invoke((Action)(() =>
                            Log($"  {tag}: {tagResult.BatchesSucceeded}/{tagResult.BatchesAttempted} batches, {tagResult.SamplesWritten} samples")));
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
        }

        /// <summary>
        /// Core gap analysis logic — always uses HistSync tag.
        /// Reusable: called from button click and auto-refresh after backfill.
        /// </summary>
        private async Task RunGapAnalysis(DateTime from, DateTime to)
        {
            bool hasPrimary   = _connections.IsPrimaryConnected;
            bool hasSecondary = _connections.IsSecondaryConnected;

            string tagName = Settings.Default.SyncTagName;
            if (string.IsNullOrWhiteSpace(tagName))
            { SetStatus("SyncTagName not configured in app settings.", true); return; }

            // Capture UI values before Task.Run
            string priHost = txtPrimary.Text.Trim();
            string secHost = txtSecondary.Text.Trim();

            SetBusy(true);
            SetStatus($"Analyzing gaps for '{tagName}'…");
            _lastPrimaryResult   = null;
            _lastSecondaryResult = null;

            try
            {
                _cts = new CancellationTokenSource();
                var token = _cts.Token;

                List<DateTime> priTimes = null;
                List<DateTime> secTimes = null;

                await Task.Run(() =>
                {
                    token.ThrowIfCancellationRequested();
                    if (hasPrimary)
                    {
                        var samples = _data.ReadRaw(_connections.Primary, tagName, from);
                        priTimes = samples
                            .Where(s => s.Time >= from && s.Time <= to)
                            .Select(s => s.Time)
                            .OrderBy(t => t)
                            .ToList();
                    }
                    token.ThrowIfCancellationRequested();
                    if (hasSecondary)
                    {
                        var samples = _data.ReadRaw(_connections.Secondary, tagName, from);
                        secTimes = samples
                            .Where(s => s.Time >= from && s.Time <= to)
                            .Select(s => s.Time)
                            .OrderBy(t => t)
                            .ToList();
                    }
                }, token);

                if (priTimes != null)
                    _lastPrimaryResult = _gapAnalysis.Analyze(priTimes, priHost);
                if (secTimes != null)
                    _lastSecondaryResult = _gapAnalysis.Analyze(secTimes, secHost);

                if (_lastPrimaryResult != null && secTimes != null)
                    _gapAnalysis.MarkBackfillFeasibility(_lastPrimaryResult, secTimes);
                if (_lastSecondaryResult != null && priTimes != null)
                    _gapAnalysis.MarkBackfillFeasibility(_lastSecondaryResult, priTimes);

                UpdateGapAnalysisUI(from, to);

                int totalGaps = (_lastPrimaryResult?.Gaps.Count ?? 0)
                              + (_lastSecondaryResult?.Gaps.Count ?? 0);
                SetStatus($"Gap analysis complete — {totalGaps} gap(s) found.");
            }
            catch (OperationCanceledException) { SetStatus("Analysis cancelled."); }
            catch (Exception ex) { SetStatus($"Analysis failed: {ex.Message}", true); }
            finally { SetBusy(false); }
        }

        /// <summary>
        /// After backfill, re-run gap analysis and reload data tables if they were populated.
        /// </summary>
        private async Task AutoRefreshAfterBackfill()
        {
            DateTime from = dtpStart.Value;
            DateTime to   = dtpEnd.Value;
            if (from >= to) return;

            Log("Auto-refreshing gap analysis after backfill…");
            await RunGapAnalysis(from, to);
        }

        private void UpdateGapAnalysisUI(DateTime from, DateTime to)
        {
            if (_lastPrimaryResult != null && _lastPrimaryResult.HasData)
                barPrimary.SetData(from, to, _lastPrimaryResult.Gaps, _lastPrimaryResult.CoverageRatio);
            else
                barPrimary.Clear();

            if (_lastSecondaryResult != null && _lastSecondaryResult.HasData)
                barSecondary.SetData(from, to, _lastSecondaryResult.Gaps, _lastSecondaryResult.CoverageRatio);
            else
                barSecondary.Clear();

            gridGaps.Rows.Clear();
            PopulateGapGrid(_lastPrimaryResult,   "Primary");
            PopulateGapGrid(_lastSecondaryResult, "Secondary");

            int totalGaps = (_lastPrimaryResult?.Gaps.Count   ?? 0)
                          + (_lastSecondaryResult?.Gaps.Count ?? 0);
            if (totalGaps == 0)
            {
                lblGapSummary.Text      = "No gaps found — data appears complete.";
                lblGapSummary.ForeColor = AppTheme.Success;
            }
            else
            {
                TimeSpan totalMissing =
                    (_lastPrimaryResult?.TotalMissingDuration   ?? TimeSpan.Zero) +
                    (_lastSecondaryResult?.TotalMissingDuration ?? TimeSpan.Zero);
                lblGapSummary.Text      = $"{totalGaps} gap(s) — {FormatDuration(totalMissing)} missing total";
                lblGapSummary.ForeColor = AppTheme.Danger;
            }
        }

        private void PopulateGapGrid(GapAnalysisResult result, string serverLabel)
        {
            if (result == null || !result.HasGaps) return;
            foreach (var gap in result.Gaps)
            {
                int backfillable = gap.Batches.Count(b => b.CanBackfill);
                gridGaps.Rows.Add(
                    serverLabel,
                    gap.Start.ToString("yyyy-MM-dd HH:mm"),
                    gap.End.ToString("yyyy-MM-dd HH:mm"),
                    FormatDuration(gap.Duration),
                    gap.Batches.Count,
                    backfillable
                );
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

        // ── Write Data ─────────────────────────────────────────────────────────────
        private async void btnWriteData_Click(object sender, EventArgs e)
        {
            if (!_connections.IsPrimaryConnected)
            { SetStatus("Primary not connected.", true); return; }

            string tagName = cboPrimary.Text;
            if (string.IsNullOrWhiteSpace(tagName))
            { SetStatus("Select a tag to write to.", true); return; }

            string valueText = txtWriteValue.Text.Trim();
            float value;
            if (!float.TryParse(valueText, out value))
            { SetStatus("Enter a valid numeric value.", true); return; }

            DateTime timestamp = dtpWriteTimestamp.Value;

            var confirm = MessageBox.Show(
                $"Write to '{tagName}' on Primary:\n\nTimestamp: {timestamp:yyyy-MM-dd HH:mm:ss}\nValue: {value}\n\nProceed?",
                "Confirm Write",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (confirm != DialogResult.Yes) return;

            SetBusy(true);
            SetStatus($"Writing to '{tagName}'…");

            try
            {
                _cts = new CancellationTokenSource();
                var errors = await Task.Run(() =>
                    _data.WriteFloatSamples(_connections.Primary, tagName,
                        new List<DateTime> { timestamp },
                        new List<float> { value }),
                    _cts.Token);

                if (errors.Count > 0)
                {
                    foreach (var err in errors) Log($"  Write error: {err}");
                    SetStatus($"Write completed with {errors.Count} error(s).", true);
                }
                else
                {
                    SetStatus($"Wrote {value} to '{tagName}' at {timestamp:HH:mm:ss}.");
                }
            }
            catch (OperationCanceledException) { SetStatus("Write cancelled."); }
            catch (Exception ex) { SetStatus($"Write failed: {ex.Message}", true); }
            finally { SetBusy(false); }
        }

        // ── MultiField Tags ────────────────────────────────────────────────────────
        private void btnAddField_Click(object sender, EventArgs e)
        {
            var row = ((System.Data.DataTable)gridFieldDefs.DataSource).NewRow();
            row["FieldName"] = "Field" + (gridFieldDefs.Rows.Count + 1);
            row["DataType"]  = "Float";
            ((System.Data.DataTable)gridFieldDefs.DataSource).Rows.Add(row);
        }

        private void btnRemoveField_Click(object sender, EventArgs e)
        {
            if (gridFieldDefs.CurrentRow == null) return;
            ((System.Data.DataTable)gridFieldDefs.DataSource)
                .Rows[gridFieldDefs.CurrentRow.Index].Delete();
        }

        private void btnCreateMultiFieldType_Click(object sender, EventArgs e)
        {
            Log("Create MultiField type: coming soon.");
        }

        private void btnWriteMultiField_Click(object sender, EventArgs e)
        {
            Log("Write MultiField: coming soon.");
        }

        // ── Log panel ──────────────────────────────────────────────────────────────
        private void btnClearLog_Click(object sender, EventArgs e) => txtLog.Clear();

        private void btnCopyLog_Click(object sender, EventArgs e)
        {
            if (!string.IsNullOrEmpty(txtLog.Text))
                Clipboard.SetText(txtLog.Text);
        }

    }
}
