using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Custom-chrome replacement for MaterialSkin's <c>MaterialForm</c>. Renders its own dark,
    /// theme-driven title bar (title + min / max / close) and frame entirely from the
    /// <see cref="SupeyTheme"/> palette so nothing MaterialSkin paints can leak its fixed gray.
    ///
    /// To keep every existing Designer layout valid, the client inset is identical to MaterialForm's
    /// (a 64px top "app bar" = 24 status + 40 action, and a 3px frame), exposed via
    /// <see cref="Padding"/> = (3, 64, 3, 3). Window behaviors people expect — drag, double-click to
    /// maximize, Aero snap, edge/corner resize, taskbar minimize, maximize-to-work-area — are handled
    /// through standard Win32 hit-testing while we draw the visuals ourselves.
    /// </summary>
    public class SupeyForm : Form
    {
        /// <summary>Top inset reserved for the title bar; matches MaterialForm (24 + 40).</summary>
        public const int TitleBarHeight = 64;
        private const int FrameWidth = 3;
        private const int ResizeBorder = 6;
        private const int ButtonWidth = 46;

        // ── Win32 ────────────────────────────────────────────────────────────────
        private const int WM_NCHITTEST = 0x0084;
        private const int WM_GETMINMAXINFO = 0x0024;
        private const int WM_NCCALCSIZE = 0x0083;
        private const int WM_NCACTIVATE = 0x0086;
        private const int WM_NCPAINT = 0x0085;

        private const int GWL_STYLE = -16;

        private const int WS_MINIMIZEBOX = 0x00020000;
        private const int WS_SYSMENU = 0x00080000;
        private const int WS_SIZEBOX = 0x00040000;
        private const int CS_DBLCLKS = 0x0008;

        private const int HTCLIENT = 1;
        private const int HTCAPTION = 2;
        private const int HTLEFT = 10;
        private const int HTRIGHT = 11;
        private const int HTTOP = 12;
        private const int HTTOPLEFT = 13;
        private const int HTTOPRIGHT = 14;
        private const int HTBOTTOM = 15;
        private const int HTBOTTOMLEFT = 16;
        private const int HTBOTTOMRIGHT = 17;

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int x; public int y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MINMAXINFO
        {
            public POINT ptReserved;
            public POINT ptMaxSize;
            public POINT ptMaxPosition;
            public POINT ptMinTrackSize;
            public POINT ptMaxTrackSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int left, top, right, bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public int dwFlags;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int dwFlags);

        [DllImport("user32.dll")]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
        private static extern IntPtr GetWindowLong32(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
        private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
        private static extern IntPtr SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        private static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex)
            => IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, nIndex) : GetWindowLong32(hWnd, nIndex);

        private static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
            => IntPtr.Size == 8
                ? SetWindowLongPtr64(hWnd, nIndex, dwNewLong)
                : SetWindowLong32(hWnd, nIndex, dwNewLong.ToInt32());

        // Which window button the cursor is over (0 = min, 1 = max, 2 = close, -1 = none).
        private int _hoverButton = -1;

        /// <summary>Extra left padding for the title text so it clears a leading control (e.g. the
        /// drawer hamburger) placed at the top-left of the title bar.</summary>
        public int TitleLeftInset { get; set; }

        public SupeyForm()
        {
            // Match MaterialForm: no UserPaint — we paint chrome in OnPaint but child controls
            // still compose normally. UserPaint on a Form causes classic/theme flicker.
            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw,
                true);

            // We draw the frame ourselves; behaviors come from the styles in CreateParams below.
            FormBorderStyle = FormBorderStyle.None;
            DoubleBuffered = true;
            BackColor = SupeyTheme.SurfaceBase;
            ForeColor = SupeyTheme.TextPrimary;
            Font = SupeyTheme.BodyFont;
            // Identical inset to MaterialForm so every Designer child coordinate stays valid.
            Padding = new Padding(FrameWidth, TitleBarHeight, FrameWidth, FrameWidth);

            SupeyThemeManager.ThemeChanged += OnSupeyThemeChanged;
        }

        private void OnSupeyThemeChanged(object sender, EventArgs e)
        {
            if (IsDisposed) return;
            BackColor = SupeyTheme.SurfaceBase;
            ForeColor = SupeyTheme.TextPrimary;
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                SupeyThemeManager.ThemeChanged -= OnSupeyThemeChanged;
            base.Dispose(disposing);
        }

        /// <summary>
        /// Runs <paramref name="action"/> asynchronously on the UI thread once the window handle
        /// exists. Safe to call from a constructor (before the handle is created): MaterialForm used
        /// to create its handle during construction, so code could <c>BeginInvoke</c> straight from the
        /// ctor; SupeyForm doesn't, so a raw <c>BeginInvoke</c> there throws "Invoke or BeginInvoke
        /// cannot be called ... until the window handle has been created." This defers to
        /// <see cref="Control.HandleCreated"/> when needed and behaves like a plain BeginInvoke once
        /// the handle is up.
        /// </summary>
        public void RunWhenReady(Action action)
        {
            if (action == null) return;
            if (IsHandleCreated)
            {
                BeginInvoke(action);
                return;
            }
            EventHandler handler = null;
            handler = (s, e) =>
            {
                HandleCreated -= handler;
                BeginInvoke(action);
            };
            HandleCreated += handler;
        }

        /// <summary>
        /// Keep native minimize + system menu. WS_SIZEBOX is added in <see cref="OnCreateControl"/>
        /// after the handle exists (MaterialForm pattern) so Aero snap works without painting a
        /// classic sizing frame during initial layout.
        /// </summary>
        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.Style |= WS_MINIMIZEBOX | WS_SYSMENU;
                cp.ClassStyle |= CS_DBLCLKS;
                return cp;
            }
        }

        protected override void OnCreateControl()
        {
            base.OnCreateControl();
            if (DesignMode || !IsHandleCreated) return;
            long style = GetWindowLongPtr(Handle, GWL_STYLE).ToInt64();
            SetWindowLongPtr(Handle, GWL_STYLE, (IntPtr)(style | WS_SIZEBOX));
        }

        // ── Window-button geometry ────────────────────────────────────────────────
        private Rectangle CloseRect => new Rectangle(ClientSize.Width - ButtonWidth, 0, ButtonWidth, 30);
        private Rectangle MaxRect => new Rectangle(ClientSize.Width - ButtonWidth * 2, 0, ButtonWidth, 30);
        private Rectangle MinRect => new Rectangle(ClientSize.Width - ButtonWidth * 3, 0, ButtonWidth, 30);

        private bool HasMax => MaximizeBox && ControlBox;
        private bool HasMin => MinimizeBox && ControlBox;

        protected override void WndProc(ref Message m)
        {
            // MaterialForm pattern: swallow NC calc so the whole window is client area (no white
            // sizing frame). Swallow NC activate/paint so Windows never flashes classic chrome.
            if (m.Msg == WM_NCCALCSIZE)
                return;

            if (m.Msg == WM_NCACTIVATE)
            {
                // -1 tells DefWindowProc not to repaint the non-client activation frame.
                m.Result = (IntPtr)(-1);
                return;
            }

            if (m.Msg == WM_NCPAINT)
            {
                m.Result = IntPtr.Zero;
                return;
            }

            if (m.Msg == WM_NCHITTEST)
            {
                m.Result = (IntPtr)HitTest(PointToClient(Cursor.Position));
                return;
            }

            if (m.Msg == WM_GETMINMAXINFO)
            {
                base.WndProc(ref m);
                AdjustMaximizedBounds(m.HWnd, m.LParam);
                return;
            }

            base.WndProc(ref m);
        }

        private int HitTest(Point p)
        {
            bool maximized = WindowState == FormWindowState.Maximized;

            // Window buttons take priority so clicks route to our mouse handlers (HTCLIENT).
            if (ControlBox && p.Y <= 30)
            {
                if (CloseRect.Contains(p)) return HTCLIENT;
                if (HasMax && MaxRect.Contains(p)) return HTCLIENT;
                if (HasMin && MinRect.Contains(p)) return HTCLIENT;
            }

            if (!maximized)
            {
                bool left = p.X <= ResizeBorder;
                bool right = p.X >= ClientSize.Width - ResizeBorder;
                bool top = p.Y <= ResizeBorder;
                bool bottom = p.Y >= ClientSize.Height - ResizeBorder;

                if (top && left) return HTTOPLEFT;
                if (top && right) return HTTOPRIGHT;
                if (bottom && left) return HTBOTTOMLEFT;
                if (bottom && right) return HTBOTTOMRIGHT;
                if (left) return HTLEFT;
                if (right) return HTRIGHT;
                if (top) return HTTOP;
                if (bottom) return HTBOTTOM;
            }

            // Anywhere else in the title bar drags / double-click-maximizes / snaps the window.
            if (p.Y <= TitleBarHeight) return HTCAPTION;
            return HTCLIENT;
        }

        /// <summary>Constrain a borderless maximize to the monitor work area (don't cover the taskbar).</summary>
        private void AdjustMaximizedBounds(IntPtr hwnd, IntPtr lParam)
        {
            const int MONITOR_DEFAULTTONEAREST = 2;
            IntPtr monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            if (monitor == IntPtr.Zero) return;

            var mi = new MONITORINFO { cbSize = Marshal.SizeOf(typeof(MONITORINFO)) };
            if (!GetMonitorInfo(monitor, ref mi)) return;

            var mmi = (MINMAXINFO)Marshal.PtrToStructure(lParam, typeof(MINMAXINFO));
            RECT work = mi.rcWork, mon = mi.rcMonitor;
            mmi.ptMaxPosition.x = Math.Abs(work.left - mon.left);
            mmi.ptMaxPosition.y = Math.Abs(work.top - mon.top);
            mmi.ptMaxSize.x = Math.Abs(work.right - work.left);
            mmi.ptMaxSize.y = Math.Abs(work.bottom - work.top);
            Marshal.StructureToPtr(mmi, lParam, true);
        }

        // ── Window-button interaction ───────────────────────────────────────────────
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int prev = _hoverButton;
            _hoverButton = -1;
            if (ControlBox && e.Y <= 30)
            {
                if (CloseRect.Contains(e.Location)) _hoverButton = 2;
                else if (HasMax && MaxRect.Contains(e.Location)) _hoverButton = 1;
                else if (HasMin && MinRect.Contains(e.Location)) _hoverButton = 0;
            }
            if (prev != _hoverButton) Invalidate(new Rectangle(ClientSize.Width - ButtonWidth * 3, 0, ButtonWidth * 3, 30));
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hoverButton != -1)
            {
                _hoverButton = -1;
                Invalidate(new Rectangle(ClientSize.Width - ButtonWidth * 3, 0, ButtonWidth * 3, 30));
            }
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button != MouseButtons.Left || !ControlBox || e.Y > 30) return;

            if (CloseRect.Contains(e.Location)) { Close(); return; }
            if (HasMax && MaxRect.Contains(e.Location))
            {
                WindowState = WindowState == FormWindowState.Maximized
                    ? FormWindowState.Normal : FormWindowState.Maximized;
                return;
            }
            if (HasMin && MinRect.Contains(e.Location)) WindowState = FormWindowState.Minimized;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Invalidate();
        }

        // ── Painting ─────────────────────────────────────────────────────────────
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // Paint the base ourselves — do not delegate to Control which flashes the system color.
            using (var body = new SolidBrush(SupeyTheme.SurfaceBase))
                e.Graphics.FillRectangle(body, ClientRectangle);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;

            g.Clear(SupeyTheme.SurfaceBase);
            using (var bar = new SolidBrush(SupeyTheme.SurfaceHeader))
                g.FillRectangle(bar, 0, 0, ClientSize.Width, TitleBarHeight);

            // Thin accent line under the title bar to separate chrome from content.
            using (var divider = new Pen(SupeyTheme.Divider))
                g.DrawLine(divider, 0, TitleBarHeight - 1, ClientSize.Width, TitleBarHeight - 1);

            // Outer frame.
            using (var frame = new Pen(SupeyTheme.BorderSubtle))
                g.DrawRectangle(frame, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);

            DrawTitle(g);
            DrawWindowButtons(g);
        }

        private void DrawTitle(Graphics g)
        {
            int x = 16 + TitleLeftInset;
            if (ShowIcon && Icon != null)
            {
                using (var bmp = Icon.ToBitmap())
                    g.DrawImage(bmp, x, (TitleBarHeight - 20) / 2, 20, 20);
                x += 28;
            }

            var rect = new Rectangle(x, 0, ClientSize.Width - x - ButtonWidth * 3 - 8, TitleBarHeight);
            TextRenderer.DrawText(g, Text ?? string.Empty, SupeyTheme.HeaderFont, rect,
                SupeyTheme.TextPrimary,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter
                | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }

        private void DrawWindowButtons(Graphics g)
        {
            if (!ControlBox) return;

            // Close.
            DrawButtonBackground(g, CloseRect, 2, isClose: true);
            DrawGlyph(g, CloseRect, 2);

            if (HasMax)
            {
                DrawButtonBackground(g, MaxRect, 1, isClose: false);
                DrawGlyph(g, MaxRect, 1);
            }
            if (HasMin)
            {
                DrawButtonBackground(g, MinRect, 0, isClose: false);
                DrawGlyph(g, MinRect, 0);
            }
        }

        private void DrawButtonBackground(Graphics g, Rectangle r, int id, bool isClose)
        {
            if (_hoverButton != id) return;
            Color c = isClose ? Color.FromArgb(200, 60, 60) : SupeyTheme.SurfaceElevated;
            using (var b = new SolidBrush(c)) g.FillRectangle(b, r);
        }

        private void DrawGlyph(Graphics g, Rectangle r, int id)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color glyph = (_hoverButton == id && id == 2) ? Color.White : SupeyTheme.TextSecondary;
            using (var pen = new Pen(glyph, 1.3f))
            {
                int cx = r.Left + r.Width / 2;
                int cy = r.Top + r.Height / 2;
                switch (id)
                {
                    case 0: // minimize
                        g.DrawLine(pen, cx - 5, cy, cx + 5, cy);
                        break;
                    case 1: // maximize / restore
                        if (WindowState == FormWindowState.Maximized)
                        {
                            g.DrawRectangle(pen, cx - 4, cy - 2, 7, 7);
                            g.DrawLine(pen, cx - 2, cy - 4, cx + 5, cy - 4);
                            g.DrawLine(pen, cx + 5, cy - 4, cx + 5, cy + 3);
                        }
                        else
                        {
                            g.DrawRectangle(pen, cx - 5, cy - 5, 10, 10);
                        }
                        break;
                    case 2: // close
                        g.DrawLine(pen, cx - 5, cy - 5, cx + 5, cy + 5);
                        g.DrawLine(pen, cx + 5, cy - 5, cx - 5, cy + 5);
                        break;
                }
            }
        }
    }
}
