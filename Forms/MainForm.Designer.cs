using HistorianSyncTool.UI;
using HistorianSyncTool.UI.Controls;
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
        private TextBox        txtPrimary;
        private Label          lblPrimaryStatus;
        private Label          lblSecondary;
        private TextBox        txtSecondary;
        private Label          lblSecondaryStatus;
        private FlatButton     btnConnect;

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
        private FlatButton     btnReadPrimary;
        private FlatButton     btnReadSecondary;
        private Label          lblGridPrimaryTag;
        private Label          lblGridSecondaryTag;
        private FlatButton     btnCompare;
        private FlatButton     btnCopyToPrimary;
        private FlatButton     btnCopyToSecondary;
        private FlatButton     btnSyncScroll;
        private FlatButton     btnExport;
        private FlatButton     btnHistory;

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
            this.Text          = "Historian Sync Tool";
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Padding       = new Padding(8, 4, 8, 0);  // symmetric breathing room around all panels

            // Fit to screen: use 90% of working area or 1380x820, whichever is smaller
            var screen = System.Windows.Forms.Screen.PrimaryScreen.WorkingArea;
            int formW  = System.Math.Min(1380, (int)(screen.Width  * 0.92));
            int formH  = System.Math.Min(820,  (int)(screen.Height * 0.92));
            this.Size  = new Size(formW, formH);

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
                Text      = "Ready",
                Left      = 34, Top = 0,
                Width     = 600, Height = AppTheme.StatusBarHeight,
                Font      = AppTheme.Default,
                ForeColor = AppTheme.TextPrimary,
                TextAlign = ContentAlignment.MiddleLeft
            };

            lblSchedule = new Label
            {
                Text       = "Schedule: off",
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
            hdrConnection = new SectionHeader { Text = "CONNECTION", Width = fw, Left = 0, Top = 0 };

            var pnlConnContent = new Panel { Left = 0, Top = hdrConnection.Bottom, Width = fw };

            lblPrimary = MakeLabel("Primary server", lw, pad, 8);
            txtPrimary = MakeTextBox(lw, pad, lblPrimary.Bottom + 2);
            lblPrimaryStatus = new Label
            {
                Text = "Not connected",
                Left = pad, Top = txtPrimary.Bottom + 2,
                Width = lw, Height = 16,
                Font = AppTheme.Small, ForeColor = AppTheme.TextSecondary, AutoSize = false
            };

            lblSecondary = MakeLabel("Secondary server", lw, pad, lblPrimaryStatus.Bottom + 6);
            txtSecondary = MakeTextBox(lw, pad, lblSecondary.Bottom + 2);
            lblSecondaryStatus = new Label
            {
                Text = "Not connected",
                Left = pad, Top = txtSecondary.Bottom + 2,
                Width = lw, Height = 16,
                Font = AppTheme.Small, ForeColor = AppTheme.TextSecondary, AutoSize = false
            };

            btnConnect = MakeButton("Connect", lw, pad, lblSecondaryStatus.Bottom + 8);
            btnConnect.Click += btnConnect_Click;

            // Server fields accept "host", "host:port", "ip" or "ip:port"
            var tipHosts = new ToolTip(components);
            tipHosts.SetToolTip(txtPrimary,
                "Hostname or IP address, optional port —\ne.g. GENTHIN, 192.168.50.186 or GENTHIN:14000");
            tipHosts.SetToolTip(txtSecondary,
                "Hostname or IP address, optional port —\ne.g. GENTHINPC2, 192.168.50.187 or GENTHINPC2:14000");

            pnlConnContent.Controls.AddRange(new Control[]
            {
                lblPrimary, txtPrimary, lblPrimaryStatus,
                lblSecondary, txtSecondary, lblSecondaryStatus,
                btnConnect
            });
            pnlConnContent.Height = btnConnect.Bottom + 10;

            // ── EVALUATION PERIOD (includes gap analysis mode radios) ──────────────
            hdrPeriod = new SectionHeader
            {
                Text = "EVALUATION PERIOD", Width = fw, Left = 0,
                Top  = hdrConnection.Bottom + pnlConnContent.Height
            };

            var pnlPeriodContent = new Panel { Left = 0, Top = hdrPeriod.Bottom, Width = fw };

            lblStartDate = MakeLabel("Start date", lw, pad, 8);
            dtpStart     = MakeDtp(lw, pad, lblStartDate.Bottom + 2);
            lblEndDate   = MakeLabel("End date", lw, pad, dtpStart.Bottom + 6);
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

            btnAnalyzeGaps = MakeButton("Analyze Gaps", lw, pad, pnlQuickDates.Bottom + 8);
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
                Text = "TAGS", Width = fw, Left = 0,
                Top  = hdrPeriod.Bottom + pnlPeriodContent.Height
            };

            var pnlTagsContent = new Panel { Left = 0, Top = hdrTags.Bottom, Width = fw };

            lblTagnameFilter = MakeLabel("Tagname filter", lw, pad, 8);
            txtTagnameFilter = MakeTextBox(lw, pad, lblTagnameFilter.Bottom + 2);

            pnlTagButtons = new Panel
            {
                Left = pad, Top = txtTagnameFilter.Bottom + 6, Width = lw, Height = AppTheme.ButtonHeight
            };
            btnBrowseTags = MakeButton("Browse Tags",  (lw - 4) / 2, 0, 0);
            btnGetStats   = MakeButton("Server Stats", (lw - 4) / 2, btnBrowseTags.Right + 4, 0);
            btnBrowseTags.ButtonStyle = FlatButtonStyle.Secondary;
            btnGetStats.ButtonStyle   = FlatButtonStyle.Secondary;
            btnBrowseTags.Click += btnBrowseTags_Click;
            btnGetStats.Click   += btnGetStats_Click;
            pnlTagButtons.Controls.Add(btnBrowseTags);
            pnlTagButtons.Controls.Add(btnGetStats);

            lblPrimaryTag   = MakeLabel("Primary tag",   lw, pad, pnlTagButtons.Bottom + 6);
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
                Text = "⇄  Linked — same tag on both servers"
            };
            btnTagLink.FlatAppearance.BorderSize = 0;
            btnTagLink.Click += btnTagLink_Click;

            lblSecondaryTag = MakeLabel("Secondary tag", lw, pad, btnTagLink.Bottom + 5);
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
            hdrGapAnalysis = new SectionHeader { Text = "MISSING DATA", Dock = DockStyle.Top };

            var pnlSummary = new Panel
            {
                Dock = DockStyle.Top, Height = 66,
                Padding = new Padding(rpad, 8, rpad, 0), BackColor = AppTheme.Surface
            };
            lblGapSummary = new Label
            {
                Text = "Connect to both servers, then click 'Analyze Gaps'",
                Dock = DockStyle.Top, Height = 40,
                Font = AppTheme.Default, ForeColor = AppTheme.TextSecondary
            };
            lblDiffHint = new Label
            {
                Text = "Click a row to zoom the timeline to it.",
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

            // Assemble right panel (add Fill first, then Top items bottom-to-top)
            pnlRightContent.Controls.Add(pnlGridWrap);     // Fill — last
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
                Title          = "ACTIVITY LOG",
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
            btnClearLog = MakeSmallButton("Clear",    DockStyle.Right);
            btnCopyLog  = MakeSmallButton("Copy Log", DockStyle.Right);
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
            pnlGrids.RowStyles.Add(new RowStyle(SizeType.Absolute, 56f));       // button + label row
            pnlGrids.RowStyles.Add(new RowStyle(SizeType.Percent,  100f));      // grid row

            // Row 0 — Read buttons + tag labels, each directly above its grid
            var pnlReadLeft = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.Background, Padding = new Padding(0, 2, 4, 0) };
            btnReadPrimary = new FlatButton { Text = "Read Primary", Dock = DockStyle.Top, Height = 30 };
            btnReadPrimary.Click += btnReadPrimary_Click;
            lblGridPrimaryTag = new Label
            {
                Text = "", Dock = DockStyle.Bottom, Height = 22,
                Font = AppTheme.Bold, ForeColor = AppTheme.Navy,
                TextAlign = ContentAlignment.MiddleCenter
            };
            pnlReadLeft.Controls.Add(lblGridPrimaryTag);
            pnlReadLeft.Controls.Add(btnReadPrimary);

            var pnlReadRight = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.Background, Padding = new Padding(4, 2, 0, 0) };
            btnReadSecondary = new FlatButton { Text = "Read Secondary", Dock = DockStyle.Top, Height = 30 };
            btnReadSecondary.Click += btnReadSecondary_Click;
            lblGridSecondaryTag = new Label
            {
                Text = "", Dock = DockStyle.Bottom, Height = 22,
                Font = AppTheme.Bold, ForeColor = AppTheme.Navy,
                TextAlign = ContentAlignment.MiddleCenter
            };
            pnlReadRight.Controls.Add(lblGridSecondaryTag);
            pnlReadRight.Controls.Add(btnReadSecondary);

            // Row 1 — Data grids with empty-state placeholder text
            gridPrimary   = new System.Windows.Forms.DataGridView { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 3, 0) };
            gridSecondary = new System.Windows.Forms.DataGridView { Dock = DockStyle.Fill, Margin = new Padding(3, 0, 0, 0) };

            gridPrimary.Paint += (s, e) =>
            {
                var dg = (System.Windows.Forms.DataGridView)s;
                if (dg.Rows.Count > 0) return;
                System.Windows.Forms.TextRenderer.DrawText(e.Graphics,
                    "Select a Primary tag to load data",
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
                    "Select a Secondary tag to load data",
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
            var pnlViewGroup = new Panel { Dock = DockStyle.Top, BackColor = AppTheme.Background };
            var hdrView      = new SectionHeader { Text = "VIEW", Dock = DockStyle.Top, Height = 20 };

            btnExport     = MakeStackedButton("Export CSV",  FlatButtonStyle.Secondary);
            btnSyncScroll = MakeStackedButton("Sync Scroll", FlatButtonStyle.Secondary);
            btnCompare    = MakeStackedButton("Compare",     FlatButtonStyle.Secondary);

            btnSyncScroll.Click += btnSyncScroll_Click;
            btnExport.Click     += btnExport_Click;
            btnCompare.Click    += btnCompare_Click;

            // Dock=Top stacks in reverse-add order. Interleave 4px spacers.
            pnlViewGroup.Controls.Add(btnExport);
            pnlViewGroup.Controls.Add(MakeSpacer(4));
            pnlViewGroup.Controls.Add(btnSyncScroll);
            pnlViewGroup.Controls.Add(MakeSpacer(4));
            pnlViewGroup.Controls.Add(btnCompare);
            pnlViewGroup.Controls.Add(MakeSpacer(6));
            pnlViewGroup.Controls.Add(hdrView);
            pnlViewGroup.Height = 20 + 6 + 30 + 4 + 30 + 4 + 30;

            // Backfill group (bottom) — write actions + preview
            var pnlBackfillGroup = new Panel { Dock = DockStyle.Bottom, BackColor = AppTheme.Background };
            var hdrBackfill      = new SectionHeader { Text = "BACKFILL", Dock = DockStyle.Top, Height = 20 };

            btnHistory         = MakeStackedButton("Backfill History…",   FlatButtonStyle.Secondary);
            btnBackfillPreview = MakeStackedButton("Preview && Backfill…", FlatButtonStyle.Info);
            btnCopyToSecondary = MakeStackedButton("Copy to Secondary →", FlatButtonStyle.Warning);
            btnCopyToPrimary   = MakeStackedButton("← Copy to Primary",   FlatButtonStyle.Warning);

            btnHistory.Click         += btnHistory_Click;
            btnBackfillPreview.Click += btnBackfillPreview_Click;
            btnCopyToSecondary.Click += btnCopyToSecondary_Click;
            btnCopyToPrimary.Click   += btnCopyToPrimary_Click;

            // Added first = bottom-most. History sits at the bottom of the group,
            // visually separated from the Copy/Preview actions.
            pnlBackfillGroup.Controls.Add(btnHistory);
            pnlBackfillGroup.Controls.Add(MakeSpacer(8));
            pnlBackfillGroup.Controls.Add(btnBackfillPreview);
            pnlBackfillGroup.Controls.Add(MakeSpacer(4));
            pnlBackfillGroup.Controls.Add(btnCopyToSecondary);
            pnlBackfillGroup.Controls.Add(MakeSpacer(4));
            pnlBackfillGroup.Controls.Add(btnCopyToPrimary);
            pnlBackfillGroup.Controls.Add(MakeSpacer(6));
            pnlBackfillGroup.Controls.Add(hdrBackfill);
            pnlBackfillGroup.Height = 20 + 6 + 30 + 4 + 30 + 4 + 30 + 8 + 30;

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
                Height    = 236,
                BackColor = AppTheme.Surface,
                Padding   = new Padding(0, 0, 0, 4)
            };
            pnlTimeline.Paint += (s, e) =>
                e.Graphics.DrawLine(new Pen(AppTheme.Border),
                    0, pnlTimeline.Height - 1, pnlTimeline.Width, pnlTimeline.Height - 1);

            hdrTimeline = new SectionHeader { Text = "SYNC TIMELINE", Dock = DockStyle.Top };

            lnkZoomOut = new LinkLabel
            {
                Text            = "⟲  zoom back",
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

            // Assemble center (Fill first, then edge-docked panels)
            pnlCenter.Controls.Add(pnlGrids);    // Fill
            pnlCenter.Controls.Add(pnlLog);      // Bottom (outermost)
            pnlCenter.Controls.Add(pnlTimeline); // Top

            // ══════════════════════════════════════════════════════════════════════
            // Add to form  (Dock order: Bottom, Left, Right, Fill)
            // ══════════════════════════════════════════════════════════════════════
            this.Controls.Add(pnlCenter);
            this.Controls.Add(pnlRight);
            this.Controls.Add(pnlLeft);
            this.Controls.Add(pnlStatusBar);

            this.ResumeLayout(false);
            this.PerformLayout();
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
            gridGaps.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Tag",        Name = "Tag",       FillWeight = 23 });
            gridGaps.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Missing on", Name = "MissingOn", FillWeight = 22 });
            gridGaps.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Count",      Name = "Samples",   FillWeight = 17, DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight } });
            gridGaps.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Period",     Name = "Range",     FillWeight = 38 });
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

        /// <summary>Invisible vertical spacer used between docked buttons.</summary>
        private static Panel MakeSpacer(int height) => new Panel
        {
            Dock = DockStyle.Top,
            Height = height,
            BackColor = AppTheme.Background
        };
    }
}
