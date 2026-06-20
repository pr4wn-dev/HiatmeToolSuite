using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// A flat, dark-themed <see cref="ComboBox"/> that fully owns its closed-state paint so it
    /// agrees with the SupeyTheme surfaces. The stock <see cref="FlatStyle.Flat"/> combo lets
    /// Windows render a light-gray 3D border and a gray arrow button; overpainting that chrome
    /// afterwards flickers and leaves gray artifacts in the corners. Instead we intercept
    /// <c>WM_PAINT</c>, draw the whole closed control into an off-screen buffer (background,
    /// selected text, themed border, lime accent chevron) and blit it in one pass — no base
    /// button paint, so no flicker and no gray corner. The dropdown list rows are still rendered
    /// by the owner-draw handler the caller wires up (a separate popup window / WM_DRAWITEM path).
    /// </summary>
    public sealed class SupeyComboBox : ComboBox
    {
        private const int WM_PAINT = 0x000F;
        private const int WM_ERASEBKGND = 0x0014;
        private const int ArrowZoneWidth = 22;

        public SupeyComboBox()
        {
            DropDownStyle = ComboBoxStyle.DropDownList;
            FlatStyle = FlatStyle.Flat;
            DrawMode = DrawMode.OwnerDrawFixed;
            DoubleBuffered = true;
            BackColor = SupeyTheme.Surface;
            ForeColor = SupeyTheme.TextPrimary;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SupeyThemeManager.ThemeChanged += OnThemeChanged;
        }

        private void OnThemeChanged(object sender, EventArgs e)
        {
            if (IsDisposed) return;
            BackColor = SupeyTheme.Surface;
            ForeColor = SupeyTheme.TextPrimary;
            BorderColor = SupeyTheme.BorderSubtle;
            ArrowColor = SupeyTheme.AccentPrimary;
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                SupeyThemeManager.ThemeChanged -= OnThemeChanged;
            base.Dispose(disposing);
        }

        /// <summary>Themed border color drawn around the closed control.</summary>
        public Color BorderColor { get; set; } = SupeyTheme.BorderSubtle;

        /// <summary>Color of the dropdown chevron.</summary>
        public Color ArrowColor { get; set; } = SupeyTheme.AccentPrimary;

        // ── MaterialComboBox Designer-compat shims ────────────────────────────────
        /// <summary>Accepted for Designer compatibility (MaterialSkin elevation); unused.</summary>
        public int Depth { get; set; }
        /// <summary>Accepted for Designer compatibility (MaterialSkin tracked mouse state); unused.</summary>
        public SupeyMouseState MouseState { get; set; } = SupeyMouseState.OUT;
        /// <summary>Accepted for Designer compatibility (MaterialComboBox auto-resize); unused.</summary>
        public bool AutoResize { get; set; }
        /// <summary>Accepted for Designer compatibility (MaterialComboBox start index); unused.</summary>
        public int StartIndex { get; set; }

        private string _hint = string.Empty;
        /// <summary>Placeholder shown when no item is selected.</summary>
        public string Hint
        {
            get => _hint;
            set { _hint = value ?? string.Empty; Invalidate(); }
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0) { base.OnDrawItem(e); return; }
            bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            Color back = selected ? SupeyTheme.ListSelected : SupeyTheme.ListBody;
            Color fore = selected ? SupeyTheme.ListSelectedText : SupeyTheme.ListText;
            using (var b = new SolidBrush(back)) e.Graphics.FillRectangle(b, e.Bounds);
            string text = GetItemText(Items[e.Index]);
            TextRenderer.DrawText(e.Graphics, text, Font,
                new Rectangle(e.Bounds.X + 6, e.Bounds.Y, e.Bounds.Width - 8, e.Bounds.Height), fore,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        protected override void OnSelectedIndexChanged(EventArgs e)
        {
            base.OnSelectedIndexChanged(e);
            Invalidate();
        }

        protected override void WndProc(ref Message m)
        {
            switch (m.Msg)
            {
                case WM_ERASEBKGND:
                    // We fully paint in WM_PAINT; skip the erase to avoid a flash.
                    m.Result = (IntPtr)1;
                    return;

                case WM_PAINT:
                    if (IsHandleCreated && !IsDisposed && Width > 0 && Height > 0)
                    {
                        var ps = new PAINTSTRUCT();
                        IntPtr hdc = BeginPaint(Handle, ref ps);
                        try
                        {
                            using (var buffer = new Bitmap(Width, Height))
                            {
                                using (var bg = Graphics.FromImage(buffer))
                                    DrawClosed(bg);
                                using (var target = Graphics.FromHdc(hdc))
                                    target.DrawImageUnscaled(buffer, 0, 0);
                            }
                        }
                        finally
                        {
                            EndPaint(Handle, ref ps);
                        }
                        m.Result = IntPtr.Zero;
                        return;
                    }
                    break;
            }

            base.WndProc(ref m);
        }

        private void DrawClosed(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.None;

            // Background — pull straight from the theme so a stale Designer BackColor (e.g. the white
            // left over from the MaterialSkin era) can never leak through.
            using (var fill = new SolidBrush(SupeyTheme.Surface))
                g.FillRectangle(fill, 0, 0, Width, Height);

            // Selected text (or hint placeholder), left-padded and vertically centered.
            string text = Text;
            int textRight = Width - ArrowZoneWidth - 2;
            if (textRight > 8)
            {
                bool hasText = !string.IsNullOrEmpty(text);
                string shown = hasText ? text : _hint;
                if (!string.IsNullOrEmpty(shown))
                {
                    Color fore = !Enabled ? SupeyTheme.TextMuted
                        : hasText ? SupeyTheme.TextPrimary : SupeyTheme.TextMuted;
                    var textRect = new Rectangle(8, 0, textRight - 8, Height);
                    TextRenderer.DrawText(g, shown, Font, textRect, fore,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                }
            }

            // Lime accent chevron, centered in the arrow zone.
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int cx = Width - (ArrowZoneWidth / 2) - 1;
            int cy = Height / 2;
            Point[] chevron =
            {
                new Point(cx - 5, cy - 2),
                new Point(cx + 5, cy - 2),
                new Point(cx, cy + 4),
            };
            using (var arrow = new SolidBrush(Enabled ? ArrowColor : SupeyTheme.TextMuted))
                g.FillPolygon(arrow, chevron);

            // Crisp 1px themed border.
            g.SmoothingMode = SmoothingMode.None;
            using (var pen = new Pen(BorderColor))
                g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }

        [DllImport("user32.dll")]
        private static extern IntPtr BeginPaint(IntPtr hWnd, ref PAINTSTRUCT lpPaint);

        [DllImport("user32.dll")]
        private static extern bool EndPaint(IntPtr hWnd, ref PAINTSTRUCT lpPaint);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PAINTSTRUCT
        {
            public IntPtr hdc;
            public bool fErase;
            public RECT rcPaint;
            public bool fRestore;
            public bool fIncUpdate;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
            public byte[] rgbReserved;
        }
    }
}
