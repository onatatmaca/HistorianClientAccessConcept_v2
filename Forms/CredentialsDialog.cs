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

        // The mirror is often administered separately - a different domain, a different account -
        // so it must be possible to give it its own login. Hidden until asked for, because on
        // most sites the two servers do share one account and a second pair of empty fields
        // would just look like something that has to be filled in.
        private readonly CheckBox _separate = new CheckBox();
        private readonly Label _mirrorHdr = new Label();
        private readonly Label _lblMirrorUser = new Label();
        private readonly Label _lblMirrorPass = new Label();
        private readonly TextBox _mirrorUser = new TextBox();
        private readonly TextBox _mirrorPass = new TextBox { UseSystemPasswordChar = true };

        // Tall enough for the three-line hint. It was clipped mid-sentence at 300/404, which is
        // the same silent-truncation class the captions were fixed for in Phase 12d.
        private const int CollapsedHeight = 324;
        private const int ExpandedHeight  = 424;

        public string UserName => _user.Text.Trim();
        public string Password => _pass.Text;
        public bool Remember => _remember.Checked;
        public bool SeparateMirror => _separate.Checked;
        public string MirrorUserName => _mirrorUser.Text.Trim();
        public string MirrorPassword => _mirrorPass.Text;

        public CredentialsDialog()
        {
            Text = Loc.T("cred.title");
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            ClientSize = new Size(460, CollapsedHeight);
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

            _separate.Text = Loc.T("cred.separate");
            _separate.Location = new Point(146, 150);
            _separate.Size = new Size(292, 24);
            _separate.CheckedChanged += (s, e) => ApplySeparateState();
            Controls.Add(_separate);

            _mirrorHdr.Text = Loc.T("cred.mirrorHeader");
            _mirrorHdr.Location = new Point(18, 182);
            _mirrorHdr.Size = new Size(424, 20);
            _mirrorHdr.ForeColor = AppTheme.TextPrimary;
            _mirrorHdr.Font = AppTheme.Bold;
            Controls.Add(_mirrorHdr);

            _lblMirrorUser.Text = Loc.T("cred.user");
            _lblMirrorUser.Location = new Point(18, 210);
            _lblMirrorUser.Size = new Size(120, 20);
            Controls.Add(_lblMirrorUser);
            _mirrorUser.Location = new Point(146, 207);
            _mirrorUser.Size = new Size(292, 24);
            Controls.Add(_mirrorUser);

            _lblMirrorPass.Text = Loc.T("cred.password");
            _lblMirrorPass.Location = new Point(18, 246);
            _lblMirrorPass.Size = new Size(120, 20);
            Controls.Add(_lblMirrorPass);
            _mirrorPass.Location = new Point(146, 243);
            _mirrorPass.Size = new Size(292, 24);
            Controls.Add(_mirrorPass);

            _remember.Text = Loc.T("cred.remember");
            _remember.Size = new Size(292, 24);
            Controls.Add(_remember);

            // Says plainly where the password would be kept, and that leaving the fields empty
            // is a legitimate choice (the Windows session) rather than an error.
            _hint.Text = Loc.T("cred.hint");
            _hint.Size = new Size(424, 54);
            _hint.ForeColor = AppTheme.TextSecondary;
            Controls.Add(_hint);

            _ok = new FlatButton
            {
                Text = Loc.T("cred.ok"),
                ButtonStyle = FlatButtonStyle.Primary,
                Size = new Size(96, 30),
                DialogResult = DialogResult.OK
            };
            _cancel = new FlatButton
            {
                Text = Loc.T("dlg.cancel"),
                Size = new Size(96, 30),
                DialogResult = DialogResult.Cancel
            };
            Controls.Add(_ok);
            Controls.Add(_cancel);
            AcceptButton = _ok;
            CancelButton = _cancel;

            // Pre-fill from whatever is currently in effect, so the user corrects rather than
            // retypes. The password is deliberately NOT pre-filled: showing a stored secret back
            // (even as dots) invites saving a wrong value without noticing.
            if (HistorianCredentials.HasLogin)
            {
                _user.Text = HistorianCredentials.Username;
                _remember.Checked = HistorianCredentials.Source == "saved";
            }
            _separate.Checked = HistorianCredentials.SeparateMirrorLogin;
            if (HistorianCredentials.SeparateMirrorLogin)
                _mirrorUser.Text = HistorianCredentials.MirrorUsername;

            ApplySeparateState();
        }

        private readonly FlatButton _ok;
        private readonly FlatButton _cancel;

        /// <summary>
        /// Shows or hides the mirror's own login and resizes the dialog around it, so the window
        /// is never padded with empty space that looks like something left unfilled.
        /// </summary>
        private void ApplySeparateState()
        {
            bool on = _separate.Checked;
            _mirrorHdr.Visible = on;
            _lblMirrorUser.Visible = on;
            _mirrorUser.Visible = on;
            _lblMirrorPass.Visible = on;
            _mirrorPass.Visible = on;

            int y = on ? 282 : 182;
            _remember.Location = new Point(146, y);
            _hint.Location = new Point(18, y + 28);
            _ok.Location = new Point(238, y + 90);
            _cancel.Location = new Point(342, y + 90);
            ClientSize = new Size(460, on ? ExpandedHeight : CollapsedHeight);
        }
    }
}
