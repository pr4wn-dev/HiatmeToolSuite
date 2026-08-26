using System;
using System.Drawing;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>Live status while Schedule Builder ASSIGN runs on WellRyde.</summary>
    internal sealed class SupeyWellRydeAssignProgressForm : SupeyForm
    {
        private const int DialogWidth = 560;
        private const int ContentPad = 24;
        private const int ContentWidth = DialogWidth - ContentPad * 2;
        private const int FooterHeight = 56;

        private readonly Label _statusLbl;
        private readonly DarkOnAccentMaterialButton _closeBtn;
        private readonly IndeterminateStripe _bar;
        private bool _done;

        public SupeyWellRydeAssignProgressForm(DateTime serviceDate, int tripCount)
        {
            Text = "Assign";
            FormBorderStyle = FormBorderStyle.None;
            MaximizeBox = false;
            MinimizeBox = false;
            Sizable = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = SupeyTheme.Surface;
            ClientSize = new Size(DialogWidth, 240);

            var footer = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = FooterHeight,
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

            var footerButtons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                Padding = new Padding(0, 8, ContentPad, 12),
                BackColor = SupeyTheme.Surface,
            };
            footerButtons.Controls.Add(_closeBtn);
            footer.Controls.Add(footerButtons);

            var body = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = SupeyTheme.Surface,
                Padding = new Padding(ContentPad, TitleBarHeight + 14, ContentPad, 12),
            };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                BackColor = SupeyTheme.Surface,
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            string dateLine = serviceDate.ToString("dddd, MMMM d, yyyy");
            layout.Controls.Add(new Label
            {
                Text = dateLine + "  ·  " + tripCount + " trip" + (tripCount == 1 ? "" : "s") + " from driver tabs",
                Font = new Font("Segoe UI", 9.25f),
                ForeColor = SupeyTheme.TextSecondary,
                AutoSize = true,
                MaximumSize = new Size(ContentWidth, 0),
                Margin = Padding.Empty,
                BackColor = SupeyTheme.Surface,
            }, 0, 0);

            _bar = new IndeterminateStripe
            {
                Dock = DockStyle.Fill,
                Height = 6,
                Margin = new Padding(0, 14, 0, 0),
            };
            layout.Controls.Add(_bar, 0, 1);

            _statusLbl = new Label
            {
                Text = "Starting…",
                Font = new Font("Segoe UI Semibold", 16f),
                ForeColor = SupeyTheme.AccentPrimary,
                AutoSize = true,
                MaximumSize = new Size(ContentWidth, 0),
                Margin = new Padding(0, 16, 0, 0),
                BackColor = SupeyTheme.Surface,
            };
            layout.Controls.Add(_statusLbl, 0, 2);

            body.Controls.Add(layout);
            Controls.Add(body);
            Controls.Add(footer);

            AcceptButton = _closeBtn;
            CancelButton = _closeBtn;
            SupeyDarkScrollBars.Apply(this);

            FormClosing += (s, e) =>
            {
                if (!_done)
                    e.Cancel = true;
            };
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            SupeyForm.CenterOnWorkingArea(this, Owner);
        }

        public void SetStatus(string text)
        {
            if (IsDisposed || string.IsNullOrWhiteSpace(text))
                return;

            void apply()
            {
                _statusLbl.Text = text.Trim();
                _statusLbl.ForeColor = SupeyTheme.AccentPrimary;
                _statusLbl.Refresh();
            }

            if (InvokeRequired)
                BeginInvoke((MethodInvoker)apply);
            else
                apply();
        }

        public void ReportDone(string summary)
        {
            _done = true;
            _bar.Stop();
            _bar.FillColor = SupeyTheme.SuccessText;
            _bar.Fraction = 1f;
            if (!string.IsNullOrWhiteSpace(summary))
            {
                _statusLbl.Text = summary.Trim();
                _statusLbl.ForeColor = SupeyTheme.SuccessText;
            }

            _closeBtn.Visible = true;
        }

        public void ForceClose()
        {
            _done = true;
            Close();
        }

        private sealed class IndeterminateStripe : Control
        {
            private readonly Timer _timer;
            private float _phase;
            private float _fraction = 0.35f;

            public Color FillColor { get; set; } = SupeyTheme.AccentPrimary;

            public float Fraction
            {
                get => _fraction;
                set { _fraction = value; Invalidate(); }
            }

            public IndeterminateStripe()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint
                    | ControlStyles.OptimizedDoubleBuffer
                    | ControlStyles.UserPaint, true);

                _timer = new Timer { Interval = 40 };
                _timer.Tick += (s, e) =>
                {
                    _phase += 0.04f;
                    if (_phase > 1f)
                        _phase = 0f;
                    Invalidate();
                };
                _timer.Start();
            }

            public void Stop() => _timer.Stop();

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.Clear(SupeyTheme.SurfaceElevated);
                if (_timer.Enabled)
                {
                    int segW = Math.Max(24, Width / 4);
                    int x = (int)((Width + segW) * _phase) - segW;
                    using (var b = new SolidBrush(FillColor))
                        e.Graphics.FillRectangle(b, x, 0, segW, Height);
                }
                else
                {
                    int w = (int)Math.Round(Width * _fraction);
                    if (w > 0)
                    {
                        using (var b = new SolidBrush(FillColor))
                            e.Graphics.FillRectangle(b, 0, 0, w, Height);
                    }
                }
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                    _timer.Dispose();
                base.Dispose(disposing);
            }
        }
    }
}
