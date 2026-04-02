using HistorianSyncTool.Models;
using HistorianSyncTool.Properties;
using HistorianSyncTool.Services;
using HistorianSyncTool.UI;
using HistorianSyncTool.UI.Controls;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace HistorianSyncTool.Forms
{
    public partial class MainForm : Form
    {
        // ── Services ───────────────────────────────────────────────────────────────
        private readonly HistorianConnectionService _connections = new HistorianConnectionService();
        private readonly HistorianDataService       _data        = new HistorianDataService();
        private readonly GapAnalysisService         _gapAnalysis = new GapAnalysisService();

        // ── Gap Analysis state ─────────────────────────────────────────────────────
        private GapAnalysisResult _lastPrimaryResult;
        private GapAnalysisResult _lastSecondaryResult;

        // ── Cancellation ───────────────────────────────────────────────────────────
        private CancellationTokenSource _cts;

        // ── Constructor ────────────────────────────────────────────────────────────
        public MainForm()
        {
            InitializeComponent();
            ApplyTheme();
            LoadSettings();
            UpdateConnectionStatus();
            UpdateTitleBar();
        }

        // ── Startup / Shutdown ─────────────────────────────────────────────────────
        private void ApplyTheme()
        {
            AppTheme.StyleGrid(gridPrimary);
            AppTheme.StyleGrid(gridSecondary);
            AppTheme.StyleGrid(gridGaps);
            AppTheme.StyleGrid(gridFieldDefs);
        }

        private void LoadSettings()
        {
            var s = Settings.Default;
            txtPrimary.Text   = s.PrimaryHostname;
            txtSecondary.Text = s.SecondaryHostname;
            txtTagnameFilter.Text = s.TagnameFilter;

            // Restore dates — fall back to sensible defaults if never saved
            dtpStart.Value = s.StartDate > DateTime.MinValue
                ? s.StartDate : DateTime.Now.AddMonths(-1);
            dtpEnd.Value = s.EndDate > DateTime.MinValue && s.EndDate > s.StartDate
                ? s.EndDate : DateTime.Now;

            radioHistSync.Checked      =  s.GapAnalysisOnHistSync;
            radioSelectedTag.Checked   = !s.GapAnalysisOnHistSync;
        }

        private void SaveSettings()
        {
            var s = Settings.Default;
            s.PrimaryHostname      = txtPrimary.Text.Trim();
            s.SecondaryHostname    = txtSecondary.Text.Trim();
            s.TagnameFilter        = txtTagnameFilter.Text.Trim();
            s.StartDate            = dtpStart.Value;
            s.EndDate              = dtpEnd.Value;
            s.GapAnalysisOnHistSync = radioHistSync.Checked;
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
            progressOp.Visible  = busy;
            btnCancel.Visible   = busy;
            btnStop.Visible     = busy;
            progressOp.Style    = busy ? ProgressBarStyle.Marquee : ProgressBarStyle.Blocks;
            if (!busy) { progressOp.Style = ProgressBarStyle.Blocks; progressOp.Value = 0; }
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
                await System.Threading.Tasks.Task.Run(() =>
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

            try
            {
                _cts = new CancellationTokenSource();
                var token = _cts.Token;

                Proficy.Historian.ClientAccess.API.Tag[] priTags = null;
                Proficy.Historian.ClientAccess.API.Tag[] secTags = null;

                await System.Threading.Tasks.Task.Run(() =>
                {
                    token.ThrowIfCancellationRequested();
                    if (_connections.IsPrimaryConnected)
                        priTags = _data.BrowseTags(_connections.Primary, mask).ToArray();
                    token.ThrowIfCancellationRequested();
                    if (_connections.IsSecondaryConnected)
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

            try
            {
                _cts = new CancellationTokenSource();
                var token = _cts.Token;
                int priCount = 0, secCount = 0;

                await System.Threading.Tasks.Task.Run(() =>
                {
                    token.ThrowIfCancellationRequested();
                    if (_connections.IsPrimaryConnected)
                        priCount = _data.BrowseTags(_connections.Primary, mask).Count;
                    token.ThrowIfCancellationRequested();
                    if (_connections.IsSecondaryConnected)
                        secCount = _data.BrowseTags(_connections.Secondary, mask).Count;
                }, token);

                if (_connections.IsPrimaryConnected)
                    Log($"Primary  ({txtPrimary.Text.Trim()}): {priCount} float tag(s) matching '{mask}'");
                if (_connections.IsSecondaryConnected)
                    Log($"Secondary ({txtSecondary.Text.Trim()}): {secCount} float tag(s) matching '{mask}'");

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

            SetBusy(true);
            SetStatus("Reading primary data…");

            try
            {
                _cts = new CancellationTokenSource();
                var tag   = cboPrimary.Text;
                var from  = DateTime.Now.AddMinutes(-10);
                var to    = DateTime.Now;

                var samples = await System.Threading.Tasks.Task.Run(
                    () => _data.ReadInterpolated(_connections.Primary, tag, from, to, 10),
                    _cts.Token);

                var dt = BuildSampleTable(samples);
                gridPrimary.DataSource = dt;
                SetStatus($"Primary: {samples.Count} samples read for '{tag}'.");
            }
            catch (OperationCanceledException) { SetStatus("Read cancelled."); }
            catch (Exception ex) { SetStatus($"Read failed: {ex.Message}", true); }
            finally { SetBusy(false); }
        }

        private async void btnReadSecondary_Click(object sender, EventArgs e)
        {
            if (!_connections.IsSecondaryConnected) { SetStatus("Secondary not connected.", true); return; }
            if (string.IsNullOrWhiteSpace(cboSecondary.Text)) { SetStatus("Select a secondary tag.", true); return; }

            SetBusy(true);
            SetStatus("Reading secondary data…");

            try
            {
                _cts = new CancellationTokenSource();
                var tag   = cboSecondary.Text;
                var from  = DateTime.Now.AddMinutes(-10);
                var to    = DateTime.Now;

                var samples = await System.Threading.Tasks.Task.Run(
                    () => _data.ReadInterpolated(_connections.Secondary, tag, from, to, 10),
                    _cts.Token);

                var dt = BuildSampleTable(samples);
                gridSecondary.DataSource = dt;
                SetStatus($"Secondary: {samples.Count} samples read for '{tag}'.");
            }
            catch (OperationCanceledException) { SetStatus("Read cancelled."); }
            catch (Exception ex) { SetStatus($"Read failed: {ex.Message}", true); }
            finally { SetBusy(false); }
        }

        private System.Data.DataTable BuildSampleTable(
            System.Collections.Generic.List<(DateTime Time, float Value, double Quality)> samples)
        {
            var dt = new System.Data.DataTable();
            dt.Columns.Add("Timestamp", typeof(string));
            dt.Columns.Add("Value",     typeof(string));
            dt.Columns.Add("Quality",   typeof(string));
            foreach (var s in samples)
            {
                var row = dt.NewRow();
                row["Timestamp"] = s.Time.ToString("yyyy-MM-dd HH:mm:ss");
                row["Value"]     = s.Value.ToString("G6");
                row["Quality"]   = $"{s.Quality:F1}%";
                dt.Rows.Add(row);
            }
            return dt;
        }

        // ── Compare ────────────────────────────────────────────────────────────────
        private async void btnCompare_Click(object sender, EventArgs e)
        {
            if (!_connections.IsPrimaryConnected && !_connections.IsSecondaryConnected)
            { SetStatus("Connect to a server first.", true); return; }
            if (string.IsNullOrWhiteSpace(cboPrimary.Text) && string.IsNullOrWhiteSpace(cboSecondary.Text))
            { SetStatus("Browse and select tags first.", true); return; }

            DateTime from = dtpStart.Value;
            DateTime to   = dtpEnd.Value;
            if (from >= to) { SetStatus("Start date must be before end date.", true); return; }

            SetBusy(true);
            SetStatus("Comparing tags over evaluation period…");

            try
            {
                _cts = new CancellationTokenSource();
                var token = _cts.Token;
                const int sampleCount = 200;

                List<(DateTime Time, float Value, double Quality)> priSamples = null;
                List<(DateTime Time, float Value, double Quality)> secSamples = null;

                await System.Threading.Tasks.Task.Run(() =>
                {
                    token.ThrowIfCancellationRequested();
                    if (_connections.IsPrimaryConnected && !string.IsNullOrWhiteSpace(cboPrimary.Text))
                        priSamples = _data.ReadInterpolated(_connections.Primary, cboPrimary.Text, from, to, sampleCount);
                    token.ThrowIfCancellationRequested();
                    if (_connections.IsSecondaryConnected && !string.IsNullOrWhiteSpace(cboSecondary.Text))
                        secSamples = _data.ReadInterpolated(_connections.Secondary, cboSecondary.Text, from, to, sampleCount);
                }, token);

                if (priSamples != null) gridPrimary.DataSource   = BuildSampleTable(priSamples);
                if (secSamples != null) gridSecondary.DataSource = BuildSampleTable(secSamples);

                int p = priSamples?.Count ?? 0;
                int s = secSamples?.Count ?? 0;
                SetStatus($"Compare: Primary {p} samples, Secondary {s} samples over evaluation period.");
            }
            catch (OperationCanceledException) { SetStatus("Compare cancelled."); }
            catch (Exception ex) { SetStatus($"Compare failed: {ex.Message}", true); }
            finally { SetBusy(false); }
        }

        private void btnCopyToPrimary_Click(object sender, EventArgs e)
        {
            Log("Copy to Primary: run Analyze Gaps first.");
        }

        private void btnCopyToSecondary_Click(object sender, EventArgs e)
        {
            Log("Copy to Secondary: run Analyze Gaps first.");
        }

        // ── Gap Analysis ───────────────────────────────────────────────────────────
        private async void btnAnalyzeGaps_Click(object sender, EventArgs e)
        {
            bool hasPrimary   = _connections.IsPrimaryConnected;
            bool hasSecondary = _connections.IsSecondaryConnected;
            if (!hasPrimary && !hasSecondary)
            { SetStatus("Connect to at least one server first.", true); return; }

            string tagName = radioHistSync.Checked
                ? Settings.Default.SyncTagName
                : cboPrimary.Text;

            if (string.IsNullOrWhiteSpace(tagName))
            { SetStatus("Select a tag (or switch to HistSync mode).", true); return; }

            DateTime from = dtpStart.Value;
            DateTime to   = dtpEnd.Value;
            if (from >= to) { SetStatus("Start date must be before end date.", true); return; }

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

                await System.Threading.Tasks.Task.Run(() =>
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
                    _lastPrimaryResult = _gapAnalysis.Analyze(priTimes, txtPrimary.Text.Trim());
                if (secTimes != null)
                    _lastSecondaryResult = _gapAnalysis.Analyze(secTimes, txtSecondary.Text.Trim());

                // Cross-server backfill feasibility
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

        private void UpdateGapAnalysisUI(DateTime from, DateTime to)
        {
            // Coverage bars
            if (_lastPrimaryResult != null && _lastPrimaryResult.HasData)
                barPrimary.SetData(from, to, _lastPrimaryResult.Gaps, _lastPrimaryResult.CoverageRatio);
            else
                barPrimary.Clear();

            if (_lastSecondaryResult != null && _lastSecondaryResult.HasData)
                barSecondary.SetData(from, to, _lastSecondaryResult.Gaps, _lastSecondaryResult.CoverageRatio);
            else
                barSecondary.Clear();

            // Gap detail grid
            gridGaps.Rows.Clear();
            PopulateGapGrid(_lastPrimaryResult,   "Primary");
            PopulateGapGrid(_lastSecondaryResult, "Secondary");

            // Summary label
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

        private void btnBackfillPreview_Click(object sender, EventArgs e)
        {
            if (_lastPrimaryResult == null && _lastSecondaryResult == null)
            { SetStatus("Run 'Analyze Gaps' first.", true); return; }
            Log("Backfill preview: coming in next update.");
        }

        // ── Stop / Cancel ──────────────────────────────────────────────────────────
        private void btnStop_Click(object sender, EventArgs e)   => CancelCurrentOperation();
        private void btnCancel_Click(object sender, EventArgs e) => CancelCurrentOperation();

        // ── Write Data ─────────────────────────────────────────────────────────────
        private void btnWriteData_Click(object sender, EventArgs e)
        {
            Log("Write data: coming soon.");
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

        // ── Log panel toggle ───────────────────────────────────────────────────────
        private void btnClearLog_Click(object sender, EventArgs e) => txtLog.Clear();

        private void btnCopyLog_Click(object sender, EventArgs e)
        {
            if (!string.IsNullOrEmpty(txtLog.Text))
                Clipboard.SetText(txtLog.Text);
        }

        // ── Mode radio changed ─────────────────────────────────────────────────────
        private void radioMode_CheckedChanged(object sender, EventArgs e)
        {
            // Visual hint: when "selected tag" mode is active, highlight the tag combos
            bool useTag = radioSelectedTag.Checked;
            cboPrimary.BackColor   = useTag ? AppTheme.NavyLight : Color.White;
            cboSecondary.BackColor = useTag ? AppTheme.NavyLight : Color.White;
        }
    }
}
