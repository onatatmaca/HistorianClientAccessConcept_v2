using HistorianSyncTool.Models;
using HistorianSyncTool.UI;
using HistorianSyncTool.UI.Controls;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace HistorianSyncTool.Forms
{
    /// <summary>
    /// Modal dialog showing a detailed per-tag sync report after a backfill completes.
    /// Accepts one report (single direction) or several (e.g. a bidirectional run shown
    /// in one window). When more than one report is present, a Direction column is added
    /// and the header/totals are aggregated. Exports to CSV (spreadsheet) or TXT.
    /// </summary>
    public class SyncReportDialog : Form
    {
        private readonly List<SyncRunReport> _reports;
        private readonly bool _multi;
        private readonly bool _samplesOnly;
        private readonly DataGridView _grid;

        /// <summary>Single-direction report (used by the per-direction Copy buttons).</summary>
        public SyncReportDialog(SyncRunReport report)
            : this(new List<SyncRunReport> { report ?? new SyncRunReport() }) { }

        /// <summary>
        /// One or more reports rendered together (combined bidirectional run). When
        /// <paramref name="samplesOnly"/> is set (reports reconstructed from the backfill
        /// journal for the history view), the batch/error columns are hidden — the journal
        /// only records which samples were written, not the original batch/verify detail.
        /// </summary>
        public SyncReportDialog(IList<SyncRunReport> reports, bool samplesOnly = false)
        {
            _reports = (reports != null && reports.Count > 0)
                ? new List<SyncRunReport>(reports)
                : new List<SyncRunReport> { new SyncRunReport() };
            _multi = _reports.Count > 1;
            _samplesOnly = samplesOnly;

            // ── Aggregates across all reports ─────────────────────────────────────
            int totalAttempted = _reports.Sum(r => r.BatchesAttempted);
            int totalSucceeded = _reports.Sum(r => r.BatchesSucceeded);
            int totalFailed    = _reports.Sum(r => r.BatchesFailed);
            int totalSamples   = _reports.Sum(r => r.SamplesWritten);
            int gapsFound      = _reports.Sum(r => r.GapsFound);
            int tagCount       = _reports.Sum(r => r.TagResults.Count);
            // Wall-clock timing across all reports (directions run sequentially, so
            // min-start → max-completed is the honest "how long did this take").
            DateTime started   = _reports.Min(r => r.StartedAt);
            DateTime completed = _reports.Max(r => r.CompletedAt);
            TimeSpan wallClock = completed > started ? completed - started : TimeSpan.Zero;

            Text            = _multi
                ? Loc.T("rep.title") + " — " + Loc.T("rep.bothDirections")
                : Loc.T("rep.title");
            Size            = new Size(940, 560);
            MinimumSize     = new Size(720, 420);
            StartPosition   = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox     = true;
            MinimizeBox     = false;
            BackColor       = AppTheme.Background;
            Font            = AppTheme.Default;

            // ── Header ────────────────────────────────────────────────────────────
            string route = string.Join("   +   ", _reports.Select(r =>
                Services.ServerNaming.RoleTitle(r.SourceServer) + " → " +
                Services.ServerNaming.Role(r.TargetServer)));

            string titleExtra = !_samplesOnly && totalAttempted > 0
                ? "   ·   " + Loc.F("rep.batchesOk", totalSucceeded, totalAttempted)
                : "";
            var lblTitle = new Label
            {
                Text = $"{route}{titleExtra}",
                Dock = DockStyle.Top, Height = 34,
                Padding = new Padding(16, 10, 16, 2),
                Font = AppTheme.AppTitle, ForeColor = AppTheme.Navy
            };

            // Explicit run timing — always visible, mirrored in Backfill History.
            var lblTiming = new Label
            {
                Text = wallClock > TimeSpan.Zero
                    ? Loc.F("rep.timing", started.ToString("yyyy-MM-dd HH:mm:ss"),
                            completed.ToString("yyyy-MM-dd HH:mm:ss"), FormatDuration(wallClock))
                    : Loc.F("rep.ran", started.ToString("yyyy-MM-dd HH:mm:ss")),
                Dock = DockStyle.Top, Height = 20,
                Padding = new Padding(16, 0, 16, 2),
                Font = AppTheme.Default, ForeColor = AppTheme.TextSecondary
            };

            var lblTotals = new Label
            {
                Text = _samplesOnly
                    ? Loc.F("rep.totalsShort", totalSamples.ToString("N0"), tagCount)
                    : Loc.F("rep.totals", totalSamples.ToString("N0"), tagCount,
                            totalFailed, gapsFound),
                Dock = DockStyle.Top, Height = 22,
                Padding = new Padding(16, 0, 16, 8),
                Font = AppTheme.Default, ForeColor = AppTheme.TextPrimary
            };
            if (!_samplesOnly && totalFailed > 0)
                lblTotals.ForeColor = AppTheme.Danger;
            else if (!_samplesOnly && totalAttempted > 0 && totalSucceeded == totalAttempted)
                lblTotals.ForeColor = AppTheme.Success;

            // ── Grid ──────────────────────────────────────────────────────────────
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

            if (_multi)
                _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Dir", Name = "Dir", FillWeight = 8, DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleCenter } });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = Loc.T("rep.col.point"), Name = "Tag", FillWeight = _samplesOnly ? (_multi ? 50 : 62) : (_multi ? 26 : 30) });
            if (!_samplesOnly)
            {
                _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Attempted", Name = "Attempted", FillWeight = 11, DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight } });
                _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Succeeded", Name = "Succeeded", FillWeight = 11, DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight } });
                _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Failed",    Name = "Failed",    FillWeight = 9,  DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight } });
            }
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = Loc.T("rep.col.readings"), Name = "Samples", FillWeight = _samplesOnly ? 30 : 14, DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight } });
            if (!_samplesOnly)
                _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = Loc.T("rep.col.errors"), Name = "Errors", FillWeight = 25 });

            foreach (var rep in _reports)
            {
                foreach (var tr in rep.TagResults)
                {
                    var cells = new List<object>();
                    if (_multi) cells.Add(rep.DirectionLabel ?? "");
                    cells.Add(tr.TagName);
                    if (!_samplesOnly)
                    {
                        cells.Add(tr.BatchesAttempted.ToString("N0"));
                        cells.Add(tr.BatchesSucceeded.ToString("N0"));
                        cells.Add(tr.BatchesFailed.ToString("N0"));
                    }
                    cells.Add(tr.SamplesWritten.ToString("N0"));
                    if (!_samplesOnly)
                        cells.Add(tr.Errors.Count == 0 ? "—" : $"{tr.Errors.Count} · {tr.Errors[0]}");

                    int rowIdx = _grid.Rows.Add(cells.ToArray());
                    var row = _grid.Rows[rowIdx];
                    if (!_samplesOnly)
                    {
                        if (tr.BatchesFailed > 0 && tr.BatchesSucceeded == 0)
                            row.DefaultCellStyle.BackColor = Color.FromArgb(255, 228, 225); // pale red
                        else if (tr.Errors.Count > 0)
                            row.DefaultCellStyle.BackColor = Color.FromArgb(255, 251, 224); // pale yellow
                        if (tr.Errors.Count > 0)
                            row.Cells["Errors"].ToolTipText = string.Join("\n", tr.Errors);
                    }
                }
            }

            // ── Buttons ───────────────────────────────────────────────────────────
            var pnlButtons = new Panel
            {
                Dock = DockStyle.Bottom, Height = 52,
                Padding = new Padding(16, 10, 16, 10), BackColor = AppTheme.Surface
            };
            pnlButtons.Paint += (s, e) =>
                e.Graphics.DrawLine(new Pen(AppTheme.Border), 0, 0, pnlButtons.Width, 0);

            var btnClose = new FlatButton
            {
                Text = Loc.T("dlg.close"), ButtonStyle = FlatButtonStyle.Secondary,
                Dock = DockStyle.Right, Width = 90
            };
            btnClose.Click += (s, e) => { DialogResult = DialogResult.OK; Close(); };

            var btnExportTxt = new FlatButton
            {
                Text = Loc.T("rep.exportTxt"), ButtonStyle = FlatButtonStyle.Secondary,
                Dock = DockStyle.Right, Width = 110
            };
            btnExportTxt.Click += (s, e) => ExportTxt();

            var btnExportCsv = new FlatButton
            {
                Text = Loc.T("rep.exportCsv"), ButtonStyle = FlatButtonStyle.Secondary,
                Dock = DockStyle.Right, Width = 110
            };
            btnExportCsv.Click += (s, e) => ExportCsv();

            pnlButtons.Controls.Add(btnClose);
            pnlButtons.Controls.Add(btnExportTxt);
            pnlButtons.Controls.Add(btnExportCsv);

            Controls.Add(_grid);         // Fill
            Controls.Add(lblTotals);     // Top
            Controls.Add(lblTiming);     // Top (between title and totals)
            Controls.Add(lblTitle);      // Top
            Controls.Add(pnlButtons);    // Bottom

            AcceptButton = btnClose;
            CancelButton = btnClose;
        }

        private void ExportCsv()
        {
            using (var dlg = new SaveFileDialog
            {
                Filter = "CSV files (*.csv)|*.csv",
                FileName = $"sync_report_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    using (var sw = new StreamWriter(dlg.FileName, false, Encoding.UTF8))
                    {
                        string route = string.Join(" + ", _reports.Select(r => $"{r.SourceServer}→{r.TargetServer}"));
                        sw.WriteLine($"Route;{route}");
                        sw.WriteLine($"Started;{_reports.Min(r => r.StartedAt):yyyy-MM-dd HH:mm:ss}");
                        sw.WriteLine($"Completed;{_reports.Max(r => r.CompletedAt):yyyy-MM-dd HH:mm:ss}");
                        sw.WriteLine($"Duration (s);{(_reports.Max(r => r.CompletedAt) - _reports.Min(r => r.StartedAt)).TotalSeconds:F1}");
                        sw.WriteLine($"Gaps found;{_reports.Sum(r => r.GapsFound)}");
                        sw.WriteLine($"Batches attempted;{_reports.Sum(r => r.BatchesAttempted)}");
                        sw.WriteLine($"Batches succeeded;{_reports.Sum(r => r.BatchesSucceeded)}");
                        sw.WriteLine($"Batches failed;{_reports.Sum(r => r.BatchesFailed)}");
                        sw.WriteLine($"Samples written;{_reports.Sum(r => r.SamplesWritten)}");
                        sw.WriteLine();
                        sw.WriteLine("Direction;Tag;Attempted;Succeeded;Failed;Samples;Errors");
                        foreach (var rep in _reports)
                        {
                            foreach (var tr in rep.TagResults)
                            {
                                string errs = tr.Errors.Count == 0 ? ""
                                    : "\"" + string.Join(" | ", tr.Errors).Replace("\"", "\"\"") + "\"";
                                sw.WriteLine($"{EscapeCsv(rep.DirectionLabel)};{EscapeCsv(tr.TagName)};{tr.BatchesAttempted};" +
                                             $"{tr.BatchesSucceeded};{tr.BatchesFailed};{tr.SamplesWritten};{errs}");
                            }
                        }
                    }
                    MessageBox.Show(this, "CSV exported.", "Export complete",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, $"Export failed: {ex.Message}", "Error",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void ExportTxt()
        {
            using (var dlg = new SaveFileDialog
            {
                Filter = "Text files (*.txt)|*.txt",
                FileName = $"sync_report_{DateTime.Now:yyyyMMdd_HHmmss}.txt"
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    using (var sw = new StreamWriter(dlg.FileName, false, Encoding.UTF8))
                    {
                        string route = string.Join(" + ", _reports.Select(r => $"{r.SourceServer}→{r.TargetServer}"));
                        sw.WriteLine("─── Sync Run Report ──────────────────────────────────────");
                        sw.WriteLine($"  {route}");
                        sw.WriteLine($"  Started:   {_reports.Min(r => r.StartedAt):yyyy-MM-dd HH:mm:ss}");
                        sw.WriteLine($"  Completed: {_reports.Max(r => r.CompletedAt):yyyy-MM-dd HH:mm:ss}");
                        sw.WriteLine($"  Duration:  {FormatDuration(_reports.Max(r => r.CompletedAt) - _reports.Min(r => r.StartedAt))}");
                        sw.WriteLine($"  Gaps:      {_reports.Sum(r => r.GapsFound)}");
                        sw.WriteLine($"  Batches:   {_reports.Sum(r => r.BatchesSucceeded)}/{_reports.Sum(r => r.BatchesAttempted)} succeeded" +
                                     (_reports.Sum(r => r.BatchesFailed) > 0 ? $", {_reports.Sum(r => r.BatchesFailed)} failed" : ""));
                        sw.WriteLine($"  Samples:   {_reports.Sum(r => r.SamplesWritten):N0}");
                        sw.WriteLine("");
                        sw.WriteLine("  {0,-6} {1,-36} {2,10} {3,10} {4,8} {5,12}",
                            "Dir", "Tag", "Attempted", "Succeeded", "Failed", "Samples");
                        sw.WriteLine("  " + new string('─', 86));
                        foreach (var rep in _reports)
                        {
                            foreach (var tr in rep.TagResults)
                            {
                                sw.WriteLine("  {0,-6} {1,-36} {2,10:N0} {3,10:N0} {4,8:N0} {5,12:N0}",
                                    rep.DirectionLabel ?? "",
                                    Truncate(tr.TagName, 36),
                                    tr.BatchesAttempted, tr.BatchesSucceeded,
                                    tr.BatchesFailed, tr.SamplesWritten);
                                foreach (var err in tr.Errors)
                                    sw.WriteLine($"      !  {err}");
                            }
                            foreach (var err in rep.Errors)
                                sw.WriteLine($"      !  ({rep.DirectionLabel}) {err}");
                        }
                        sw.WriteLine("──────────────────────────────────────────────────────────");
                    }
                    MessageBox.Show(this, "TXT exported.", "Export complete",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, $"Export failed: {ex.Message}", "Error",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private static string EscapeCsv(string s) =>
            string.IsNullOrEmpty(s) ? ""
            : (s.Contains(";") || s.Contains("\"") || s.Contains("\n")
                ? "\"" + s.Replace("\"", "\"\"") + "\""
                : s);

        private static string Truncate(string s, int len) =>
            string.IsNullOrEmpty(s) ? "" : (s.Length <= len ? s : s.Substring(0, len - 1) + "…");

        /// <summary>Human duration: "2m 31s", "1h 04m 09s", "8.3s".</summary>
        internal static string FormatDuration(TimeSpan ts)
        {
            if (ts < TimeSpan.Zero) ts = TimeSpan.Zero;
            if (ts.TotalHours >= 1)   return $"{(int)ts.TotalHours}h {ts.Minutes:00}m {ts.Seconds:00}s";
            if (ts.TotalMinutes >= 1) return $"{(int)ts.TotalMinutes}m {ts.Seconds:00}s";
            return $"{ts.TotalSeconds:F1}s";
        }
    }
}
