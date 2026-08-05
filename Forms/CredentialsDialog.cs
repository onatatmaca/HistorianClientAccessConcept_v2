using System;
using System.Drawing;
using System.Windows.Forms;
using HistorianSyncTool.Services;
using HistorianSyncTool.UI;
using HistorianSyncTool.UI.Controls;

namespace HistorianSyncTool.Forms
{
    /// <summary>
    /// Asks for the Historian login.
    ///
    /// This exists because the hand-out package ships without credentials on purpose, so a
    /// tester connecting to a server that requires a login previously got
    /// "the server has rejected the client credentials" and no way to do anything about it
    /// except hand-editing the .config file next to the exe.
    /// </summary>
    public class CredentialsDialog : Form
    {
        private readonly TextBox _user = new TextBox();
        private readonly TextBox _pass = new TextBox { UseSystemPasswordChar = true };
        private readonly CheckBox _remember = new CheckBox();
        private readonly Label _hint = new Label();

        public string UserName => _user.Text.Trim();
        public string Password => _pass.Text;
        public bool Remember => _remember.Checked;

        public CredentialsDialog()
        {
            Text = Loc.T("cred.title");
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            ClientSize = new Size(460, 262);
            BackColor = AppTheme.Surface;
            Font = AppTheme.Default;

            var lblIntro = new Label
            {
                Text = Loc.T("cred.intro"),
                Location = new Point(18, 16),
                Size = new Size(424, 58),
                ForeColor = AppTheme.TextSecondary
            };
            Controls.Add(lblIntro);

            Controls.Add(new Label
            {
                Text = Loc.T("cred.user"),
                Location = new Point(18, 86),
                Size = new Size(120, 20)
            });
            _user.Location = new Point(146, 83);
            _user.Size = new Size(292, 24);
            Controls.Add(_user);

            Controls.Add(new Label
            {
                Text = Loc.T("cred.password"),
                Location = new Point(18, 122),
                Size = new Size(120, 20)
            });
            _pass.Location = new Point(146, 119);
            _pass.Size = new Size(292, 24);
            Controls.Add(_pass);

            _remember.Text = Loc.T("cred.remember");
            _remember.Location = new Point(146, 152);
            _remember.Size = new Size(292, 24);
            Controls.Add(_remember);

            // Says plainly where the password would be kept, and that leaving the fields empty
            // is a legitimate choice (the Windows session) rather than an error.
            _hint.Text = Loc.T("cred.hint");
            _hint.Location = new Point(18, 180);
            _hint.Size = new Size(424, 36);
            _hint.ForeColor = AppTheme.TextSecondary;
            Controls.Add(_hint);

            var ok = new FlatButton
            {
                Text = Loc.T("cred.ok"),
                ButtonStyle = FlatButtonStyle.Primary,
                Location = new Point(238, 222),
                Size = new Size(96, 30),
                DialogResult = DialogResult.OK
            };
            var cancel = new FlatButton
            {
                Text = Loc.T("dlg.cancel"),
                Location = new Point(342, 222),
                Size = new Size(96, 30),
                DialogResult = DialogResult.Cancel
            };
            Controls.Add(ok);
            Controls.Add(cancel);
            AcceptButton = ok;
            CancelButton = cancel;

            // Pre-fill from whatever is currently in effect, so the user corrects rather than
            // retypes. The password is deliberately NOT pre-filled: showing a stored secret back
            // (even as dots) invites saving a wrong value without noticing.
            if (HistorianCredentials.HasLogin)
            {
                _user.Text = HistorianCredentials.Username;
                _remember.Checked = HistorianCredentials.Source == "saved";
            }
        }
    }
}
