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

        // ── View mode ──────────────────────────────────────────────────────────────
        // Simple is the product; Advanced only ADDS the technical surface (data tables,
        // activity log, filters, per-direction copies, batch counters). Nothing is
        // deleted, so anything used during an acceptance test is one click away.
        private bool _advanced;
        private bool _applyingViewState;   // guards the Advanced checkbox event during setup

        /// <summary>
        /// Controls the view mode has hidden.
        ///
        /// We cannot ask a control: <c>Control.Visible</c> returns EFFECTIVE visibility, which
        /// is false for every child while the form itself has not been shown yet. Reading it
        /// during construction made the layout helpers treat everything as hidden — an empty
        /// sidebar section and a zero-height action column. So the intent is tracked here.
        /// </summary>
        private readonly HashSet<Control> _hiddenByViewMode = new HashSet<Control>();

        private void SetShown(Control c, bool shown)
        {
            c.Visible = shown;
            if (shown) _hiddenByViewMode.Remove(c);
            else       _hiddenByViewMode.Add(c);
        }

        /// <summary>Desired visibility, independent of whether the form is on screen yet.</summary>
        private bool IsShown(Control c) => c != null && !_hiddenByViewMode.Contains(c);

        /// <summary>
        /// The tag mask to browse with. The filter box only exists in Advanced, so the simple
        /// view must fall back to "*" — otherwise a mask saved during an earlier Advanced
        /// session (e.g. "STAT6.T*") would silently hide most measurement points from a user
        /// who has no control to clear it.
        /// </summary>
        private string EffectiveMask()
        {
            if (!_advanced) return "*";
            return string.IsNullOrWhiteSpace(txtTagnameFilter.Text) ? "*" : txtTagnameFilter.Text.Trim();
        }

        // ── Selected measurement point — explicit app state ────────────────────────
        // The combo boxes are an INPUT DEVICE, not the source of truth. A hidden combo (the
        // simple view hides the mirror selector) has no window handle, and then neither
        // ComboBox.Text nor ComboBox.SelectedItem reports the bound selection — Text comes
        // back empty. That is not just a cosmetic label problem: an empty point name makes
        // RunGapAnalysis fall back to the configured HistSync heartbeat tag, so the app would
        // analyse — and offer to repair — a different point than the one shown on screen.
        // Everything therefore reads these two fields, which are set whenever the selection
        // changes, whatever changed it.
        private string _pointPrimary   = "";
        private string _pointSecondary = "";

        /// <summary>Reads the point name off a combo the user can actually see/type in.</summary>
        private static string PointName(ComboBox combo)
        {
            if (combo == null) return "";

            // Typed text wins while the combo is really on screen — Advanced users type to
            // filter, and the typed name may not be the bound item yet.
            if (combo.IsHandleCreated && !string.IsNullOrWhiteSpace(combo.Text))
                return combo.Text.Trim();

            var tag = combo.SelectedItem as Tag;
            if (tag != null && !string.IsNullOrWhiteSpace(tag.Name)) return tag.Name;
            if (combo.SelectedItem != null)
            {
                string s = combo.SelectedItem.ToString();
                if (!string.IsNullOrWhiteSpace(s)) return s;
            }
            return combo.Text ?? "";
        }

        /// <summary>True once an analysis has produced a summary, so re-applying the
        /// language doesn't overwrite a live result with the "connect first" prompt.</summary>
        private bool _hasAnalysis;

        /// <summary>Offline demo session (<c>--demo</c>) — no server is ever contacted.</summary>
        private readonly bool _demoMode;

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
            // > 0, not >= 0: a zero grace disables the race guard entirely — a collector could
            // write into a second between the target read and our write, we'd replace it, verify
            // would pass, and it would be journaled — so a later revert would delete a sample the
            // tool never created.
            return TimeSpan.FromSeconds(
                int.TryParse(cfg, out seconds) && seconds > 0 ? seconds : 120);
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
            // Language must be resolved BEFORE InitializeComponent — the designer's
            // ApplyTexts() runs at the end of it.
            Loc.Language = Loc.Parse(Settings.Default.Language);
            _demoMode    = Program.DemoMode;

            InitializeComponent();

            int maxRetries;
            string retryStr = ConfigurationManager.AppSettings["MaxRetryAttempts"];
            maxRetries = int.TryParse(retryStr, out maxRetries) ? maxRetries : 3;

            if (_demoMode)
            {
                // Offline demo: two in-memory servers. EnableDemoMode creates the two
                // sentinel connections WITHOUT connecting, and DemoDataService never calls
                // its base class, so no server can be reached even by accident.
                _connections.EnableDemoMode("DEMO-MAIN", "DEMO-MIRROR");
                _data = new DemoDataService(_connections.Primary);
            }
            else
            {
                _data = new HistorianDataService(maxRetries);
            }

            ApplyTheme();
            SetupVirtualMode();
            LoadSettings();
            UpdateConnectionStatus();
            UpdateTitleBar();

            // View mode + language wiring (after the controls exist and settings are loaded)
            _advanced = Settings.Default.AdvancedMode;
            _applyingViewState = true;
            chkAdvanced.Checked = _advanced;
            _applyingViewState = false;
            ApplyViewMode();
            ApplyLanguage();
            ShowOverview();   // the all-points list is the landing screen

            lstOverview.EmptyMessage = Loc.T("ov.empty");

            if (_demoMode)
            {
                pnlDemoBanner.Visible = true;
                var demo = (DemoDataService)_data;
                dtpStart.Value = demo.SuggestedFrom;
                dtpEnd.Value   = demo.SuggestedTo;
                txtPrimary.Text   = "DEMO-MAIN";
                txtSecondary.Text = "DEMO-MIRROR";
                UpdateConnectionStatus();
            }

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

        }

        /// <summary>
        /// One-shot scheduled run right after startup.
        ///
        /// ⚠ This path used to be unreachable: it requires both servers to be connected, and
        /// before auto-connect existed that was never true on a cold start. Auto-connect makes
        /// it live, so a "run on startup" flag someone ticked long ago would suddenly perform
        /// an unattended write to a production historian. It is therefore confirmed once,
        /// explicitly, and the answer is remembered.
        /// </summary>
        private async Task TryRunScheduledOnStartup()
        {
            if (_isBusy || _demoMode) return;
            if (!Settings.Default.ScheduleEnabled || !Settings.Default.ScheduleRunOnStartup) return;
            if (!_connections.IsPrimaryConnected || !_connections.IsSecondaryConnected) return;

            if (!Settings.Default.ScheduleStartupConfirmed)
            {
                var ok = MessageBox.Show(this,
                    "This tool is set to run an automatic repair immediately after startup.\n\n" +
                    "It would copy missing readings between the two servers without asking again.\n\n" +
                    "Start automatic repairs on startup from now on?",
                    "Automatic repair on startup",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2);
                if (ok != DialogResult.Yes)
                {
                    Settings.Default.ScheduleRunOnStartup = false;
                    Settings.Default.Save();
                    ScheduleLogger.Append("Startup run declined by the user — 'run on startup' switched off.");
                    return;
                }
                Settings.Default.ScheduleStartupConfirmed = true;
                Settings.Default.Save();
            }

            ScheduleLogger.Append("Startup run triggered after automatic connect.");
            Log("Automatic repair on startup is enabled — starting a scheduled run.");
            try { await _schedule.TriggerNowAsync(); }
            catch (Exception ex) { ScheduleLogger.Append($"Startup-run failed: {ex.Message}"); }
        }

        // ── View mode (simple / advanced) ──────────────────────────────────────────

        private void chkAdvanced_CheckedChanged(object sender, EventArgs e)
        {
            if (_applyingViewState) return;
            _advanced = chkAdvanced.Checked;
            Settings.Default.AdvancedMode = _advanced;
            Settings.Default.Save();
            ApplyViewMode();
        }

        /// <summary>
        /// Shows or hides the technical surface. Nothing here changes behaviour — only what
        /// is on screen. Anything hidden must keep a working default (the tag filter falls
        /// back to "*", the tag link is forced on) so hiding a control can never orphan the
        /// logic that reads it.
        /// </summary>
        private void ApplyViewMode()
        {
            SuspendLayout();
            try
            {
                // Sidebar: filter + statistics + the second point selector are technical.
                SetShown(lblTagnameFilter, _advanced);
                SetShown(txtTagnameFilter, _advanced);
                SetShown(btnGetStats,      _advanced);
                // Both buttons stay full width and stack: "Load measurement points" does not
                // fit in half a sidebar and was rendering as "Load".
                int lw = AppTheme.LeftPanelWidth - 2 - 16;
                btnBrowseTags.Width = lw;
                btnGetStats.Width   = lw;
                btnGetStats.Left    = 0;                        // stacked, not side by side
                btnGetStats.Top     = AppTheme.ButtonHeight + 4;
                pnlTagButtons.Height = _advanced ? AppTheme.ButtonHeight * 2 + 4 : AppTheme.ButtonHeight;
                SetShown(btnTagLink,      _advanced);
                SetShown(lblSecondaryTag, _advanced);
                SetShown(cboSecondary,    _advanced);

                // In the simple view the two servers always show the SAME point — that is
                // what "is my mirror complete?" means. Force the link on so the hidden
                // secondary selector can never drift out of sync with the visible one.
                if (!_advanced && !_tagLinkEnabled)
                {
                    _tagLinkEnabled = true;
                    UpdateTagLinkVisual();
                }

                RelayoutTagsSection();

                // Centre: the activity log and the row-by-row comparison are the technical view.
                SetShown(pnlLog,        _advanced);
                SetShown(btnCompare,    _advanced);
                SetShown(btnSyncScroll, _advanced);

                // Actions: one guarded restore in the simple view, the full set in Advanced.
                SetShown(btnRestore,         !_advanced);
                SetShown(btnCopyToPrimary,   _advanced);
                SetShown(btnCopyToSecondary, _advanced);
                SetShown(btnBackfillPreview, _advanced);
                SizeActionGroups();

                // Quality reads "OK / uncertain / bad" in the simple view and as a percentage
                // in Advanced, so the loaded tables have to be re-rendered.
                RebuildGridRowsForViewMode();
            }
            finally { ResumeLayout(true); }
        }

        /// <summary>Re-maps the loaded samples so the Quality column matches the view mode.</summary>
        private void RebuildGridRowsForViewMode()
        {
            if (_isCompareMode) return;   // compare mode owns its own row building
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
        }

        // ── All-points overview (Phase 12b) ────────────────────────────────────────

        /// <summary>Wall-clock budget for one overview scan. The boss's requirement is that
        /// the landing screen appears quickly; anything not reached in time is shown as
        /// "not checked yet" with a button to finish, never silently dropped.</summary>
        private static readonly TimeSpan ScanBudget = TimeSpan.FromSeconds(10);

        private CoverageScan _lastScan;
        private string _overviewVerdict = "";
        private List<string> _scanTags = new List<string>();
        private bool _showingDetail;

        /// <summary>Shows the all-points list (centre card 1).</summary>
        private void ShowOverview()
        {
            _showingDetail = false;
            pnlDetail.Visible   = false;
            pnlOverview.Visible = true;
            pnlOverview.BringToFront();

            // The right panel belongs to whatever is on screen: coming back from a point it
            // would otherwise still show that ONE point's numbers next to the full list.
            if (_lastScan != null)
            {
                UpdateOverviewSummary();
                UpdateOverviewTotals();
                SetStatus(_overviewVerdict);
            }
        }

        /// <summary>Shows one measurement point in detail (centre card 2).</summary>
        private void ShowDetailCard(string point)
        {
            _showingDetail = true;
            lblDetailPoint.Text = point ?? "";
            pnlOverview.Visible = false;
            pnlDetail.Visible   = true;
            pnlDetail.BringToFront();
        }

        private void lnkBackToOverview_Click(object sender, EventArgs e)
        {
            if (_isBusy) return;
            ShowOverview();
        }

        private void txtOverviewSearch_TextChanged(object sender, EventArgs e)
        {
            lstOverview.SetFilter(txtOverviewSearch.Text);
            UpdateOverviewSummary();
        }

        private async void btnScanRest_Click(object sender, EventArgs e)
        {
            // Finish what the budget cut short — same scan, no time limit this time.
            await ScanOverview(TimeSpan.Zero);
        }

        private async void lstOverview_PointActivated(string point)
        {
            if (_isBusy || string.IsNullOrWhiteSpace(point)) return;
            await OpenPoint(point);
        }

        /// <summary>
        /// Opens one measurement point: selects it on both sides, loads both tables and runs the
        /// EXACT check (SyncPlanner) for it — the overview only ever showed an estimate.
        /// </summary>
        private async Task OpenPoint(string point)
        {
            _pointPrimary = point;
            SyncCombo(cboPrimary, point);
            if (_tagLinkEnabled) TryMirrorTagSelection(cboPrimary, cboSecondary);
            else { _pointSecondary = point; SyncCombo(cboSecondary, point); }

            ShowDetailCard(point);
            await ShowSelectedPoint();
        }

        /// <summary>
        /// Scans every shared measurement point and fills the overview list.
        /// </summary>
        /// <param name="budget">Time limit; <see cref="TimeSpan.Zero"/> means "no limit"
        /// (used by "Check the rest").</param>
        private async Task ScanOverview(TimeSpan? budget = null)
        {
            if (!_connections.IsPrimaryConnected && !_connections.IsSecondaryConnected)
            { lstOverview.Clear(Loc.T("ov.empty")); return; }

            DateTime from = dtpStart.Value, to = dtpEnd.Value;
            if (from >= to) { SetStatus(Loc.T("msg.dateOrder"), true); return; }

            // Points that exist on BOTH servers — the overview is about comparing them.
            if (_scanTags.Count == 0)
            {
                var pri = new HashSet<string>(_browsedPrimaryTags, StringComparer.OrdinalIgnoreCase);
                _scanTags = _browsedSecondaryTags.Where(t => pri.Contains(t))
                                                 .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
                                                 .ToList();
                if (_scanTags.Count == 0)   // nothing shared: still show what the main server has
                    _scanTags = _browsedPrimaryTags.OrderBy(t => t, StringComparer.OrdinalIgnoreCase).ToList();
            }
            if (_scanTags.Count == 0) { lstOverview.Clear(Loc.T("msg.noShared")); return; }

            // One bucket per horizontal pixel at most — finer would be invisible and slower.
            int buckets = Math.Max(120, Math.Min(600, lstOverview.Width - 160));
            TimeSpan limit = budget ?? ScanBudget;

            SetBusy(true, Loc.T("hdr.overview"));
            SetStatus(Loc.F("ov.scanning", 0, _scanTags.Count));
            var conns = new { Main = _connections.Primary, Mirror = _connections.Secondary };

            try
            {
                ResetCts();
                var token = _cts.Token;
                var tags  = _scanTags;

                var scan = await Task.Run(() => CoverageScanner.Scan(
                    _data, conns.Main, conns.Mirror, tags, from, to, buckets, limit, token,
                    (done, total) => SetPhaseProgress(done, total, Loc.F("ov.scanning", done, total))),
                    token);

                _lastScan = scan;
                lstOverview.SetData(scan.Points);
                UpdateOverviewSummary();
                UpdateOverviewTotals();
                SetStatus(_overviewVerdict);   // one verdict, never two different ones
            }
            catch (OperationCanceledException) { SetStatus(Loc.T("msg.checkCancelled")); }
            catch (Exception ex) { SetStatus(Loc.F("msg.checkFailed", ex.Message), true); }
            finally { SetBusy(false); }
        }

        private void UpdateOverviewSummary()
        {
            if (_lastScan == null)
            {
                lblOverviewSummary.Text = "";
                btnScanRest.Visible = false;
                return;
            }

            int needAttention = _lastScan.Points.Count(p => p.Scanned && p.Error == null && !p.InSync);
            _overviewVerdict = needAttention == 0
                ? Loc.F("ov.summaryAllOk", _lastScan.Points.Count, _lastScan.Seconds)
                : Loc.F("ov.summary", _lastScan.Points.Count, needAttention, _lastScan.Seconds);
            // The estimate caveat belongs above the list, where there is room; the status bar
            // gets the verdict alone so it stays on one line.
            lblOverviewSummary.Text = _overviewVerdict + "   ·   " + Loc.T("ov.estimateNote");

            // Honest about what was left out, with the way to finish it.
            btnScanRest.Visible = _lastScan.Truncated;
            if (_lastScan.Truncated)
            {
                _overviewVerdict = Loc.F("ov.truncated",
                    _lastScan.ScannedCount, _lastScan.Points.Count);
                lblOverviewSummary.Text = _overviewVerdict;
            }
        }

        /// <summary>
        /// Fills the right-hand "what's missing" panel from the scan while the overview is up.
        /// Without this it kept telling the user to press a button they had already pressed.
        /// The figures are the scan's ESTIMATE and are marked as such — opening a point
        /// replaces them with the exact SyncPlanner numbers.
        /// </summary>
        private void UpdateOverviewTotals()
        {
            if (_lastScan == null) return;

            int toMirror = _lastScan.Points.Where(p => p.Scanned && p.Error == null).Sum(p => p.EstMissingOnMirror);
            int toMain   = _lastScan.Points.Where(p => p.Scanned && p.Error == null).Sum(p => p.EstMissingOnMain);

            _lastDiffRows = new List<DiffSummaryRow>();
            gridGaps.Rows.Clear();
            lblDiffHint.Visible = false;
            _hasAnalysis = true;

            if (toMirror == 0 && toMain == 0)
            {
                lblGapSummary.Text      = Loc.T("missing.inSync");
                lblGapSummary.ForeColor = AppTheme.Success;
            }
            else
            {
                lblGapSummary.Text      = Loc.F("missing.summaryEst",
                                              toMirror.ToString("N0"), toMain.ToString("N0"));
                lblGapSummary.ForeColor = AppTheme.Danger;
            }
        }

        // ── Language ───────────────────────────────────────────────────────────────

        private void SetLanguage(AppLanguage lang)
        {
            if (Loc.Language == lang) return;
            Loc.Language = lang;
            Settings.Default.Language = lang.ToString();
            Settings.Default.Save();
            ApplyLanguage();
        }

        /// <summary>
        /// Re-applies every text after a language switch. ApplyTexts covers the static
        /// labels; the updaters below re-render the values that depend on live state, so
        /// nothing is left behind in the previous language.
        /// </summary>
        private void ApplyLanguage()
        {
            ApplyTexts();
            lnkLangEn.Font = Loc.Language == AppLanguage.En ? AppTheme.Bold : AppTheme.SectionLabel;
            lnkLangDe.Font = Loc.Language == AppLanguage.De ? AppTheme.Bold : AppTheme.SectionLabel;
            lnkLangEn.LinkColor = Loc.Language == AppLanguage.En ? Color.White : Color.FromArgb(205, 220, 238);
            lnkLangDe.LinkColor = Loc.Language == AppLanguage.De ? Color.White : Color.FromArgb(205, 220, 238);

            UpdateConnectionStatus();
            UpdateTagLinkVisual();
            UpdateScheduleStatusLabel();
            UpdateHeaderServers();
            UpdateGridHeaders();
            if (!_hasAnalysis && string.IsNullOrEmpty(lblStatus.Text)) lblStatus.Text = Loc.T("status.ready");
            PopulateDiffGrid();
            RebuildGridRowsForViewMode();
            timeline.Invalidate();

            // Text produced DURING a run (timeline captions, the strip note, the summaries)
            // would otherwise keep the previous language. Refresh whatever is on screen —
            // and only that: re-running the point analysis while the overview is showing
            // would replace the list's totals with one point's numbers.
            if (!_showingDetail)
            {
                lstOverview.Invalidate();     // row text is drawn through Loc at paint time
                if (_lastScan != null)
                {
                    UpdateOverviewSummary();
                    UpdateOverviewTotals();
                    SetStatus(_overviewVerdict);
                }
            }
            else if (_hasAnalysis && !_isBusy && dtpStart.Value < dtpEnd.Value)
            {
                var again = RunGapAnalysis(dtpStart.Value, dtpEnd.Value);
                GC.KeepAlive(again);
            }
        }

        /// <summary>"GENTHIN — main server   ↔   GENTHINPC2 — mirror" in the header strip.</summary>
        private void UpdateHeaderServers()
        {
            string pri = ServerNaming.Display(ServerNaming.PrimaryLabel, txtPrimary.Text);
            string sec = ServerNaming.Display(ServerNaming.SecondaryLabel, txtSecondary.Text);
            lblHeaderServers.Text = pri + "     ↔     " + sec;
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
            btnSyncScroll.Text = Loc.T(_scrollSyncEnabled ? "btn.unsyncScroll" : "btn.syncScroll");
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

            UpdateHeaderServers();
            BeginInvoke((Action)(async () => { await StartupSequence(); }));
        }

        /// <summary>
        /// Connect on startup so the app is useful without the user pressing anything,
        /// then honour a configured startup repair run. A failed auto-connect is not an
        /// error the user has to dismiss — the Connect button stays there.
        /// </summary>
        private async Task StartupSequence()
        {
            if (!_demoMode)
            {
                if (!Settings.Default.AutoConnectOnStartup) return;
                if (string.IsNullOrWhiteSpace(txtPrimary.Text)) return;
                await ConnectAsync();
            }

            // Connected (or in demo): load the points and open on the all-points overview,
            // so the window is never a blank form and the worst point is already on top.
            if (_connections.IsPrimaryConnected)
            {
                await BrowseTagsAsync();
                await ScanOverview();
            }

            await TryRunScheduledOnStartup();
        }

        // ── Title bar ──────────────────────────────────────────────────────────────
        private void UpdateTitleBar()
        {
            string pri = string.IsNullOrWhiteSpace(txtPrimary.Text) ? "—" : txtPrimary.Text.Trim();
            string sec = string.IsNullOrWhiteSpace(txtSecondary.Text) ? "—" : txtSecondary.Text.Trim();
            bool bothConnected = _connections.IsPrimaryConnected && _connections.IsSecondaryConnected;
            Text = bothConnected
                ? $"{Loc.T("app.title")}  —  {pri}  ↔  {sec}"
                : Loc.T("app.title");
            UpdateHeaderServers();
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
            // "Batch" is an internal chunking detail — the simple view shows plain progress.
            _progressDlg?.UpdateStep(current, total,
                _advanced ? Loc.F("prog.batch", current, total) : Loc.F("prog.step", current, total));
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

            lblPrimaryStatus.Text      = Loc.T(pri ? "conn.connected" : "conn.notConnected");
            lblPrimaryStatus.ForeColor = pri ? AppTheme.Success : AppTheme.TextSecondary;
            txtPrimary.BackColor       = pri ? Color.FromArgb(240, 255, 245) : SystemColors.Window;

            lblSecondaryStatus.Text      = sec ? Loc.T("conn.connected")
                : (string.IsNullOrWhiteSpace(txtSecondary.Text) ? "—" : Loc.T("conn.notConnected"));
            lblSecondaryStatus.ForeColor = sec ? AppTheme.Success : AppTheme.TextSecondary;
            txtSecondary.BackColor       = sec ? Color.FromArgb(240, 255, 245) : SystemColors.Window;

            dotStatus.State = (pri || sec) ? ConnectionState.Connected : ConnectionState.Disconnected;
            UpdateTitleBar();
        }

        private void SetConnecting()
        {
            if (InvokeRequired) { Invoke((Action)SetConnecting); return; }
            lblPrimaryStatus.Text      = Loc.T("conn.connecting");
            lblPrimaryStatus.ForeColor = AppTheme.Warning;
            lblSecondaryStatus.Text    = string.IsNullOrWhiteSpace(txtSecondary.Text) ? "—" : Loc.T("conn.connecting");
            lblSecondaryStatus.ForeColor = AppTheme.Warning;
            dotStatus.State = ConnectionState.Connecting;
        }

        private void SetConnectionError()
        {
            if (InvokeRequired) { Invoke((Action)SetConnectionError); return; }
            lblPrimaryStatus.Text      = Loc.T("conn.failed");
            lblPrimaryStatus.ForeColor = AppTheme.Danger;
            txtPrimary.BackColor       = SystemColors.Window;
            lblSecondaryStatus.Text    = string.IsNullOrWhiteSpace(txtSecondary.Text) ? "—" : Loc.T("conn.failed");
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
                Quality   = QualityText(s.Quality)
            }).ToList();
        }

        /// <summary>
        /// "Quality" means nothing to a plant technician as a percentage. The simple view
        /// says OK / uncertain / bad; Advanced keeps the exact figure the API returned.
        /// </summary>
        private string QualityText(double percentGood)
        {
            if (_advanced) return percentGood.ToString("F1") + "%";
            if (percentGood >= 100.0) return Loc.T("quality.good");
            if (percentGood > 0)      return Loc.T("quality.uncertain");
            return Loc.T("quality.bad");
        }

        private void ExitCompareMode()
        {
            if (!_isCompareMode) return;
            _isCompareMode = false;
            btnCompare.Text = Loc.T("btn.compare");
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
                        Quality   = QualityText(priSamples[i].Quality),
                        IsMismatch = mismatch
                    });
                    secAligned.Add(new GridRow
                    {
                        RawTime   = sTime,
                        Timestamp = sTime.ToString("yyyy-MM-dd HH:mm:ss"),
                        Value     = secSamples[j].Value.ToString("G6"),
                        Quality   = QualityText(secSamples[j].Quality),
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
                        Quality   = QualityText(priSamples[i].Quality),
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
                        Quality   = QualityText(secSamples[j].Quality),
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
                    Value = priSamples[i].Value.ToString("G6"), Quality = QualityText(priSamples[i].Quality),
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
                    Value = secSamples[j].Value.ToString("G6"), Quality = QualityText(secSamples[j].Quality),
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
            await ConnectAsync();
        }

        private async Task ConnectAsync()
        {
            if (_demoMode) return;   // demo sessions are "connected" from the start

            string pri = txtPrimary.Text.Trim();
            string sec = txtSecondary.Text.Trim();
            if (string.IsNullOrWhiteSpace(pri)) { SetStatus(Loc.T("msg.enterHost"), true); return; }

            SetBusy(true, Loc.T("prog.connecting"));
            SetStatus(Loc.T("msg.connecting"));
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
                SetStatus(string.IsNullOrWhiteSpace(sec)
                    ? Loc.F("msg.connectedTo", pri)
                    : Loc.F("msg.connectedToBoth", pri, sec));
            }
            catch (OperationCanceledException)
            {
                UpdateConnectionStatus();
                SetStatus(Loc.T("msg.connectCancelled"));
            }
            catch (Exception ex)
            {
                SetConnectionError();
                SetStatus(Loc.F("msg.connectFailed", ex.Message), true);
            }
            finally { SetBusy(false); }
        }

        // ── Browse Tags ────────────────────────────────────────────────────────────
        private async void btnBrowseTags_Click(object sender, EventArgs e)
        {
            await BrowseTagsAsync();
        }

        private async Task BrowseTagsAsync()
        {
            if (!_connections.IsPrimaryConnected && !_connections.IsSecondaryConnected)
            { SetStatus(Loc.T("msg.connectFirst"), true); return; }

            SetBusy(true);
            SetStatus(Loc.T("msg.loadingPoints"));
            string mask = EffectiveMask();
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
                _scanTags.Clear();   // the overview's point list is derived from this browse

                // Binding a DataSource selects item 0 without raising SelectedIndexChanged for
                // a control that has no handle yet, so seed the explicit selection here: keep
                // the point the user was on if it survived the browse, otherwise take the first.
                _pointPrimary   = PickPoint(_pointPrimary,   _browsedPrimaryTags);
                _pointSecondary = PickPoint(_pointSecondary, _browsedSecondaryTags);
                SyncCombo(cboPrimary,   _pointPrimary);
                SyncCombo(cboSecondary, _pointSecondary);

                int p = priTags?.Length ?? 0;
                int s = secTags?.Length ?? 0;
                SetStatus(Loc.F("msg.pointsLoaded", p, s));
            }
            catch (OperationCanceledException) { SetStatus(Loc.T("msg.browseCancelled")); }
            catch (Exception ex) { SetStatus(Loc.F("msg.browseFailed", ex.Message), true); }
            finally { SetBusy(false); }
        }

        /// <summary>Keeps the current point if the browse still contains it, else takes the first.</summary>
        private static string PickPoint(string current, string[] available)
        {
            if (available == null || available.Length == 0) return "";
            if (!string.IsNullOrWhiteSpace(current) &&
                available.Any(n => string.Equals(n, current, StringComparison.OrdinalIgnoreCase)))
                return current;
            return available[0];
        }

        /// <summary>Moves a combo onto <paramref name="point"/> without raising the auto-read chain.</summary>
        private void SyncCombo(ComboBox combo, string point)
        {
            if (combo == null || string.IsNullOrWhiteSpace(point)) return;
            for (int i = 0; i < combo.Items.Count; i++)
            {
                var t = combo.Items[i] as Tag;
                string name = t != null ? t.Name : combo.Items[i]?.ToString();
                if (!string.Equals(name, point, StringComparison.OrdinalIgnoreCase)) continue;

                _suppressAutoRead = true;
                try { combo.SelectedIndex = i; combo.Text = point; }
                finally { _suppressAutoRead = false; }
                return;
            }
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
            { SetStatus(Loc.T("msg.connectFirst"), true); return; }

            SetBusy(true);
            SetStatus(Loc.T("msg.statsLoading"));
            string mask = EffectiveMask();
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

                SetStatus(Loc.T("msg.statsLoaded"));
            }
            catch (OperationCanceledException) { SetStatus(Loc.T("msg.browseCancelled")); }
            catch (Exception ex) { SetStatus(Loc.F("msg.statsFailed", ex.Message), true); }
            finally { SetBusy(false); }
        }

        // ── Read Data ──────────────────────────────────────────────────────────────
        private async void btnReadPrimary_Click(object sender, EventArgs e)
        {
            if (!_connections.IsPrimaryConnected) { SetStatus(Loc.T("msg.notConnectedMain"), true); return; }
            if (string.IsNullOrWhiteSpace(_pointPrimary)) { SetStatus(Loc.T("msg.selectPoint"), true); return; }
            await ReadPrimaryData();
        }

        private async void cboPrimary_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_suppressAutoRead || _isBusy) return;
            if (!_connections.IsPrimaryConnected) return;

            _pointPrimary = PointName(cboPrimary);   // the user just changed it
            if (string.IsNullOrWhiteSpace(_pointPrimary)) return;

            // Linked mode: auto-select the identical tag on the secondary side too
            bool mirrored = _tagLinkEnabled && !_isLinkPropagating
                && TryMirrorTagSelection(cboPrimary, cboSecondary);

            UpdateGridHeaders();
            await ReadPrimaryData();

            if (mirrored && _connections.IsSecondaryConnected
                && !string.IsNullOrWhiteSpace(_pointSecondary))
            {
                UpdateGridHeaders();
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
            if (from >= to) { SetStatus(Loc.T("msg.dateOrder"), true); return; }

            string tag     = _pointPrimary;
            string priHost = txtPrimary.Text.Trim();

            SetBusy(true);
            SetStatus(Loc.T("msg.readingMain"));
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
                UpdateGridHeaders();
                SetStatus(Loc.F("msg.readMain", samples.Count.ToString("N0"), tag));
            }
            catch (OperationCanceledException) { SetStatus(Loc.T("msg.readCancelled")); }
            catch (Exception ex) { SetStatus(Loc.F("msg.readFailed", ex.Message), true); }
            finally { SetBusy(false); }
        }

        private async void btnReadSecondary_Click(object sender, EventArgs e)
        {
            if (!_connections.IsSecondaryConnected) { SetStatus(Loc.T("msg.notConnectedMirror"), true); return; }
            if (string.IsNullOrWhiteSpace(_pointSecondary)) { SetStatus(Loc.T("msg.selectPoint"), true); return; }
            await ReadSecondaryData();
        }

        private async void cboSecondary_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_suppressAutoRead || _isBusy) return;
            if (!_connections.IsSecondaryConnected) return;

            _pointSecondary = PointName(cboSecondary);   // the user just changed it
            if (string.IsNullOrWhiteSpace(_pointSecondary)) return;

            bool mirrored = _tagLinkEnabled && !_isLinkPropagating
                && TryMirrorTagSelection(cboSecondary, cboPrimary);

            UpdateGridHeaders();
            await ReadSecondaryData();

            if (mirrored && _connections.IsPrimaryConnected
                && !string.IsNullOrWhiteSpace(_pointPrimary))
            {
                UpdateGridHeaders();
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
            string name = changed == cboPrimary ? _pointPrimary : _pointSecondary;
            if (string.IsNullOrWhiteSpace(name)) return false;
            string otherName = other == cboPrimary ? _pointPrimary : _pointSecondary;
            if (string.Equals(otherName, name, StringComparison.OrdinalIgnoreCase)) return false;

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
                    SetStatus(Loc.F("msg.notOnOther", name));
                return false;
            }

            _isLinkPropagating = true;
            _suppressAutoRead = true;
            try { other.SelectedIndex = match; }
            finally { _suppressAutoRead = false; _isLinkPropagating = false; }

            // Record the selection explicitly: a hidden combo will not report it back.
            if (other == cboPrimary) _pointPrimary = name; else _pointSecondary = name;
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
            if (_tagLinkEnabled && !string.IsNullOrWhiteSpace(_pointPrimary)
                && TryMirrorTagSelection(cboPrimary, cboSecondary)
                && _connections.IsSecondaryConnected)
            {
                UpdateGridHeaders();
                var _ = ReadSecondaryThenReanalyze();
            }
        }

        /// <summary>
        /// Loads both servers' data for the point currently selected and checks it, mirroring
        /// the tag to the other side first when the link is on. Used on startup (where binding
        /// the combo raises no SelectedIndexChanged) and anywhere the selection is set in code.
        /// </summary>
        private async Task ShowSelectedPoint()
        {
            if (string.IsNullOrWhiteSpace(_pointPrimary)) return;

            if (_tagLinkEnabled) TryMirrorTagSelection(cboPrimary, cboSecondary);

            if (_connections.IsPrimaryConnected)
            {
                UpdateGridHeaders();
                await ReadPrimaryData();
            }
            if (_connections.IsSecondaryConnected && !string.IsNullOrWhiteSpace(_pointSecondary))
            {
                UpdateGridHeaders();
                await ReadSecondaryData();
            }

            DateTime from = dtpStart.Value, to = dtpEnd.Value;
            if (from < to) await RunGapAnalysis(from, to);
        }

        /// <summary>
        /// Re-labels the two data tables from the current state ("HOST — point").
        ///
        /// Derived in ONE place instead of assigned at six call sites, and forced to repaint:
        /// these labels were updated while a modal progress dialog covered the window and the
        /// text change alone did not reach the screen, leaving a stale caption next to fresh
        /// data. Refresh() paints now rather than whenever the next invalidation happens.
        /// </summary>
        private void UpdateGridHeaders()
        {
            if (InvokeRequired) { Invoke((Action)UpdateGridHeaders); return; }

            // Just the point name: the button above already says which server this table is,
            // and the header strip names both hosts. The old "HOST — point" caption did not
            // fit the label and silently rendered as "HOST — " with the point name cut off.
            lblGridPrimaryTag.Text   = _pointPrimary   ?? "";
            lblGridSecondaryTag.Text = _pointSecondary ?? "";
            lblGridPrimaryTag.Refresh();
            lblGridSecondaryTag.Refresh();
        }

        private async Task ReadSecondaryThenReanalyze()
        {
            await ReadSecondaryData();
            _gapAutoAnalyzeTimer?.Stop();
            _gapAutoAnalyzeTimer?.Start();
        }

        private void UpdateTagLinkVisual()
        {
            btnTagLink.Text = Loc.T(_tagLinkEnabled ? "btn.tagLink.on" : "btn.tagLink.off");
            btnTagLink.BackColor = _tagLinkEnabled ? AppTheme.NavyLight : AppTheme.Background;
            btnTagLink.ForeColor = _tagLinkEnabled ? AppTheme.Navy : AppTheme.TextSecondary;
        }

        private async Task ReadSecondaryData()
        {
            DateTime from = dtpStart.Value;
            DateTime to   = dtpEnd.Value;
            if (from >= to) { SetStatus(Loc.T("msg.dateOrder"), true); return; }

            string tag     = _pointSecondary;
            string secHost = txtSecondary.Text.Trim();

            SetBusy(true);
            SetStatus(Loc.T("msg.readingMirror"));
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
                UpdateGridHeaders();
                SetStatus(Loc.F("msg.readMirror", samples.Count.ToString("N0"), tag));
            }
            catch (OperationCanceledException) { SetStatus(Loc.T("msg.readCancelled")); }
            catch (Exception ex) { SetStatus(Loc.F("msg.readFailed", ex.Message), true); }
            finally { SetBusy(false); }
        }

        // ── Compare ────────────────────────────────────────────────────────────────
        private async void btnCompare_Click(object sender, EventArgs e)
        {
            // Toggle off
            if (_isCompareMode)
            {
                _isCompareMode = false;
                btnCompare.Text = Loc.T("btn.compare");
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
                SetStatus(Loc.T("msg.plainList"));
                return;
            }

            // Need both servers
            if (!_connections.IsPrimaryConnected || !_connections.IsSecondaryConnected)
            { SetStatus(Loc.T("msg.connectBothFirst"), true); return; }

            string priTag = _pointPrimary;
            string secTag = _pointSecondary;
            if (string.IsNullOrWhiteSpace(priTag) || string.IsNullOrWhiteSpace(secTag))
            { SetStatus(Loc.T("msg.comparePoints"), true); return; }

            DateTime from = dtpStart.Value;
            DateTime to   = dtpEnd.Value;
            if (from >= to) { SetStatus(Loc.T("msg.dateOrder"), true); return; }

            string priHost = txtPrimary.Text.Trim();
            string secHost = txtSecondary.Text.Trim();

            SetBusy(true);
            SetStatus(Loc.T("msg.comparing"));

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

                UpdateGridHeaders();

                _isCompareMode = true;
                btnCompare.Text = Loc.T("btn.rawView");

                // Summary
                int matched   = _primaryRows.Count(r => !r.IsSpacer && !r.IsExtra);
                int priOnly   = _primaryRows.Count(r => r.IsExtra);
                int secOnly   = _secondaryRows.Count(r => r.IsExtra);
                int mismatches = _primaryRows.Count(r => r.IsMismatch);
                SetStatus(Loc.F("msg.compareSummary", priSamples.Count, secSamples.Count,
                    matched, priOnly, secOnly, mismatches));
            }
            catch (OperationCanceledException) { SetStatus(Loc.T("msg.compareCancelled")); }
            catch (Exception ex) { SetStatus(Loc.F("msg.compareFailed", ex.Message), true); }
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
                SetStatus(Loc.T("msg.needBoth"), true);
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
            SetStatus(Loc.T("msg.loadingShared"));
            try
            {
                var priTags = _data.BrowseTags(_connections.Primary, "*").Select(t => t.Name).ToList();
                var secTags = _data.BrowseTags(_connections.Secondary, "*").Select(t => t.Name).ToList();
                var shared  = priTags.Intersect(secTags).OrderBy(n => n).ToList();
                if (shared.Count == 0)
                {
                    SetStatus(Loc.T("msg.noShared"), true);
                    return null;
                }
                SetStatus(Loc.F("msg.sharedCount", shared.Count));
                return shared;
            }
            catch (Exception ex)
            {
                SetStatus(Loc.F("msg.sharedFailed", ex.Message), true);
                return null;
            }
        }

        private async void btnCopyToSecondary_Click(object sender, EventArgs e)
        {
            // Same live-edge clamp as the backfill itself, so the dialog's "Will copy"
            // numbers match exactly what ExecuteBackfill will write.
            DateTime from = dtpStart.Value;
            DateTime to   = ClampLiveEdge(dtpEnd.Value);
            if (from >= to) { SetStatus(Loc.T("msg.rangeInvalid"), true); return; }

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
            if (from >= to) { SetStatus(Loc.T("msg.rangeInvalid"), true); return; }

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
            { SetStatus(Loc.T("msg.needBoth"), true); return; }

            DateTime from = dtpStart.Value;
            DateTime to   = ClampLiveEdge(dtpEnd.Value);
            if (from >= to) { SetStatus(Loc.T("msg.rangeInvalid"), true); return; }

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
            if (evalFrom >= evalTo) { SetStatus(Loc.T("msg.rangeInvalid"), true); return null; }

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

            SetBusy(true, Loc.F("prog.restoring", ServerNaming.Short(targetLabel, targetHost)));
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
                                    // Journal in UTC ticks. Sample times are LOCAL above the data
                                    // service, but journals already on disk hold UTC ticks — keeping
                                    // UTC means old and new entries revert identically, with no
                                    // migration. RevertBackfill re-tags them Kind=Utc on the way out.
                                    foreach (var bs in batchSamples)
                                        ticks.Add(SampleFilter.ToSecondTicks(bs.Time.ToUniversalTime()));
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
                SetStatus(_advanced
                    ? Loc.F("msg.restoreDoneAdv", report.BatchesSucceeded, report.BatchesAttempted,
                            totalTags, report.SamplesWritten.ToString("N0"))
                    : Loc.F("msg.restoreDone", report.SamplesWritten.ToString("N0")));
            }
            catch (OperationCanceledException)
            {
                wasCancelled = true;
                report.CompletedAt = DateTime.Now;
                report.Errors.Add("Operation cancelled by user.");
                LogRunReport(report);
                SetStatus(Loc.T("msg.restoreCancelled"));
            }
            catch (Exception ex)
            {
                report.CompletedAt = DateTime.Now;
                report.Errors.Add($"Fatal: {ex.Message}");
                LogRunReport(report);
                SetStatus(Loc.F("msg.restoreFailed", ex.Message), true);
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
                    Loc.F("cancel.body", journal.TotalSamples.ToString("N0"),
                          ServerNaming.Short(targetLabel, targetHost)),
                    Loc.T("cancel.title"),
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
                SetStatus(Loc.F("msg.undoConnect", entry.TargetHost), true);
                return;
            }

            SetBusy(true, Loc.T("prog.undoing"));
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
                        // Journal ticks are UTC (both legacy and current entries). Tag them Kind=Utc
                        // so the data service passes them straight through instead of re-converting
                        // local->UTC — that would delete at an instant 1-2 h off, i.e. real data.
                        var times = t.Ticks.Select(tk => new DateTime(tk, DateTimeKind.Utc)).ToList();

                        int idx = i;
                        Invoke((Action)(() =>
                        {
                            Log($"Revert {idx + 1}/{tags.Count}: {t.TagName} — deleting {times.Count} sample(s)");
                            SetStatus(Loc.F("msg.undoRunning", idx + 1, tags.Count, t.TagName));
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
                    SetStatus(Loc.F("msg.undoDone", totalDeleted.ToString("N0"),
                        ServerNaming.Short(entry.TargetLabel, entry.TargetHost)));
                }
                else
                {
                    SetStatus(Loc.F("msg.undoErrors", errorCount, totalDeleted.ToString("N0")), true);
                }
            }
            catch (OperationCanceledException) { SetStatus(Loc.T("msg.undoCancelled")); }
            catch (Exception ex) { SetStatus(Loc.F("msg.undoFailed", ex.Message), true); }
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
                lblSchedule.Text      = Loc.T("status.schedule.off");
                lblSchedule.ForeColor = AppTheme.TextSecondary;
                return;
            }

            if (_schedule.RunInProgress)
            {
                lblSchedule.Text      = Loc.T("status.schedule.running");
                lblSchedule.ForeColor = AppTheme.Teal;
                return;
            }

            var next = _schedule.NextRunLocal;
            if (next == DateTime.MaxValue)
            {
                lblSchedule.Text      = Loc.T("status.schedule.pending");
                lblSchedule.ForeColor = AppTheme.TextSecondary;
                return;
            }

            // If next-run is today, show only HH:mm. Otherwise show MM-dd HH:mm.
            string nextText = next.Date == DateTime.Today
                ? next.ToString("HH:mm")
                : next.ToString("MM-dd HH:mm");
            lblSchedule.Text      = Loc.F("status.schedule.next", nextText);
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
            { SetStatus(Loc.T("msg.connectFirst"), true); return; }

            DateTime from = dtpStart.Value;
            DateTime to   = dtpEnd.Value;
            if (from >= to) { SetStatus(Loc.T("msg.dateOrder"), true); return; }

            // The button means "check the thing I am looking at": the whole list on the
            // overview, this one point in the detail view.
            if (!_showingDetail)
            {
                await ScanOverview();
                return;
            }

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
                && !string.IsNullOrWhiteSpace(_pointPrimary))
            {
                await ReadPrimaryData();
            }
            if (secHadData && _connections.IsSecondaryConnected
                && !string.IsNullOrWhiteSpace(_pointSecondary))
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
            string priTag = string.IsNullOrWhiteSpace(_pointPrimary)   ? fallback : _pointPrimary;
            string secTag = string.IsNullOrWhiteSpace(_pointSecondary) ? fallback : _pointSecondary;

            // Capture UI values before Task.Run
            string priHost = txtPrimary.Text.Trim();
            string secHost = txtSecondary.Text.Trim();

            SetBusy(true);
            SetStatus(priTag == secTag
                ? Loc.F("msg.checking", priTag)
                : Loc.F("msg.checkingTwo", priTag, secTag));
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
                            stripNote = Loc.F("timeline.strip.independent", planToSec.MatchRate.ToString("P0"));
                    }
                    else if (priTag != secTag)
                        stripNote = Loc.T("timeline.strip.differentTags");
                    else
                        stripNote = Loc.T("timeline.strip.connect");

                    // Blue "backfilled by this tool" bands from the revert journal
                    priBack = LoadBackfilledRanges(priHost, priTag, from, to);
                    secBack = LoadBackfilledRanges(secHost, secTag, from, to);
                }, token);

                _lastPrimaryResult   = priResult;
                _lastSecondaryResult = secResult;
                _lastDiffRows        = diffRows ?? new List<DiffSummaryRow>();

                var priTrack = BuildTrack(priResult, ServerNaming.PrimaryLabel, priHost, priFeasKnown, priFill, priUnfill, priBack);
                var secTrack = BuildTrack(secResult, ServerNaming.SecondaryLabel, secHost, secFeasKnown, secFill, secUnfill, secBack);
                UpdateGapAnalysisUI(from, to, priTrack, secTrack, copyable, stripNote);

                // The value curve is drawn from the SAME samples the tables below are showing
                // (loaded by ReadPrimaryData / ReadSecondaryData), so the graph and the table
                // can never disagree. Missing periods are shaded from this same analysis.
                chart.SetData(from, to, _rawPrimarySamples, _rawSecondarySamples,
                    priFill.Concat(priUnfill).ToList(), secFill.Concat(secUnfill).ToList(),
                    ServerNaming.Short(ServerNaming.PrimaryLabel, priHost),
                    ServerNaming.Short(ServerNaming.SecondaryLabel, secHost));

                int totalGaps = (_lastPrimaryResult?.Gaps.Count ?? 0)
                              + (_lastSecondaryResult?.Gaps.Count ?? 0);
                SetStatus(Loc.F("msg.checkDone", totalGaps));
            }
            catch (OperationCanceledException) { SetStatus(Loc.T("msg.checkCancelled")); }
            catch (Exception ex) { SetStatus(Loc.F("msg.checkFailed", ex.Message), true); }
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

            SetStatus(Loc.T("msg.refreshing"));

            // Refresh whichever card the user is on, so the restored data is visible
            // immediately: the whole list on the overview, this point in the detail view.
            if (!_showingDetail)
            {
                await ScanOverview();
                return;
            }

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
            _hasAnalysis = true;
            if (toSecondary == 0 && toPrimary == 0)
            {
                lblGapSummary.Text      = Loc.T("missing.inSync");
                lblGapSummary.ForeColor = AppTheme.Success;
            }
            else
            {
                lblGapSummary.Text      = Loc.F("missing.summary",
                                              toSecondary.ToString("N0"), toPrimary.ToString("N0"));
                lblGapSummary.ForeColor = AppTheme.Danger;
            }
        }

        /// <summary>Packs one server's analysis into the data the timeline track needs.</summary>
        private TimelineTrackData BuildTrack(GapAnalysisResult result, string sideLabel, string host,
            bool feasibilityKnown, List<TimeRange> fillable, List<TimeRange> unfillable,
            List<TimeRange> backfilled)
        {
            // sideLabel is the INTERNAL role ("Primary"/"Secondary"); everything the user
            // sees goes through ServerNaming.
            string who = ServerNaming.Display(sideLabel, host);

            if (result == null)
            {
                return new TimelineTrackData
                {
                    Label = who,
                    CoverageRatio = -1,
                    EmptyText = Loc.T(string.IsNullOrWhiteSpace(host)
                        ? "timeline.notConnected" : "timeline.notAnalyzed")
                };
            }

            string tag = result.TagName ?? "(tag)";
            // The rule behind "is this a gap?" is shown on the track in Advanced, so the
            // colours are never a black box. In the simple view it is noise — the tooltip
            // on the segment still explains what the colour means.
            string rule = _advanced && result.HasData && result.GapThreshold > TimeSpan.Zero
                ? Loc.F("timeline.rule", FormatDuration(result.GapThreshold))
                : "";
            return new TimelineTrackData
            {
                Label            = $"{who} · {tag}" + (result.HasData ? rule : "  (" + Loc.T("timeline.noData") + ")"),
                TooltipName      = ServerNaming.Short(sideLabel, host),
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
                            // Journal ticks are UTC (deliberately — see architecture.md), while
                            // `from`/`to` and the timeline axis are LOCAL. Reading them with a
                            // plain `new DateTime(tk)` produced Kind=Unspecified and compared raw
                            // ticks, so the blue "restored by this tool" band was drawn 1-2 h off
                            // (and clipped against the wrong window edges). Convert, exactly like
                            // RevertBackfill does when it hands them back to the data service.
                            var dt = new DateTime(tk, DateTimeKind.Utc).ToLocalTime();
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
            string priHost = txtPrimary.Text.Trim();
            string secHost = txtSecondary.Text.Trim();
            foreach (var r in _lastDiffRows)
            {
                // ToSecondary == true means the MIRROR is the one lacking the readings.
                string missingLabel = r.ToSecondary ? ServerNaming.SecondaryLabel : ServerNaming.PrimaryLabel;
                string sourceLabel  = r.ToSecondary ? ServerNaming.PrimaryLabel   : ServerNaming.SecondaryLabel;
                string missingOn    = ServerNaming.Short(missingLabel, r.ToSecondary ? secHost : priHost);
                string source       = ServerNaming.Short(sourceLabel,  r.ToSecondary ? priHost : secHost);

                string range = (r.First.HasValue && r.Last.HasValue)
                    ? $"{r.First.Value:MM-dd HH:mm} → {r.Last.Value:MM-dd HH:mm}"
                    : "—";
                int rowIdx = gridGaps.Rows.Add(r.Tag, missingOn, r.Count.ToString("N0"), range);
                var row = gridGaps.Rows[rowIdx];
                row.DefaultCellStyle.BackColor = r.ToSecondary ? AppTheme.RowAlt : AppTheme.RowAltWarm;

                // Columns are narrow in the right panel — a full plain-language sentence
                // on hover explains exactly what the row means.
                string full = Loc.F("grid.rowTip", r.Count.ToString("N0"), r.Tag, source, missingOn);
                if (r.First.HasValue && r.Last.HasValue)
                    full += $"\n{r.First.Value:yyyy-MM-dd HH:mm:ss} → {r.Last.Value:yyyy-MM-dd HH:mm:ss}";
                full += Loc.T("grid.rowTipRule");
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
            { SetStatus(Loc.T("msg.noExport"), true); return; }

            using (var dlg = new SaveFileDialog())
            {
                dlg.Filter   = "CSV files (*.csv)|*.csv";
                dlg.Title    = Loc.T("msg.exportTitle");
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
                    SetStatus(Loc.F("msg.exported", count));
                }
                catch (Exception ex)
                {
                    SetStatus(Loc.F("msg.exportFailed", ex.Message), true);
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
