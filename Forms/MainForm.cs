using HistorianSyncTool.Services;
using System;
using System.Windows.Forms;

namespace HistorianSyncTool.Forms
{
    public partial class MainForm : Form
    {
        private readonly HistorianConnectionService _connections = new HistorianConnectionService();
        private readonly HistorianDataService _data = new HistorianDataService();
        private readonly GapAnalysisService _gapAnalysis = new GapAnalysisService();

        public MainForm()
        {
            InitializeComponent();
            Text = "Historian Sync Tool v2";
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _connections.Dispose();
            base.OnFormClosing(e);
        }
    }
}
