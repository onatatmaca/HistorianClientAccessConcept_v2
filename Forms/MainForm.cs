using HistorianSyncTool.Properties;
using HistorianSyncTool.Services;
using HistorianSyncTool.UI;
using HistorianSyncTool.UI.Controls;
using System;
using System.Drawing;
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
        private void btnGetStats_Click(object sender, EventArgs e)
        {
            Log("Server stats: coming soon.");
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
        private void btnCompare_Click(object sender, EventArgs e)
        {
            Log("Compare: coming soon.");
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
        private void btnAnalyzeGaps_Click(object sender, EventArgs e)
        {
            Log("Gap analysis: coming soon.");
        }

        private void btnBackfillPreview_Click(object sender, EventArgs e)
        {
            Log("Backfill preview: coming soon.");
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
