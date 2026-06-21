using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// MaterialTextBox2-style field: outer <see cref="Control"/> paints hint, underline, and icons;
    /// an inner borderless <see cref="TextBox"/> holds the editable text (16px side padding, 24px icons).
    /// </summary>
    internal class SupeyTextBox : Control
    {
        private const int IconSize = 24;
        private const int LeftPadding = 16;
        private const int RightPadding = 12;
        private const int HintSmallY = 4;
        private const int HintSmallH = 18;
        private const int FontHeight = 20;
        private const int ActivationH = 2;
        private const int TallHeight = 58;
        private const int ShortHeight = 36;
        private const int EM_SETCUEBANNER = 0x1501;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        private readonly TextBox _inner;
        private readonly Timer _focusAnimTimer;
        private string _hint = string.Empty;
        private Image _leadingIcon;
        private Image _trailingIcon;
        private bool _useTallSize = true;
        private bool _focused;
        private float _focusAnim;
        private int _lineY;
        private int _leftPad;
        private int _rightPad;
        private Rectangle _leadingBounds;
        private Rectangle _trailingBounds;

        public SupeyTextBox()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            BackColor = SupeyTheme.Surface;
            ForeColor = SupeyTheme.TextPrimary;
            Size = new Size(250, TallHeight);

            _focusAnimTimer = new Timer { Interval = 15 };
            _focusAnimTimer.Tick += FocusAnimTick;

            _inner = new TextBox
            {
                BorderStyle = BorderStyle.None,
                ForeColor = SupeyTheme.TextPrimary,
                BackColor = SupeyTheme.Surface,
                Multiline = false,
                TabStop = true,
            };
            TabStop = false;
            Controls.Add(_inner);

            _inner.HandleCreated += (_, __) => SyncInnerEditorState();
            _inner.GotFocus += (_, __) => { _focused = true; SyncInnerEditorState(); StartFocusAnim(); Invalidate(); };
            _inner.LostFocus += (_, __) => { _focused = false; SyncInnerEditorState(); StartFocusAnim(); Invalidate(); };
            _inner.TextChanged += (_, __) => { OnTextChanged(EventArgs.Empty); SyncInnerEditorState(); Invalidate(); };
            _inner.KeyDown += (s, e) => OnKeyDown(e);
            _inner.KeyPress += (s, e) => OnKeyPress(e);
            _inner.KeyUp += (s, e) => OnKeyUp(e);

            SupeyThemeManager.ThemeChanged += OnThemeChanged;

            // After _inner exists — Font assignment triggers OnFontChanged.
            Font = SupeyTheme.BodyFont;
            UpdateHeight();
            SyncInnerEditorState();
        }

        private void StartFocusAnim()
        {
            if (!_focusAnimTimer.Enabled)
                _focusAnimTimer.Start();
        }

        private void FocusAnimTick(object sender, EventArgs e)
        {
            float target = _focused ? 1f : 0f;
            const float step = 0.12f;
            if (_focusAnim < target) _focusAnim = Math.Min(target, _focusAnim + step);
            else if (_focusAnim > target) _focusAnim = Math.Max(target, _focusAnim - step);
            Invalidate();
            if (Math.Abs(_focusAnim - target) < 0.001f) { _focusAnim = target; _focusAnimTimer.Stop(); }
        }

        /// <summary>Raised when the trailing icon is clicked (e.g. reveal password).</summary>
        public event EventHandler TrailingIconClick;

        [Browsable(true)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
        public override string Text
        {
            get => _inner?.Text ?? string.Empty;
            set
            {
                if (_inner == null) return;
                _inner.Text = value ?? string.Empty;
                SyncInnerEditorState();
                Invalidate();
            }
        }

        public string Hint
        {
            get => _hint;
            set { _hint = value ?? string.Empty; SyncInnerEditorState(); Invalidate(); }
        }

        public Image LeadingIcon
        {
            get => _leadingIcon;
            set { _leadingIcon = value; UpdateRects(); Invalidate(); }
        }

        public Image TrailingIcon
        {
            get => _trailingIcon;
            set { _trailingIcon = value; UpdateRects(); Invalidate(); }
        }

        /// <summary>Tall fields keep the floating hint visible above the text (Material UseTallSize).</summary>
        public bool UseTallSize
        {
            get => _useTallSize;
            set { _useTallSize = value; UpdateHeight(); UpdateRects(); Invalidate(); }
        }

        public bool Password
        {
            get => _inner.UseSystemPasswordChar;
            set => _inner.UseSystemPasswordChar = value;
        }

        public bool Multiline
        {
            get => _inner.Multiline;
            set => _inner.Multiline = value;
        }

        public int MaxLength
        {
            get => _inner.MaxLength;
            set => _inner.MaxLength = value;
        }

        public bool ReadOnly
        {
            get => _inner.ReadOnly;
            set
            {
                if (_inner == null) return;
                _inner.ReadOnly = value;
                SyncInnerEditorState();
            }
        }

        public new bool Enabled
        {
            get => base.Enabled;
            set
            {
                base.Enabled = value;
                SyncInnerEditorState();
                Invalidate();
            }
        }

        public override Font Font
        {
            get => base.Font;
            set
            {
                base.Font = value;
                if (_inner == null) return;
                _inner.Font = value;
                UpdateRects();
                Invalidate();
            }
        }

        // ── Designer-compat (MaterialTextBox / MaterialTextBox2) ─────────────────
        public enum PrefixSuffixTypes { None, Prefix, Suffix }

        public bool AnimateReadOnly { get; set; }
        public int Depth { get; set; }
        public SupeyMouseState MouseState { get; set; } = SupeyMouseState.OUT;
        public bool UseAccent { get; set; } = true;
        public string HelperText { get; set; } = string.Empty;
        public PrefixSuffixTypes PrefixSuffix { get; set; } = PrefixSuffixTypes.None;
        public string PrefixSuffixText { get; set; } = string.Empty;

        /// <summary>Designer compat — border is painted by this control, not the inner edit.</summary>
        [Browsable(false)]
        public BorderStyle BorderStyle { get; set; } = BorderStyle.None;

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (_inner != null)
                _inner.TabIndex = TabIndex;
        }

        private void OnThemeChanged(object sender, EventArgs e)
        {
            if (IsDisposed || _inner == null) return;
            BackColor = SupeyTheme.Surface;
            ForeColor = SupeyTheme.TextPrimary;
            _inner.BackColor = SupeyTheme.Surface;
            _inner.ForeColor = SupeyTheme.TextPrimary;
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

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            UpdateHeight();
            UpdateRects();
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            if (_inner == null) return;
            _inner.Font = Font;
            UpdateRects();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (_trailingIcon != null && _trailingBounds.Contains(e.Location))
            {
                TrailingIconClick?.Invoke(this, EventArgs.Empty);
                return;
            }
            if (_inner == null) return;
            if (!_inner.Focused)
                _inner.Focus();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.None;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            using (var bg = new SolidBrush(SupeyTheme.Surface))
                g.FillRectangle(bg, 0, 0, Width, _lineY);

            if (_inner != null)
                _inner.BackColor = SupeyTheme.Surface;

            if (_leadingIcon != null)
                g.DrawImage(_leadingIcon, _leadingBounds);
            if (_trailingIcon != null)
                g.DrawImage(_trailingIcon, _trailingBounds);

            bool hasHint = !string.IsNullOrEmpty(_hint);
            bool userText = !string.IsNullOrEmpty(Text);
            bool floatHint = hasHint && _useTallSize && userText;

            var hintRect = new Rectangle(_leftPad, HintSmallY, Width - _leftPad - _rightPad, HintSmallH);

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
        }

        /// <summary>Keep the inner editor visible; parent <see cref="Control.Visible"/> handles show/hide.</summary>
        private void SyncInnerEditorState()
        {
            if (_inner == null) return;

            _inner.Visible = true;
            _inner.Enabled = Enabled && !ReadOnly;
            UpdateRects();

            if (_inner.IsHandleCreated)
            {
                bool showCue = string.IsNullOrEmpty(Text) && !string.IsNullOrEmpty(_hint);
                SendMessage(_inner.Handle, EM_SETCUEBANNER, (IntPtr)1, showCue ? _hint : string.Empty);
            }
        }

        private void UpdateHeight()
        {
            int h = _useTallSize ? TallHeight : ShortHeight;
            if (Height != h)
                Height = h;
            _lineY = Height - ActivationH;
        }

        private void UpdateRects()
        {
            if (_inner == null) return;
            _leftPad = _leadingIcon != null ? LeftPadding + IconSize : LeftPadding;
            _rightPad = _trailingIcon != null ? RightPadding + IconSize : RightPadding;

            bool hasHint = !string.IsNullOrEmpty(_hint);
            bool floatHint = hasHint && _useTallSize && !string.IsNullOrEmpty(Text);

            int textY = floatHint ? 22 : Math.Max(0, (_lineY / 2) - (FontHeight / 2));
            _inner.SetBounds(_leftPad, textY, Math.Max(20, Width - _leftPad - _rightPad), FontHeight);

            int iconY = (_lineY / 2) - (IconSize / 2);
            _leadingBounds = new Rectangle(8, iconY, IconSize, IconSize);
            _trailingBounds = new Rectangle(Width - IconSize - 8, iconY, IconSize, IconSize);
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
