using HistorianSyncTool.UI;
using HistorianSyncTool.UI.Controls;
using System;
using System.Drawing;
using System.Windows.Forms;

namespace HistorianSyncTool.Forms
{
    /// <summary>
    /// The single progress surface for every long-running operation. Shown modally so
    /// the rest of the application cannot be poked mid-operation; the only way out is
    /// the one Cancel button (or waiting for completion). MainForm drives it through
    /// UpdateDetail / UpdatePhase / UpdateStep and closes it via RequestClose().
    ///
    /// No ETA is shown by design: batch sizes vary too much for an honest estimate,
    /// and a wrong ETA is worse than none. Elapsed time + real counters instead.
    /// </summary>
    public class ProgressDialog : Form
    {
        private readonly Label _lblOperation;
        private readonly Label _lblDetail;
        private readonly Label _lblPhase;
        private readonly ProgressBar _barPhase;
        private readonly Label _lblStep;
        private readonly ProgressBar _barStep;
        private readonly Label _lblElapsed;
        private readonly FlatButton _btnCancel;
        private readonly Timer _elapsedTimer;
        private readonly DateTime _startedAt;

        private bool _allowClose;
        private bool _cancelRequested;

        /// <summary>Raised once when the user presses Cancel (or tries to close the window).</summary>
        public event EventHandler CancelRequested;

        public ProgressDialog(string operationTitle)
        {
            Text            = Loc.T("prog.working");
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition   = FormStartPosition.CenterParent;
            ClientSize      = new Size(460, 196);
            MaximizeBox     = false;
            MinimizeBox     = false;
            ControlBox      = false;
            ShowInTaskbar   = false;
            BackColor       = AppTheme.Surface;
            Font            = AppTheme.Default;

            _startedAt = DateTime.Now;

            _lblOperation = new Label
            {
                Text = operationTitle,
                Left = 20, Top = 14, Width = 420, Height = 24,
                Font = new Font("Segoe UI", 11f, FontStyle.Bold),
                ForeColor = AppTheme.Navy, AutoSize = false
            };

            _lblDetail = new Label
            {
                Text = Loc.T("prog.starting"),
                Left = 20, Top = 40, Width = 420, Height = 18,
                Font = AppTheme.Default, ForeColor = AppTheme.TextSecondary,
                AutoEllipsis = true, AutoSize = false
            };

            // Phase row (e.g. "Tag 3 / 12 — FT_4711") — hidden until UpdatePhase is called
            _lblPhase = new Label
            {
                Text = "", Left = 20, Top = 66, Width = 420, Height = 16,
                Font = AppTheme.Small, ForeColor = AppTheme.TextPrimary,
                AutoEllipsis = true, AutoSize = false, Visible = false
            };
            _barPhase = new ProgressBar
            {
                Left = 20, Top = 84, Width = 420, Height = 10,
                Style = ProgressBarStyle.Blocks, Visible = false
            };

            // Step row (e.g. batches within the current tag) — starts as marquee
            _lblStep = new Label
            {
                Text = "", Left = 20, Top = 100, Width = 420, Height = 16,
                Font = AppTheme.Small, ForeColor = AppTheme.TextPrimary,
                AutoEllipsis = true, AutoSize = false
            };
            _barStep = new ProgressBar
            {
                Left = 20, Top = 118, Width = 420, Height = 14,
                Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 25
            };

            _lblElapsed = new Label
            {
                Text = Loc.F("prog.elapsed", "00:00"),
                Left = 20, Top = 152, Width = 200, Height = 18,
                Font = AppTheme.Small, ForeColor = AppTheme.TextSecondary, AutoSize = false
            };

            _btnCancel = new FlatButton
            {
                Text = Loc.T("prog.cancel"), ButtonStyle = FlatButtonStyle.Danger,
                Left = 340, Top = 146, Width = 100, Height = 30
            };
            _btnCancel.Click += (s, e) => RaiseCancel();

            Controls.AddRange(new Control[]
            {
                _lblOperation, _lblDetail,
                _lblPhase, _barPhase,
                _lblStep, _barStep,
                _lblElapsed, _btnCancel
            });
            CancelButton = _btnCancel;   // ESC = cancel

            _elapsedTimer = new Timer { Interval = 1000 };
            _elapsedTimer.Tick += (s, e) =>
            {
                TimeSpan t = DateTime.Now - _startedAt;
                _lblElapsed.Text = Loc.F("prog.elapsed", t.TotalHours >= 1
                    ? $"{(int)t.TotalHours}:{t.Minutes:D2}:{t.Seconds:D2}"
                    : $"{t.Minutes:D2}:{t.Seconds:D2}");
            };
            _elapsedTimer.Start();

            // The window has no X button, but Alt+F4 etc. must behave like Cancel,
            // never like a hard close that abandons the running operation.
            FormClosing += (s, e) =>
            {
                if (_allowClose) return;
                e.Cancel = true;
                RaiseCancel();
            };
        }

        private void RaiseCancel()
        {
            if (_cancelRequested) return;
            _cancelRequested = true;
            _btnCancel.Enabled = false;
            _btnCancel.Text = Loc.T("prog.cancelling");
            _lblDetail.Text = Loc.T("prog.stopping");
            CancelRequested?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>One-line status text under the title (same text as the status bar).</summary>
        public void UpdateDetail(string text)
        {
            if (_cancelRequested) return; // keep "Stopping…" visible once cancel was pressed
            _lblDetail.Text = text ?? "";
        }

        /// <summary>Outer progress: which tag/item of how many (shows the phase row).</summary>
        public void UpdatePhase(int current, int total, string label)
        {
            if (total <= 0) return;
            _lblPhase.Visible = true;
            _barPhase.Visible = true;
            _lblPhase.Text = label ?? "";
            _barPhase.Maximum = total;
            _barPhase.Value = Math.Min(Math.Max(0, current), total);
        }

        /// <summary>Inner progress: step within the current phase. Switches marquee → blocks.</summary>
        public void UpdateStep(int current, int total, string label = null)
        {
            if (total <= 0) return;
            _barStep.Style = ProgressBarStyle.Blocks;
            _barStep.Maximum = total;
            _barStep.Value = Math.Min(Math.Max(0, current), total);
            if (label != null) _lblStep.Text = label;
        }

        /// <summary>Called by the owner when the operation has actually finished.</summary>
        public void RequestClose()
        {
            _allowClose = true;
            _elapsedTimer.Stop();
            try { Close(); } catch { }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _elapsedTimer?.Dispose();
            base.Dispose(disposing);
        }
    }
}
