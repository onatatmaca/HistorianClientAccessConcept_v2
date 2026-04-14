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
        private ProgressBar    progressOp;
        private Button         btnCancel;

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
        private Label          lblSecondaryTag;
        private ComboBox       cboSecondary;

        // Right panel
        private Panel          pnlRight;
        private Panel          pnlRightContent;
        private Panel          pnlRightBottom;
        private SectionHeader  hdrGapAnalysis;
        private Label          lblPrimaryGap;
        private CoverageBar    barPrimary;
        private Label          lblSecondaryGap;
        private CoverageBar    barSecondary;
        private Label          lblGapSummary;
        private System.Windows.Forms.DataGridView gridGaps;
        private FlatButton     btnBackfillPreview;
        private FlatButton     btnStop;

        // Center
        private Panel          pnlCenter;
        private CollapsiblePanel pnlLog;
        private RichTextBox    txtLog;
        private Panel          pnlLogButtons;
        private Button         btnClearLog;
        private Button         btnCopyLog;
        private CollapsiblePanel pnlLower;
        private TabControl     tabLower;
        private TabPage        tabWrite;
        private TabPage        tabMultiField;
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

        // Write Data tab
        private Label          lblWriteTag;
        private Label          lblWriteTimestamp;
        private DateTimePicker dtpWriteTimestamp;
        private Label          lblWriteValue;
        private TextBox        txtWriteValue;
        private FlatButton     btnWriteData;

        // MultiField tab
        private Label          lblTypeName;
        private TextBox        txtTypeName;
        private System.Windows.Forms.DataGridView gridFieldDefs;
        private Panel          pnlMFButtons;
        private FlatButton     btnAddField;
        private FlatButton     btnRemoveField;
        private FlatButton     btnCreateMultiFieldType;
        private FlatButton     btnWriteMultiField;

        // ── InitializeComponent ────────────────────────────────────────────────────
        private void InitializeComponent()
        {
            this.SuspendLayout();

            // ── Form ───────────────────────────────────────────────────────────────
            this.Font          = AppTheme.Default;
            this.BackColor     = AppTheme.Background;
            this.AutoScaleMode = AutoScaleMode.Font;
            this.MinimumSize   = new Size(1000, 650);
            this.Text          = "Historian Sync Tool";
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Padding       = new Padding(8, 4, 8, 0);  // breathing room around all panels

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

            btnCancel = new Button
            {
                Text      = "Cancel",
                Dock      = DockStyle.Right,
                Width     = 80,
                FlatStyle = FlatStyle.Flat,
                BackColor = AppTheme.Danger,
                ForeColor = Color.White,
                Font      = AppTheme.SectionLabel,
                Cursor    = Cursors.Hand,
                Visible   = false
            };
            btnCancel.FlatAppearance.BorderSize = 0;
            btnCancel.Click += btnCancel_Click;

            progressOp = new ProgressBar
            {
                Dock    = DockStyle.Right,
                Width   = 200,
                Style   = ProgressBarStyle.Marquee,
                Visible = false,
                Margin  = new Padding(0, 6, 4, 6)
            };

            pnlStatusBar.Controls.Add(dotStatus);
            pnlStatusBar.Controls.Add(lblStatus);
            pnlStatusBar.Controls.Add(btnCancel);
            pnlStatusBar.Controls.Add(progressOp);

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
            lblSecondaryTag = MakeLabel("Secondary tag", lw, pad, cboPrimary.Bottom + 6);
            cboSecondary    = MakeCombo(lw, pad, lblSecondaryTag.Bottom + 2);

            pnlTagsContent.Controls.AddRange(new Control[]
            {
                lblTagnameFilter, txtTagnameFilter, pnlTagButtons,
                lblPrimaryTag, cboPrimary, lblSecondaryTag, cboSecondary
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

            // Top section: coverage bars + summary (Dock=Top, absolute positioning inside)
            hdrGapAnalysis = new SectionHeader { Text = "GAP ANALYSIS", Dock = DockStyle.Top };

            var pnlCoverage = new Panel { Dock = DockStyle.Top, Padding = new Padding(rpad, 10, rpad, 4) };

            lblPrimaryGap = new Label
            {
                Text = "Primary server coverage", Dock = DockStyle.Top, Height = 16,
                Font = AppTheme.Small, ForeColor = AppTheme.TextSecondary
            };
            barPrimary = new CoverageBar { Dock = DockStyle.Top, Height = 28 };

            lblSecondaryGap = new Label
            {
                Text = "Secondary server coverage", Dock = DockStyle.Top, Height = 16,
                Font = AppTheme.Small, ForeColor = AppTheme.TextSecondary,
                Padding = new Padding(0, 8, 0, 0)
            };
            barSecondary = new CoverageBar { Dock = DockStyle.Top, Height = 28 };

            lblGapSummary = new Label
            {
                Text = "Connect to both servers, then click 'Analyze Gaps'",
                Dock = DockStyle.Top, Height = 32,
                Font = AppTheme.Small, ForeColor = AppTheme.TextSecondary,
                Padding = new Padding(0, 6, 0, 0)
            };

            // Add in reverse order for Dock=Top (last added docks first)
            pnlCoverage.Controls.Add(lblGapSummary);
            pnlCoverage.Controls.Add(barSecondary);
            pnlCoverage.Controls.Add(lblSecondaryGap);
            pnlCoverage.Controls.Add(barPrimary);
            pnlCoverage.Controls.Add(lblPrimaryGap);
            pnlCoverage.Height = 16 + 28 + 24 + 28 + 32 + 10 + 4; // labels + bars + summary + padding

            // Grid fills remaining space — wrapped in panel for padding
            var pnlGridWrap = new Panel
            {
                Dock    = DockStyle.Fill,
                Padding = new Padding(rpad, 4, rpad, 4)
            };
            gridGaps = new System.Windows.Forms.DataGridView { Dock = DockStyle.Fill };
            SetupGapGrid();
            pnlGridWrap.Controls.Add(gridGaps);

            // Bottom action bar
            pnlRightBottom = new Panel
            {
                Dock      = DockStyle.Bottom,
                Height    = AppTheme.ButtonHeight * 2 + 14,
                BackColor = AppTheme.Surface,
                Padding   = new Padding(rpad, 4, rpad, 6)
            };
            pnlRightBottom.Paint += (s, e) =>
                e.Graphics.DrawLine(new Pen(AppTheme.Border), 0, 0, pnlRightBottom.Width, 0);

            btnBackfillPreview = new FlatButton
            {
                Text  = "Preview & Backfill…",
                Dock  = DockStyle.Bottom
            };
            btnBackfillPreview.Click += btnBackfillPreview_Click;

            btnStop = new FlatButton
            {
                Text        = "■  Stop",
                ButtonStyle = FlatButtonStyle.Danger,
                Dock        = DockStyle.Top,
                Visible     = false
            };
            btnStop.Click += btnStop_Click;

            pnlRightBottom.Controls.Add(btnStop);
            pnlRightBottom.Controls.Add(btnBackfillPreview);

            // Assemble right panel (add Bottom/Top before Fill)
            pnlRightContent.Controls.Add(pnlGridWrap);     // Fill — last
            pnlRightContent.Controls.Add(pnlCoverage);     // Top — after header
            pnlRightContent.Controls.Add(hdrGapAnalysis);   // Top — first
            pnlRight.Controls.Add(pnlRightBottom);          // Bottom
            pnlRight.Controls.Add(pnlRightContent);         // Fill

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

            // ── Write / MultiField  (Dock=Bottom, collapsed by default) ──────────
            pnlLower = new CollapsiblePanel
            {
                Dock           = DockStyle.Bottom,
                Title          = "WRITE DATA  |  MULTIFIELD TAGS",
                ExpandedHeight = 185
            };
            pnlLower.IsExpanded = false;

            tabLower      = new TabControl { Dock = DockStyle.Fill, Font = AppTheme.Default, Padding = new System.Drawing.Point(10, 4) };
            tabWrite      = new TabPage { Text = "Write Data",      BackColor = AppTheme.Surface, Padding = new Padding(8) };
            tabMultiField = new TabPage { Text = "MultiField Tags", BackColor = AppTheme.Surface, Padding = new Padding(8) };
            BuildWriteTab();
            BuildMultiFieldTab();
            tabLower.TabPages.Add(tabWrite);
            tabLower.TabPages.Add(tabMultiField);
            pnlLower.Content.Controls.Add(tabLower);

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
                Text = "", Dock = DockStyle.Bottom, Height = 20,
                Font = AppTheme.SectionLabel, ForeColor = AppTheme.Navy,
                TextAlign = ContentAlignment.MiddleCenter
            };
            pnlReadLeft.Controls.Add(lblGridPrimaryTag);
            pnlReadLeft.Controls.Add(btnReadPrimary);

            var pnlReadRight = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.Background, Padding = new Padding(4, 2, 0, 0) };
            btnReadSecondary = new FlatButton { Text = "Read Secondary", Dock = DockStyle.Top, Height = 30 };
            btnReadSecondary.Click += btnReadSecondary_Click;
            lblGridSecondaryTag = new Label
            {
                Text = "", Dock = DockStyle.Bottom, Height = 20,
                Font = AppTheme.SectionLabel, ForeColor = AppTheme.Navy,
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
                    "Select a Primary tag and click  'Read Primary'  to load data",
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
                    "Select a Secondary tag and click  'Read Secondary'  to load data",
                    AppTheme.Default,
                    new Rectangle(0, dg.ColumnHeadersHeight, dg.Width, dg.Height - dg.ColumnHeadersHeight),
                    AppTheme.TextSecondary,
                    System.Windows.Forms.TextFormatFlags.HorizontalCenter |
                    System.Windows.Forms.TextFormatFlags.VerticalCenter   |
                    System.Windows.Forms.TextFormatFlags.WordBreak);
            };

            // Row 1 — Action column: read-only Compare + write-caution Copy buttons
            pnlGridActions = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.Background };

            btnCompare         = MakeCenterButton("Compare",           50);
            btnCopyToPrimary   = MakeCenterButton("← Copy to Primary", btnCompare.Bottom   + 8);
            btnCopyToSecondary = MakeCenterButton("Copy to Secondary →", btnCopyToPrimary.Bottom + 4);

            btnSyncScroll = MakeCenterButton("Sync Scroll", btnCopyToSecondary.Bottom + 12);
            btnSyncScroll.ButtonStyle = FlatButtonStyle.Secondary;
            btnSyncScroll.Click += btnSyncScroll_Click;

            btnCompare.ButtonStyle         = FlatButtonStyle.Secondary;
            btnCopyToPrimary.ButtonStyle   = FlatButtonStyle.Warning;
            btnCopyToSecondary.ButtonStyle = FlatButtonStyle.Warning;
            btnCompare.Click           += btnCompare_Click;
            btnCopyToPrimary.Click     += btnCopyToPrimary_Click;
            btnCopyToSecondary.Click   += btnCopyToSecondary_Click;
            pnlGridActions.Controls.AddRange(new Control[]
                { btnCompare, btnCopyToPrimary, btnCopyToSecondary, btnSyncScroll });

            // Spacer for middle cell in button row
            var spacer = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.Background };

            pnlGrids.Controls.Add(pnlReadLeft,    0, 0);
            pnlGrids.Controls.Add(spacer,         1, 0);
            pnlGrids.Controls.Add(pnlReadRight,   2, 0);
            pnlGrids.Controls.Add(gridPrimary,    0, 1);
            pnlGrids.Controls.Add(pnlGridActions, 1, 1);
            pnlGrids.Controls.Add(gridSecondary,  2, 1);

            // Assemble center (Bottom controls before Fill)
            pnlCenter.Controls.Add(pnlGrids);    // Fill
            pnlCenter.Controls.Add(pnlLower);    // Bottom
            pnlCenter.Controls.Add(pnlLog);      // Bottom (outermost)

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
            gridGaps.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Server",       Name = "Server",   FillWeight = 12 });
            gridGaps.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Start",        Name = "Start",    FillWeight = 24 });
            gridGaps.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "End",          Name = "End",      FillWeight = 24 });
            gridGaps.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Duration",     Name = "Duration", FillWeight = 18 });
            gridGaps.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Batches",      Name = "Batches",  FillWeight = 11 });
            gridGaps.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Backfillable", Name = "Backfill", FillWeight = 11 });
        }

        // ══════════════════════════════════════════════════════════════════════════
        // Tab content builders
        // ══════════════════════════════════════════════════════════════════════════
        private void BuildWriteTab()
        {
            lblWriteTag = MakeLabel("Tag (from Primary selector)", 300, 0, 0);

            lblWriteTimestamp = MakeLabel("Timestamp", 100, 0, lblWriteTag.Bottom + 6);
            dtpWriteTimestamp = new DateTimePicker
            {
                Left = 0, Top = lblWriteTimestamp.Bottom + 2,
                Width = 200, Format = DateTimePickerFormat.Custom,
                CustomFormat = "yyyy-MM-dd HH:mm:ss", ShowUpDown = true
            };

            lblWriteValue = MakeLabel("Value", 100, 0, dtpWriteTimestamp.Bottom + 6);
            txtWriteValue = MakeTextBox(120, 0, lblWriteValue.Bottom + 2);

            btnWriteData = new FlatButton
            {
                Text = "Write Data",
                Left = txtWriteValue.Right + 8, Top = txtWriteValue.Top, Width = 100
            };
            btnWriteData.Click += btnWriteData_Click;

            tabWrite.Controls.AddRange(new Control[]
                { lblWriteTag, lblWriteTimestamp, dtpWriteTimestamp,
                  lblWriteValue, txtWriteValue, btnWriteData });
        }

        private void BuildMultiFieldTab()
        {
            lblTypeName = MakeLabel("Type name", 120, 0, 0);
            txtTypeName = MakeTextBox(200, 0, lblTypeName.Bottom + 2);

            gridFieldDefs = new System.Windows.Forms.DataGridView
            {
                Left = 0, Top = txtTypeName.Bottom + 6, Width = 340, Height = 70,
                AllowUserToAddRows = false,
                AutoGenerateColumns = false          // prevent DataTable from creating duplicate columns
            };
            gridFieldDefs.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Field Name", Name = "FieldName", DataPropertyName = "FieldName", FillWeight = 60 });
            gridFieldDefs.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Data Type",  Name = "DataType",  DataPropertyName = "DataType",  FillWeight = 40 });
            var mfTable = new System.Data.DataTable();
            mfTable.Columns.Add("FieldName");
            mfTable.Columns.Add("DataType");
            gridFieldDefs.DataSource = mfTable;

            pnlMFButtons = new Panel
            {
                Left = 0, Top = gridFieldDefs.Bottom + 4, Width = 470, Height = AppTheme.ButtonHeight
            };
            btnAddField             = MakeButton("+ Add Field",   120, 0,   0);
            btnRemoveField          = MakeButton("- Remove",       90, 126, 0);
            btnCreateMultiFieldType = MakeButton("Create Type",   110, 222, 0);
            btnWriteMultiField      = MakeButton("Write Sample",  110, 338, 0);
            btnAddField.ButtonStyle    = FlatButtonStyle.Secondary;
            btnRemoveField.ButtonStyle = FlatButtonStyle.Danger;
            btnAddField.Click             += btnAddField_Click;
            btnRemoveField.Click          += btnRemoveField_Click;
            btnCreateMultiFieldType.Click += btnCreateMultiFieldType_Click;
            btnWriteMultiField.Click      += btnWriteMultiField_Click;
            pnlMFButtons.Controls.AddRange(new Control[]
                { btnAddField, btnRemoveField, btnCreateMultiFieldType, btnWriteMultiField });

            tabMultiField.Controls.AddRange(new Control[]
                { lblTypeName, txtTypeName, gridFieldDefs, pnlMFButtons });
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
            Font = AppTheme.Default, DropDownStyle = ComboBoxStyle.DropDownList
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
    }
}
