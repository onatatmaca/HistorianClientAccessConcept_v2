using HistorianSyncTool.Models;
using HistorianSyncTool.UI;
using HistorianSyncTool.UI.Controls;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace HistorianSyncTool.Forms
{
    /// <summary>
    /// Lists past backfill runs from the journal and lets the user revert one — a revert
    /// deletes exactly the samples that run wrote. Opening this dialog is harmless; the
    /// revert itself is double-guarded: the danger button stays disabled until an explicit
    /// "enable" checkbox is ticked, and a final confirmation dialog must be accepted.
    /// </summary>
    public class BackfillHistoryDialog : Form
    {
        private readonly List<BackfillJournalEntry> _entries;
        private readonly DataGridView _grid;
        private readonly CheckBox     _chkArm;
        private readonly FlatButton   _btnRevert;
        private readonly FlatButton   _btnView;

        /// <summary>Set to the Id of the run the user confirmed for revert; null otherwise.</summary>
        public string RevertEntryId { get; private set; }

        public BackfillHistoryDialog(List<BackfillJournalEntry> entries)
        {
            _entries = entries ?? new List<BackfillJournalEntry>();

            Text            = Loc.T("hist.title");
            Size            = new Size(860, 520);
            MinimumSize     = new Size(680, 380);
            StartPosition   = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox     = true;
            MinimizeBox     = false;
            BackColor       = AppTheme.Background;
            Font            = AppTheme.Default;

            var lblHeader = new Label
            {
                Text = "Past backfill runs. Select a run and revert it to delete exactly the " +
                       "samples it wrote — pre-existing data is never touched.",
                Dock = DockStyle.Top, Height = 40,
                Padding = new Padding(16, 12, 16, 4),
                Font = AppTheme.Default, ForeColor = AppTheme.TextPrimary
            };

            _grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                RowHeadersVisible = false,
                ReadOnly = true,
                BackgroundColor = AppTheme.Surface,
                BorderStyle = BorderStyle.FixedSingle,
                CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
                GridColor = AppTheme.Border,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                EnableHeadersVisualStyles = false,
                ColumnHeadersHeight = 28
            };
            _grid.ColumnHeadersDefaultCellStyle.BackColor = AppTheme.Navy;
            _grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
            _grid.ColumnHeadersDefaultCellStyle.Font      = AppTheme.SectionLabel;
            _grid.AlternatingRowsDefaultCellStyle.BackColor = AppTheme.RowAlt;

            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = Loc.T("hist.col.run"),      Name = "Run",     FillWeight = 19 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = Loc.T("hist.col.duration"), Name = "Dur",     FillWeight = 10, DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight } });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = Loc.T("hist.col.mode"),     Name = "Mode",    FillWeight = 10 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = Loc.T("hist.col.direction"),Name = "Dir2",    FillWeight = 23 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = Loc.T("hist.col.points"),   Name = "Tags",    FillWeight = 9,  DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight } });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = Loc.T("hist.col.readings"), Name = "Samples", FillWeight = 12, DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight } });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = Loc.T("hist.col.status"),   Name = "Status",  FillWeight = 17 });

            foreach (var e in _entries)
            {
                // Older journal files predate CompletedLocal — show "—" for those runs.
                string duration = e.CompletedLocal.HasValue && e.CompletedLocal.Value >= e.RunLocal
                    ? SyncReportDialog.FormatDuration(e.CompletedLocal.Value - e.RunLocal)
                    : "—";
                int idx = _grid.Rows.Add(
                    e.RunLocal.ToString("yyyy-MM-dd HH:mm:ss"),
                    duration,
                    e.Mode,
                    $"{e.SourceLabel} → {e.TargetLabel}",
                    e.TagCount.ToString("N0"),
                    e.TotalSamples.ToString("N0"),
                    e.Reverted ? $"Reverted {e.RevertedLocal:MM-dd HH:mm}" : "Active");
                _grid.Rows[idx].Tag = e;
                if (e.Reverted)
                    _grid.Rows[idx].DefaultCellStyle.ForeColor = AppTheme.TextSecondary;
            }
            _grid.SelectionChanged += (s, ev) => UpdateRevertEnabled();

            // ── Bottom bar: arm checkbox + revert + close ─────────────────────────
            var pnlButtons = new Panel
            {
                Dock = DockStyle.Bottom, Height = 92,
                Padding = new Padding(16, 10, 16, 10), BackColor = AppTheme.Surface
            };
            pnlButtons.Paint += (s, ev) =>
                ev.Graphics.DrawLine(new Pen(AppTheme.Border), 0, 0, pnlButtons.Width, 0);

            _chkArm = new CheckBox
            {
                Text = Loc.T("hist.enable"),
                Location = new Point(16, 10),
                Size = new Size(620, 22),
                ForeColor = AppTheme.Danger,
                Font = AppTheme.Bold
            };
            _chkArm.CheckedChanged += (s, ev) => UpdateRevertEnabled();

            _btnRevert = new FlatButton
            {
                Text = Loc.T("hist.revert"),
                ButtonStyle = FlatButtonStyle.Danger,
                Left = 16, Top = 46, Width = 200, Height = 32,
                Enabled = false
            };
            _btnRevert.Click += BtnRevert_Click;

            _btnView = new FlatButton
            {
                Text = Loc.T("hist.viewReport"),
                ButtonStyle = FlatButtonStyle.Secondary,
                Left = 224, Top = 46, Width = 160, Height = 32
            };
            _btnView.Click += BtnView_Click;

            var btnClose = new FlatButton
            {
                Text = Loc.T("dlg.close"), ButtonStyle = FlatButtonStyle.Secondary,
                Top = 46, Width = 100, Height = 32,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Left = pnlButtons.Width - 116
            };
            btnClose.Click += (s, ev) => { DialogResult = DialogResult.Cancel; Close(); };

            pnlButtons.Controls.Add(_chkArm);
            pnlButtons.Controls.Add(_btnRevert);
            pnlButtons.Controls.Add(_btnView);
            pnlButtons.Controls.Add(btnClose);

            var lblEmpty = new Label
            {
                Text = Loc.T("hist.empty"),
                Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = AppTheme.TextSecondary, Visible = _entries.Count == 0
            };

            Controls.Add(_grid);        // Fill
            if (_entries.Count == 0) { _grid.Visible = false; Controls.Add(lblEmpty); }
            Controls.Add(lblHeader);    // Top
            Controls.Add(pnlButtons);   // Bottom

            CancelButton = btnClose;
            if (_grid.Rows.Count > 0) _grid.Rows[0].Selected = true;
            UpdateRevertEnabled();
        }

        private BackfillJournalEntry Selected =>
            _grid.SelectedRows.Count > 0 ? _grid.SelectedRows[0].Tag as BackfillJournalEntry : null;

        private void UpdateRevertEnabled()
        {
            var sel = Selected;
            _btnRevert.Enabled = _chkArm.Checked && sel != null && !sel.Reverted;
            if (_btnView != null) _btnView.Enabled = sel != null;
        }

        private void BtnRevert_Click(object sender, EventArgs e)
        {
            var sel = Selected;
            if (sel == null || sel.Reverted) return;

            // ServerNaming, not the raw label: "Secondary" is an internal storage value that
            // appears nowhere else on screen, and this is the one dialog where the user must be
            // certain WHICH server they are about to delete from.
            var confirm = MessageBox.Show(this,
                Loc.F("undo.confirm.body",
                    sel.TotalSamples.ToString("N0"), sel.TagCount,
                    Services.ServerNaming.Short(sel.TargetLabel, sel.TargetHost),
                    sel.RunLocal.ToString("yyyy-MM-dd HH:mm:ss")),
                Loc.T("undo.confirm.title"),
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);

            if (confirm != DialogResult.Yes) return;

            RevertEntryId = sel.Id;
            DialogResult = DialogResult.OK;
            Close();
        }

        private void BtnView_Click(object sender, EventArgs e)
        {
            var sel = Selected;
            if (sel == null) return;

            // Entries since Phase 10 carry the FULL report of their run (both directions
            // for a bidirectional run) — reopen the exact results dialog the user saw,
            // including batches, errors and Started/Finished/Duration.
            if (sel.ReportDirections != null && sel.ReportDirections.Count > 0)
            {
                var reports = sel.ReportDirections.Select(d => d.ToReport()).ToList();
                using (var dlg = new SyncReportDialog(reports, samplesOnly: false))
                    dlg.ShowDialog(this);
                return;
            }

            // Older entries: reconstruct what we can from the journaled samples.
            using (var dlg = new SyncReportDialog(
                new List<SyncRunReport> { BuildReportFromEntry(sel) }, samplesOnly: true))
                dlg.ShowDialog(this);
        }

        /// <summary>
        /// Rebuilds a per-tag report from a journal entry for the history view. The journal
        /// records the exact samples written per tag, so the sample counts are accurate; the
        /// original batch/verify/error detail isn't stored, so the report is shown samples-only.
        /// </summary>
        private static SyncRunReport BuildReportFromEntry(BackfillJournalEntry e)
        {
            var r = new SyncRunReport
            {
                StartedAt      = e.RunLocal,
                CompletedAt    = e.CompletedLocal ?? e.RunLocal, // old entries: no end time
                SourceServer   = e.SourceLabel,
                TargetServer   = e.TargetLabel,
                DirectionLabel = $"{First1(e.SourceLabel)}→{First1(e.TargetLabel)}"
            };
            if (e.Tags != null)
                foreach (var t in e.Tags)
                    r.TagResults.Add(new TagBackfillResult
                    {
                        TagName        = t.TagName,
                        SamplesWritten = t.Ticks?.Length ?? 0
                    });
            return r;
        }

        private static string First1(string s) =>
            string.IsNullOrEmpty(s) ? "?" : s.Substring(0, 1);
    }
}
