using System;
using System.Drawing;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Picture-in-picture host for the Schedule Builder map. The same
    /// <see cref="SupeyMapWorkspace"/> is reparented here — this form does not own a second map.
    /// Pin / Dock / Hide live on the compact title bar.
    /// </summary>
    internal sealed class ScheduleBuilderMapFloatForm : SupeyForm
    {
        private const int ActionBtnW = 50;
        private const int ActionBtnH = 28;
        private const int ActionGap = 4;
        private const int WindowButtonsWidth = 46 * 3;

        private readonly Panel _root;
        private readonly FlowLayoutPanel _titleActions;
        private readonly SupeyButton _pinBtn;
        private readonly SupeyButton _dockBtn;
        private readonly SupeyButton _hideBtn;
        private bool _allowClose;

        public Panel MapHost { get; }

        public event Action HideRequested;
        public event Action DockRequested;
        public event Action PinChanged;

        protected override int ChromeTitleHeight => 36;

        protected override int TitleBarExtraRightReserve =>
            ActionBtnW * 3 + ActionGap * 2 + 8;

        public bool Pinned
        {
            get => TopMost;
            set
            {
                if (TopMost == value)
                    return;
                TopMost = value;
                RefreshPinButton();
                PinChanged?.Invoke();
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

            MapHost = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = SupeyTheme.SurfaceBase,
            };

            _root.Controls.Add(MapHost);
            Controls.Add(_root);
            SetMaterialContent(_root, leftGutter: 0, sidePad: 2);

            _pinBtn = MakeTitleAction("Pin", "Keep this window above other windows");
            _dockBtn = MakeTitleAction("Dock", "Put the map back into Schedule Builder");
            _hideBtn = MakeTitleAction("Hide", "Hide the map. Show it again from Schedule Builder.");
            _pinBtn.Click += (s, e) => Pinned = !Pinned;
            _dockBtn.Click += (s, e) => DockRequested?.Invoke();
            _hideBtn.Click += (s, e) => HideRequested?.Invoke();

            _titleActions = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = SupeyTheme.SurfaceHeader,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
            };
            _titleActions.Controls.Add(_pinBtn);
            _titleActions.Controls.Add(_dockBtn);
            _titleActions.Controls.Add(_hideBtn);
            Controls.Add(_titleActions);
            _titleActions.BringToFront();

            ApplyChromeTheme();
            RefreshPinButton();
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

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            LayoutTitleActions();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutTitleActions();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (DesignMode || WindowState == FormWindowState.Maximized)
                return;
            DrawOuterFrame(e.Graphics);
        }

        private void LayoutTitleActions()
        {
            if (_titleActions == null || _titleActions.IsDisposed)
                return;
            Size need = _titleActions.PreferredSize;
            int x = ClientSize.Width - WindowButtonsWidth - 8 - need.Width;
            int y = Math.Max(0, (ChromeTitleHeight - need.Height) / 2);
            _titleActions.Location = new Point(Math.Max(8, x), y);
            _titleActions.BringToFront();
        }

        private void RefreshPinButton()
        {
            if (_pinBtn == null || _pinBtn.IsDisposed)
                return;
            _pinBtn.Kind = TopMost ? SupeyButton.Variant.Primary : SupeyButton.Variant.Secondary;
        }

        private static SupeyButton MakeTitleAction(string text, string tip)
        {
            var btn = new SupeyButton
            {
                Text = text,
                Kind = SupeyButton.Variant.Secondary,
                Size = new Size(ActionBtnW, ActionBtnH),
                Margin = new Padding(0, 0, ActionGap, 0),
            };
            var tool = SupeyToolTip.Create(initialDelay: 250);
            tool.SetToolTip(btn, tip);
            return btn;
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
            MapHost.BackColor = SupeyTheme.SurfaceBase;
            if (_titleActions != null)
                _titleActions.BackColor = SupeyTheme.SurfaceHeader;
        }
    }
}
