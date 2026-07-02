using System;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Live progress for the schedule email batch: themed bar, per-driver results,
    /// cancel between sends, and a final summary — replaces the plain result MessageBox.
    /// </summary>
    internal sealed class ScheduleEmailProgressForm : SupeyForm
    {
        private const int DialogWidth = 500;
        private const int ContentPad = 24;
        private const int ContentWidth = DialogWidth - ContentPad * 2;
        private const int FooterHeight = 56;

        private readonly int _total;
        private readonly Label _statusLbl;
        private readonly Label _summaryLbl;
        private readonly TextBox _resultsBox;
        private readonly ProgressStripe _bar;
        private readonly SupeyMaterialButton _cancelBtn;
        private readonly DarkOnAccentMaterialButton _closeBtn;
        private readonly StringBuilder _results = new StringBuilder();

        private bool _done;

        public bool CancelRequested { get; private set; }

        public ScheduleEmailProgressForm(int totalRecipients, DateTime serviceDate, string fromAddress)
        {
            _total = Math.Max(1, totalRecipients);

            Text = "Email schedules";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = SupeyTheme.Surface;
            ClientSize = new Size(DialogWidth, 418);

            var footer = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = FooterHeight,
                BackColor = SupeyTheme.Surface,
            };

            var footerButtons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                Padding = new Padding(0, 8, 20, 12),
                BackColor = SupeyTheme.Surface,
            };

            _closeBtn = new DarkOnAccentMaterialButton
            {
                Text = "CLOSE",
                AutoSize = false,
                Type = SupeyMaterialButton.MaterialButtonType.Contained,
                UseAccentColor = true,
                Size = new Size(96, 36),
                Visible = false,
            };
            _closeBtn.Click += (s, e) => Close();

            _cancelBtn = new SupeyMaterialButton
            {
                Text = "CANCEL",
                AutoSize = false,
                Type = SupeyMaterialButton.MaterialButtonType.Text,
                UseAccentColor = false,
                NoAccentTextColor = SupeyTheme.TextSecondary,
                Size = new Size(96, 36),
                Margin = new Padding(0, 0, 8, 0),
            };
            _cancelBtn.Click += (s, e) =>
            {
                CancelRequested = true;
                _cancelBtn.Enabled = false;
                SetStatus("Cancelling — finishing the current send…");
            };

            footerButtons.Controls.Add(_closeBtn);
            footerButtons.Controls.Add(_cancelBtn);
            footer.Controls.Add(footerButtons);

            var body = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = SupeyTheme.Surface,
                Padding = new Padding(ContentPad, TitleBarHeight + 14, ContentPad, 8),
            };

            var stack = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                BackColor = SupeyTheme.Surface,
            };

            stack.Controls.Add(new Label
            {
                Text = "Emailing driver schedules",
                Font = new Font("Segoe UI Semibold", 11f),
                ForeColor = SupeyTheme.TextPrimary,
                AutoSize = true,
                MaximumSize = new Size(ContentWidth, 0),
                Margin = new Padding(0),
                BackColor = SupeyTheme.Surface,
            });

            string from = (fromAddress ?? "").Trim();
            stack.Controls.Add(new Label
            {
                Text = "Schedule for " + serviceDate.ToString("MMMM d, yyyy")
                    + " · " + totalRecipients + " driver" + (totalRecipients == 1 ? "" : "s")
                    + (from.Length > 0 ? " · From: " + from : ""),
                Font = new Font("Segoe UI", 9f),
                ForeColor = SupeyTheme.TextSecondary,
                AutoSize = true,
                MaximumSize = new Size(ContentWidth, 0),
                Margin = new Padding(0, 6, 0, 0),
                BackColor = SupeyTheme.Surface,
            });

            _bar = new ProgressStripe
            {
                Width = ContentWidth,
                Height = 6,
                Margin = new Padding(0, 16, 0, 0),
            };
            stack.Controls.Add(_bar);

            _statusLbl = new Label
            {
                Text = "Preparing…",
                Font = new Font("Segoe UI", 9.25f),
                ForeColor = SupeyTheme.TextPrimary,
                AutoSize = false,
                Size = new Size(ContentWidth, 20),
                AutoEllipsis = true,
                Margin = new Padding(0, 10, 0, 0),
                BackColor = SupeyTheme.Surface,
            };
            stack.Controls.Add(_statusLbl);

            _resultsBox = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Consolas", 8.75f),
                BackColor = SupeyTheme.SurfaceElevated,
                ForeColor = SupeyTheme.TextPrimary,
                Width = ContentWidth,
                Height = 128,
                Margin = new Padding(0, 12, 0, 0),
                TabStop = false,
            };
            stack.Controls.Add(_resultsBox);

            _summaryLbl = new Label
            {
                Text = "",
                Font = new Font("Segoe UI", 8.75f),
                ForeColor = SupeyTheme.TextSecondary,
                AutoSize = true,
                MaximumSize = new Size(ContentWidth, 0),
                Margin = new Padding(0, 10, 0, 0),
                BackColor = SupeyTheme.Surface,
            };
            stack.Controls.Add(_summaryLbl);

            body.Controls.Add(stack);

            Controls.Add(body);
            Controls.Add(footer);

            SupeyDarkScrollBars.Apply(this);

            // X mid-send = cancel request, not a hard close (the loop still owns the batch).
            FormClosing += (s, e) =>
            {
                if (_done)
                    return;
                e.Cancel = true;
                CancelRequested = true;
                _cancelBtn.Enabled = false;
                SetStatus("Cancelling — finishing the current send…");
            };
        }

        public void ReportPreparing(string text) => SetStatus(text);

        public void ReportPacing(string nextDriver, int oneBasedIndex)
        {
            SetStatus("Waiting a moment… next: " + nextDriver
                + " (" + oneBasedIndex + " of " + _total + ")");
        }

        public void ReportSending(string driver, int oneBasedIndex)
        {
            SetStatus("Emailing " + driver + " (" + oneBasedIndex + " of " + _total + ")…");
        }

        public void ReportResult(string driver, string email, bool ok, string error)
        {
            _results.Append(ok ? "  ✓  " : "  ✗  ").Append(driver);
            if (!string.IsNullOrWhiteSpace(email))
                _results.Append("  <").Append(email.Trim()).Append(">");
            if (!ok)
                _results.Append("  — ").Append((error ?? "failed").Replace("\r", " ").Replace("\n", " ").Trim());
            _results.AppendLine();
            _resultsBox.Text = _results.ToString();
            _resultsBox.SelectionStart = _resultsBox.TextLength;
            _resultsBox.ScrollToCaret();
        }

        public void ReportSkipped(string driver, string email)
        {
            _results.Append("  –  ").Append(driver);
            if (!string.IsNullOrWhiteSpace(email))
                _results.Append("  <").Append(email.Trim()).Append(">");
            _results.AppendLine("  — skipped (cancelled)");
            _resultsBox.Text = _results.ToString();
        }

        public void SetProgress(int completed)
        {
            _bar.Fraction = Math.Max(0f, Math.Min(1f, completed / (float)_total));
        }

        public void ReportDone(int sent, int failed, int skipped)
        {
            _done = true;
            _bar.Fraction = 1f;
            _bar.FillColor = failed > 0 ? SupeyTheme.WarnText : SupeyTheme.SuccessText;

            string status = "Sent " + sent + " of " + _total + ".";
            if (failed > 0) status += "  " + failed + " failed.";
            if (skipped > 0) status += "  " + skipped + " skipped.";
            SetStatus(status);
            _statusLbl.ForeColor = failed > 0 ? SupeyTheme.WarnText : SupeyTheme.SuccessText;

            _summaryLbl.Text = "If a driver says they didn't get it: have them check Spam, "
                + "mark the message \"Not spam\", and add the sender to their contacts.";

            _cancelBtn.Visible = false;
            _closeBtn.Visible = true;
            AcceptButton = _closeBtn;
            CancelButton = _closeBtn;
            _closeBtn.Focus();
        }

        /// <summary>Close unconditionally (error before/after the batch) — bypasses the cancel-on-X guard.</summary>
        public void ForceClose()
        {
            _done = true;
            Close();
        }

        private void SetStatus(string text)
        {
            _statusLbl.Text = text ?? "";
        }

        /// <summary>Minimal themed progress bar — flat track + accent fill.</summary>
        private sealed class ProgressStripe : Control
        {
            private float _fraction;

            public Color FillColor { get; set; } = SupeyTheme.AccentPrimary;

            public float Fraction
            {
                get => _fraction;
                set { _fraction = value; Invalidate(); }
            }

            public ProgressStripe()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint
                    | ControlStyles.OptimizedDoubleBuffer
                    | ControlStyles.UserPaint, true);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.Clear(SupeyTheme.SurfaceElevated);
                int w = (int)Math.Round(Width * _fraction);
                if (w > 0)
                {
                    using (var b = new SolidBrush(FillColor))
                        e.Graphics.FillRectangle(b, 0, 0, w, Height);
                }
            }
        }
    }
}
