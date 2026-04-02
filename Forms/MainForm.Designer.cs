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
        private Label          lblSecondary;
        private Panel          pnlSecondaryRow;
        private TextBox        txtSecondary;
        private ConnectionDot  dotSecondary;
        private FlatButton     btnConnect;

        // Left — Period
        private SectionHeader  hdrPeriod;
        private Label          lblStartDate;
        private DateTimePicker dtpStart;
        private Label          lblEndDate;
        private DateTimePicker dtpEnd;
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

        // Left — Analysis mode
        private SectionHeader  hdrMode;
        private RadioButton    radioHistSync;
        private RadioButton    radioSelectedTag;

        // Right panel
        private Panel          pnlRight;
        private Panel          pnlRightContent;
        private SectionHeader  hdrGapAnalysis;
        private Label          lblPrimaryGap;
        private CoverageBar    barPrimary;
        private Label          lblSecondaryGap;
        private CoverageBar    barSecondary;
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
        private Panel          pnlLower;
        private TabControl     tabLower;
        private TabPage        tabWrite;
        private TabPage        tabMultiField;
        private Panel          pnlReadHeader;
        private FlatButton     btnReadPrimary;
        private FlatButton     btnReadSecondary;
        private TableLayoutPanel pnlGrids;
        private System.Windows.Forms.DataGridView gridPrimary;
        private Panel          pnlGridActions;
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
            this.Font           = AppTheme.Default;
            this.BackColor      = AppTheme.Background;
            this.MinimumSize    = new Size(1100, 700);
            this.Size           = new Size(1380, 820);
            this.Text           = "Historian Sync Tool";
            this.StartPosition  = FormStartPosition.CenterScreen;

            // ══════════════════════════════════════════════════════════════════════
            // STATUS BAR (Dock=Bottom)
            // ══════════════════════════════════════════════════════════════════════
            pnlStatusBar = new Panel
            {
                Dock      = DockStyle.Bottom,
                Height    = AppTheme.StatusBarHeight,
                BackColor = AppTheme.Surface,
                Padding   = new Padding(10, 0, 10, 0)
            };
            pnlStatusBar.Paint += (s, e) =>
            {
                e.Graphics.DrawLine(new Pen(AppTheme.Border),
                    0, 0, pnlStatusBar.Width, 0);
            };

            dotStatus = new ConnectionDot
            {
                Left = 12, Top = (AppTheme.StatusBarHeight - 14) / 2,
                State = ConnectionState.Disconnected
            };

            lblStatus = new Label
            {
                Text      = "Ready",
                Left      = 34, Top = 0,
                Width     = 500, Height = AppTheme.StatusBarHeight,
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
                Width   = 180,
                Style   = ProgressBarStyle.Marquee,
                Visible = false,
                Margin  = new Padding(0, 6, 4, 6)
            };

            pnlStatusBar.Controls.Add(dotStatus);
            pnlStatusBar.Controls.Add(lblStatus);
            pnlStatusBar.Controls.Add(btnCancel);
            pnlStatusBar.Controls.Add(progressOp);

            // ══════════════════════════════════════════════════════════════════════
            // LEFT SIDEBAR (Dock=Left)
            // ══════════════════════════════════════════════════════════════════════
            pnlLeft = new Panel
            {
                Dock      = DockStyle.Left,
                Width     = AppTheme.LeftPanelWidth,
                BackColor = AppTheme.Surface
            };
            pnlLeft.Paint += (s, e) =>
            {
                e.Graphics.DrawLine(new Pen(AppTheme.Border),
                    pnlLeft.Width - 1, 0, pnlLeft.Width - 1, pnlLeft.Height);
            };

            pnlLeftContent = new Panel
            {
                Dock        = DockStyle.Fill,
                AutoScroll  = true,
                Padding     = new Padding(0, 0, 1, 0)
            };

            int lw = AppTheme.LeftPanelWidth - 2; // usable width in left panel

            // ── CONNECTION section ─────────────────────────────────────────────────
            hdrConnection = new SectionHeader
            { Text = "CONNECTION", Width = lw, Left = 0, Top = 0 };

            lblPrimary = MakeLabel("Primary server", lw, 0, hdrConnection.Bottom + 8);

            txtPrimary = MakeTextBox(lw - 22, 0, lblPrimary.Bottom + 2);

            dotPrimary = new ConnectionDot
            {
                Left  = txtPrimary.Right + 4,
                Top   = txtPrimary.Top + (txtPrimary.Height - 14) / 2
            };

            pnlPrimaryRow = new Panel
            {
                Left   = 0,
                Top    = lblPrimary.Bottom + 2,
                Width  = lw,
                Height = AppTheme.ControlHeight
            };
            txtPrimary.Left = 0; txtPrimary.Top = 0;
            dotPrimary.Left = txtPrimary.Width + 4;
            dotPrimary.Top  = (AppTheme.ControlHeight - 14) / 2;
            pnlPrimaryRow.Controls.Add(txtPrimary);
            pnlPrimaryRow.Controls.Add(dotPrimary);

            lblSecondary = MakeLabel("Secondary server", lw, 0, pnlPrimaryRow.Bottom + 6);

            pnlSecondaryRow = new Panel
            {
                Left   = 0,
                Top    = lblSecondary.Bottom + 2,
                Width  = lw,
                Height = AppTheme.ControlHeight
            };
            txtSecondary = MakeTextBox(lw - 22, 0, 0);
            dotSecondary = new ConnectionDot
            {
                Left = txtSecondary.Width + 4,
                Top  = (AppTheme.ControlHeight - 14) / 2
            };
            pnlSecondaryRow.Controls.Add(txtSecondary);
            pnlSecondaryRow.Controls.Add(dotSecondary);

            btnConnect = MakeButton("Connect", lw, 0, pnlSecondaryRow.Bottom + 8);

            var pnlConnectionContent = new Panel
            {
                Left   = 0,
                Top    = hdrConnection.Bottom,
                Width  = lw,
                Height = btnConnect.Bottom + 10
            };
            // reposition controls relative to content panel
            lblPrimary.Top        = 8;
            pnlPrimaryRow.Top     = lblPrimary.Bottom + 2;
            lblSecondary.Top      = pnlPrimaryRow.Bottom + 6;
            pnlSecondaryRow.Top   = lblSecondary.Bottom + 2;
            btnConnect.Top        = pnlSecondaryRow.Bottom + 8;
            btnConnect.Width      = lw;
            pnlConnectionContent.Height = btnConnect.Bottom + 10;
            pnlConnectionContent.Controls.AddRange(new Control[]
                { lblPrimary, pnlPrimaryRow, lblSecondary, pnlSecondaryRow, btnConnect });

            // ── EVALUATION PERIOD section ──────────────────────────────────────────
            hdrPeriod = new SectionHeader
            { Text = "EVALUATION PERIOD", Width = lw, Left = 0,
              Top = hdrConnection.Bottom + pnlConnectionContent.Height };

            lblStartDate = MakeLabel("Start date", lw, 0, 8);
            dtpStart     = MakeDtp(lw, 0, lblStartDate.Bottom + 2);
            lblEndDate   = MakeLabel("End date", lw, 0, dtpStart.Bottom + 6);
            dtpEnd       = MakeDtp(lw, 0, lblEndDate.Bottom + 2);
            btnAnalyzeGaps = MakeButton("Analyze Gaps", lw, 0, dtpEnd.Bottom + 8);
            var pnlPeriodContent = new Panel
            {
                Left  = 0, Top = hdrPeriod.Bottom, Width = lw,
                Height = btnAnalyzeGaps.Bottom + 10
            };
            pnlPeriodContent.Controls.AddRange(new Control[]
                { lblStartDate, dtpStart, lblEndDate, dtpEnd, btnAnalyzeGaps });

            // ── TAGS section ───────────────────────────────────────────────────────
            hdrTags = new SectionHeader
            { Text = "TAGS", Width = lw, Left = 0,
              Top = hdrPeriod.Bottom + pnlPeriodContent.Height };

            lblTagnameFilter = MakeLabel("Tagname filter", lw, 0, 8);
            txtTagnameFilter = MakeTextBox(lw, 0, lblTagnameFilter.Bottom + 2);

            pnlTagButtons = new Panel
            {
                Left = 0, Top = txtTagnameFilter.Bottom + 6,
                Width = lw, Height = AppTheme.ButtonHeight
            };
            btnBrowseTags = MakeButton("Browse Tags", (lw - 4) / 2, 0, 0);
            btnGetStats   = MakeButton("Get Stats", (lw - 4) / 2, btnBrowseTags.Right + 4, 0);
            btnGetStats.ButtonStyle = FlatButtonStyle.Secondary;
            pnlTagButtons.Controls.Add(btnBrowseTags);
            pnlTagButtons.Controls.Add(btnGetStats);

            lblPrimaryTag   = MakeLabel("Primary tag", lw, 0, pnlTagButtons.Bottom + 6);
            cboPrimary      = MakeCombo(lw, 0, lblPrimaryTag.Bottom + 2);
            lblSecondaryTag = MakeLabel("Secondary tag", lw, 0, cboPrimary.Bottom + 6);
            cboSecondary    = MakeCombo(lw, 0, lblSecondaryTag.Bottom + 2);

            var pnlTagsContent = new Panel
            {
                Left = 0, Top = hdrTags.Bottom, Width = lw,
                Height = cboSecondary.Bottom + 10
            };
            pnlTagsContent.Controls.AddRange(new Control[]
                { lblTagnameFilter, txtTagnameFilter, pnlTagButtons,
                  lblPrimaryTag, cboPrimary, lblSecondaryTag, cboSecondary });

            // ── ANALYSIS MODE section ──────────────────────────────────────────────
            hdrMode = new SectionHeader
            { Text = "GAP ANALYSIS MODE", Width = lw, Left = 0,
              Top = hdrTags.Bottom + pnlTagsContent.Height };

            radioHistSync = new RadioButton
            {
                Text      = "HistSync heartbeat tag",
                Left = 10, Top = 8, Width = lw - 10,
                Font      = AppTheme.Default,
                ForeColor = AppTheme.TextPrimary,
                Checked   = true
            };
            radioSelectedTag = new RadioButton
            {
                Text = "Currently selected tag",
                Left = 10, Top = radioHistSync.Bottom + 4, Width = lw - 10,
                Font      = AppTheme.Default,
                ForeColor = AppTheme.TextPrimary
            };
            radioHistSync.CheckedChanged    += radioMode_CheckedChanged;
            radioSelectedTag.CheckedChanged += radioMode_CheckedChanged;

            var pnlModeContent = new Panel
            {
                Left = 0, Top = hdrMode.Bottom, Width = lw,
                Height = radioSelectedTag.Bottom + 10
            };
            pnlModeContent.Controls.AddRange(new Control[]
                { radioHistSync, radioSelectedTag });

            pnlLeftContent.Controls.AddRange(new Control[]
            {
                hdrConnection, pnlConnectionContent,
                hdrPeriod,     pnlPeriodContent,
                hdrTags,       pnlTagsContent,
                hdrMode,       pnlModeContent
            });
            pnlLeft.Controls.Add(pnlLeftContent);

            // ══════════════════════════════════════════════════════════════════════
            // RIGHT PANEL (Dock=Right)
            // ══════════════════════════════════════════════════════════════════════
            pnlRight = new Panel
            {
                Dock      = DockStyle.Right,
                Width     = AppTheme.RightPanelWidth,
                BackColor = AppTheme.Surface
            };
            pnlRight.Paint += (s, e) =>
            {
                e.Graphics.DrawLine(new Pen(AppTheme.Border),
                    0, 0, 0, pnlRight.Height);
            };

            pnlRightContent = new Panel { Dock = DockStyle.Fill, Padding = new Padding(1, 0, 0, 0) };

            int rw = AppTheme.RightPanelWidth - 2;

            hdrGapAnalysis = new SectionHeader
            { Text = "GAP ANALYSIS", Left = 0, Top = 0, Width = rw };

            lblPrimaryGap = MakeLabel("Primary server coverage", rw, 0, hdrGapAnalysis.Bottom + 10);
            barPrimary = new CoverageBar
            { Left = 0, Top = lblPrimaryGap.Bottom + 3, Width = rw, Height = 30 };

            lblSecondaryGap = MakeLabel("Secondary server coverage", rw, 0, barPrimary.Bottom + 10);
            barSecondary = new CoverageBar
            { Left = 0, Top = lblSecondaryGap.Bottom + 3, Width = rw, Height = 30 };

            gridGaps = new System.Windows.Forms.DataGridView
            {
                Left = 0, Top = barSecondary.Bottom + 10,
                Width = rw, Height = 260,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom
            };
            SetupGapGrid();

            btnBackfillPreview = new FlatButton
            {
                Text   = "Preview & Backfill…",
                Left   = 0,
                Width  = rw,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };
            btnBackfillPreview.Click += btnBackfillPreview_Click;

            btnStop = new FlatButton
            {
                Text        = "■  Stop",
                ButtonStyle = FlatButtonStyle.Danger,
                Left        = 0,
                Width       = rw,
                Visible     = false,
                Anchor      = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };
            btnStop.Click += btnStop_Click;

            // Position bottom buttons
            btnBackfillPreview.Top = pnlRight.Height - btnBackfillPreview.Height - 48;
            btnStop.Top            = btnBackfillPreview.Top - btnStop.Height - 6;

            pnlRightContent.Controls.AddRange(new Control[]
            {
                hdrGapAnalysis,
                lblPrimaryGap, barPrimary,
                lblSecondaryGap, barSecondary,
                gridGaps,
                btnStop, btnBackfillPreview
            });
            pnlRight.Controls.Add(pnlRightContent);

            // ══════════════════════════════════════════════════════════════════════
            // CENTER PANEL (Dock=Fill)
            // ══════════════════════════════════════════════════════════════════════
            pnlCenter = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.Background };

            // ── Log (Dock=Bottom) ──────────────────────────────────────────────────
            pnlLog = new CollapsiblePanel
            {
                Dock           = DockStyle.Bottom,
                Title          = "ACTIVITY LOG",
                ExpandedHeight = 170
            };

            txtLog = new RichTextBox
            {
                Dock       = DockStyle.Fill,
                ReadOnly   = true,
                BackColor  = Color.FromArgb(18, 18, 24),
                ForeColor  = Color.FromArgb(180, 220, 180),
                Font       = AppTheme.Mono,
                BorderStyle = BorderStyle.None,
                ScrollBars = RichTextBoxScrollBars.Vertical
            };

            pnlLogButtons = new Panel
            {
                Dock      = DockStyle.Bottom,
                Height    = 28,
                BackColor = AppTheme.Surface,
                Padding   = new Padding(4, 2, 4, 2)
            };
            btnClearLog = MakeSmallButton("Clear",      DockStyle.Right);
            btnCopyLog  = MakeSmallButton("Copy Log",   DockStyle.Right);
            btnClearLog.Click += btnClearLog_Click;
            btnCopyLog.Click  += btnCopyLog_Click;
            pnlLogButtons.Controls.Add(btnClearLog);
            pnlLogButtons.Controls.Add(btnCopyLog);

            pnlLog.Content.Controls.Add(txtLog);
            pnlLog.Content.Controls.Add(pnlLogButtons);

            // ── Lower tabs (Dock=Bottom) ───────────────────────────────────────────
            pnlLower = new Panel
            {
                Dock      = DockStyle.Bottom,
                Height    = 170,
                BackColor = AppTheme.Surface
            };
            pnlLower.Paint += (s, e) =>
            {
                e.Graphics.DrawLine(new Pen(AppTheme.Border), 0, 0, pnlLower.Width, 0);
            };

            tabLower = new TabControl
            {
                Dock      = DockStyle.Fill,
                Font      = AppTheme.Default,
                Padding   = new System.Drawing.Point(10, 4)
            };

            tabWrite      = new TabPage { Text = "Write Data",     BackColor = AppTheme.Surface, Padding = new Padding(8) };
            tabMultiField = new TabPage { Text = "MultiField Tags", BackColor = AppTheme.Surface, Padding = new Padding(8) };

            BuildWriteTab();
            BuildMultiFieldTab();

            tabLower.TabPages.Add(tabWrite);
            tabLower.TabPages.Add(tabMultiField);
            pnlLower.Controls.Add(tabLower);

            // ── Read header (Dock=Top) ─────────────────────────────────────────────
            pnlReadHeader = new Panel
            {
                Dock      = DockStyle.Top,
                Height    = 44,
                BackColor = AppTheme.Surface,
                Padding   = new Padding(8, 6, 8, 6)
            };
            pnlReadHeader.Paint += (s, e) =>
            {
                e.Graphics.DrawLine(new Pen(AppTheme.Border),
                    0, pnlReadHeader.Height - 1, pnlReadHeader.Width, pnlReadHeader.Height - 1);
            };

            btnReadPrimary = new FlatButton
            {
                Text   = "Read Primary",
                Left   = 8, Top = 6, Width = 140
            };
            btnReadSecondary = new FlatButton
            {
                Text        = "Read Secondary",
                Left        = 156, Top = 6, Width = 150,
                ButtonStyle = FlatButtonStyle.Secondary
            };
            btnReadPrimary.Click   += btnReadPrimary_Click;
            btnReadSecondary.Click += btnReadSecondary_Click;
            pnlReadHeader.Controls.Add(btnReadPrimary);
            pnlReadHeader.Controls.Add(btnReadSecondary);

            // ── Grids (Dock=Fill) ──────────────────────────────────────────────────
            pnlGrids = new TableLayoutPanel
            {
                Dock         = DockStyle.Fill,
                ColumnCount  = 3,
                RowCount     = 1,
                BackColor    = AppTheme.Background,
                Padding      = new Padding(6)
            };
            pnlGrids.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            pnlGrids.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 54));
            pnlGrids.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            pnlGrids.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            gridPrimary = new System.Windows.Forms.DataGridView { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 3, 0) };
            gridSecondary = new System.Windows.Forms.DataGridView { Dock = DockStyle.Fill, Margin = new Padding(3, 0, 0, 0) };

            pnlGridActions = new Panel
            {
                Dock      = DockStyle.Fill,
                BackColor = AppTheme.Background
            };
            btnCompare           = MakeCenterButton("Compare",  0);
            btnCopyToPrimary     = MakeCenterButton("<<",       btnCompare.Bottom + 6);
            btnCopyToSecondary   = MakeCenterButton(">>",       btnCopyToPrimary.Bottom + 4);
            btnCopyToPrimary.ButtonStyle   = FlatButtonStyle.Secondary;
            btnCopyToSecondary.ButtonStyle = FlatButtonStyle.Secondary;
            btnCompare.Click           += btnCompare_Click;
            btnCopyToPrimary.Click     += btnCopyToPrimary_Click;
            btnCopyToSecondary.Click   += btnCopyToSecondary_Click;
            pnlGridActions.Controls.AddRange(new Control[]
                { btnCompare, btnCopyToPrimary, btnCopyToSecondary });

            pnlGrids.Controls.Add(gridPrimary,    0, 0);
            pnlGrids.Controls.Add(pnlGridActions, 1, 0);
            pnlGrids.Controls.Add(gridSecondary,  2, 0);

            // ── Assemble center (add in Dock order: Bottom first, Fill last) ────────
            pnlCenter.Controls.Add(pnlGrids);       // Fill — added first but docked last
            pnlCenter.Controls.Add(pnlReadHeader);  // Top
            pnlCenter.Controls.Add(pnlLower);       // Bottom
            pnlCenter.Controls.Add(pnlLog);         // Bottom (outermost)

            // ══════════════════════════════════════════════════════════════════════
            // Add panels to form (Dock order: Bottom, Left, Right, Fill)
            // ══════════════════════════════════════════════════════════════════════
            this.Controls.Add(pnlCenter);
            this.Controls.Add(pnlRight);
            this.Controls.Add(pnlLeft);
            this.Controls.Add(pnlStatusBar);

            this.ResumeLayout(false);
            this.PerformLayout();
        }

        // ══════════════════════════════════════════════════════════════════════════
        // Grid column setup
        // ══════════════════════════════════════════════════════════════════════════
        private void SetupGapGrid()
        {
            gridGaps.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Start",         Name = "Start",       FillWeight = 25 });
            gridGaps.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "End",           Name = "End",         FillWeight = 25 });
            gridGaps.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Duration",      Name = "Duration",    FillWeight = 20 });
            gridGaps.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Batches",       Name = "Batches",     FillWeight = 15 });
            gridGaps.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Backfillable",  Name = "Backfill",    FillWeight = 15 });
        }

        private void BuildWriteTab()
        {
            lblWriteTag = MakeLabel("Tag (from Primary selector)", 300, 0, 0);

            lblWriteTimestamp  = MakeLabel("Timestamp", 100, 0, lblWriteTag.Bottom + 6);
            dtpWriteTimestamp  = new DateTimePicker
            {
                Left = 0, Top = lblWriteTimestamp.Bottom + 2,
                Width = 200, Format = DateTimePickerFormat.Custom,
                CustomFormat = "yyyy-MM-dd HH:mm:ss",
                ShowUpDown   = true
            };

            lblWriteValue = MakeLabel("Value", 100, 0, dtpWriteTimestamp.Bottom + 6);
            txtWriteValue = MakeTextBox(120, 0, lblWriteValue.Bottom + 2);

            btnWriteData = new FlatButton
            {
                Text = "Write Data",
                Left = txtWriteValue.Right + 8, Top = txtWriteValue.Top,
                Width = 100
            };
            btnWriteData.Click += btnWriteData_Click;

            tabWrite.Controls.AddRange(new Control[]
                { lblWriteTag, lblWriteTimestamp, dtpWriteTimestamp,
                  lblWriteValue, txtWriteValue, btnWriteData });
        }

        private void BuildMultiFieldTab()
        {
            lblTypeName  = MakeLabel("Type name", 120, 0, 0);
            txtTypeName  = MakeTextBox(200, 0, lblTypeName.Bottom + 2);

            gridFieldDefs = new System.Windows.Forms.DataGridView
            {
                Left = 0, Top = txtTypeName.Bottom + 6,
                Width = 340, Height = 70,
                AllowUserToAddRows = false
            };
            gridFieldDefs.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Field Name", Name = "FieldName", FillWeight = 60 });
            gridFieldDefs.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Data Type",  Name = "DataType",  FillWeight = 40 });
            gridFieldDefs.DataSource = new System.Data.DataTable();
            ((System.Data.DataTable)gridFieldDefs.DataSource).Columns.Add("FieldName");
            ((System.Data.DataTable)gridFieldDefs.DataSource).Columns.Add("DataType");

            pnlMFButtons = new Panel
            {
                Left = 0, Top = gridFieldDefs.Bottom + 4,
                Width = 380, Height = AppTheme.ButtonHeight
            };
            btnAddField            = MakeButton("+ Add Field",     120, 0, 0);
            btnRemoveField         = MakeButton("- Remove",         90, 126, 0);
            btnCreateMultiFieldType = MakeButton("Create Type",    110, 222, 0);
            btnWriteMultiField     = MakeButton("Write Sample",    110, 338, 0);
            btnAddField.ButtonStyle = FlatButtonStyle.Secondary;
            btnRemoveField.ButtonStyle = FlatButtonStyle.Danger;
            btnAddField.Click              += btnAddField_Click;
            btnRemoveField.Click           += btnRemoveField_Click;
            btnCreateMultiFieldType.Click  += btnCreateMultiFieldType_Click;
            btnWriteMultiField.Click       += btnWriteMultiField_Click;
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
            Text      = text,
            Left      = left, Top = top,
            Width     = width, Height = 18,
            Font      = AppTheme.Default,
            ForeColor = AppTheme.TextSecondary,
            AutoSize  = false
        };

        private static TextBox MakeTextBox(int width, int left, int top) => new TextBox
        {
            Left      = left, Top = top,
            Width     = width, Height = AppTheme.ControlHeight,
            Font      = AppTheme.Default,
            BorderStyle = BorderStyle.FixedSingle
        };

        private static DateTimePicker MakeDtp(int width, int left, int top) => new DateTimePicker
        {
            Left   = left, Top = top,
            Width  = width, Height = AppTheme.ControlHeight,
            Format = DateTimePickerFormat.Custom,
            CustomFormat = "yyyy-MM-dd HH:mm:ss",
            ShowUpDown   = true
        };

        private static FlatButton MakeButton(string text, int width, int left, int top)
        {
            var btn = new FlatButton { Text = text, Left = left, Top = top, Width = width };
            return btn;
        }

        private static ComboBox MakeCombo(int width, int left, int top) => new ComboBox
        {
            Left          = left, Top = top,
            Width         = width, Height = AppTheme.ControlHeight,
            Font          = AppTheme.Default,
            DropDownStyle = ComboBoxStyle.DropDownList
        };

        private static Button MakeSmallButton(string text, DockStyle dock)
        {
            var btn = new Button
            {
                Text      = text, Dock = dock, Width = 80,
                FlatStyle = FlatStyle.Flat,
                Font      = AppTheme.Small,
                BackColor = AppTheme.NavyLight,
                ForeColor = AppTheme.Navy,
                Cursor    = Cursors.Hand
            };
            btn.FlatAppearance.BorderSize = 0;
            return btn;
        }

        private static FlatButton MakeCenterButton(string text, int top)
        {
            var btn = new FlatButton
            {
                Text   = text, Left = 2, Top = top,
                Width  = 50, Height = 28,
                Font   = AppTheme.SectionLabel
            };
            return btn;
        }
    }
}
