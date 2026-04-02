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
    /// Modal dialog that lets the user pick which tags to backfill.
    /// Shows only tags that exist on BOTH servers (intersection).
    /// </summary>
    public class TagSelectionDialog : Form
    {
        private readonly Label           _lblSummary;
        private readonly CheckedListBox  _tagList;
        private readonly FlatButton      _btnSelectAll;
        private readonly FlatButton      _btnSelectNone;
        private readonly FlatButton      _btnOk;
        private readonly FlatButton      _btnCancel;

        public List<string> SelectedTags { get; private set; } = new List<string>();

        public TagSelectionDialog(
            string sourceLabel,
            string targetLabel,
            int gapCount,
            int backfillableBatches,
            List<string> sharedTags)
        {
            // ── Form ──────────────────────────────────────────────────────────────
            Text            = "Select Tags for Backfill";
            Size            = new Size(420, 520);
            MinimumSize     = new Size(360, 400);
            StartPosition   = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox     = false;
            MinimizeBox     = false;
            BackColor       = AppTheme.Background;
            Font            = AppTheme.Default;

            // ── Summary label ─────────────────────────────────────────────────────
            _lblSummary = new Label
            {
                Text      = $"Backfill: {sourceLabel} \u2192 {targetLabel}\n" +
                            $"{gapCount} gap window(s), {backfillableBatches} backfillable batch(es)\n\n" +
                            $"Select the tags to copy ({sharedTags.Count} tags on both servers):",
                Left      = 16, Top = 12,
                Width     = 370, Height = 72,
                Font      = AppTheme.Default,
                ForeColor = AppTheme.TextPrimary,
                AutoSize  = false
            };

            // ── CheckedListBox ────────────────────────────────────────────────────
            _tagList = new CheckedListBox
            {
                Left            = 16, Top = _lblSummary.Bottom + 4,
                Width           = 370, Height = 310,
                Font            = AppTheme.Default,
                BorderStyle     = BorderStyle.FixedSingle,
                CheckOnClick    = true,
                IntegralHeight  = false,
                Anchor          = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom
            };
            foreach (var tag in sharedTags)
                _tagList.Items.Add(tag, false);

            // ── Select All / None ─────────────────────────────────────────────────
            _btnSelectAll = new FlatButton
            {
                Text        = "Select All",
                ButtonStyle = FlatButtonStyle.Secondary,
                Left        = 16, Top = _tagList.Bottom + 8,
                Width       = 100, Height = 28
            };
            _btnSelectAll.Click += (s, e) =>
            {
                for (int i = 0; i < _tagList.Items.Count; i++)
                    _tagList.SetItemChecked(i, true);
            };

            _btnSelectNone = new FlatButton
            {
                Text        = "Select None",
                ButtonStyle = FlatButtonStyle.Secondary,
                Left        = _btnSelectAll.Right + 8, Top = _tagList.Bottom + 8,
                Width       = 100, Height = 28
            };
            _btnSelectNone.Click += (s, e) =>
            {
                for (int i = 0; i < _tagList.Items.Count; i++)
                    _tagList.SetItemChecked(i, false);
            };

            // ── OK / Cancel ───────────────────────────────────────────────────────
            _btnOk = new FlatButton
            {
                Text   = "Start Backfill",
                Left   = 286 - 100, Top = _tagList.Bottom + 8,
                Width  = 100, Height = 28
            };
            _btnOk.Click += (s, e) =>
            {
                SelectedTags = _tagList.CheckedItems.Cast<string>().ToList();
                if (SelectedTags.Count == 0)
                {
                    MessageBox.Show("Select at least one tag.", "No Tags Selected",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                DialogResult = DialogResult.OK;
                Close();
            };

            _btnCancel = new FlatButton
            {
                Text        = "Cancel",
                ButtonStyle = FlatButtonStyle.Secondary,
                Left        = _btnOk.Right + 8, Top = _tagList.Bottom + 8,
                Width       = 80, Height = 28
            };
            _btnCancel.Click += (s, e) =>
            {
                DialogResult = DialogResult.Cancel;
                Close();
            };

            // ── Layout ────────────────────────────────────────────────────────────
            Controls.AddRange(new Control[]
            {
                _lblSummary, _tagList,
                _btnSelectAll, _btnSelectNone,
                _btnOk, _btnCancel
            });

            AcceptButton = _btnOk;
            CancelButton = _btnCancel;
        }
    }
}
