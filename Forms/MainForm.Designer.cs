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
        private Panel          pnlPrimaryRow;
        private TextBox        txtPrimary;
        private ConnectionDot  dotPrimary;
        private Label          lblPrimaryStatus;
        private Label          lblSecondary;
        private Panel          pnlSecondaryRow;
        private TextBox        txtSecondary;
        private ConnectionDot  dotSecondary;
        private Label          lblSecondaryStatus;
        private FlatButton     btnConnect;

        // Left — Evaluation period (includes mode radios)
        private SectionHeader  hdrPeriod;
        private Label          lblStartDate;
        private DateTimePicker dtpStart;
        private Label          lblEndDate;
        private DateTimePicker dtpEnd;
        private Panel          pnlQuickDates;
        private Button         btnLast7d;
        private Button         btnLast30d;
        private Button         btnLastYear;
        private Label          lblModeHeader;
        private RadioButton    radioHistSync;
        private RadioButton    radioSelectedTag;
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
        private Panel          pnlGridActions;
        private FlatButton     btnReadPrimary;
        private FlatButton     btnReadSecondary;
        private FlatButton     btnCompare;
        private FlatButton     btnCopyToPrimary;
        private FlatButton     btnCopyToSecondary;
        private System.Windows.Forms.DataGridView gridSecondary;

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
            this.MinimumSize   = new Size(1100, 700);
            this.Size          = new Size(1380, 820);
            this.Text          = "Historian Sync Tool";
            this.StartPosition = FormStartPosition.CenterScreen;

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

            int lw = AppTheme.LeftPanelWidth - 2;   // usable width

            // ── CONNECTION ─────────────────────────────────────────────────────────
            hdrConnection = new SectionHeader { Text = "CONNECTION", Width = lw, Left = 0, Top = 0 };

            var pnlConnContent = new Panel { Left = 0, Top = hdrConnection.Bottom, Width = lw };

            // Primary row
            lblPrimary = MakeLabel("Primary server", lw, 0, 8);

            pnlPrimaryRow = new Panel
            {
                Left = 0, Top = lblPrimary.Bottom + 2, Width = lw, Height = AppTheme.ControlHeight
            };
            txtPrimary = new TextBox
            {
                Left = 0, Top = 0, Width = lw - 22, Height = AppTheme.ControlHeight,
                Font = AppTheme.Default, BorderStyle = BorderStyle.FixedSingle
            };
            dotPrimary = new ConnectionDot
            {
                Left = txtPrimary.Right + 4, Top = (AppTheme.ControlHeight - 14) / 2
            };
            pnlPrimaryRow.Controls.Add(txtPrimary);
            pnlPrimaryRow.Controls.Add(dotPrimary);

            lblPrimaryStatus = new Label
            {
                Text = "Not connected",
                Left = 2, Top = pnlPrimaryRow.Bottom + 2,
                Width = lw, Height = 16,
                Font = AppTheme.Small, ForeColor = AppTheme.TextSecondary, AutoSize = false
            };

            // Secondary row
            lblSecondary = MakeLabel("Secondary server", lw, 0, lblPrimaryStatus.Bottom + 6);

            pnlSecondaryRow = new Panel
            {
                Left = 0, Top = lblSecondary.Bottom + 2, Width = lw, Height = AppTheme.ControlHeight
            };
            txtSecondary = new TextBox
            {
                Left = 0, Top = 0, Width = lw - 22, Height = AppTheme.ControlHeight,
                Font = AppTheme.Default, BorderStyle = BorderStyle.FixedSingle
            };
            dotSecondary = new ConnectionDot
            {
                Left = txtSecondary.Right + 4, Top = (AppTheme.ControlHeight - 14) / 2
            };
            pnlSecondaryRow.Controls.Add(txtSecondary);
            pnlSecondaryRow.Controls.Add(dotSecondary);

            lblSecondaryStatus = new Label
            {
                Text = "Not connected",
                Left = 2, Top = pnlSecondaryRow.Bottom + 2,
                Width = lw, Height = 16,
                Font = AppTheme.Small, ForeColor = AppTheme.TextSecondary, AutoSize = false
            };

            btnConnect = MakeButton("Connect", lw, 0, lblSecondaryStatus.Bottom + 8);
            btnConnect.Click += btnConnect_Click;

            pnlConnContent.Controls.AddRange(new Control[]
            {
                lblPrimary, pnlPrimaryRow, lblPrimaryStatus,
                lblSecondary, pnlSecondaryRow, lblSecondaryStatus,
                btnConnect
            });
            pnlConnContent.Height = btnConnect.Bottom + 10;

            // ── EVALUATION PERIOD (includes gap analysis mode radios) ──────────────
            hdrPeriod = new SectionHeader
            {
                Text = "EVALUATION PERIOD", Width = lw, Left = 0,
                Top  = hdrConnection.Bottom + pnlConnContent.Height
            };

            var pnlPeriodContent = new Panel { Left = 0, Top = hdrPeriod.Bottom, Width = lw };

            lblStartDate = MakeLabel("Start date", lw, 0, 8);
            dtpStart     = MakeDtp(lw, 0, lblStartDate.Bottom + 2);
            lblEndDate   = MakeLabel("End date", lw, 0, dtpStart.Bottom + 6);
            dtpEnd       = MakeDtp(lw, 0, lblEndDate.Bottom + 2);

            // Quick-select presets
            pnlQuickDates = new Panel { Left = 0, Top = dtpEnd.Bottom + 6, Width = lw, Height = 24 };
            int qw = (lw - 8) / 3;
            btnLast7d   = MakeQuickBtn("Last 7 days",  0);
            btnLast30d  = MakeQuickBtn("Last 30 days", qw + 4);
            btnLastYear = MakeQuickBtn("Last year",    (qw + 4) * 2);
            btnLast7d.Width = btnLast30d.Width = btnLastYear.Width = qw;
            btnLast7d.Click   += (s, e) => { dtpStart.Value = System.DateTime.Now.AddDays(-7);   dtpEnd.Value = System.DateTime.Now; };
            btnLast30d.Click  += (s, e) => { dtpStart.Value = System.DateTime.Now.AddDays(-30);  dtpEnd.Value = System.DateTime.Now; };
            btnLastYear.Click += (s, e) => { dtpStart.Value = System.DateTime.Now.AddYears(-1);  dtpEnd.Value = System.DateTime.Now; };
            pnlQuickDates.Controls.Add(btnLast7d);
            pnlQuickDates.Controls.Add(btnLast30d);
            pnlQuickDates.Controls.Add(btnLastYear);

            // Mode selector (directly above Analyze Gaps)
            lblModeHeader = new Label
            {
                Text = "Analyze by:", Left = 0, Top = pnlQuickDates.Bottom + 10,
                Width = lw, Height = 16,
                Font = AppTheme.Small, ForeColor = AppTheme.TextSecondary, AutoSize = false
            };

            radioHistSync = new RadioButton
            {
                Text = "HistSync heartbeat tag",
                Left = 8, Top = lblModeHeader.Bottom + 2, Width = lw - 8,
                Font = AppTheme.Default, ForeColor = AppTheme.TextPrimary, Checked = true
            };
            radioSelectedTag = new RadioButton
            {
                Text = "Currently selected tag",
                Left = 8, Top = radioHistSync.Bottom + 2, Width = lw - 8,
                Font = AppTheme.Default, ForeColor = AppTheme.TextPrimary
            };
            radioHistSync.CheckedChanged    += radioMode_CheckedChanged;
            radioSelectedTag.CheckedChanged += radioMode_CheckedChanged;

            btnAnalyzeGaps = MakeButton("Analyze Gaps", lw, 0, radioSelectedTag.Bottom + 8);
            btnAnalyzeGaps.Click += btnAnalyzeGaps_Click;

            pnlPeriodContent.Controls.AddRange(new Control[]
            {
                lblStartDate, dtpStart, lblEndDate, dtpEnd,
                pnlQuickDates,
                lblModeHeader, radioHistSync, radioSelectedTag,
                btnAnalyzeGaps
            });
            pnlPeriodContent.Height = btnAnalyzeGaps.Bottom + 10;

            // ── TAGS ──────────────────────────────────────────────────────────────
            hdrTags = new SectionHeader
            {
                Text = "TAGS", Width = lw, Left = 0,
                Top  = hdrPeriod.Bottom + pnlPeriodContent.Height
            };

            var pnlTagsContent = new Panel { Left = 0, Top = hdrTags.Bottom, Width = lw };

            lblTagnameFilter = MakeLabel("Tagname filter", lw, 0, 8);
            txtTagnameFilter = MakeTextBox(lw, 0, lblTagnameFilter.Bottom + 2);

            pnlTagButtons = new Panel
            {
                Left = 0, Top = txtTagnameFilter.Bottom + 6, Width = lw, Height = AppTheme.ButtonHeight
            };
            btnBrowseTags = MakeButton("Browse Tags",  (lw - 4) / 2, 0, 0);
            btnGetStats   = MakeButton("Server Stats", (lw - 4) / 2, btnBrowseTags.Right + 4, 0);
            btnBrowseTags.ButtonStyle = FlatButtonStyle.Secondary;
            btnGetStats.ButtonStyle   = FlatButtonStyle.Secondary;
            btnBrowseTags.Click += btnBrowseTags_Click;
            btnGetStats.Click   += btnGetStats_Click;
            pnlTagButtons.Controls.Add(btnBrowseTags);
            pnlTagButtons.Controls.Add(btnGetStats);

            lblPrimaryTag   = MakeLabel("Primary tag",   lw, 0, pnlTagButtons.Bottom + 6);
            cboPrimary      = MakeCombo(lw, 0, lblPrimaryTag.Bottom + 2);
            lblSecondaryTag = MakeLabel("Secondary tag", lw, 0, cboPrimary.Bottom + 6);
            cboSecondary    = MakeCombo(lw, 0, lblSecondaryTag.Bottom + 2);

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
            int rw = AppTheme.RightPanelWidth - 2;

            hdrGapAnalysis = new SectionHeader { Text = "GAP ANALYSIS", Left = 0, Top = 0, Width = rw };

            lblPrimaryGap = MakeLabel("Primary server coverage", rw, 0, hdrGapAnalysis.Bottom + 10);
            barPrimary    = new CoverageBar { Left = 0, Top = lblPrimaryGap.Bottom + 3, Width = rw, Height = 30 };

            lblSecondaryGap = MakeLabel("Secondary server coverage", rw, 0, barPrimary.Bottom + 10);
            barSecondary    = new CoverageBar { Left = 0, Top = lblSecondaryGap.Bottom + 3, Width = rw, Height = 30 };

            lblGapSummary = new Label
            {
                Text = "Run 'Analyze Gaps' to see results",
                Left = 0, Top = barSecondary.Bottom + 8,
                Width = rw, Height = 18,
                Font = AppTheme.Small, ForeColor = AppTheme.TextSecondary, AutoSize = false
            };

            gridGaps = new System.Windows.Forms.DataGridView
            {
                Left   = 0, Top = lblGapSummary.Bottom + 4,
                Width  = rw, Height = 220,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom
            };
            SetupGapGrid();

            btnBackfillPreview = new FlatButton
            {
                Text   = "Preview & Backfill…",
                Left   = 0, Width = rw,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };
            btnBackfillPreview.Click += btnBackfillPreview_Click;

            btnStop = new FlatButton
            {
                Text        = "■  Stop",
                ButtonStyle = FlatButtonStyle.Danger,
                Left        = 0, Width = rw,
                Visible     = false,
                Anchor      = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };
            btnStop.Click += btnStop_Click;

            btnBackfillPreview.Top = pnlRight.Height - btnBackfillPreview.Height - 48;
            btnStop.Top            = btnBackfillPreview.Top - btnStop.Height - 6;

            pnlRightContent.Controls.AddRange(new Control[]
            {
                hdrGapAnalysis,
                lblPrimaryGap,   barPrimary,
                lblSecondaryGap, barSecondary,
                lblGapSummary,   gridGaps,
                btnStop,         btnBackfillPreview
            });
            pnlRight.Controls.Add(pnlRightContent);

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
                Title          = "WRITE DATA & MULTIFIELD TAGS",
                ExpandedHeight = 160
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
            // Row 0: Read buttons above their respective grids
            // Row 1: Primary grid | Action buttons | Secondary grid
            pnlGrids = new TableLayoutPanel
            {
                Dock        = DockStyle.Fill,
                ColumnCount = 3,
                RowCount    = 2,
                BackColor   = AppTheme.Background,
                Padding     = new Padding(6)
            };
            pnlGrids.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,  50f));
            pnlGrids.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 128));   // action column
            pnlGrids.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,  50f));
            pnlGrids.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));           // button row
            pnlGrids.RowStyles.Add(new RowStyle(SizeType.Percent,  100f));         // grid row

            // Row 0 — Read buttons, each directly above its grid
            var pnlReadLeft = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.Background, Padding = new Padding(0, 2, 4, 2) };
            btnReadPrimary = new FlatButton { Text = "Read Primary", Dock = DockStyle.Fill };
            btnReadPrimary.Click += btnReadPrimary_Click;
            pnlReadLeft.Controls.Add(btnReadPrimary);

            var pnlReadRight = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.Background, Padding = new Padding(4, 2, 0, 2) };
            btnReadSecondary = new FlatButton { Text = "Read Secondary", Dock = DockStyle.Fill, ButtonStyle = FlatButtonStyle.Secondary };
            btnReadSecondary.Click += btnReadSecondary_Click;
            pnlReadRight.Controls.Add(btnReadSecondary);

            // Row 1 — Data grids
            gridPrimary   = new System.Windows.Forms.DataGridView { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 3, 0) };
            gridSecondary = new System.Windows.Forms.DataGridView { Dock = DockStyle.Fill, Margin = new Padding(3, 0, 0, 0) };

            // Row 1 — Action column (Compare + write operations)
            pnlGridActions = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.Background };

            // Vertically stack buttons with top offset so they sit near the grid center
            btnCompare         = MakeCenterButton("Compare",         50);
            btnCopyToPrimary   = MakeCenterButton("← Copy to Pri",   btnCompare.Bottom   + 8);
            btnCopyToSecondary = MakeCenterButton("Copy to Sec →",   btnCopyToPrimary.Bottom + 4);

            btnCompare.ButtonStyle         = FlatButtonStyle.Secondary;
            btnCopyToPrimary.ButtonStyle   = FlatButtonStyle.Danger;
            btnCopyToSecondary.ButtonStyle = FlatButtonStyle.Danger;
            btnCompare.Click           += btnCompare_Click;
            btnCopyToPrimary.Click     += btnCopyToPrimary_Click;
            btnCopyToSecondary.Click   += btnCopyToSecondary_Click;
            pnlGridActions.Controls.AddRange(new Control[]
                { btnCompare, btnCopyToPrimary, btnCopyToSecondary });

            // Spacer for middle cell in row 0
            var spacer = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.Background };

            pnlGrids.Controls.Add(pnlReadLeft,    0, 0);
            pnlGrids.Controls.Add(spacer,         1, 0);
            pnlGrids.Controls.Add(pnlReadRight,   2, 0);
            pnlGrids.Controls.Add(gridPrimary,    0, 1);
            pnlGrids.Controls.Add(pnlGridActions, 1, 1);
            pnlGrids.Controls.Add(gridSecondary,  2, 1);

            // Assemble center (Bottom controls added before Fill)
            pnlCenter.Controls.Add(pnlGrids);    // Fill — processed last by WinForms
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
            gridGaps.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Start",        Name = "Start",    FillWeight = 25 });
            gridGaps.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "End",          Name = "End",      FillWeight = 25 });
            gridGaps.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Duration",     Name = "Duration", FillWeight = 20 });
            gridGaps.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Batches",      Name = "Batches",  FillWeight = 15 });
            gridGaps.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Backfillable", Name = "Backfill", FillWeight = 15 });
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
                AllowUserToAddRows = false
            };
            gridFieldDefs.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Field Name", Name = "FieldName", FillWeight = 60 });
            gridFieldDefs.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Data Type",  Name = "DataType",  FillWeight = 40 });
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
            Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd HH:mm:ss", ShowUpDown = true
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
            Text = text, Left = 4, Top = top, Width = 120,
            Font = AppTheme.SectionLabel
        };
    }
}
