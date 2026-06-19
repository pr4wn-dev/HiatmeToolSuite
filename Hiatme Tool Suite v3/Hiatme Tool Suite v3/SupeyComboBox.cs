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
    internal sealed class SupeyComboBox : ComboBox
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
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        /// <summary>Themed border color drawn around the closed control.</summary>
        public Color BorderColor { get; set; } = SupeyTheme.BorderSubtle;

        /// <summary>Color of the dropdown chevron.</summary>
        public Color ArrowColor { get; set; } = SupeyTheme.AccentPrimary;

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

            // Background.
            using (var fill = new SolidBrush(BackColor))
                g.FillRectangle(fill, 0, 0, Width, Height);

            // Selected text, left-padded and vertically centered.
            string text = Text;
            int textRight = Width - ArrowZoneWidth - 2;
            if (!string.IsNullOrEmpty(text) && textRight > 8)
            {
                var textRect = new Rectangle(8, 0, textRight - 8, Height);
                TextRenderer.DrawText(g, text, Font, textRect,
                    Enabled ? ForeColor : SupeyTheme.TextMuted,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
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
