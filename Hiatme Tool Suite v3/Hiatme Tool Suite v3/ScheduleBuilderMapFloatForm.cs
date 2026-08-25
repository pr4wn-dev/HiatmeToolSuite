using System;
using System.Drawing;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Picture-in-picture host for the Schedule Builder map. The same
    /// <see cref="SupeyMapWorkspace"/> is reparented here — this form does not own a second map.
    /// Close / Hide hide the window; Dock puts the map back in the schedule tab.
    /// </summary>
    internal sealed class ScheduleBuilderMapFloatForm : SupeyForm
    {
        private readonly Panel _root;
        private readonly Panel _actions;
        private readonly Label _hintLbl;
        private readonly SupeyButton _pinBtn;
        private readonly SupeyButton _dockBtn;
        private readonly SupeyButton _hideBtn;
        private bool _allowClose;

        public Panel MapHost { get; }

        public event Action DockRequested;
        public event Action HideRequested;
        public event Action PinChanged;

        public bool Pinned
        {
            get => TopMost;
            set
            {
                TopMost = value;
                UpdatePinCaption();
            }
        }

        public ScheduleBuilderMapFloatForm()
        {
            Text = "Schedule map";
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = true;
            ShowIcon = true;
            MinimizeBox = true;
            MaximizeBox = true;
            ControlBox = true;
            Sizable = true;
            MinimumSize = new Size(420, 340);
            Size = new Size(560, 480);
            KeyPreview = true;

            _root = new Panel
            {
                BackColor = SupeyTheme.SurfaceBase,
            };

            _actions = new Panel
            {
                Dock = DockStyle.Top,
                Height = 40,
                Padding = new Padding(10, 6, 10, 6),
            };

            _hintLbl = new Label
            {
                Dock = DockStyle.Fill,
                Text = "Drag the title bar to move. Resize from any edge.",
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 8.5f),
                AutoEllipsis = true,
            };

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Right,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Margin = new Padding(0),
                Padding = new Padding(0),
            };

            _pinBtn = MakeChromeButton("Pin", 64);
            _dockBtn = MakeChromeButton("Dock", 64);
            _hideBtn = MakeChromeButton("Hide", 60);
            _pinBtn.Click += (s, e) =>
            {
                Pinned = !Pinned;
                PinChanged?.Invoke();
            };
            _dockBtn.Click += (s, e) => DockRequested?.Invoke();
            _hideBtn.Click += (s, e) => HideRequested?.Invoke();

            buttons.Controls.Add(_pinBtn);
            buttons.Controls.Add(_dockBtn);
            buttons.Controls.Add(_hideBtn);

            _actions.Controls.Add(_hintLbl);
            _actions.Controls.Add(buttons);

            var divider = new Panel
            {
                Dock = DockStyle.Top,
                Height = 1,
            };

            MapHost = new Panel
            {
                Dock = DockStyle.Fill,
            };

            _root.Controls.Add(MapHost);
            _root.Controls.Add(divider);
            _root.Controls.Add(_actions);

            Controls.Add(_root);
            SetMaterialContent(_root, leftGutter: 0, sidePad: 2);

            var tip = SupeyToolTip.Create(initialDelay: 250);
            tip.SetToolTip(_pinBtn, "Keep this window above other windows.");
            tip.SetToolTip(_dockBtn, "Put the map back into Schedule Builder.");
            tip.SetToolTip(_hideBtn, "Hide the map. Show it again from Schedule Builder.");

            ApplyChromeTheme();
            UpdatePinCaption();
            SupeyThemeManager.ThemeChanged += OnFloatThemeChanged;
        }

        public void SetDriverCaption(string driverTab)
        {
            string name = (driverTab ?? "").Trim();
            Text = string.IsNullOrEmpty(name)
                ? "Schedule map"
                : "Schedule map · " + name;
            RefreshTitleBarChrome();
        }

        public void AllowClose()
        {
            _allowClose = true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (DesignMode || WindowState == FormWindowState.Maximized)
                return;
            DrawOuterFrame(e.Graphics);
        }

        private void DrawOuterFrame(Graphics g)
        {
            int w = ClientSize.Width;
            int h = ClientSize.Height;
            if (w < 4 || h < 4)
                return;

            const int thickness = 2;
            using (var brush = new SolidBrush(FrameColor()))
            {
                g.FillRectangle(brush, 0, 0, w, thickness);
                g.FillRectangle(brush, 0, h - thickness, w, thickness);
                g.FillRectangle(brush, 0, 0, thickness, h);
                g.FillRectangle(brush, w - thickness, 0, thickness, h);
            }
        }

        private static Color FrameColor()
        {
            Color accent = SupeyTheme.AccentStripe;
            if (accent.IsEmpty)
                accent = SupeyTheme.AccentPrimary;
            if (accent.IsEmpty)
                accent = SupeyTheme.BorderSubtle;
            return accent;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                e.Handled = true;
                HideRequested?.Invoke();
                return;
            }

            base.OnKeyDown(e);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_allowClose && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                HideRequested?.Invoke();
                return;
            }

            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                SupeyThemeManager.ThemeChanged -= OnFloatThemeChanged;
            base.Dispose(disposing);
        }

        private void OnFloatThemeChanged(object sender, EventArgs e)
        {
            if (IsDisposed)
                return;
            ApplyChromeTheme();
            Invalidate(true);
        }

        private void ApplyChromeTheme()
        {
            BackColor = SupeyTheme.SurfaceBase;
            ForeColor = SupeyTheme.TextPrimary;
            _root.BackColor = SupeyTheme.SurfaceBase;
            _actions.BackColor = SupeyTheme.SurfaceHeader;
            _hintLbl.ForeColor = SupeyTheme.TextMuted;
            _hintLbl.BackColor = SupeyTheme.SurfaceHeader;
            MapHost.BackColor = SupeyTheme.SurfaceBase;
            foreach (Control child in _actions.Controls)
                child.BackColor = SupeyTheme.SurfaceHeader;
        }

        private void UpdatePinCaption()
        {
            _pinBtn.Text = TopMost ? "Pinned" : "Pin";
            _pinBtn.Kind = TopMost ? SupeyButton.Variant.Primary : SupeyButton.Variant.Secondary;
        }

        private static SupeyButton MakeChromeButton(string text, int width)
        {
            return new SupeyButton
            {
                Text = text,
                Kind = SupeyButton.Variant.Secondary,
                Size = new Size(width, 26),
                Margin = new Padding(6, 1, 0, 0),
            };
        }
    }
}
