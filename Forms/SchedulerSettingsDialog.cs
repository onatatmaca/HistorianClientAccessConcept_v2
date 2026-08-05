using HistorianSyncTool.Properties;
using HistorianSyncTool.UI;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace HistorianSyncTool.Forms
{
    /// <summary>
    /// Modal settings dialog for unattended scheduled backfills. Reads from and writes
    /// directly back to <see cref="Settings.Default"/>. The owning form re-applies the
    /// settings to <see cref="Services.ScheduleService"/> after DialogResult.OK.
    /// Tags can be chosen either by a filter mask or by explicit multiselect.
    /// </summary>
    public class SchedulerSettingsDialog : Form
    {
        private CheckBox        _chkEnabled;
        private NumericUpDown   _numInterval;
        private NumericUpDown   _numWindow;
        private ComboBox        _cboDirection;
        private RadioButton     _rdoMask;
        private RadioButton     _rdoList;
        private TextBox         _txtTagFilter;
        private CheckedListBox  _clbTags;
        private CheckBox        _chkRunOnStartup;
        private Label           _lblLastRun;
        private Label           _lblNextRun;
        private Button          _btnOk;
        private Button          _btnCancel;
        private Button          _btnRunNow;

        private readonly List<string> _availableTags;

        public bool RunNowRequested { get; private set; }

        public SchedulerSettingsDialog(DateTime nextRunLocal, DateTime lastRunUtc,
                                       IEnumerable<string> availableTags = null)
        {
            _availableTags = (availableTags ?? Enumerable.Empty<string>())
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Distinct()
                .OrderBy(t => t)
                .ToList();

            Text            = Loc.T("sch.title");
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition   = FormStartPosition.CenterParent;
            MaximizeBox     = false;
            MinimizeBox     = false;
            ShowInTaskbar   = false;
            BackColor       = AppTheme.Surface;
            Font            = AppTheme.Default;
            ClientSize      = new Size(470, 600);

            BuildUi(nextRunLocal, lastRunUtc);
            LoadFromSettings();
            UpdateEnabledControls();
        }

        private void BuildUi(DateTime nextRunLocal, DateTime lastRunUtc)
        {
            const int leftLabel  = 16;
            const int leftField  = 170;
            const int fieldWidth = 270;
            int y = 16;

            _chkEnabled = new CheckBox
            {
                Text     = Loc.T("sch.enable"),
                Location = new Point(leftLabel, y),
                Size     = new Size(400, 22),
                Font     = AppTheme.Bold
            };
            _chkEnabled.CheckedChanged += (s, e) => UpdateEnabledControls();
            Controls.Add(_chkEnabled);
            y += 36;

            AddLabel(Loc.T("sch.every"), leftLabel, y);
            _numInterval = new NumericUpDown
            {
                Location = new Point(leftField, y - 2), Width = 100,
                Minimum = 1, Maximum = 7 * 24 * 60, Increment = 5, Value = 60
            };
            Controls.Add(_numInterval);
            y += 32;

            AddLabel(Loc.T("sch.window"), leftLabel, y);
            _numWindow = new NumericUpDown
            {
                Location = new Point(leftField, y - 2), Width = 100,
                Minimum = 1, Maximum = 24 * 365, Increment = 1, Value = 24
            };
            Controls.Add(_numWindow);
            y += 32;

            AddLabel(Loc.T("sch.direction"), leftLabel, y);
            _cboDirection = new ComboBox
            {
                Location = new Point(leftField, y - 2), Width = fieldWidth,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            _cboDirection.Items.AddRange(new object[]
            {
                Loc.T("sch.dirToMirror"),
                Loc.T("sch.dirToMain"),
                Loc.T("sch.dirBoth")
            });
            _cboDirection.SelectedIndex = 2;
            Controls.Add(_cboDirection);
            y += 38;

            // ── Tag selection: mask OR explicit multiselect ──────────────────────
            var hdrTags = new Label
            {
                Text = Loc.T("sch.which"), Location = new Point(leftLabel, y),
                Size = new Size(400, 18), Font = AppTheme.Bold, ForeColor = AppTheme.Navy
            };
            Controls.Add(hdrTags);
            y += 24;

            _rdoMask = new RadioButton
            {
                Text = Loc.T("sch.byFilter"), Location = new Point(leftLabel, y),
                Size = new Size(150, 22), Checked = true
            };
            _rdoMask.CheckedChanged += (s, e) => UpdateEnabledControls();
            Controls.Add(_rdoMask);
            _txtTagFilter = new TextBox { Location = new Point(leftField, y), Width = fieldWidth };
            Controls.Add(_txtTagFilter);
            y += 22;
            Controls.Add(new Label
            {
                Text = Loc.T("sch.filterHint"),
                Location = new Point(leftField, y), Size = new Size(fieldWidth, 16),
                Font = AppTheme.Small, ForeColor = AppTheme.TextSecondary
            });
            y += 24;

            _rdoList = new RadioButton
            {
                Text = Loc.T("sch.byList"), Location = new Point(leftLabel, y),
                Size = new Size(400, 22)
            };
            _rdoList.CheckedChanged += (s, e) => UpdateEnabledControls();
            Controls.Add(_rdoList);
            y += 26;

            _clbTags = new CheckedListBox
            {
                Location = new Point(leftLabel, y),
                Size = new Size(ClientSize.Width - 32, 120),
                CheckOnClick = true,
                IntegralHeight = false,
                BorderStyle = BorderStyle.FixedSingle
            };
            _clbTags.Items.AddRange(_availableTags.Cast<object>().ToArray());
            Controls.Add(_clbTags);
            y += 124;

            if (_availableTags.Count == 0)
            {
                Controls.Add(new Label
                {
                    Text = Loc.T("sch.noTags"),
                    Location = new Point(leftLabel, y), Size = new Size(ClientSize.Width - 32, 28),
                    Font = AppTheme.Small, ForeColor = AppTheme.TextSecondary
                });
            }
            y += 30;

            _chkRunOnStartup = new CheckBox
            {
                Text = Loc.T("sch.runOnStartup"),
                Location = new Point(leftLabel, y), Size = new Size(400, 22)
            };
            Controls.Add(_chkRunOnStartup);
            y += 30;

            var pnlInfo = new Panel
            {
                Location = new Point(leftLabel, y), Size = new Size(ClientSize.Width - 32, 52),
                BackColor = AppTheme.NavyLight
            };
            _lblLastRun = new Label
            {
                Text = lastRunUtc == DateTime.MinValue
                            ? Loc.T("sch.lastNever")
                            : Loc.F("sch.last", lastRunUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")),
                Location = new Point(8, 5), Size = new Size(pnlInfo.Width - 16, 18),
                ForeColor = AppTheme.Navy
            };
            _lblNextRun = new Label
            {
                Text = nextRunLocal == DateTime.MaxValue
                            ? Loc.T("sch.nextOff")
                            : Loc.F("sch.next", nextRunLocal.ToString("yyyy-MM-dd HH:mm:ss")),
                Location = new Point(8, 26), Size = new Size(pnlInfo.Width - 16, 18),
                ForeColor = AppTheme.Navy
            };
            pnlInfo.Controls.Add(_lblLastRun);
            pnlInfo.Controls.Add(_lblNextRun);
            Controls.Add(pnlInfo);

            // ── Bottom button row ─────────────────────────────────────────────────
            _btnRunNow = MakeButton(Loc.T("sch.runNow"), 110, AppTheme.Teal, Color.White);
            _btnRunNow.Location = new Point(16, ClientSize.Height - 44);
            _btnRunNow.Click += (s, e) =>
            {
                if (!SaveToSettings()) return;
                RunNowRequested = true;
                DialogResult = DialogResult.OK;
                Close();
            };
            Controls.Add(_btnRunNow);

            _btnOk = MakeButton(Loc.T("sch.save"), 90, AppTheme.Navy, Color.White);
            _btnOk.Location = new Point(ClientSize.Width - 180, ClientSize.Height - 44);
            _btnOk.Click += (s, e) =>
            {
                if (!SaveToSettings()) return;
                DialogResult = DialogResult.OK;
                Close();
            };
            Controls.Add(_btnOk);

            _btnCancel = MakeButton(Loc.T("dlg.cancel"), 90, AppTheme.Surface, AppTheme.TextPrimary);
            _btnCancel.FlatAppearance.BorderColor = AppTheme.Border;
            _btnCancel.Location = new Point(ClientSize.Width - 92, ClientSize.Height - 44);
            _btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(_btnCancel);

            AcceptButton = _btnOk;
            CancelButton = _btnCancel;
        }

        private static Button MakeButton(string text, int width, Color back, Color fore)
        {
            var b = new Button
            {
                Text = text, Size = new Size(width, 30),
                FlatStyle = FlatStyle.Flat, BackColor = back, ForeColor = fore,
                Font = AppTheme.SectionLabel, Cursor = Cursors.Hand
            };
            b.FlatAppearance.BorderSize = 0;
            return b;
        }

        private void AddLabel(string text, int x, int y)
        {
            Controls.Add(new Label
            {
                Text = text, Location = new Point(x, y), Size = new Size(150, 20),
                Font = AppTheme.Default
            });
        }

        private void LoadFromSettings()
        {
            var s = Settings.Default;
            _chkEnabled.Checked      = s.ScheduleEnabled;
            _numInterval.Value       = Math.Max(1, Math.Min(_numInterval.Maximum, s.ScheduleIntervalMinutes));
            _numWindow.Value         = Math.Max(1, Math.Min(_numWindow.Maximum,   s.ScheduleEvalWindowHours));
            _txtTagFilter.Text       = string.IsNullOrWhiteSpace(s.ScheduleTagFilter) ? "*" : s.ScheduleTagFilter;
            _chkRunOnStartup.Checked = s.ScheduleRunOnStartup;

            switch (s.ScheduleDirection)
            {
                case "PrimaryToSecondary": _cboDirection.SelectedIndex = 0; break;
                case "SecondaryToPrimary": _cboDirection.SelectedIndex = 1; break;
                default:                   _cboDirection.SelectedIndex = 2; break;
            }

            // Pre-check any saved tags that still exist in the available list.
            var saved = new HashSet<string>(
                (s.ScheduleTagList ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(t => t.Trim()),
                StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < _clbTags.Items.Count; i++)
                if (saved.Contains(_clbTags.Items[i].ToString()))
                    _clbTags.SetItemChecked(i, true);

            _rdoList.Checked = s.ScheduleUseTagList;
            _rdoMask.Checked = !s.ScheduleUseTagList;
        }

        /// <summary>
        /// Returns false if the dialog must stay open (the settings were not saved).
        ///
        /// The tag list needs care: an unattended run WRITES to a production historian, so its
        /// scope must never widen without the user asking. Two ways that used to happen
        /// silently, both fixed here — see the comments inline.
        /// </summary>
        private bool SaveToSettings()
        {
            var s = Settings.Default;
            s.ScheduleEnabled         = _chkEnabled.Checked;
            s.ScheduleIntervalMinutes = (int)_numInterval.Value;
            s.ScheduleEvalWindowHours = (int)_numWindow.Value;
            s.ScheduleTagFilter       = string.IsNullOrWhiteSpace(_txtTagFilter.Text)
                                            ? "*" : _txtTagFilter.Text.Trim();
            s.ScheduleRunOnStartup    = _chkRunOnStartup.Checked;
            s.ScheduleDirection =
                _cboDirection.SelectedIndex == 0 ? "PrimaryToSecondary" :
                _cboDirection.SelectedIndex == 1 ? "SecondaryToPrimary" : "Both";

            // Only rewrite the list when there was actually a list to choose from. The box is
            // filled from the tags the main form last browsed, so it is EMPTY whenever nothing
            // has been browsed yet (e.g. the mirror was down at startup). Rewriting from it
            // then wiped a carefully chosen "only these 3 points" to "", while
            // ScheduleUseTagList stayed true — and the scheduler falls back to the `*` mask
            // when the list is blank. Changing the interval could therefore silently widen the
            // next unattended run to every shared point.
            if (_clbTags.Items.Count > 0)
                s.ScheduleTagList = string.Join(";", _clbTags.CheckedItems.Cast<object>().Select(o => o.ToString()));

            // Same fallback, reached deliberately: "specific points" chosen with none ticked
            // is not a request to write to everything. Make the user resolve it.
            bool listEmpty = string.IsNullOrWhiteSpace(s.ScheduleTagList);
            if (_chkEnabled.Checked && _rdoList.Checked && listEmpty)
            {
                MessageBox.Show(this, Loc.T("sched.needPoints.body"), Loc.T("sched.needPoints.title"),
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            s.ScheduleUseTagList = _rdoList.Checked;
            s.Save();
            return true;
        }

        private void UpdateEnabledControls()
        {
            bool on = _chkEnabled.Checked;
            _numInterval.Enabled     = on;
            _numWindow.Enabled       = on;
            _cboDirection.Enabled    = on;
            _rdoMask.Enabled         = on;
            _rdoList.Enabled         = on;
            _txtTagFilter.Enabled    = on && _rdoMask.Checked;
            _clbTags.Enabled         = on && _rdoList.Checked;
            _chkRunOnStartup.Enabled = on;
        }
    }
}
