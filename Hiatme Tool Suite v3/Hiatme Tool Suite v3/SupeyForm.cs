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

    /// Window drag, resize, and snap follow the same model as <c>MaterialForm</c>: title chrome is

    /// painted on the form (no title-bar child HWND), edge resize is detected in

    /// <see cref="OnMouseMove"/> via <see cref="Control.GetChildAtPoint"/>, and sizing starts with

    /// <c>WM_NCLBUTTONDOWN</c> from <see cref="OnMouseDown"/>.

    /// </summary>

    public class SupeyForm : Form

    {

        /// <summary>Top inset reserved for the title bar; matches MaterialForm (24 + 40).</summary>

        public const int TitleBarHeight = 64;



        /// <summary>Resize hit target width — matches MaterialSkin <c>BORDER_WIDTH</c>.</summary>

        internal const int BorderWidth = 7;



        private const int FrameWidth = 3;

        private const int ButtonWidth = 46;



        internal enum ResizeDirection

        {

            None,

            Left,

            Right,

            Top,

            Bottom,

            TopLeft,

            TopRight,

            BottomLeft,

            BottomRight,

        }



        // ── Win32 ────────────────────────────────────────────────────────────────

        private const int WM_LBUTTONDOWN = 0x0201;

        private const int WM_LBUTTONDBLCLK = 0x0203;

        private const int WM_NCLBUTTONDOWN = 0x00A1;

        private const int WM_GETMINMAXINFO = 0x0024;

        private const int WM_NCCALCSIZE = 0x0083;

        private const int WM_NCACTIVATE = 0x0086;

        private const int WM_NCPAINT = 0x0085;

        private const int WM_ERASEBKGND = 0x0014;

        private const int WM_SIZE = 0x0005;



        private const int DWMWA_TRANSITIONS_FORCEDISABLED = 3;

        private const int GWL_STYLE = -16;



        private const int WS_MINIMIZEBOX = 0x00020000;

        private const int WS_SYSMENU = 0x00080000;

        private const int WS_SIZEBOX = 0x00040000;

        private const int CS_DBLCLKS = 0x0008;



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



        [StructLayout(LayoutKind.Sequential)]

        private struct NCCALCSIZE_PARAMS

        {

            public RECT rgrc0;

            public RECT rgrc1;

            public RECT rgrc2;

            public IntPtr lppos;

        }



        [DllImport("dwmapi.dll")]

        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);



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



        [DllImport("user32.dll")]

        private static extern bool ReleaseCapture();



        [DllImport("user32.dll", CharSet = CharSet.Auto)]

        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);



        private static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex)

            => IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, nIndex) : GetWindowLong32(hWnd, nIndex);



        private static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong)

            => IntPtr.Size == 8

                ? SetWindowLongPtr64(hWnd, nIndex, dwNewLong)

                : SetWindowLong32(hWnd, nIndex, dwNewLong.ToInt32());



        private static readonly Cursor[] ResizeCursors =

        {

            Cursors.SizeNESW, Cursors.SizeWE, Cursors.SizeNWSE, Cursors.SizeWE, Cursors.SizeNS,

        };



        private int _hoverButton = -1;

        private bool _navMenuHot;

        private bool _themeBtnHot;

        private FormWindowState _lastWindowState = FormWindowState.Normal;

        private ResizeDirection _resizeDir = ResizeDirection.None;



        private const int NavMenuButtonWidth = 38;

        private const int NavMenuButtonHeight = 30;



        /// <summary>When true, edge resize is enabled (MaterialSkin <c>Sizable</c>).</summary>

        public bool Sizable { get; set; } = true;



        /// <summary>When true, a nav-menu (hamburger) button is painted in the title bar.</summary>

        public bool ShowNavMenuButton { get; set; }



        /// <summary>Width of the leading title-bar gutter the nav button centers within (e.g. 64 for the drawer rail).</summary>

        public int TitleLeadingGutterWidth { get; set; }



        /// <summary>Raised when the painted title-bar nav-menu button is clicked.</summary>

        public event EventHandler NavMenuClick;



        /// <summary>Text for the painted theme-picker chip in the title bar (empty = hidden).</summary>

        public string TitleBarThemeText { get; set; }



        /// <summary>Raised when the painted theme chip is clicked.</summary>

        public event EventHandler TitleBarThemeClick;



        /// <summary>Extra left padding for the title text so it clears a leading control (e.g. the drawer hamburger).</summary>

        public int TitleLeftInset { get; set; }



        public SupeyForm()

        {

            SetStyle(

                ControlStyles.AllPaintingInWmPaint

                | ControlStyles.OptimizedDoubleBuffer

                | ControlStyles.ResizeRedraw,

                true);



            FormBorderStyle = FormBorderStyle.None;

            DoubleBuffered = true;

            BackColor = SupeyTheme.SurfaceBase;

            ForeColor = SupeyTheme.TextPrimary;

            Font = SupeyTheme.BodyFont;

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

            try

            {

                int disable = 1;

                DwmSetWindowAttribute(Handle, DWMWA_TRANSITIONS_FORCEDISABLED, ref disable, sizeof(int));

            }

            catch { }

        }



        /// <summary>Repaint the painted title bar (no child HWND — chrome is drawn on the form).</summary>

        protected void RefreshTitleBarChrome() => Invalidate(TitleBarBounds);



        // ── Window-button geometry ───────────────────────────────────────────────

        private Rectangle CloseRectFor(int barWidth) => new Rectangle(barWidth - ButtonWidth, 0, ButtonWidth, 30);

        private Rectangle MaxRectFor(int barWidth) => new Rectangle(barWidth - ButtonWidth * 2, 0, ButtonWidth, 30);

        private Rectangle MinRectFor(int barWidth) => new Rectangle(barWidth - ButtonWidth * 3, 0, ButtonWidth, 30);

        private Rectangle CloseRect => CloseRectFor(ClientSize.Width);

        private Rectangle MaxRect => MaxRectFor(ClientSize.Width);

        private Rectangle MinRect => MinRectFor(ClientSize.Width);



        private Rectangle NavMenuRect

        {

            get

            {

                if (!ShowNavMenuButton || TitleLeadingGutterWidth <= 0)

                    return Rectangle.Empty;

                int x = Math.Max(4, (TitleLeadingGutterWidth - NavMenuButtonWidth) / 2);

                int y = (TitleBarHeight - NavMenuButtonHeight) / 2;

                return new Rectangle(x, y, NavMenuButtonWidth, NavMenuButtonHeight);

            }

        }



        private Rectangle ThemeButtonRectFor(int barWidth)

        {

            if (string.IsNullOrEmpty(TitleBarThemeText))

                return Rectangle.Empty;

            Size text = TextRenderer.MeasureText(TitleBarThemeText, SupeyTheme.BodyFont);

            int w = Math.Max(120, Math.Min(text.Width + 24, barWidth - ButtonWidth * 3 - 48));

            int h = 30;

            int x = Math.Max(TitleLeadingGutterWidth + 8, barWidth - ButtonWidth * 3 - w - 12);

            int y = (TitleBarHeight - h) / 2;

            return new Rectangle(x, y, w, h);

        }



        private Rectangle ThemeButtonRect => ThemeButtonRectFor(ClientSize.Width);



        public Rectangle TitleBarThemeButtonBounds => ThemeButtonRect;



        private bool HasMax => MaximizeBox && ControlBox;

        private bool HasMin => MinimizeBox && ControlBox;



        private bool IsTitleBarInteractivePoint(Point p)

        {

            if (ShowNavMenuButton && NavMenuRect.Contains(p))

                return true;



            var themeRect = ThemeButtonRect;

            if (!themeRect.IsEmpty && themeRect.Contains(p))

                return true;



            if (ControlBox && p.Y <= 30)

            {

                if (CloseRect.Contains(p)) return true;

                if (HasMax && MaxRect.Contains(p)) return true;

                if (HasMin && MinRect.Contains(p)) return true;

            }



            return false;

        }



        private bool IsOverCaption(Point p) => p.Y <= TitleBarHeight && !IsTitleBarInteractivePoint(p);



        /// <summary>Called from <see cref="SupeyDrawerHost"/> when the user grabs a drawer edge.</summary>

        internal void BeginResizeFromDrawer(ResizeDirection direction) => ResizeForm(direction);



        internal void SetDrawerResizeCursor(ResizeDirection direction, Cursor cursor)

        {

            if (!Sizable || WindowState == FormWindowState.Maximized)

            {

                _resizeDir = ResizeDirection.None;

                return;

            }



            _resizeDir = direction;

            Cursor = cursor;

        }



        protected override void WndProc(ref Message m)

        {

            if (m.Msg == WM_NCCALCSIZE)

            {

                if (m.WParam == IntPtr.Zero)

                {

                    m.Result = IntPtr.Zero;

                    return;

                }



                var nc = (NCCALCSIZE_PARAMS)Marshal.PtrToStructure(m.LParam, typeof(NCCALCSIZE_PARAMS));

                nc.rgrc1 = nc.rgrc0;

                nc.rgrc2 = nc.rgrc0;

                Marshal.StructureToPtr(nc, m.LParam, false);

                m.Result = IntPtr.Zero;

                return;

            }



            if (m.Msg == WM_NCACTIVATE)

            {

                m.Result = (IntPtr)(-1);

                return;

            }



            if (m.Msg == WM_NCPAINT)

            {

                m.Result = IntPtr.Zero;

                return;

            }



            if (m.Msg == WM_ERASEBKGND)

            {

                using (var g = Graphics.FromHdc(m.WParam))

                    FillChromeBackground(g);

                m.Result = (IntPtr)1;

                return;

            }



            if (m.Msg == WM_GETMINMAXINFO)

            {

                base.WndProc(ref m);

                AdjustMaximizedBounds(m.HWnd, m.LParam);

                return;

            }



            base.WndProc(ref m);



            if (DesignMode || IsDisposed)

                return;



            // MaterialForm pattern: caption drag + double-click maximize via client messages.

            var cursorPos = PointToClient(Cursor.Position);

            if (m.Msg == WM_LBUTTONDBLCLK && IsOverCaption(cursorPos) && HasMax)

            {

                WindowState = WindowState == FormWindowState.Maximized

                    ? FormWindowState.Normal

                    : FormWindowState.Maximized;

            }

            else if (m.Msg == WM_LBUTTONDOWN && IsOverCaption(cursorPos))

            {

                ReleaseCapture();

                SendMessage(Handle, WM_NCLBUTTONDOWN, HTCAPTION, 0);

            }



            if (m.Msg == WM_SIZE && IsHandleCreated && !Disposing && !IsDisposed)

            {

                var state = WindowState;

                if (state != _lastWindowState)

                {

                    if (_lastWindowState == FormWindowState.Minimized && state != FormWindowState.Minimized)

                    {

                        SyncRepaintAfterRestore();

                        OnRestoredFromMinimized();

                    }



                    if (_lastWindowState == FormWindowState.Minimized || state == FormWindowState.Minimized

                        || _lastWindowState == FormWindowState.Maximized || state == FormWindowState.Maximized)

                        OnWindowStateTransition();



                    _lastWindowState = state;

                }

            }

        }



        protected void SyncRepaintAfterRestore()

        {

            if (Disposing || IsDisposed || !IsHandleCreated) return;

            try

            {

                Invalidate(TitleBarBounds);

                var dr = DisplayRectangle;

                if (dr.Width > 0 && dr.Height > 0)

                    Invalidate(dr);

                Update();

            }

            catch { }

        }



        protected virtual void OnRestoredFromMinimized() { }

        protected virtual void OnWindowStateTransition() { }



        private void RefreshChrome()

        {

            if (Disposing || IsDisposed || !IsHandleCreated)

                return;

            try { Invalidate(TitleBarBounds); } catch { }

        }



        protected override void OnActivated(EventArgs e)

        {

            base.OnActivated(e);

            if (!Disposing && !IsDisposed && WindowState != FormWindowState.Minimized)

                RefreshChrome();

        }



        private void FillChromeBackground(Graphics g)

        {

            int w = Math.Max(1, ClientSize.Width);

            int h = Math.Max(1, ClientSize.Height);

            using (var header = new SolidBrush(SupeyTheme.SurfaceHeader))

                g.FillRectangle(header, 0, 0, w, TitleBarHeight);



            PaintTitleBarChrome(g, w);



            if (h <= TitleBarHeight) return;



            int bodyTop = TitleBarHeight;

            int bodyH = h - TitleBarHeight;



            int gutterW = TitleLeadingGutterWidth > 0

                ? TitleLeadingGutterWidth

                : (Padding.Left > FrameWidth ? Padding.Left : 0);

            if (gutterW > FrameWidth)

            {

                using (var gutter = new SolidBrush(SupeyTheme.SurfaceHeader))

                    g.FillRectangle(gutter, 0, bodyTop, gutterW, bodyH);

            }



            var dr = DisplayRectangle;

            if (dr.Width > 0 && dr.Height > 0 && Padding.Left > FrameWidth)

            {

                using (var body = new SolidBrush(SupeyTheme.SurfaceBase))

                    g.FillRectangle(body, dr.X, dr.Y, dr.Width, dr.Height);

            }

            else if (gutterW <= FrameWidth)

            {

                using (var body = new SolidBrush(SupeyTheme.SurfaceBase))

                    g.FillRectangle(body, 0, bodyTop, w, bodyH);

            }

        }



        private Rectangle TitleBarBounds =>

            new Rectangle(0, 0, Math.Max(1, ClientSize.Width), TitleBarHeight);



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



            int workW = mmi.ptMaxSize.x;

            int workH = mmi.ptMaxSize.y;

            if (workW > 0 && mmi.ptMinTrackSize.x > workW)

                mmi.ptMinTrackSize.x = workW;

            if (workH > 0 && mmi.ptMinTrackSize.y > workH)

                mmi.ptMinTrackSize.y = workH;



            Marshal.StructureToPtr(mmi, lParam, true);

        }



        // ── MaterialForm-style edge resize ───────────────────────────────────────

        private void UpdateResizeHit(Point coords)

        {

            if (!Sizable || WindowState == FormWindowState.Maximized)

            {

                _resizeDir = ResizeDirection.None;

                return;

            }



            int b = BorderWidth;

            bool isChildUnderMouse = GetChildAtPoint(coords) != null;

            bool allowLeft = !isChildUnderMouse || TitleLeadingGutterWidth > 0;



            if (!isChildUnderMouse && coords.Y < b && coords.X > b && coords.X < ClientSize.Width - b)

            {

                _resizeDir = ResizeDirection.Top;

                Cursor = Cursors.SizeNS;

            }

            else if (!isChildUnderMouse && coords.X <= b && coords.Y < b)

            {

                _resizeDir = ResizeDirection.TopLeft;

                Cursor = Cursors.SizeNWSE;

            }

            else if (!isChildUnderMouse && coords.X >= ClientSize.Width - b && coords.Y < b)

            {

                _resizeDir = ResizeDirection.TopRight;

                Cursor = Cursors.SizeNESW;

            }

            else if (!isChildUnderMouse && coords.X <= b && coords.Y >= ClientSize.Height - b)

            {

                _resizeDir = ResizeDirection.BottomLeft;

                Cursor = Cursors.SizeNESW;

            }

            else if (allowLeft && coords.X <= b)

            {

                _resizeDir = ResizeDirection.Left;

                Cursor = Cursors.SizeWE;

            }

            else if (!isChildUnderMouse && coords.X >= ClientSize.Width - b && coords.Y >= ClientSize.Height - b)

            {

                _resizeDir = ResizeDirection.BottomRight;

                Cursor = Cursors.SizeNWSE;

            }

            else if (!isChildUnderMouse && coords.X >= ClientSize.Width - b)

            {

                _resizeDir = ResizeDirection.Right;

                Cursor = Cursors.SizeWE;

            }

            else if (!isChildUnderMouse && coords.Y >= ClientSize.Height - b)

            {

                _resizeDir = ResizeDirection.Bottom;

                Cursor = Cursors.SizeNS;

            }

            else

            {

                _resizeDir = ResizeDirection.None;

                if (Array.IndexOf(ResizeCursors, Cursor) >= 0)

                    Cursor = Cursors.Default;

            }

        }



        private void ResizeForm(ResizeDirection direction)

        {

            if (DesignMode || direction == ResizeDirection.None)

                return;



            int dir;

            switch (direction)

            {

                case ResizeDirection.BottomLeft: dir = HTBOTTOMLEFT; break;

                case ResizeDirection.Left: dir = HTLEFT; break;

                case ResizeDirection.Right: dir = HTRIGHT; break;

                case ResizeDirection.BottomRight: dir = HTBOTTOMRIGHT; break;

                case ResizeDirection.Bottom: dir = HTBOTTOM; break;

                case ResizeDirection.Top: dir = HTTOP; break;

                case ResizeDirection.TopLeft: dir = HTTOPLEFT; break;

                case ResizeDirection.TopRight: dir = HTTOPRIGHT; break;

                default: return;

            }



            ReleaseCapture();

            SendMessage(Handle, WM_NCLBUTTONDOWN, dir, 0);

        }



        protected override void OnMouseMove(MouseEventArgs e)

        {

            base.OnMouseMove(e);

            UpdateResizeHit(e.Location);

            if (e.Y <= TitleBarHeight)

                ProcessTitleBarMouseMove(e.Location);

        }



        protected override void OnMouseLeave(EventArgs e)

        {

            base.OnMouseLeave(e);

            _resizeDir = ResizeDirection.None;

            ProcessTitleBarMouseLeave();

            if (Array.IndexOf(ResizeCursors, Cursor) >= 0)

                Cursor = Cursors.Default;

        }



        protected override void OnMouseDown(MouseEventArgs e)

        {

            base.OnMouseDown(e);

            if (e.Button == MouseButtons.Left && Sizable && WindowState != FormWindowState.Maximized

                && Array.IndexOf(ResizeCursors, Cursor) >= 0)

            {

                ResizeForm(_resizeDir);

            }

        }



        protected override void OnMouseClick(MouseEventArgs e)

        {

            base.OnMouseClick(e);

            if (e.Y <= TitleBarHeight)

                ProcessTitleBarMouseClick(e.Location, e.Button);

        }



        private void ProcessTitleBarMouseMove(Point p)

        {

            if (_resizeDir != ResizeDirection.None)

                return;



            int prev = _hoverButton;

            _hoverButton = -1;

            if (ControlBox && p.Y <= 30)

            {

                if (CloseRect.Contains(p)) _hoverButton = 2;

                else if (HasMax && MaxRect.Contains(p)) _hoverButton = 1;

                else if (HasMin && MinRect.Contains(p)) _hoverButton = 0;

            }



            bool prevNavHot = _navMenuHot;

            _navMenuHot = ShowNavMenuButton && NavMenuRect.Contains(p);



            bool prevThemeHot = _themeBtnHot;

            var themeRect = ThemeButtonRect;

            _themeBtnHot = !themeRect.IsEmpty && themeRect.Contains(p);



            if (prevNavHot != _navMenuHot)

            {

                var r = NavMenuRect;

                if (!r.IsEmpty) Invalidate(r);

            }

            if (prevThemeHot != _themeBtnHot && !themeRect.IsEmpty)

                Invalidate(themeRect);



            var hand = _navMenuHot || _themeBtnHot || _hoverButton != -1;

            if (hand)

                Cursor = Cursors.Hand;



            if (prev != _hoverButton)

                Invalidate(new Rectangle(ClientSize.Width - ButtonWidth * 3, 0, ButtonWidth * 3, 30));

        }



        private void ProcessTitleBarMouseLeave()

        {

            if (_navMenuHot)

            {

                _navMenuHot = false;

                var r = NavMenuRect;

                if (!r.IsEmpty) Invalidate(r);

            }

            if (_themeBtnHot)

            {

                _themeBtnHot = false;

                var r = ThemeButtonRect;

                if (!r.IsEmpty) Invalidate(r);

            }

            if (_hoverButton != -1)

            {

                _hoverButton = -1;

                Invalidate(new Rectangle(ClientSize.Width - ButtonWidth * 3, 0, ButtonWidth * 3, 30));

            }



            if (_resizeDir == ResizeDirection.None)

                Cursor = Cursors.Default;

        }



        private void ProcessTitleBarMouseClick(Point p, MouseButtons button)

        {

            if (button != MouseButtons.Left) return;



            if (ShowNavMenuButton && NavMenuRect.Contains(p))

            {

                try { NavMenuClick?.Invoke(this, EventArgs.Empty); } catch { }

                return;

            }



            if (!ThemeButtonRect.IsEmpty && ThemeButtonRect.Contains(p))

            {

                try { TitleBarThemeClick?.Invoke(this, EventArgs.Empty); } catch { }

                return;

            }



            if (!ControlBox || p.Y > 30) return;



            if (CloseRect.Contains(p)) { Close(); return; }

            if (HasMax && MaxRect.Contains(p))

            {

                WindowState = WindowState == FormWindowState.Maximized

                    ? FormWindowState.Normal : FormWindowState.Maximized;

                return;

            }

            if (HasMin && MinRect.Contains(p)) WindowState = FormWindowState.Minimized;

        }



        protected override void OnResize(EventArgs e)

        {

            base.OnResize(e);

            if (IsHandleCreated && !Disposing && !IsDisposed)

            {

                Invalidate(TitleBarBounds);

                OnLayoutTitleBarControls();

            }

        }



        protected virtual void OnLayoutTitleBarControls() { }



        protected override void OnPaintBackground(PaintEventArgs e) => FillChromeBackground(e.Graphics);



        protected override void OnPaint(PaintEventArgs e) => base.OnPaint(e);



        private void PaintTitleBarChrome(Graphics g, int barWidth)

        {

            var bar = new Rectangle(0, 0, Math.Max(1, barWidth), TitleBarHeight);



            using (var divider = new Pen(SupeyTheme.Divider))

                g.DrawLine(divider, 0, TitleBarHeight - 1, bar.Width, TitleBarHeight - 1);



            using (var frame = new Pen(SupeyTheme.BorderSubtle))

                g.DrawRectangle(frame, 0, 0, bar.Width - 1, bar.Height - 1);



            DrawTitle(g, barWidth);

            DrawNavMenuButton(g);

            DrawThemeButton(g, barWidth);

            DrawWindowButtons(g, barWidth);

        }



        private void DrawThemeButton(Graphics g, int barWidth)

        {

            var r = ThemeButtonRectFor(barWidth);

            if (r.IsEmpty) return;



            using (var bg = new SolidBrush(_themeBtnHot ? SupeyTheme.SurfaceElevated : SupeyTheme.SurfaceHeader))

                g.FillRectangle(bg, r);



            TextRenderer.DrawText(g, TitleBarThemeText, SupeyTheme.BodyFont, r,

                SupeyTheme.TextPrimary,

                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter

                | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);

        }



        private void DrawNavMenuButton(Graphics g)

        {

            var r = NavMenuRect;

            if (r.IsEmpty) return;



            using (var bg = new SolidBrush(_navMenuHot ? SupeyTheme.SurfaceElevated : SupeyTheme.SurfaceHeader))

                g.FillRectangle(bg, r);



            Color line = _navMenuHot ? SupeyTheme.TextPrimary : SupeyTheme.TextSecondary;

            int cx = r.Left + r.Width / 2;

            int cy = r.Top + r.Height / 2;

            using (var pen = new Pen(line, 2f))

            {

                g.DrawLine(pen, cx - 8, cy - 6, cx + 8, cy - 6);

                g.DrawLine(pen, cx - 8, cy, cx + 8, cy);

                g.DrawLine(pen, cx - 8, cy + 6, cx + 8, cy + 6);

            }

        }



        private int TitleTextRight(int barWidth)

        {

            int right = barWidth - ButtonWidth * 3 - 8;

            var theme = ThemeButtonRectFor(barWidth);

            if (!theme.IsEmpty)

                right = Math.Min(right, theme.Left - 8);

            return right;

        }



        private void DrawTitle(Graphics g, int barWidth)

        {

            int x = 16 + TitleLeftInset;

            var nav = NavMenuRect;

            if (!nav.IsEmpty)

                x = Math.Max(x, nav.Right + 8);

            if (ShowIcon && Icon != null)

            {

                using (var bmp = Icon.ToBitmap())

                    g.DrawImage(bmp, x, (TitleBarHeight - 20) / 2, 20, 20);

                x += 28;

            }



            var rect = new Rectangle(x, 0, Math.Max(0, TitleTextRight(barWidth) - x), TitleBarHeight);

            TextRenderer.DrawText(g, Text ?? string.Empty, SupeyTheme.HeaderFont, rect,

                SupeyTheme.TextPrimary,

                TextFormatFlags.Left | TextFormatFlags.VerticalCenter

                | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);

        }



        private void DrawWindowButtons(Graphics g, int barWidth)

        {

            if (!ControlBox) return;



            DrawButtonBackground(g, CloseRectFor(barWidth), 2, isClose: true);

            DrawGlyph(g, CloseRectFor(barWidth), 2);



            if (HasMax)

            {

                DrawButtonBackground(g, MaxRectFor(barWidth), 1, isClose: false);

                DrawGlyph(g, MaxRectFor(barWidth), 1);

            }

            if (HasMin)

            {

                DrawButtonBackground(g, MinRectFor(barWidth), 0, isClose: false);

                DrawGlyph(g, MinRectFor(barWidth), 0);

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

                    case 0:

                        g.DrawLine(pen, cx - 5, cy, cx + 5, cy);

                        break;

                    case 1:

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

                    case 2:

                        g.DrawLine(pen, cx - 5, cy - 5, cx + 5, cy + 5);

                        g.DrawLine(pen, cx + 5, cy - 5, cx - 5, cy + 5);

                        break;

                }

            }

        }

    }

}


