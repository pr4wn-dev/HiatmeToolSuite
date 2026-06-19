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

        public SupeyTextBox()
        {
            BorderStyle = BorderStyle.None;
            BackColor = SupeyTheme.Surface;
            ForeColor = SupeyTheme.TextPrimary;
            Font = SupeyTheme.BodyFont;
            SupeyThemeManager.ThemeChanged += OnThemeChanged;
        }

        /// <summary>Raised when the trailing icon is clicked (e.g. the reveal-password eye).</summary>
        public event EventHandler TrailingIconClick;

        public string Hint
        {
            get => _hint;
            set { _hint = value ?? string.Empty; ApplyCueBanner(); Invalidate(); }
        }

        private void ApplyCueBanner()
        {
            if (IsHandleCreated)
                SendMessage(Handle, EM_SETCUEBANNER, (IntPtr)1, _hint);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyCueBanner();
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
                SupeyThemeManager.ThemeChanged -= OnThemeChanged;
            base.Dispose(disposing);
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); RedrawBorder(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); RedrawBorder(); Invalidate(); }
        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }

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
                        int v = Math.Max(1, (rect.Bottom - rect.Top - Font.Height) / 2);
                        rect.Left += LeftInset;
                        rect.Right -= RightInset;
                        rect.Top += v;
                        rect.Bottom -= v;
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
                    var full = new Rectangle(0, 0, Width, Height);
                    using (var b = new SolidBrush(SupeyTheme.Surface))
                    {
                        // Fill the non-client frame (the area outside the inset text rect).
                        g.FillRectangle(b, 0, 0, Width, Height);
                    }

                    Color borderColor = Focused ? SupeyTheme.AccentPrimary : SupeyTheme.BorderSubtle;
                    using (var pen = new Pen(borderColor))
                        g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);

                    int cy = (Height - IconSize) / 2;
                    if (_leadingIcon != null)
                        g.DrawImage(_leadingIcon, 1 + Pad, cy, IconSize, IconSize);
                    if (_trailingIcon != null)
                        g.DrawImage(_trailingIcon, Width - 1 - Pad - IconSize, cy, IconSize, IconSize);
                }
            }
            finally
            {
                ReleaseDC(Handle, hdc);
            }
        }

    }
}
