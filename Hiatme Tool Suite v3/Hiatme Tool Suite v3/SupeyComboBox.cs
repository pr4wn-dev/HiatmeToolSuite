using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// MaterialComboBox-style dropdown: floating hint, underline + accent focus line, themed popup rows.
    /// Matches <see cref="SupeyTextBox"/> field height and horizontal padding on login forms.
    /// </summary>
    public sealed class SupeyComboBox : ComboBox
    {
        private const int WM_ERASEBKGND = 0x0014;
        private const int WM_PAINT = 0x000F;
        private const int LeftPadding = 16;
        private const int RightPadding = 12;
        private const int HintSmallY = 4;
        private const int HintSmallH = 18;
        private const int BottomPad = 3;
        private const int ActivationH = 2;
        private const int TallHeight = 58;
        private const int ShortHeight = 36;
        /// <summary>Matches Billing/toolbar row height (30px).</summary>
        private const int ToolbarHeight = 30;
        private const int ArrowInset = 12;
        private const int IconSize = 24;

        private readonly Timer _focusAnimTimer;
        private string _hint = string.Empty;
        private bool _useTallSize = true;
        private bool _useToolbarSize;
        private bool _focused;
        private float _focusAnim;
        private int _lineY;

        public SupeyComboBox()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw,
                true);

            DropDownStyle = ComboBoxStyle.DropDownList;
            DrawMode = DrawMode.OwnerDrawVariable;
            FlatStyle = FlatStyle.Flat;
            IntegralHeight = false;
            MaxDropDownItems = 8;
            Font = SupeyTheme.BodyFont;
            BackColor = SupeyTheme.Surface;
            ForeColor = SupeyTheme.TextPrimary;

            _focusAnimTimer = new Timer { Interval = 15 };
            _focusAnimTimer.Tick += FocusAnimTick;

            GotFocus += (_, __) => { _focused = true; StartFocusAnim(); Invalidate(); };
            LostFocus += (_, __) => { _focused = false; StartFocusAnim(); Invalidate(); };
            DropDown += (_, __) => { _focused = true; StartFocusAnim(); Invalidate(); };
            DropDownClosed += (_, __) => { if (!Focused) { _focused = false; StartFocusAnim(); } Invalidate(); };
            SelectedIndexChanged += (_, __) => Invalidate();
            MouseEnter += (_, __) => Invalidate();
            MouseLeave += (_, __) => Invalidate();

            SupeyThemeManager.ThemeChanged += OnThemeChanged;
            ApplyHeightMetrics();
        }

        // ── MaterialComboBox designer shims ───────────────────────────────────────
        public int Depth { get; set; }
        public SupeyMouseState MouseState { get; set; } = SupeyMouseState.OUT;
        public bool AutoResize { get; set; }
        public bool UseAccent { get; set; } = true;
        public int StartIndex { get; set; }

        /// <summary>Shift value text right to line up with <see cref="SupeyTextBox"/> fields that have leading icons.</summary>
        public bool AlignTextWithIconFields { get; set; }

        private int TextPad => AlignTextWithIconFields ? LeftPadding + IconSize : LeftPadding;

        public bool UseTallSize
        {
            get => _useTallSize;
            set { _useTallSize = value; if (value) _useToolbarSize = false; ApplyHeightMetrics(); Invalidate(); }
        }

        /// <summary>30px height for Billing-style toolbar rows (date picker / button alignment).</summary>
        public bool UseToolbarSize
        {
            get => _useToolbarSize;
            set { _useToolbarSize = value; if (value) _useTallSize = false; ApplyHeightMetrics(); Invalidate(); }
        }

        public string Hint
        {
            get => _hint;
            set { _hint = value ?? string.Empty; Invalidate(); }
        }

        private void OnThemeChanged(object sender, EventArgs e)
        {
            if (IsDisposed) return;
            BackColor = SupeyTheme.Surface;
            ForeColor = SupeyTheme.TextPrimary;
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                SupeyThemeManager.ThemeChanged -= OnThemeChanged;
                _focusAnimTimer?.Stop();
                _focusAnimTimer?.Dispose();
            }
            base.Dispose(disposing);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (!DesignMode && Items.Count > 0 && StartIndex >= 0 && StartIndex < Items.Count && SelectedIndex < 0)
                SelectedIndex = StartIndex;
            SuppressNativeComboChrome();
        }

        protected override void OnParentChanged(EventArgs e)
        {
            base.OnParentChanged(e);
            SuppressNativeComboChrome();
        }

        protected override void OnLocationChanged(EventArgs e)
        {
            base.OnLocationChanged(e);
            SuppressNativeComboChrome();
        }

        protected override void OnControlAdded(ControlEventArgs e)
        {
            base.OnControlAdded(e);
            SuppressNativeComboChrome();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_ERASEBKGND)
            {
                m.Result = (IntPtr)1;
                return;
            }

            base.WndProc(ref m);

            if (m.Msg == WM_PAINT)
                SuppressNativeComboChrome();
        }

        /// <summary>
        /// WinForms ComboBox keeps native child HWNDs that paint the stock combo chrome at stale
        /// positions when the parent is reparented/resized — hide them; we draw everything in OnPaint.
        /// </summary>
        private void SuppressNativeComboChrome()
        {
            if (IsDisposed || !IsHandleCreated) return;
            try
            {
                foreach (Control child in Controls)
                    child.Visible = false;
            }
            catch
            {
                // ignore during teardown
            }
        }

        protected override void OnCreateControl()
        {
            base.OnCreateControl();
            MeasureItem += OnMeasureItem;
            DrawItem += DrawPopupItem;
            ApplyHeightMetrics();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            ApplyHeightMetrics();
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            ApplyHeightMetrics();
            Invalidate();
        }

        private void StartFocusAnim()
        {
            if (!_focusAnimTimer.Enabled)
                _focusAnimTimer.Start();
        }

        private void FocusAnimTick(object sender, EventArgs e)
        {
            float target = (_focused || DroppedDown) ? 1f : 0f;
            const float step = 0.12f;
            if (_focusAnim < target) _focusAnim = Math.Min(target, _focusAnim + step);
            else if (_focusAnim > target) _focusAnim = Math.Max(target, _focusAnim - step);
            Invalidate();
            if (Math.Abs(_focusAnim - target) < 0.001f) { _focusAnim = target; _focusAnimTimer.Stop(); }
        }

        private void ApplyHeightMetrics()
        {
            int h = _useTallSize ? TallHeight : (_useToolbarSize ? ToolbarHeight : ShortHeight);
            if (Height != h)
                Height = h;
            _lineY = h - BottomPad;
            ItemHeight = Math.Max(22, h - 8);
            DropDownHeight = ItemHeight * Math.Min(MaxDropDownItems, Math.Max(4, Items.Count)) + 2;
            DropDownWidth = Math.Max(Width, DropDownWidth);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(SupeyTheme.Surface);
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            using (var bg = new SolidBrush(SupeyTheme.Surface))
                g.FillRectangle(bg, 0, 0, Width, _lineY);

            bool hasHint = !string.IsNullOrEmpty(_hint);
            bool hasValue = SelectedIndex >= 0 && !string.IsNullOrEmpty(Text);
            bool floatHint = hasHint && _useTallSize && hasValue;

            var hintRect = new Rectangle(TextPad, floatHint ? HintSmallY : 0, Width - TextPad - RightPadding - 20, floatHint ? HintSmallH : _lineY);

            using (var div = new SolidBrush(SupeyTheme.BorderSubtle))
                g.FillRectangle(div, 0, _lineY, Width, 1);

            if (_focusAnim > 0f)
            {
                int half = (int)(Width / 2f * _focusAnim);
                int cx = Width / 2;
                using (var acc = new SolidBrush(SupeyTheme.AccentPrimary))
                    g.FillRectangle(acc, cx - half, _lineY, half * 2, ActivationH);
            }

            if (floatHint)
            {
                Color hintColor = Blend(SupeyTheme.TextSecondary, SupeyTheme.AccentPrimary, _focusAnim);
                TextRenderer.DrawText(g, _hint, SupeyTheme.CaptionFont, hintRect, hintColor,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }

            if (hasValue)
            {
                var textRect = new Rectangle(
                    TextPad,
                    floatHint ? hintRect.Bottom - 2 : 0,
                    Width - TextPad - RightPadding - 16,
                    floatHint ? _lineY - (hintRect.Bottom - 2) : _lineY);
                TextRenderer.DrawText(g, Text, Font, textRect,
                    Enabled ? SupeyTheme.TextPrimary : SupeyTheme.TextMuted,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
            else if (hasHint && !floatHint)
            {
                TextRenderer.DrawText(g, _hint, Font, hintRect, SupeyTheme.TextMuted,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }

            DrawArrow(g);
        }

        private void DrawArrow(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int cy = _lineY / 2;
            int ax = Width - ArrowInset;
            Color arrowColor = !Enabled ? SupeyTheme.TextMuted
                : (DroppedDown || _focused) ? SupeyTheme.AccentPrimary : SupeyTheme.TextSecondary;
            using (var brush = new SolidBrush(arrowColor))
            {
                var tri = new Point[]
                {
                    new Point(ax - 5, cy - 2),
                    new Point(ax + 5, cy - 2),
                    new Point(ax, cy + 3),
                };
                g.FillPolygon(brush, tri);
            }
            g.SmoothingMode = SmoothingMode.None;
        }

        private void OnMeasureItem(object sender, MeasureItemEventArgs e)
        {
            e.ItemHeight = ItemHeight;
        }

        private void DrawPopupItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            Color back = selected ? SupeyTheme.ListSelected : SupeyTheme.ListBody;
            Color fore = selected ? SupeyTheme.ListSelectedText : SupeyTheme.ListText;
            using (var b = new SolidBrush(back))
                e.Graphics.FillRectangle(b, e.Bounds);
            string text = GetItemText(Items[e.Index]);
            var textRect = new Rectangle(e.Bounds.X + LeftPadding, e.Bounds.Y, e.Bounds.Width - LeftPadding - 8, e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, text, Font, textRect, fore,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        private static Color Blend(Color a, Color b, float t)
        {
            t = Math.Max(0f, Math.Min(1f, t));
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }
    }
}
