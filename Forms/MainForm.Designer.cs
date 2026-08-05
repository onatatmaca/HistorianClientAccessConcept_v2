using HistorianSyncTool.UI;
using HistorianSyncTool.UI.Controls;
using System;
using System.Drawing;
using System.Windows.Forms;

namespace HistorianSyncTool.Forms
{
    partial class MainForm
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && components != null) components.Dispose();
            base.Dispose(disposing);
        }

        // ── Control declarations ───────────────────────────────────────────────────

        // Header strip (app identity + which servers + language + Advanced)
        private Panel          pnlHeader;
        private Label          lblAppTitle;
        private Label          lblHeaderServers;
        private LinkLabel      lnkLangEn;
        private LinkLabel      lnkLangDe;
        private CheckBox       chkAdvanced;
        private Panel          pnlDemoBanner;
        private Label          lblDemoBanner;

        // Status bar
        private Panel          pnlStatusBar;
        private ConnectionDot  dotStatus;
        private Label          lblStatus;
        private Label          lblSchedule;

        // Left sidebar
        private Panel          pnlLeft;
        private Panel          pnlLeftContent;

        // Left — Connection
        private SectionHeader  hdrConnection;
        private Label          lblPrimary;
        private ComboBox       txtPrimary;
        private Label          lblPrimaryStatus;
        private Label          lblSecondary;
        private ComboBox       txtSecondary;
        private Label          lblSecondaryStatus;
        private FlatButton     btnConnect;
        private FlatButton     btnCredentials;

        // Left — Evaluation period
        private SectionHeader  hdrPeriod;
        private Label          lblStartDate;
        private DateTimePicker dtpStart;
        private Label          lblEndDate;
        private DateTimePicker dtpEnd;
        private Panel          pnlQuickDates;
        private Button         btnLast1h;
        private Button         btnLast6h;
        private Button         btnLast24h;
        private Button         btnLast3d;
        private Button         btnLast7d;
        private Button         btnLast30d;
        private Button         btnLast90d;
        private Button         btnLastYear;
        private FlatButton     btnAnalyzeGaps;

        // Left — Tags
        private SectionHeader  hdrTags;
        private Label          lblTagnameFilter;
        private TextBox        txtTagnameFilter;
        private Panel          pnlTagButtons;
        private FlatButton     btnBrowseTags;
        private FlatButton     btnGetStats;
        private Label          lblPrimaryTag;
        private ComboBox       cboPrimary;
        private Button         btnTagLink;
        private Label          lblSecondaryTag;
        private ComboBox       cboSecondary;

        // Right panel
        private Panel          pnlRight;
        private Panel          pnlRightContent;
        private SectionHeader  hdrGapAnalysis;
        private Label          lblGapSummary;
        private Label          lblDiffHint;
        private System.Windows.Forms.DataGridView gridGaps;
        private FlatButton     btnBackfillPreview;

        // Center card 1 — all-points overview (the landing screen)
        private Panel          pnlOverview;
        private SectionHeader  hdrOverview;
        private TextBox        txtOverviewSearch;
        private Label          lblOverviewSummary;
        private TagOverviewList lstOverview;

        // Center card 2 — one measurement point in detail
        private Panel          pnlDetail;
        private Panel          pnlDetailHeader;
        private FlatButton     lnkBackToOverview;
        private Label          lblDetailPoint;

        // Center — measured values of the open point, both servers overlaid
        private Panel          pnlChart;
        private SectionHeader  hdrChart;
        private LinkLabel      lnkEnlarge;
        private ValueChart     chart;

        // Center — sync timeline (both servers on one shared time axis)
        private Panel          pnlTimeline;
        private SectionHeader  hdrTimeline;
        private LinkLabel      lnkZoomOut;
        private GapTimeline    timeline;

        // Center
        private Panel          pnlCenter;
        private CollapsiblePanel pnlLog;
        private RichTextBox    txtLog;
        private Panel          pnlLogButtons;
        private Button         btnClearLog;
        private Button         btnCopyLog;
        private TableLayoutPanel pnlGrids;
        private System.Windows.Forms.DataGridView gridPrimary;
        private System.Windows.Forms.DataGridView gridSecondary;
        private Panel          pnlGridActions;
        private Label          lblGridPrimaryTag;
        private Label          lblGridSecondaryTag;
        private FlatButton     btnCompare;
        private FlatButton     btnCopyToPrimary;
        private FlatButton     btnCopyToSecondary;
        private FlatButton     btnSyncScroll;
        private FlatButton     btnExport;
        private FlatButton     btnHistory;

        /// <summary>Simple view's single write action — opens the same preview-then-confirm
        /// flow as "Preview &amp; restore", so nothing is written without a confirmation.</summary>
        private FlatButton     btnRestore;

        // Group containers + headers (fields so Advanced mode can hide them and
        // ApplyTexts can re-label them)
        private Panel          pnlViewGroup;
        private Panel          pnlBackfillGroup;
        private SectionHeader  hdrView;
        private SectionHeader  hdrBackfill;

        /// <summary>Shared tooltip provider — all texts assigned in ApplyTexts.</summary>
        private ToolTip        _tips;

        // ── InitializeComponent ────────────────────────────────────────────────────
        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();
            this.SuspendLayout();

            // ── Form ───────────────────────────────────────────────────────────────
            this.Font          = AppTheme.Default;
            this.BackColor     = AppTheme.Background;
            this.AutoScaleMode = AutoScaleMode.Font;
            this.MinimumSize   = new Size(1000, 650);
            this.Text          = Loc.T("app.title");
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Padding       = new Padding(8, 4, 8, 0);  // symmetric breathing room around all panels

            // Fit to screen: use 90% of working area or 1380x820, whichever is smaller
            var screen = System.Windows.Forms.Screen.PrimaryScreen.WorkingArea;
            int formW  = System.Math.Min(1380, (int)(screen.Width  * 0.92));
            int formH  = System.Math.Min(820,  (int)(screen.Height * 0.92));
            this.Size  = new Size(formW, formH);

            // ══════════════════════════════════════════════════════════════════════
            // HEADER STRIP  (Dock=Top) — who am I, which servers, language, Advanced
            // ══════════════════════════════════════════════════════════════════════
            pnlHeader = new Panel
            {
                Dock      = DockStyle.Top,
                Height    = 40,
                BackColor = AppTheme.Navy,
                Padding   = new Padding(12, 0, 8, 0)
            };

            lblAppTitle = new Label
            {
                Dock      = DockStyle.Left,
                Width     = 210,
                Font      = AppTheme.AppTitle,
                ForeColor = Color.White,
                TextAlign = ContentAlignment.MiddleLeft
            };

            // "GENTHIN — main server   ↔   GENTHINPC2 — mirror". Filled by UpdateHeaderServers().
            lblHeaderServers = new Label
            {
                Dock      = DockStyle.Fill,
                Font      = AppTheme.Default,
                ForeColor = Color.FromArgb(205, 220, 238),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding   = new Padding(14, 0, 0, 0)
            };

            // Advanced switch — the ONE control that reveals the technical surface.
            chkAdvanced = new CheckBox
            {
                Dock       = DockStyle.Right,
                Width      = 96,
                Font       = AppTheme.Default,
                ForeColor  = Color.White,
                TextAlign  = ContentAlignment.MiddleLeft,
                Cursor     = Cursors.Hand,
                FlatStyle  = FlatStyle.Flat,
                Padding    = new Padding(4, 0, 0, 0)
            };
            chkAdvanced.FlatAppearance.BorderSize = 0;
            chkAdvanced.CheckedChanged += chkAdvanced_CheckedChanged;

            lnkLangDe = MakeLangLink("DE");
            lnkLangEn = MakeLangLink("EN");
            lnkLangDe.Click += (s, e) => SetLanguage(AppLanguage.De);
            lnkLangEn.Click += (s, e) => SetLanguage(AppLanguage.En);

            // Dock=Right stacks right-to-left in add order: Advanced, then DE, then EN.
            pnlHeader.Controls.Add(lblHeaderServers);  // Fill first
            pnlHeader.Controls.Add(lblAppTitle);
            pnlHeader.Controls.Add(chkAdvanced);
            pnlHeader.Controls.Add(lnkLangDe);
            pnlHeader.Controls.Add(lnkLangEn);

            // ── Demo banner (only visible with --demo) ────────────────────────────
            pnlDemoBanner = new Panel
            {
                Dock      = DockStyle.Top,
                Height    = 24,
                BackColor = AppTheme.Warning,
                Visible   = false
            };
            lblDemoBanner = new Label
            {
                Dock      = DockStyle.Fill,
                Font      = AppTheme.Bold,
                ForeColor = Color.FromArgb(60, 40, 0),
                TextAlign = ContentAlignment.MiddleCenter
            };
            pnlDemoBanner.Controls.Add(lblDemoBanner);

            // ══════════════════════════════════════════════════════════════════════
            // STATUS BAR  (Dock=Bottom)
            // ══════════════════════════════════════════════════════════════════════
            pnlStatusBar = new Panel
            {
                Dock      = DockStyle.Bottom,
                Height    = AppTheme.StatusBarHeight,
                BackColor = AppTheme.Surface,
                Padding   = new Padding(10, 0, 10, 0)
            };
            pnlStatusBar.Paint += (s, e) =>
                e.Graphics.DrawLine(new Pen(AppTheme.Border), 0, 0, pnlStatusBar.Width, 0);

            dotStatus = new ConnectionDot
            {
                Left  = 12,
                Top   = (AppTheme.StatusBarHeight - 14) / 2,
                State = ConnectionState.Disconnected
            };

            lblStatus = new Label
            {
                Text      = "",
                Left      = 34, Top = 0,
                Width     = 600, Height = AppTheme.StatusBarHeight,
                Font      = AppTheme.Default,
                ForeColor = AppTheme.TextPrimary,
                TextAlign = ContentAlignment.MiddleLeft
            };

            lblSchedule = new Label
            {
                Text       = "",
                Dock       = DockStyle.Right,
                Width      = 200,
                Font       = AppTheme.Default,
                ForeColor  = AppTheme.TextSecondary,
                TextAlign  = ContentAlignment.MiddleRight,
                Cursor     = Cursors.Hand,
                Padding    = new Padding(0, 0, 12, 0)
            };
            lblSchedule.Click += lblSchedule_Click;

            pnlStatusBar.Controls.Add(dotStatus);
            pnlStatusBar.Controls.Add(lblStatus);
            pnlStatusBar.Controls.Add(lblSchedule);

            // ══════════════════════════════════════════════════════════════════════
            // LEFT SIDEBAR  (Dock=Left)
            // ══════════════════════════════════════════════════════════════════════
            pnlLeft = new Panel
            {
                Dock      = DockStyle.Left,
                Width     = AppTheme.LeftPanelWidth,
                BackColor = AppTheme.Surface
            };
            pnlLeft.Paint += (s, e) =>
                e.Graphics.DrawLine(new Pen(AppTheme.Border),
                    pnlLeft.Width - 1, 0, pnlLeft.Width - 1, pnlLeft.Height);

            pnlLeftContent = new Panel
            {
                Dock       = DockStyle.Fill,
                AutoScroll = true,
                Padding    = new Padding(0, 0, 1, 0)
            };

            // fw = full width (section header bars span edge-to-edge within the panel)
            // pad = left indent for content controls
            // lw  = usable width for indented content
            int fw  = AppTheme.LeftPanelWidth - 2;
            const int pad = 8;
            int lw  = fw - pad * 2;

            // ── CONNECTION ─────────────────────────────────────────────────────────
            hdrConnection = new SectionHeader { Text = "", Width = fw, Left = 0, Top = 0 };

            var pnlConnContent = new Panel { Left = 0, Top = hdrConnection.Bottom, Width = fw };

            lblPrimary = MakeLabel("", lw, pad, 8);
            txtPrimary = MakeHostCombo(lw, pad, lblPrimary.Bottom + 2);
            lblPrimaryStatus = new Label
            {
                Text = "Not connected",
                Left = pad, Top = txtPrimary.Bottom + 2,
                Width = lw, Height = 16,
                Font = AppTheme.Small, ForeColor = AppTheme.TextSecondary, AutoSize = false
            };

            lblSecondary = MakeLabel("", lw, pad, lblPrimaryStatus.Bottom + 6);
            txtSecondary = MakeHostCombo(lw, pad, lblSecondary.Bottom + 2);
            lblSecondaryStatus = new Label
            {
                Text = "Not connected",
                Left = pad, Top = txtSecondary.Bottom + 2,
                Width = lw, Height = 16,
                Font = AppTheme.Small, ForeColor = AppTheme.TextSecondary, AutoSize = false
            };

            btnConnect = MakeButton("", lw, pad, lblSecondaryStatus.Bottom + 8);
            btnConnect.Click += btnConnect_Click;

            // Servers that reject an empty user name need a login, and the delivered package
            // deliberately ships without one. Without this button the only way to supply it was
            // to hand-edit the .config beside the exe.
            btnCredentials = MakeButton("", lw, pad, btnConnect.Bottom + 6);
            btnCredentials.ButtonStyle = FlatButtonStyle.Secondary;
            btnCredentials.Click += btnCredentials_Click;

            // Shared tooltip provider — texts are assigned in ApplyTexts so they follow
            // the language switch. Server fields accept "host", "host:port", "ip", "ip:port".
            _tips = new ToolTip(components) { AutoPopDelay = 12000, InitialDelay = 400 };

            pnlConnContent.Controls.AddRange(new Control[]
            {
                lblPrimary, txtPrimary, lblPrimaryStatus,
                lblSecondary, txtSecondary, lblSecondaryStatus,
                btnConnect, btnCredentials
            });
            pnlConnContent.Height = btnCredentials.Bottom + 10;

            // ── EVALUATION PERIOD (includes gap analysis mode radios) ──────────────
            hdrPeriod = new SectionHeader
            {
                Text = "", Width = fw, Left = 0,
                Top  = hdrConnection.Bottom + pnlConnContent.Height
            };

            var pnlPeriodContent = new Panel { Left = 0, Top = hdrPeriod.Bottom, Width = fw };

            lblStartDate = MakeLabel("", lw, pad, 8);
            dtpStart     = MakeDtp(lw, pad, lblStartDate.Bottom + 2);
            lblEndDate   = MakeLabel("", lw, pad, dtpStart.Bottom + 6);
            dtpEnd       = MakeDtp(lw, pad, lblEndDate.Bottom + 2);

            // Quick-select presets — 4x2 grid
            pnlQuickDates = new Panel { Left = pad, Top = dtpEnd.Bottom + 6, Width = lw, Height = 52 };
            int qw = (lw - 12) / 4;  // 4 columns with 4px gaps
            int qh = 22;
            btnLast1h   = MakeQuickBtn("1h",   0);
            btnLast6h   = MakeQuickBtn("6h",   qw + 4);
            btnLast24h  = MakeQuickBtn("24h",  (qw + 4) * 2);
            btnLast3d   = MakeQuickBtn("3d",   (qw + 4) * 3);
            btnLast7d   = MakeQuickBtn("7d",   0);
            btnLast30d  = MakeQuickBtn("30d",  qw + 4);
            btnLast90d  = MakeQuickBtn("90d",  (qw + 4) * 2);
            btnLastYear = MakeQuickBtn("1y",   (qw + 4) * 3);
            // Row 1
            btnLast1h.Width = btnLast6h.Width = btnLast24h.Width = btnLast3d.Width = qw;
            btnLast1h.Height = btnLast6h.Height = btnLast24h.Height = btnLast3d.Height = qh;
            // Row 2
            btnLast7d.Top = btnLast30d.Top = btnLast90d.Top = btnLastYear.Top = qh + 4;
            btnLast7d.Width = btnLast30d.Width = btnLast90d.Width = btnLastYear.Width = qw;
            btnLast7d.Height = btnLast30d.Height = btnLast90d.Height = btnLastYear.Height = qh;

            btnLast1h.Click   += (s, e) => { dtpStart.Value = System.DateTime.Now.AddHours(-1);  dtpEnd.Value = System.DateTime.Now; };
            btnLast6h.Click   += (s, e) => { dtpStart.Value = System.DateTime.Now.AddHours(-6);  dtpEnd.Value = System.DateTime.Now; };
            btnLast24h.Click  += (s, e) => { dtpStart.Value = System.DateTime.Now.AddHours(-24); dtpEnd.Value = System.DateTime.Now; };
            btnLast3d.Click   += (s, e) => { dtpStart.Value = System.DateTime.Now.AddDays(-3);   dtpEnd.Value = System.DateTime.Now; };
            btnLast7d.Click   += (s, e) => { dtpStart.Value = System.DateTime.Now.AddDays(-7);   dtpEnd.Value = System.DateTime.Now; };
            btnLast30d.Click  += (s, e) => { dtpStart.Value = System.DateTime.Now.AddDays(-30);  dtpEnd.Value = System.DateTime.Now; };
            btnLast90d.Click  += (s, e) => { dtpStart.Value = System.DateTime.Now.AddDays(-90);  dtpEnd.Value = System.DateTime.Now; };
            btnLastYear.Click += (s, e) => { dtpStart.Value = System.DateTime.Now.AddYears(-1);  dtpEnd.Value = System.DateTime.Now; };
            pnlQuickDates.Controls.AddRange(new Control[]
                { btnLast1h, btnLast6h, btnLast24h, btnLast3d, btnLast7d, btnLast30d, btnLast90d, btnLastYear });

            btnAnalyzeGaps = MakeButton("", lw, pad, pnlQuickDates.Bottom + 8);
            btnAnalyzeGaps.Click += btnAnalyzeGaps_Click;

            pnlPeriodContent.Controls.AddRange(new Control[]
            {
                lblStartDate, dtpStart, lblEndDate, dtpEnd,
                pnlQuickDates,
                btnAnalyzeGaps
            });
            pnlPeriodContent.Height = btnAnalyzeGaps.Bottom + 10;

            // ── TAGS ──────────────────────────────────────────────────────────────
            hdrTags = new SectionHeader
            {
                Text = "", Width = fw, Left = 0,
                Top  = hdrPeriod.Bottom + pnlPeriodContent.Height
            };

            var pnlTagsContent = new Panel { Left = 0, Top = hdrTags.Bottom, Width = fw };

            lblTagnameFilter = MakeLabel("", lw, pad, 8);
            txtTagnameFilter = MakeTextBox(lw, pad, lblTagnameFilter.Bottom + 2);

            pnlTagButtons = new Panel
            {
                Left = pad, Top = txtTagnameFilter.Bottom + 6, Width = lw, Height = AppTheme.ButtonHeight
            };
            btnBrowseTags = MakeButton("",  (lw - 4) / 2, 0, 0);
            btnGetStats   = MakeButton("", (lw - 4) / 2, btnBrowseTags.Right + 4, 0);
            btnBrowseTags.ButtonStyle = FlatButtonStyle.Secondary;
            btnGetStats.ButtonStyle   = FlatButtonStyle.Secondary;
            btnBrowseTags.Click += btnBrowseTags_Click;
            btnGetStats.Click   += btnGetStats_Click;
            pnlTagButtons.Controls.Add(btnBrowseTags);
            pnlTagButtons.Controls.Add(btnGetStats);

            lblPrimaryTag   = MakeLabel("",   lw, pad, pnlTagButtons.Bottom + 6);
            cboPrimary      = MakeCombo(lw, pad, lblPrimaryTag.Bottom + 2);
            cboPrimary.SelectedIndexChanged += cboPrimary_SelectedIndexChanged;

            // Link toggle: picking a tag on one side auto-selects the same tag on the
            // other side (when it exists there). Click to unlink for independent tags.
            btnTagLink = new Button
            {
                Left = pad, Top = cboPrimary.Bottom + 5, Width = lw, Height = 22,
                FlatStyle = FlatStyle.Flat, Font = AppTheme.Small,
                BackColor = AppTheme.NavyLight, ForeColor = AppTheme.Navy,
                Cursor = Cursors.Hand, TextAlign = ContentAlignment.MiddleCenter,
                Text = ""
            };
            btnTagLink.FlatAppearance.BorderSize = 0;
            btnTagLink.Click += btnTagLink_Click;

            lblSecondaryTag = MakeLabel("", lw, pad, btnTagLink.Bottom + 5);
            cboSecondary    = MakeCombo(lw, pad, lblSecondaryTag.Bottom + 2);
            cboSecondary.SelectedIndexChanged += cboSecondary_SelectedIndexChanged;

            pnlTagsContent.Controls.AddRange(new Control[]
            {
                lblTagnameFilter, txtTagnameFilter, pnlTagButtons,
                lblPrimaryTag, cboPrimary, btnTagLink, lblSecondaryTag, cboSecondary
            });
            pnlTagsContent.Height = cboSecondary.Bottom + 10;

            pnlLeftContent.Controls.AddRange(new Control[]
            {
                hdrConnection,  pnlConnContent,
                hdrPeriod,      pnlPeriodContent,
                hdrTags,        pnlTagsContent
            });
            pnlLeft.Controls.Add(pnlLeftContent);

            // ══════════════════════════════════════════════════════════════════════
            // RIGHT PANEL  (Dock=Right)
            // ══════════════════════════════════════════════════════════════════════
            pnlRight = new Panel
            {
                Dock      = DockStyle.Right,
                Width     = AppTheme.RightPanelWidth,
                BackColor = AppTheme.Surface
            };
            pnlRight.Paint += (s, e) =>
                e.Graphics.DrawLine(new Pen(AppTheme.Border), 0, 0, 0, pnlRight.Height);

            pnlRightContent = new Panel { Dock = DockStyle.Fill, Padding = new Padding(1, 0, 0, 0) };
            const int rpad = 8;

            // The coverage bars moved to the full-width SYNC TIMELINE in the center —
            // this panel now holds only the per-direction summary + the missing-data table.
            hdrGapAnalysis = new SectionHeader { Text = "", Dock = DockStyle.Top };

            var pnlSummary = new Panel
            {
                Dock = DockStyle.Top, Height = 66,
                Padding = new Padding(rpad, 8, rpad, 0), BackColor = AppTheme.Surface
            };
            lblGapSummary = new Label
            {
                Text = "",
                Dock = DockStyle.Top, Height = 40,
                Font = AppTheme.Default, ForeColor = AppTheme.TextSecondary
            };
            lblDiffHint = new Label
            {
                Text = "",
                Dock = DockStyle.Top, Height = 16,
                Font = AppTheme.Small, ForeColor = AppTheme.TextSecondary,
                Visible = false
            };
            pnlSummary.Controls.Add(lblDiffHint);      // below summary (added first)
            pnlSummary.Controls.Add(lblGapSummary);    // top

            // Grid fills remaining space — wrapped in panel for padding
            var pnlGridWrap = new Panel
            {
                Dock    = DockStyle.Fill,
                Padding = new Padding(rpad, 4, rpad, rpad)
            };
            gridGaps = new System.Windows.Forms.DataGridView { Dock = DockStyle.Fill };
            SetupGapGrid();
            pnlGridWrap.Controls.Add(gridGaps);

            // The one write action lives HERE, next to the numbers that justify it, because
            // this panel is visible on BOTH centre cards. It used to sit in the action column
            // between the two data tables — which only exists in the single-point view, so the
            // all-points screen could report thousands of missing readings with no way to act.
            btnRestore = new FlatButton
            {
                Text = "", ButtonStyle = FlatButtonStyle.Info,
                Dock = DockStyle.Top, Height = 44, Font = AppTheme.Bold
            };
            btnRestore.Click += btnBackfillPreview_Click;   // same preview-then-confirm flow

            btnHistory = new FlatButton
            {
                Text = "", ButtonStyle = FlatButtonStyle.Secondary,
                Dock = DockStyle.Top, Height = 30, Font = AppTheme.SectionLabel
            };
            btnHistory.Click += btnHistory_Click;

            var pnlRightActions = new Panel
            {
                Dock = DockStyle.Bottom, Height = 44 + 6 + 30 + 8,
                BackColor = AppTheme.Surface, Padding = new Padding(rpad, 0, rpad, 8)
            };
            pnlRightActions.Controls.Add(btnHistory);
            pnlRightActions.Controls.Add(MakeSpacer(6));
            pnlRightActions.Controls.Add(btnRestore);

            // Assemble right panel (add Fill first, then edge-docked items)
            pnlRightContent.Controls.Add(pnlGridWrap);     // Fill — added first
            pnlRightContent.Controls.Add(pnlRightActions); // Bottom
            pnlRightContent.Controls.Add(pnlSummary);      // Top — after header
            pnlRightContent.Controls.Add(hdrGapAnalysis);  // Top — first
            pnlRight.Controls.Add(pnlRightContent);        // Fill

            // ══════════════════════════════════════════════════════════════════════
            // CENTER PANEL  (Dock=Fill)
            // ══════════════════════════════════════════════════════════════════════
            pnlCenter = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.Background };

            // ── Activity Log  (Dock=Bottom, collapsed by default) ─────────────────
            pnlLog = new CollapsiblePanel
            {
                Dock           = DockStyle.Bottom,
                Title          = "",
                ExpandedHeight = 170
            };
            pnlLog.IsExpanded = false;

            txtLog = new RichTextBox
            {
                Dock        = DockStyle.Fill,
                ReadOnly    = true,
                BackColor   = Color.FromArgb(18, 18, 24),
                ForeColor   = Color.FromArgb(180, 220, 180),
                Font        = AppTheme.Mono,
                BorderStyle = BorderStyle.None,
                ScrollBars  = RichTextBoxScrollBars.Vertical
            };

            pnlLogButtons = new Panel
            {
                Dock      = DockStyle.Bottom,
                Height    = 28,
                BackColor = AppTheme.Surface,
                Padding   = new Padding(4, 2, 4, 2)
            };
            btnClearLog = MakeSmallButton("", DockStyle.Right);
            btnCopyLog  = MakeSmallButton("", DockStyle.Right);
            btnClearLog.Click += btnClearLog_Click;
            btnCopyLog.Click  += btnCopyLog_Click;
            pnlLogButtons.Controls.Add(btnClearLog);
            pnlLogButtons.Controls.Add(btnCopyLog);
            pnlLog.Content.Controls.Add(txtLog);
            pnlLog.Content.Controls.Add(pnlLogButtons);

            // ── Grid area  (Dock=Fill) — 2-row × 3-col TableLayoutPanel ──────────
            //   Row 0: [Read Primary btn | spacer | Read Secondary btn]   38px
            //   Row 1: [gridPrimary | action buttons | gridSecondary]     fill
            pnlGrids = new TableLayoutPanel
            {
                Dock        = DockStyle.Fill,
                ColumnCount = 3,
                RowCount    = 2,
                BackColor   = AppTheme.Background,
                Padding     = new Padding(6)
            };
            pnlGrids.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,  50f));
            pnlGrids.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 156)); // action column
            pnlGrids.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,  50f));
            pnlGrids.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f));       // point-name caption row
            pnlGrids.RowStyles.Add(new RowStyle(SizeType.Percent,  100f));      // grid row

            // Row 0 — Read buttons + tag labels, each directly above its grid
            // No "Load data" buttons: opening a point loads both servers automatically, so a
            // manual load button could only ever repeat what already happened.
            var pnlReadLeft = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.Background, Padding = new Padding(0, 2, 4, 0) };
            lblGridPrimaryTag = new Label
            {
                Text = "", Dock = DockStyle.Fill, Height = 22,
                Font = AppTheme.Default, ForeColor = AppTheme.Navy,
                TextAlign = ContentAlignment.MiddleCenter,
                AutoEllipsis = true   // long point names must degrade visibly, not vanish
            };
            pnlReadLeft.Controls.Add(lblGridPrimaryTag);

            var pnlReadRight = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.Background, Padding = new Padding(4, 2, 0, 0) };
            lblGridSecondaryTag = new Label
            {
                Text = "", Dock = DockStyle.Fill, Height = 22,
                Font = AppTheme.Default, ForeColor = AppTheme.Navy,
                TextAlign = ContentAlignment.MiddleCenter,
                AutoEllipsis = true
            };
            pnlReadRight.Controls.Add(lblGridSecondaryTag);

            // Row 1 — Data grids with empty-state placeholder text
            gridPrimary   = new System.Windows.Forms.DataGridView { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 3, 0) };
            gridSecondary = new System.Windows.Forms.DataGridView { Dock = DockStyle.Fill, Margin = new Padding(3, 0, 0, 0) };

            gridPrimary.Paint += (s, e) =>
            {
                var dg = (System.Windows.Forms.DataGridView)s;
                if (dg.Rows.Count > 0) return;
                System.Windows.Forms.TextRenderer.DrawText(e.Graphics,
                    _emptyMsgPrimary,
                    AppTheme.Default,
                    new Rectangle(0, dg.ColumnHeadersHeight, dg.Width, dg.Height - dg.ColumnHeadersHeight),
                    AppTheme.TextSecondary,
                    System.Windows.Forms.TextFormatFlags.HorizontalCenter |
                    System.Windows.Forms.TextFormatFlags.VerticalCenter   |
                    System.Windows.Forms.TextFormatFlags.WordBreak);
            };

            gridSecondary.Paint += (s, e) =>
            {
                var dg = (System.Windows.Forms.DataGridView)s;
                if (dg.Rows.Count > 0) return;
                System.Windows.Forms.TextRenderer.DrawText(e.Graphics,
                    _emptyMsgSecondary,
                    AppTheme.Default,
                    new Rectangle(0, dg.ColumnHeadersHeight, dg.Width, dg.Height - dg.ColumnHeadersHeight),
                    AppTheme.TextSecondary,
                    System.Windows.Forms.TextFormatFlags.HorizontalCenter |
                    System.Windows.Forms.TextFormatFlags.VerticalCenter   |
                    System.Windows.Forms.TextFormatFlags.WordBreak);
            };

            // Row 1 — Action column: VIEW group (top) + BACKFILL group (bottom)
            pnlGridActions = new Panel
            {
                Dock      = DockStyle.Fill,
                BackColor = AppTheme.Background,
                Padding   = new Padding(4, 4, 4, 4)
            };

            // View group (top) — read-only actions
            pnlViewGroup = new Panel { Dock = DockStyle.Top, BackColor = AppTheme.Background };
            hdrView      = new SectionHeader { Text = "", Dock = DockStyle.Top, Height = 20 };

            btnExport     = MakeStackedButton("", FlatButtonStyle.Secondary);
            btnSyncScroll = MakeStackedButton("", FlatButtonStyle.Secondary);
            btnCompare    = MakeStackedButton("", FlatButtonStyle.Secondary);

            btnSyncScroll.Click += btnSyncScroll_Click;
            btnExport.Click     += btnExport_Click;
            btnCompare.Click    += btnCompare_Click;

            // Dock=Top stacks in reverse-add order. Each spacer is tagged with the button it
            // belongs to, so hiding a button in the simple view hides its gap too and the
            // group keeps its exact height (see SizeActionGroups).
            pnlViewGroup.Controls.Add(btnExport);
            pnlViewGroup.Controls.Add(MakeSpacer(4, btnSyncScroll));
            pnlViewGroup.Controls.Add(btnSyncScroll);
            pnlViewGroup.Controls.Add(MakeSpacer(4, btnCompare));
            pnlViewGroup.Controls.Add(btnCompare);
            pnlViewGroup.Controls.Add(MakeSpacer(6));
            pnlViewGroup.Controls.Add(hdrView);

            // Repair group (bottom) — everything that writes.
            //   Simple view : one guarded action (btnRestore) + the undo history.
            //   Advanced    : the per-direction copies and the two-direction preview as well.
            // All of them end in the same preview-then-confirm flow; none writes directly.
            pnlBackfillGroup = new Panel { Dock = DockStyle.Bottom, BackColor = AppTheme.Background };
            hdrBackfill      = new SectionHeader { Text = "", Dock = DockStyle.Top, Height = 20 };

            // Advanced extras only — the primary restore action and the undo history live in
            // the right panel so they are reachable from both centre cards.
            btnBackfillPreview = MakeStackedButton("", FlatButtonStyle.Info);
            btnCopyToSecondary = MakeStackedButton("", FlatButtonStyle.Warning);
            btnCopyToPrimary   = MakeStackedButton("", FlatButtonStyle.Warning);

            btnBackfillPreview.Click += btnBackfillPreview_Click;
            btnCopyToSecondary.Click += btnCopyToSecondary_Click;
            btnCopyToPrimary.Click   += btnCopyToPrimary_Click;

            pnlBackfillGroup.Controls.Add(btnBackfillPreview);
            pnlBackfillGroup.Controls.Add(MakeSpacer(4, btnCopyToSecondary));
            pnlBackfillGroup.Controls.Add(btnCopyToSecondary);
            pnlBackfillGroup.Controls.Add(MakeSpacer(4, btnCopyToPrimary));
            pnlBackfillGroup.Controls.Add(btnCopyToPrimary);
            pnlBackfillGroup.Controls.Add(MakeSpacer(6));
            pnlBackfillGroup.Controls.Add(hdrBackfill);

            pnlGridActions.Controls.Add(pnlViewGroup);     // Top group
            pnlGridActions.Controls.Add(pnlBackfillGroup); // Bottom group

            // Spacer for middle cell in button row
            var spacer = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.Background };

            pnlGrids.Controls.Add(pnlReadLeft,    0, 0);
            pnlGrids.Controls.Add(spacer,         1, 0);
            pnlGrids.Controls.Add(pnlReadRight,   2, 0);
            pnlGrids.Controls.Add(gridPrimary,    0, 1);
            pnlGrids.Controls.Add(pnlGridActions, 1, 1);
            pnlGrids.Controls.Add(gridSecondary,  2, 1);

            // ── Sync timeline (Dock=Top): both servers on ONE shared time axis ────
            pnlTimeline = new Panel
            {
                Dock      = DockStyle.Top,
                Height    = 200,
                BackColor = AppTheme.Surface,
                Padding   = new Padding(0, 0, 0, 4)
            };
            pnlTimeline.Paint += (s, e) =>
                e.Graphics.DrawLine(new Pen(AppTheme.Border),
                    0, pnlTimeline.Height - 1, pnlTimeline.Width, pnlTimeline.Height - 1);

            hdrTimeline = new SectionHeader { Text = "", Dock = DockStyle.Top };

            lnkZoomOut = new LinkLabel
            {
                Text            = "",
                Dock            = DockStyle.Right,
                Width           = 110,
                LinkColor       = Color.White,
                ActiveLinkColor = AppTheme.Teal,
                LinkBehavior    = LinkBehavior.HoverUnderline,
                TextAlign       = ContentAlignment.MiddleRight,
                Padding         = new Padding(0, 0, 12, 0),
                Font            = AppTheme.SectionLabel,
                BackColor       = AppTheme.Navy,
                Visible         = false
            };
            lnkZoomOut.Click += lnkZoomOut_Click;
            hdrTimeline.Controls.Add(lnkZoomOut);

            timeline = new GapTimeline { Dock = DockStyle.Fill };

            var pnlTimelineWrap = new Panel
            {
                Dock = DockStyle.Fill, BackColor = AppTheme.Surface,
                Padding = new Padding(10, 8, 10, 0)
            };
            pnlTimelineWrap.Controls.Add(timeline);

            pnlTimeline.Controls.Add(pnlTimelineWrap);  // Fill
            pnlTimeline.Controls.Add(hdrTimeline);      // Top

            // ══════════════════════════════════════════════════════════════════════
            // CENTRE CARD 2 — one point in detail (timeline + the two data tables).
            // Exactly what the centre used to be, now behind a "‹ All measurement
            // points" header so it can be swapped with the overview.
            // ══════════════════════════════════════════════════════════════════════
            pnlDetailHeader = new Panel
            {
                Dock = DockStyle.Top, Height = 34, BackColor = AppTheme.Background,
                Padding = new Padding(4, 3, 4, 3)
            };
            // A faint link was easy to miss; this is the way back out of the point view.
            lnkBackToOverview = new FlatButton
            {
                Dock        = DockStyle.Left,
                Width       = 230,
                ButtonStyle = FlatButtonStyle.Secondary,
                Font        = AppTheme.Bold,
                Cursor      = Cursors.Hand
            };
            lnkBackToOverview.Click += lnkBackToOverview_Click;
            lblDetailPoint = new Label
            {
                Dock = DockStyle.Fill, Font = AppTheme.Bold, ForeColor = AppTheme.TextPrimary,
                TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true
            };
            pnlDetailHeader.Controls.Add(lblDetailPoint);
            pnlDetailHeader.Controls.Add(lnkBackToOverview);

            // Measured values of this point from both servers, under the completeness bars
            // and above the tables — same time axis, same samples as the tables.
            pnlChart = new Panel
            {
                // Two stacked plots need more height than one; the timeline gives some back.
                Dock = DockStyle.Top, Height = 214, BackColor = AppTheme.Surface,
                Padding = new Padding(10, 4, 10, 6)
            };
            hdrChart = new SectionHeader { Text = "", Dock = DockStyle.Top };
            lnkEnlarge = new LinkLabel
            {
                Text            = "",
                Dock            = DockStyle.Right,
                Width           = 150,
                LinkColor       = Color.White,
                ActiveLinkColor = AppTheme.Teal,
                LinkBehavior    = LinkBehavior.HoverUnderline,
                TextAlign       = ContentAlignment.MiddleRight,
                Padding         = new Padding(0, 0, 12, 0),
                Font            = AppTheme.SectionLabel,
                BackColor       = AppTheme.Navy,
                Cursor          = Cursors.Hand
            };
            lnkEnlarge.Click += lnkEnlarge_Click;
            hdrChart.Controls.Add(lnkEnlarge);
            chart = new ValueChart { Dock = DockStyle.Fill };
            var pnlChartWrap = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.Surface };
            pnlChartWrap.Controls.Add(chart);
            pnlChart.Controls.Add(pnlChartWrap);
            pnlChart.Controls.Add(hdrChart);

            pnlDetail = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.Background };
            pnlDetail.Controls.Add(pnlGrids);        // Fill
            pnlDetail.Controls.Add(pnlChart);        // Top
            pnlDetail.Controls.Add(pnlTimeline);     // Top
            pnlDetail.Controls.Add(pnlDetailHeader); // Top (outermost = above everything)

            // ══════════════════════════════════════════════════════════════════════
            // CENTRE CARD 1 — all measurement points at a glance (landing screen)
            // ══════════════════════════════════════════════════════════════════════
            pnlOverview = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.Surface };
            hdrOverview = new SectionHeader { Text = "", Dock = DockStyle.Top };

            var pnlOverviewTop = new Panel
            {
                Dock = DockStyle.Top, Height = 78, BackColor = AppTheme.Surface,
                Padding = new Padding(10, 8, 10, 4)
            };
            txtOverviewSearch = new TextBox
            {
                Dock = DockStyle.Top, Font = AppTheme.Default, BorderStyle = BorderStyle.FixedSingle
            };
            txtOverviewSearch.TextChanged += txtOverviewSearch_TextChanged;

            lblOverviewSummary = new Label
            {
                // Two lines: verdict + resolution + the estimate caveat do not fit on one,
                // and the caveat is the part that must not be cut off.
                Dock = DockStyle.Bottom, Height = 36, Font = AppTheme.Small,
                ForeColor = AppTheme.TextSecondary, TextAlign = ContentAlignment.TopLeft
            };
            // No "Check the rest" button: a scan covers every point (see MainForm.ScanBudget).
            var pnlSummaryRow = new Panel { Dock = DockStyle.Bottom, Height = 40, BackColor = AppTheme.Surface };
            pnlSummaryRow.Controls.Add(lblOverviewSummary);

            pnlOverviewTop.Controls.Add(pnlSummaryRow);
            pnlOverviewTop.Controls.Add(txtOverviewSearch);

            lstOverview = new TagOverviewList { Dock = DockStyle.Fill };
            lstOverview.PointActivated += lstOverview_PointActivated;

            var pnlOverviewWrap = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6, 0, 6, 6), BackColor = AppTheme.Surface };
            pnlOverviewWrap.Controls.Add(lstOverview);

            pnlOverview.Controls.Add(pnlOverviewWrap);  // Fill
            pnlOverview.Controls.Add(pnlOverviewTop);   // Top
            pnlOverview.Controls.Add(hdrOverview);      // Top (outermost)

            // Assemble center (Fill first, then edge-docked panels). Both cards are Fill;
            // exactly one is visible at a time — see MainForm.ShowOverview/ShowDetail.
            pnlCenter.Controls.Add(pnlDetail);   // Fill
            pnlCenter.Controls.Add(pnlOverview); // Fill
            pnlCenter.Controls.Add(pnlLog);      // Bottom (outermost)

            // ══════════════════════════════════════════════════════════════════════
            // Add to form. Later-added docks OUTERMOST, so the header strip must come
            // last to sit at the very top, with the demo banner directly beneath it.
            // ══════════════════════════════════════════════════════════════════════
            this.Controls.Add(pnlCenter);
            this.Controls.Add(pnlRight);
            this.Controls.Add(pnlLeft);
            this.Controls.Add(pnlStatusBar);
            this.Controls.Add(pnlDemoBanner);
            this.Controls.Add(pnlHeader);

            SizeActionGroups();
            ApplyTexts();

            this.ResumeLayout(false);
            this.PerformLayout();
        }

        // ══════════════════════════════════════════════════════════════════════════
        // Text assignment — ONE place, so switching language cannot leave a control
        // behind in the old language. Called from InitializeComponent and whenever
        // Loc.Language changes. Values that depend on live state (connection status,
        // schedule label, tag-link toggle) are refreshed by their own updaters right
        // after this runs — see MainForm.ApplyLanguage().
        // ══════════════════════════════════════════════════════════════════════════
        private void ApplyTexts()
        {
            this.Text            = Loc.T("app.title");
            lblAppTitle.Text     = Loc.T("app.title");
            chkAdvanced.Text     = Loc.T("app.advanced");
            lblDemoBanner.Text   = Loc.T("app.demoBanner");

            // Sidebar — servers
            hdrConnection.Text   = Loc.T("hdr.connection");
            lblPrimary.Text      = Loc.T("lbl.primaryServer");
            lblSecondary.Text    = Loc.T("lbl.secondaryServer");
            btnConnect.Text      = Loc.T("btn.connect");
            btnCredentials.Text  = Loc.T("cred.button");

            // Sidebar — time range
            hdrPeriod.Text       = Loc.T("hdr.period");
            lblStartDate.Text    = Loc.T("lbl.startDate");
            lblEndDate.Text      = Loc.T("lbl.endDate");
            btnAnalyzeGaps.Text  = Loc.T("btn.analyze");

            // Sidebar — measurement points
            hdrTags.Text         = Loc.T("hdr.tags");
            lblTagnameFilter.Text= Loc.T("lbl.tagFilter");
            btnBrowseTags.Text   = Loc.T("btn.browseTags");
            btnGetStats.Text     = Loc.T("btn.serverStats");
            lblPrimaryTag.Text   = Loc.T("lbl.primaryTag");
            lblSecondaryTag.Text = Loc.T("lbl.secondaryTag");

            // Centre
            hdrTimeline.Text     = Loc.T("hdr.timeline");
            lnkZoomOut.Text      = Loc.T("link.zoomBack");
            pnlLog.Title         = Loc.T("hdr.log");
            btnClearLog.Text     = Loc.T("btn.clearLog");
            btnCopyLog.Text      = Loc.T("btn.copyLog");

            hdrChart.Text             = Loc.T("hdr.values");
            lnkEnlarge.Text           = Loc.T("chart.enlarge");
            chart.SetEmptyMessage(Loc.T("chart.empty"));

            hdrOverview.Text          = Loc.T("hdr.overview");
            lnkBackToOverview.Text    = Loc.T("ov.back");
            lstOverview.EmptyMessage  = Loc.T("ov.empty");
            SetCueBanner(txtOverviewSearch, Loc.T("ov.search"));
            lstOverview.Invalidate();

            hdrView.Text         = Loc.T("hdr.view");
            hdrBackfill.Text     = Loc.T("hdr.repair");
            btnExport.Text       = Loc.T("btn.export");
            btnSyncScroll.Text   = Loc.T(_scrollSyncEnabled ? "btn.unsyncScroll" : "btn.syncScroll");
            btnCompare.Text      = Loc.T(_isCompareMode ? "btn.rawView" : "btn.compare");
            btnRestore.Text      = Loc.T("btn.restore");
            btnCopyToPrimary.Text   = Loc.T("btn.copyToPrimary");
            btnCopyToSecondary.Text = Loc.T("btn.copyToSecondary");
            btnBackfillPreview.Text = Loc.T("btn.previewBackfill");
            btnHistory.Text      = Loc.T("btn.history");

            // Right panel
            hdrGapAnalysis.Text  = Loc.T("hdr.missing");
            lblDiffHint.Text     = Loc.T("missing.clickHint");
            if (!_hasAnalysis) lblGapSummary.Text = Loc.T("missing.prompt");

            // Grid headers
            gridGaps.Columns["Tag"].HeaderText       = Loc.T("grid.point");
            gridGaps.Columns["MissingOn"].HeaderText = Loc.T("grid.missingOn");
            gridGaps.Columns["Samples"].HeaderText   = Loc.T("grid.readings");
            gridGaps.Columns["Range"].HeaderText     = Loc.T("grid.period");
            foreach (var g in new[] { gridPrimary, gridSecondary })
            {
                if (g.Columns.Count < 3) continue;   // SetupVirtualMode not run yet
                g.Columns["Timestamp"].HeaderText = Loc.T("grid.time");
                g.Columns["Value"].HeaderText     = Loc.T("grid.value");
                g.Columns["Quality"].HeaderText   = Loc.T("grid.quality");
            }

            // Tooltips
            _tips.SetToolTip(txtPrimary,     Loc.T("conn.hostTip"));
            _tips.SetToolTip(txtSecondary,   Loc.T("conn.hostTip"));
            _tips.SetToolTip(btnAnalyzeGaps, Loc.T("btn.analyze.tip"));
            _tips.SetToolTip(btnRestore,     Loc.T("btn.restore.tip"));
            _tips.SetToolTip(chkAdvanced,    Loc.T("app.advanced.tip"));
            _tips.SetToolTip(lblDemoBanner,  Loc.T("app.demoBanner.tip"));

            timeline.SetEmptyMessage(Loc.T("timeline.empty"));
            gridPrimary.Invalidate();
            gridSecondary.Invalidate();
        }

        /// <summary>
        /// Heights of the two docked action groups, from their VISIBLE children only —
        /// the simple view hides several buttons, and a fixed height would leave a hole.
        /// </summary>
        private void SizeActionGroups()
        {
            foreach (var grp in new[] { pnlViewGroup, pnlBackfillGroup })
            {
                int h = 0;
                foreach (Control c in grp.Controls)
                {
                    // A spacer tagged with a control follows that control's visibility.
                    var owner = c.Tag as Control;
                    if (owner != null) SetShown(c, IsShown(owner));
                    if (IsShown(c)) h += c.Height;
                }
                grp.Height = h;
            }
        }

        /// <summary>
        /// Re-stacks the MEASUREMENT POINTS section top-down over its visible controls.
        /// The sidebar is absolutely positioned, so hiding a control in the simple view
        /// would otherwise leave a gap. This section is the last one in the sidebar, so
        /// its height can change freely.
        /// </summary>
        private void RelayoutTagsSection()
        {
            const int pad = 8;
            int y = 8;
            foreach (Control c in new Control[]
                { lblTagnameFilter, txtTagnameFilter, pnlTagButtons,
                  lblPrimaryTag, cboPrimary, btnTagLink, lblSecondaryTag, cboSecondary })
            {
                if (!IsShown(c)) continue;
                c.Left = pad;
                c.Top  = y;
                y = c.Bottom + (c is Label ? 2 : 6);
            }
            if (cboSecondary.Parent != null)
                cboSecondary.Parent.Height = y + 4;
        }

        // ══════════════════════════════════════════════════════════════════════════
        // Gap grid column setup
        // ══════════════════════════════════════════════════════════════════════════
        private void SetupGapGrid()
        {
            AppTheme.StyleGrid(gridGaps);
            gridGaps.ReadOnly = true;
            gridGaps.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            gridGaps.ScrollBars = ScrollBars.Vertical;   // vertical-only: the right panel must never need horizontal space
            // Headers wrap (taller header row) instead of truncating in the narrow right panel.
            gridGaps.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            gridGaps.ColumnHeadersHeight = 46;
            gridGaps.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.True;
            // Cross-server diff for the selected tag(s): each row says one server lacks
            // samples the other has. Plain-language columns; a full sentence on hover;
            // clicking a row zooms the timeline to that span. (All-tags view lives in
            // the Preview & Backfill dialog.)
            // Header texts come from ApplyTexts. Weights leave room for the longest header
            // in both languages ("Readings" / "Messwerte") without clipping.
            gridGaps.Columns.Add(new DataGridViewTextBoxColumn { Name = "Tag",       FillWeight = 26 });
            gridGaps.Columns.Add(new DataGridViewTextBoxColumn { Name = "MissingOn", FillWeight = 21 });
            gridGaps.Columns.Add(new DataGridViewTextBoxColumn { Name = "Samples",   FillWeight = 20, DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight } });
            gridGaps.Columns.Add(new DataGridViewTextBoxColumn { Name = "Range",     FillWeight = 33 });
        }

        // ══════════════════════════════════════════════════════════════════════════
        // Layout helper factories
        // ══════════════════════════════════════════════════════════════════════════
        private static Label MakeLabel(string text, int width, int left, int top) => new Label
        {
            Text = text, Left = left, Top = top, Width = width, Height = 18,
            Font = AppTheme.Default, ForeColor = AppTheme.TextSecondary, AutoSize = false
        };

        private static TextBox MakeTextBox(int width, int left, int top) => new TextBox
        {
            Left = left, Top = top, Width = width, Height = AppTheme.ControlHeight,
            Font = AppTheme.Default, BorderStyle = BorderStyle.FixedSingle
        };

        /// <summary>
        /// Server address field: free text, but remembering what has been typed before so a
        /// site with a handful of Historians can pick instead of retype. The list is persisted
        /// (Settings.ServerHistory) and grows on every successful connect.
        /// </summary>
        private static ComboBox MakeHostCombo(int width, int left, int top) => new ComboBox
        {
            Left = left, Top = top, Width = width, Height = AppTheme.ControlHeight,
            Font = AppTheme.Default,
            DropDownStyle      = ComboBoxStyle.DropDown,
            AutoCompleteMode   = AutoCompleteMode.SuggestAppend,
            AutoCompleteSource = AutoCompleteSource.ListItems,
            FlatStyle          = FlatStyle.Flat
        };

        private static DateTimePicker MakeDtp(int width, int left, int top) => new DateTimePicker
        {
            Left = left, Top = top, Width = width, Height = AppTheme.ControlHeight,
            Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd HH:mm:ss", ShowUpDown = false
        };

        private static FlatButton MakeButton(string text, int width, int left, int top) =>
            new FlatButton { Text = text, Left = left, Top = top, Width = width };

        private static ComboBox MakeCombo(int width, int left, int top) => new ComboBox
        {
            Left = left, Top = top, Width = width, Height = AppTheme.ControlHeight,
            Font = AppTheme.Default,
            // Editable + type-ahead so hundreds of tags are searchable: start typing
            // and the list filters to matching tag names. The AutoComplete custom
            // source is (re)built in btnBrowseTags_Click after the combos are populated.
            DropDownStyle      = ComboBoxStyle.DropDown,
            AutoCompleteMode   = AutoCompleteMode.SuggestAppend,
            AutoCompleteSource = AutoCompleteSource.CustomSource
        };

        private static Button MakeSmallButton(string text, DockStyle dock)
        {
            var btn = new Button
            {
                Text = text, Dock = dock, Width = 80,
                FlatStyle = FlatStyle.Flat, Font = AppTheme.Small,
                BackColor = AppTheme.NavyLight, ForeColor = AppTheme.Navy, Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderSize = 0;
            return btn;
        }

        private static Button MakeQuickBtn(string text, int left)
        {
            var btn = new Button
            {
                Text = text, Left = left, Top = 0, Height = 22,
                FlatStyle = FlatStyle.Flat, Font = AppTheme.Small,
                BackColor = AppTheme.NavyLight, ForeColor = AppTheme.Navy,
                Cursor = Cursors.Hand, TextAlign = ContentAlignment.MiddleCenter
            };
            btn.FlatAppearance.BorderSize = 0;
            return btn;
        }

        private static FlatButton MakeCenterButton(string text, int top) => new FlatButton
        {
            Text = text, Left = 4, Top = top, Width = 148,
            Font = AppTheme.SectionLabel
        };

        /// <summary>Factory for buttons in the docked VIEW/BACKFILL groups: Dock=Top, fixed 30px height.</summary>
        private static FlatButton MakeStackedButton(string text, FlatButtonStyle style) => new FlatButton
        {
            Text = text,
            ButtonStyle = style,
            Dock = DockStyle.Top,
            Height = 30,
            Font = AppTheme.SectionLabel
        };

        /// <summary>
        /// Invisible vertical spacer used between docked buttons. When <paramref name="owner"/>
        /// is given, the spacer follows that control's visibility (see SizeActionGroups), so
        /// hiding a button in the simple view does not leave its gap behind.
        /// </summary>
        private static Panel MakeSpacer(int height, Control owner = null) => new Panel
        {
            Dock = DockStyle.Top,
            Height = height,
            BackColor = AppTheme.Background,
            Tag = owner
        };

        /// <summary>
        /// Grey in-box hint ("Search…") that disappears as soon as the user types.
        /// .NET Framework 4.8's TextBox has no PlaceholderText, so this uses the Win32
        /// cue-banner message; it needs a created handle, hence the deferred hook-up.
        /// </summary>
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        private static void SetCueBanner(TextBox box, string hint)
        {
            const int EM_SETCUEBANNER = 0x1501;
            if (box == null) return;
            if (box.IsHandleCreated) { SendMessage(box.Handle, EM_SETCUEBANNER, (IntPtr)1, hint ?? ""); return; }
            EventHandler once = null;
            once = (s, e) => { box.HandleCreated -= once; SendMessage(box.Handle, EM_SETCUEBANNER, (IntPtr)1, hint ?? ""); };
            box.HandleCreated += once;
        }

        /// <summary>EN / DE switch in the header strip.</summary>
        private static LinkLabel MakeLangLink(string text) => new LinkLabel
        {
            Text            = text,
            Dock            = DockStyle.Right,
            Width           = 34,
            Font            = AppTheme.SectionLabel,
            LinkColor       = Color.FromArgb(205, 220, 238),
            ActiveLinkColor = AppTheme.Teal,
            LinkBehavior    = LinkBehavior.HoverUnderline,
            TextAlign       = ContentAlignment.MiddleCenter,
            Cursor          = Cursors.Hand
        };
    }
}
