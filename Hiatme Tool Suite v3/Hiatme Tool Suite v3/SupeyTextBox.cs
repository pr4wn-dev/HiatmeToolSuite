using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Dark, theme-driven single-line text field — the Supey replacement for MaterialTextBox /
    /// MaterialTextBox2. It owns its border (a 1px themed line that turns lime on focus), an optional
    /// leading and trailing icon, a placeholder <see cref="Hint"/>, and a <see cref="Password"/> toggle
    /// with a <see cref="TrailingIconClick"/> event (used for the "reveal password" eye). The text area
    /// itself is a real Win32 edit, so <c>Text</c>, <c>TextChanged</c>, key events and tab order all
    /// behave natively. MaterialSkin-only members (<c>UseTallSize</c>, <c>AnimateReadOnly</c>,
    /// <c>Depth</c>, <c>MouseState</c>) are accepted as no-ops for Designer compatibility.
    /// </summary>
    internal class SupeyTextBox : TextBox
    {
        private const int WM_NCCALCSIZE = 0x0083;
        private const int WM_NCPAINT = 0x0085;
        private const int WM_NCHITTEST = 0x0084;
        private const int WM_NCLBUTTONDOWN = 0x00A1;
        private const int WM_PAINT = 0x000F;
        private const int IconSize = 16;
        private const int Pad = 6;
        private const int LabelH = 16;     // height reserved for the floating label
        private const int Underline = 2;   // accent underline thickness

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        private const int EM_SETCUEBANNER = 0x1501;

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindowDC(IntPtr hWnd);
        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        private Image _leadingIcon;
        private Image _trailingIcon;
        private string _hint = string.Empty;
        private Font _labelFont;
        private readonly Timer _focusAnimTimer;
        private float _focus;   // 0 = blurred, 1 = focused (drives the underline grow + label colour)

        public SupeyTextBox()
        {
            BorderStyle = BorderStyle.None;
            BackColor = SupeyTheme.Surface;
            ForeColor = SupeyTheme.TextPrimary;
            Font = SupeyTheme.BodyFont;
            _focusAnimTimer = new Timer { Interval = 15 };
            _focusAnimTimer.Tick += FocusAnimTick;
            SupeyThemeManager.ThemeChanged += OnThemeChanged;
        }

        private Font LabelFont
        {
            get
            {
                if (_labelFont == null)
                {
                    float size = Math.Max(7f, Font.Size * 0.72f);
                    _labelFont = new Font(Font.FontFamily, size, FontStyle.Regular, Font.Unit);
                }
                return _labelFont;
            }
        }

        /// <summary>True when the field is tall enough to host the Material floating label above the text.</summary>
        private bool UseFloatingLabel => !string.IsNullOrEmpty(_hint) && Height >= 40;

        private void FocusAnimTick(object sender, EventArgs e)
        {
            float target = Focused ? 1f : 0f;
            const float step = 0.18f;
            if (_focus < target) _focus = Math.Min(target, _focus + step);
            else if (_focus > target) _focus = Math.Max(target, _focus - step);
            RedrawBorder();
            if (Math.Abs(_focus - target) < 0.001f) { _focus = target; _focusAnimTimer.Stop(); }
        }

        /// <summary>Raised when the trailing icon is clicked (e.g. the reveal-password eye).</summary>
        public event EventHandler TrailingIconClick;

        public string Hint
        {
            get => _hint;
            set { _hint = value ?? string.Empty; ApplyCueBanner(); RecalcBorder(); }
        }

        private void ApplyCueBanner()
        {
            if (!IsHandleCreated) return;
            // When the field is tall enough for a floating label we show that label instead of an
            // in-line cue banner; otherwise the cue banner is the resting placeholder.
            string banner = UseFloatingLabel ? string.Empty : _hint;
            SendMessage(Handle, EM_SETCUEBANNER, (IntPtr)0, banner);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyCueBanner();
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            _labelFont?.Dispose();
            _labelFont = null;
            RecalcBorder();
        }

        public Image LeadingIcon
        {
            get => _leadingIcon;
            set { _leadingIcon = value; RecalcBorder(); }
        }

        public Image TrailingIcon
        {
            get => _trailingIcon;
            set { _trailingIcon = value; RecalcBorder(); }
        }

        /// <summary>When true the field masks input (password). Mirrors MaterialTextBox.Password.</summary>
        public bool Password
        {
            get => UseSystemPasswordChar;
            set { UseSystemPasswordChar = value; }
        }

        // ── Designer-compat no-ops (MaterialTextBox / MaterialTextBox2 members) ────
        public enum PrefixSuffixTypes { None, Prefix, Suffix }

        public bool UseTallSize { get; set; }
        public bool AnimateReadOnly { get; set; }
        public int Depth { get; set; }
        public SupeyMouseState MouseState { get; set; } = SupeyMouseState.OUT;
        public bool UseAccent { get; set; }
        public string HelperText { get; set; } = string.Empty;
        public PrefixSuffixTypes PrefixSuffix { get; set; } = PrefixSuffixTypes.None;
        public string PrefixSuffixText { get; set; } = string.Empty;

        private int LeftInset => 1 + Pad + (_leadingIcon != null ? IconSize + Pad : 0);
        private int RightInset => 1 + Pad + (_trailingIcon != null ? IconSize + Pad : 0);

        private void OnThemeChanged(object sender, EventArgs e)
        {
            if (IsDisposed) return;
            BackColor = SupeyTheme.Surface;
            ForeColor = SupeyTheme.TextPrimary;
            RedrawBorder();
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                SupeyThemeManager.ThemeChanged -= OnThemeChanged;
                _focusAnimTimer?.Stop();
                _focusAnimTimer?.Dispose();
                _labelFont?.Dispose();
            }
            base.Dispose(disposing);
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); if (!_focusAnimTimer.Enabled) _focusAnimTimer.Start(); RedrawBorder(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); if (!_focusAnimTimer.Enabled) _focusAnimTimer.Start(); RedrawBorder(); }
        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); RedrawBorder(); }

        private void RecalcBorder()
        {
            if (IsHandleCreated)
                SetWindowPos();
            Invalidate();
        }

        private void SetWindowPos()
        {
            // Force a non-client recalc so the new icon insets take effect.
            const int SWP_NOMOVE = 0x2, SWP_NOSIZE = 0x1, SWP_NOZORDER = 0x4, SWP_FRAMECHANGED = 0x20;
            SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_FRAMECHANGED);
        }

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        protected override void WndProc(ref Message m)
        {
            switch (m.Msg)
            {
                case WM_NCCALCSIZE:
                    base.WndProc(ref m);
                    if (m.WParam != IntPtr.Zero)
                    {
                        var rect = (RECT)Marshal.PtrToStructure(m.LParam, typeof(RECT));
                        int topStrip = UseFloatingLabel ? LabelH : 0;
                        int bottomStrip = Underline + 2;
                        int avail = (rect.Bottom - rect.Top) - topStrip - bottomStrip;
                        int v = Math.Max(1, (avail - Font.Height) / 2);
                        rect.Left += LeftInset;
                        rect.Right -= RightInset;
                        rect.Top += topStrip + v;
                        rect.Bottom -= bottomStrip + v;
                        Marshal.StructureToPtr(rect, m.LParam, false);
                    }
                    return;

                case WM_NCPAINT:
                    base.WndProc(ref m);
                    PaintBorder();
                    return;

                case WM_NCHITTEST:
                    base.WndProc(ref m);
                    // Tag the trailing-icon zone so we can route the click in WM_NCLBUTTONDOWN.
                    if (_trailingIcon != null)
                    {
                        Point screen = new Point(m.LParam.ToInt32());
                        Point client = PointToClient(screen);
                        if (client.X >= Width - RightInset)
                            m.Result = (IntPtr)18; // HTBORDER-ish custom marker
                    }
                    return;

                case WM_NCLBUTTONDOWN:
                    if ((int)m.WParam == 18 && _trailingIcon != null)
                    {
                        TrailingIconClick?.Invoke(this, EventArgs.Empty);
                        return;
                    }
                    break;
            }
            base.WndProc(ref m);
        }

        private void RedrawBorder()
        {
            if (IsHandleCreated) PaintBorder();
        }

        private void PaintBorder()
        {
            if (!IsHandleCreated) return;
            IntPtr hdc = GetWindowDC(Handle);
            if (hdc == IntPtr.Zero) return;
            try
            {
                using (var g = Graphics.FromHdc(hdc))
                {
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

                    // Exclude the inner edit area so animation repaints never erase the live text.
                    int topStrip = UseFloatingLabel ? LabelH : 0;
                    int bottomStrip = Underline + 2;
                    int avail = Height - topStrip - bottomStrip;
                    int v = Math.Max(1, (avail - Font.Height) / 2);
                    var clientRect = Rectangle.FromLTRB(LeftInset, topStrip + v, Width - RightInset, Height - bottomStrip - v);
                    g.ExcludeClip(clientRect);

                    using (var b = new SolidBrush(SupeyTheme.Surface))
                    {
                        // Fill the non-client frame (the area outside the inset text rect).
                        g.FillRectangle(b, 0, 0, Width, Height);
                    }

                    // Baseline: a 1px resting line that the accent underline grows over on focus.
                    int baseY = Height - Underline;
                    using (var pen = new Pen(SupeyTheme.BorderSubtle))
                        g.DrawLine(pen, 0, baseY, Width, baseY);

                    // Accent underline grows from the centre as the field gains focus.
                    if (_focus > 0f)
                    {
                        int half = (int)(Width / 2f * _focus);
                        int cx = Width / 2;
                        using (var b = new SolidBrush(SupeyTheme.AccentPrimary))
                            g.FillRectangle(b, cx - half, Height - Underline, half * 2, Underline);
                    }

                    // Floating label (when tall enough); colour shifts to accent on focus.
                    if (UseFloatingLabel)
                    {
                        Color labelColor = Blend(SupeyTheme.TextSecondary, SupeyTheme.AccentPrimary, _focus);
                        using (var b = new SolidBrush(labelColor))
                            g.DrawString(_hint, LabelFont, b, LeftInset - 1, 2f);
                    }

                    int cyIcon = (Height - IconSize) / 2;
                    if (_leadingIcon != null)
                        g.DrawImage(_leadingIcon, 1 + Pad, cyIcon, IconSize, IconSize);
                    if (_trailingIcon != null)
                        g.DrawImage(_trailingIcon, Width - 1 - Pad - IconSize, cyIcon, IconSize, IconSize);
                }
            }
            finally
            {
                ReleaseDC(Handle, hdc);
            }
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
