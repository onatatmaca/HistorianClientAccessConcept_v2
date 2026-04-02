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

        // ── Constructor ────────────────────────────────────────────────────────────
        public MainForm()
        {
            InitializeComponent();

            // Read retry config
            int maxRetries;
            string retryStr = ConfigurationManager.AppSettings["MaxRetryAttempts"];
            maxRetries = int.TryParse(retryStr, out maxRetries) ? maxRetries : 3;
            _data = new HistorianDataService(maxRetries);

            ApplyTheme();
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
            _isBusy = busy;
            progressOp.Visible  = busy;
            btnCancel.Visible   = busy;
            btnStop.Visible     = busy;
            progressOp.Style    = busy ? ProgressBarStyle.Marquee : ProgressBarStyle.Blocks;
            if (!busy) { progressOp.Style = ProgressBarStyle.Blocks; progressOp.Value = 0; }

            // Disable/enable action buttons to prevent re-entrancy
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

            try
            {
                _cts = new CancellationTokenSource();
                var token = _cts.Token;

                Tag[] priTags = null;
                Tag[] secTags = null;

                await Task.Run(() =>
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

                await Task.Run(() =>
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

                var samples = await Task.Run(
                    () => _data.ReadInterpolated(_connections.Primary, tag, from, to, 10),
                    _cts.Token);

                gridPrimary.DataSource = BuildSampleTable(samples);
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

                var samples = await Task.Run(
                    () => _data.ReadInterpolated(_connections.Secondary, tag, from, to, 10),
                    _cts.Token);

                gridSecondary.DataSource = BuildSampleTable(samples);
                SetStatus($"Secondary: {samples.Count} samples read for '{tag}'.");
            }
            catch (OperationCanceledException) { SetStatus("Read cancelled."); }
            catch (Exception ex) { SetStatus($"Read failed: {ex.Message}", true); }
            finally { SetBusy(false); }
        }

        private System.Data.DataTable BuildSampleTable(
            List<(DateTime Time, float Value, double Quality)> samples)
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

                await Task.Run(() =>
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

        // ── Copy / Backfill ────────────────────────────────────────────────────────
        private async void btnCopyToSecondary_Click(object sender, EventArgs e)
        {
            await ExecuteBackfill(
                sourceResult: _lastSecondaryResult,
                sourceConn: _connections.Primary,
                targetConn: _connections.Secondary,
                sourceLabel: "Primary",
                targetLabel: "Secondary");
        }

        private async void btnCopyToPrimary_Click(object sender, EventArgs e)
        {
            await ExecuteBackfill(
                sourceResult: _lastPrimaryResult,
                sourceConn: _connections.Secondary,
                targetConn: _connections.Primary,
                sourceLabel: "Secondary",
                targetLabel: "Primary");
        }

        private async void btnBackfillPreview_Click(object sender, EventArgs e)
        {
            if (_lastPrimaryResult == null && _lastSecondaryResult == null)
            { SetStatus("Run 'Analyze Gaps' first.", true); return; }

            // Show preview summary
            int priGaps = _lastPrimaryResult?.Gaps.Count ?? 0;
            int secGaps = _lastSecondaryResult?.Gaps.Count ?? 0;
            int priBatches = _lastPrimaryResult?.Gaps.Sum(g => g.Batches.Count(b => b.CanBackfill)) ?? 0;
            int secBatches = _lastSecondaryResult?.Gaps.Sum(g => g.Batches.Count(b => b.CanBackfill)) ?? 0;

            string tagName = radioHistSync.Checked
                ? Settings.Default.SyncTagName
                : cboPrimary.Text;

            string msg = $"Backfill Preview for '{tagName}'\n\n" +
                         $"Primary gaps:   {priGaps} ({priBatches} backfillable batches)\n" +
                         $"Secondary gaps: {secGaps} ({secBatches} backfillable batches)\n\n" +
                         $"Batch size: {_gapAnalysis.BatchSize.TotalMinutes} min\n\n" +
                         "This will read data from the server that HAS data and write it\n" +
                         "to the server with the gap. Original sample quality is preserved.\n\n" +
                         "Proceed with backfill of BOTH servers?";

            var result = MessageBox.Show(msg, "Confirm Backfill",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (result != DialogResult.Yes) return;

            // Backfill secondary gaps (source = primary)
            if (_lastSecondaryResult != null && _lastSecondaryResult.HasGaps
                && _connections.IsPrimaryConnected && _connections.IsSecondaryConnected)
            {
                await ExecuteBackfill(_lastSecondaryResult, _connections.Primary,
                    _connections.Secondary, "Primary", "Secondary");
            }

            // Backfill primary gaps (source = secondary)
            if (_lastPrimaryResult != null && _lastPrimaryResult.HasGaps
                && _connections.IsPrimaryConnected && _connections.IsSecondaryConnected)
            {
                await ExecuteBackfill(_lastPrimaryResult, _connections.Secondary,
                    _connections.Primary, "Secondary", "Primary");
            }
        }

        /// <summary>
        /// Core backfill engine: reads from source, writes to target, batch by batch.
        /// sourceResult = the gap analysis of the TARGET (it has the gaps we need to fill).
        /// sourceConn = server that HAS the data. targetConn = server MISSING the data.
        /// </summary>
        private async Task ExecuteBackfill(
            GapAnalysisResult sourceResult,
            ServerConnection sourceConn,
            ServerConnection targetConn,
            string sourceLabel,
            string targetLabel)
        {
            if (sourceResult == null || !sourceResult.HasGaps)
            {
                SetStatus($"No gaps found on {targetLabel} — nothing to backfill.", true);
                return;
            }
            if (sourceConn == null || targetConn == null)
            {
                SetStatus("Both servers must be connected for backfill.", true);
                return;
            }

            string tagName = radioHistSync.Checked
                ? Settings.Default.SyncTagName
                : cboPrimary.Text;

            if (string.IsNullOrWhiteSpace(tagName))
            { SetStatus("No tag selected for backfill.", true); return; }

            // Count backfillable batches
            var allBatches = sourceResult.Gaps
                .SelectMany(g => g.Batches)
                .Where(b => b.CanBackfill)
                .ToList();

            if (allBatches.Count == 0)
            {
                SetStatus($"No backfillable batches on {targetLabel}.");
                return;
            }

            var confirmResult = MessageBox.Show(
                $"Backfill {allBatches.Count} batch(es) from {sourceLabel} → {targetLabel}\n" +
                $"Tag: {tagName}\n\nProceed?",
                "Confirm Copy",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (confirmResult != DialogResult.Yes) return;

            SetBusy(true, $"Backfilling {targetLabel}…");
            var report = new SyncRunReport
            {
                StartedAt    = DateTime.Now,
                SourceServer = sourceLabel,
                TargetServer = targetLabel,
                SourceTag    = tagName,
                TargetTag    = tagName,
                GapsFound    = sourceResult.Gaps.Count
            };

            try
            {
                _cts = new CancellationTokenSource();
                var token = _cts.Token;
                int batchIndex = 0;
                int totalBatches = allBatches.Count;

                await Task.Run(() =>
                {
                    foreach (var batch in allBatches)
                    {
                        token.ThrowIfCancellationRequested();
                        batchIndex++;
                        report.BatchesAttempted++;

                        try
                        {
                            // Read from source
                            var samples = _data.ReadRawInRange(sourceConn, tagName, batch.Start, batch.End);

                            if (samples.Count == 0)
                            {
                                Invoke((Action)(() => Log($"  Batch {batchIndex}/{totalBatches}: no source data in [{batch.Start:HH:mm} – {batch.End:HH:mm}], skipped.")));
                                continue;
                            }

                            // Build arrays preserving quality
                            var times     = samples.Select(s => s.Time).ToArray();
                            var values    = samples.Select(s => s.Value).ToArray();
                            var qualities = samples.Select(s =>
                                s.Quality >= 100.0 ? DataQuality.Good :
                                s.Quality > 0      ? DataQuality.Uncertain :
                                                     DataQuality.Bad).ToArray();

                            // Write to target
                            var errors = _data.WriteFloatSamplesWithQuality(targetConn, tagName, times, values, qualities);
                            batch.SamplesWritten = samples.Count;

                            if (errors.Count > 0)
                            {
                                foreach (var err in errors)
                                    report.Errors.Add($"Batch {batchIndex}: {err}");
                                Invoke((Action)(() => Log($"  Batch {batchIndex}/{totalBatches}: wrote {samples.Count} samples with {errors.Count} error(s).")));
                            }
                            else
                            {
                                report.BatchesSucceeded++;
                                report.SamplesWritten += samples.Count;
                            }

                            // Read-after-write verification
                            var verify = _data.VerifyWrite(targetConn, tagName, batch.Start, batch.End, samples.Count);
                            batch.Verified = verify.Actual >= verify.Expected;
                            if (!batch.Verified)
                            {
                                string msg = $"Batch {batchIndex}: verification mismatch — wrote {verify.Expected}, found {verify.Actual}";
                                report.Errors.Add(msg);
                                Invoke((Action)(() => Log($"  ⚠ {msg}")));
                            }
                        }
                        catch (Exception ex)
                        {
                            report.BatchesFailed++;
                            report.Errors.Add($"Batch {batchIndex}: {ex.Message}");
                            Invoke((Action)(() => Log($"  Batch {batchIndex}/{totalBatches}: FAILED — {ex.Message}")));
                            // Skip & continue
                        }

                        // Update progress on UI thread
                        Invoke((Action)(() => SetProgress(batchIndex, totalBatches)));
                    }
                }, token);

                report.CompletedAt = DateTime.Now;
                LogRunReport(report);
                SetStatus($"Backfill complete: {report.BatchesSucceeded}/{report.BatchesAttempted} batches, {report.SamplesWritten} samples written.");
            }
            catch (OperationCanceledException)
            {
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
        }

        private void LogRunReport(SyncRunReport report)
        {
            Log("─── Sync Run Report ───────────────────────────");
            Log($"  {report.SourceServer} → {report.TargetServer}  |  Tag: {report.SourceTag}");
            Log($"  Duration: {report.Duration.TotalSeconds:F1}s");
            Log($"  Gaps: {report.GapsFound}  |  Batches: {report.BatchesAttempted} attempted, {report.BatchesSucceeded} succeeded, {report.BatchesFailed} failed");
            Log($"  Samples written: {report.SamplesWritten}");
            if (report.Errors.Count > 0)
            {
                Log($"  Errors ({report.Errors.Count}):");
                foreach (var err in report.Errors)
                    Log($"    • {err}");
            }
            Log("────────────────────────────────────────────────");
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
            bool useTag = radioSelectedTag.Checked;
            cboPrimary.BackColor   = useTag ? AppTheme.NavyLight : Color.White;
            cboSecondary.BackColor = useTag ? AppTheme.NavyLight : Color.White;
        }
    }
}
