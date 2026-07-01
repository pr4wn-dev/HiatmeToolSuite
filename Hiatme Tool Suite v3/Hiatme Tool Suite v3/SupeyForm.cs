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

        private const int WM_SIZE = 0x0005;

        private const int WM_ENTERSIZEMOVE = 0x0231;

        private const int WM_EXITSIZEMOVE = 0x0232;

        private const int WM_SETICON = 0x0080;

        private const int ICON_SMALL = 0;

        private const int ICON_BIG = 1;



        private const int DWMWA_TRANSITIONS_FORCEDISABLED = 3;

        private const int DWMWA_BORDER_COLOR = 34;

        private const int SWP_FRAMECHANGED = 0x0020;

        private const int SWP_NOMOVE = 0x0002;

        private const int SWP_NOSIZE = 0x0001;

        private const int SWP_NOZORDER = 0x0004;

        private const int SWP_NOACTIVATE = 0x0010;

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



        [DllImport("user32.dll", SetLastError = true)]

        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);



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



        [DllImport("user32.dll", CharSet = CharSet.Auto)]

        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, IntPtr lParam);



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

        private bool _themePickerOpen;

        private FormWindowState _lastWindowState = FormWindowState.Normal;

        private bool _inSizeMove;

        private Control _materialContent;

        private int _contentLeftGutter;

        private int _contentSidePad = 3;

        private ResizeDirection _resizeDir = ResizeDirection.None;



        private const int NavMenuButtonWidth = 38;

        private const int NavMenuButtonHeight = 30;

        private const int ThemeComboHeight = 30;

        private const int ThemeArrowWidth = 28;

        private const int ThemeMinWidth = 176;

        private const int ThemeLabelInnerWidth = 44;



        /// <summary>When true, edge resize is enabled (MaterialSkin <c>Sizable</c>).</summary>

        public bool Sizable { get; set; } = true;



        /// <summary>When true, a nav-menu (hamburger) button is painted in the title bar.</summary>

        public bool ShowNavMenuButton { get; set; }



        /// <summary>Width of the leading title-bar gutter the nav button centers within (e.g. 64 for the drawer rail).</summary>

        public int TitleLeadingGutterWidth { get; set; }



        /// <summary>Raised when the painted title-bar nav-menu button is clicked.</summary>

        public event EventHandler NavMenuClick;



        /// <summary>Text for the painted theme-picker chip in the title bar (empty = hidden). Legacy — prefer <see cref="TitleBarThemeValue"/>.</summary>

        public string TitleBarThemeText { get; set; }

        /// <summary>Active theme preset name shown in the title-bar combo.</summary>

        public string TitleBarThemeValue { get; set; }

        /// <summary>True while the theme dropdown menu is open (highlights arrow like SupeyComboBox).</summary>

        public bool TitleBarThemeOpen
        {
            get => _themePickerOpen;
            set
            {
                if (_themePickerOpen == value)
                    return;
                _themePickerOpen = value;
                if (IsHandleCreated)
                    Invalidate(ThemeButtonRect);
            }
        }



        /// <summary>Raised when the painted theme chip is clicked.</summary>

        public event EventHandler TitleBarThemeClick;



        /// <summary>Raised when the user finishes a live resize or move (WM_EXITSIZEMOVE).</summary>

        public event EventHandler LiveResizeEnded;



        /// <summary>True while the user is drag-resizing or moving the window.</summary>

        public bool InLiveResize => _inSizeMove;



        /// <summary>Extra left padding for the title text so it clears a leading control (e.g. the drawer hamburger).</summary>

        public int TitleLeftInset { get; set; }



        public SupeyForm()

        {

            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);



            FormBorderStyle = FormBorderStyle.None;

            DoubleBuffered = true;

            BackColor = SupeyTheme.SurfaceBase;

            ForeColor = SupeyTheme.TextPrimary;

            Font = SupeyTheme.BodyFont;

            Padding = Padding.Empty;

            SupeyThemeManager.ThemeChanged += OnSupeyThemeChanged;

            Application.AddMessageFilter(_mouseFilter);

            _mouseFilter.MouseMove += OnGlobalMouseMove;

        }



        private readonly SupeyMouseMessageFilter _mouseFilter = new SupeyMouseMessageFilter();



        private void OnSupeyThemeChanged(object sender, EventArgs e)

        {

            if (IsDisposed) return;

            BackColor = SupeyTheme.SurfaceBase;

            ForeColor = SupeyTheme.TextPrimary;

            if (IsHandleCreated)

                try { ApplyDwmChrome(); } catch { }

            Invalidate();

        }



        protected override void Dispose(bool disposing)

        {

            if (disposing)

            {

                Application.RemoveMessageFilter(_mouseFilter);

                _mouseFilter.MouseMove -= OnGlobalMouseMove;

                SupeyThemeManager.ThemeChanged -= OnSupeyThemeChanged;

            }

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



        protected override void OnHandleCreated(EventArgs e)

        {

            base.OnHandleCreated(e);

            if (DesignMode) return;

            EnsureApplicationIcon();

            ApplyNativeWindowIcons();

            RefreshTitleBarChrome();

        }



        protected override void OnCreateControl()

        {

            base.OnCreateControl();

            if (DesignMode || !IsHandleCreated) return;

            long style = GetWindowLongPtr(Handle, GWL_STYLE).ToInt64();

            SetWindowLongPtr(Handle, GWL_STYLE, (IntPtr)(style | WS_SIZEBOX));

            try

            {

                ApplyDwmChrome();

            }

            catch { }

        }



        private void EnsureApplicationIcon()

        {

            if (DesignMode || Icon != null) return;

            try

            {

                string path = Application.ExecutablePath;

                if (string.IsNullOrEmpty(path)) return;

                Icon = System.Drawing.Icon.ExtractAssociatedIcon(path);

            }

            catch { }

        }



        private void ApplyNativeWindowIcons()

        {

            if (DesignMode || Icon == null || !IsHandleCreated) return;

            try

            {

                SendMessage(Handle, WM_SETICON, ICON_BIG, Icon.Handle);

                SendMessage(Handle, WM_SETICON, ICON_SMALL, Icon.Handle);

            }

            catch { }

        }



        /// <summary>MaterialSkin MaterialForm: kill DWM top strip + match Win11 border to header.</summary>

        private void ApplyDwmChrome()

        {

            int disable = 1;

            DwmSetWindowAttribute(Handle, DWMWA_TRANSITIONS_FORCEDISABLED, ref disable, sizeof(int));

            int borderColor = ColorTranslator.ToWin32(SupeyTheme.SurfaceHeader);

            DwmSetWindowAttribute(Handle, DWMWA_BORDER_COLOR, ref borderColor, sizeof(int));

            SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0,

                SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);

        }



        /// <summary>Repaint the painted title bar (no child HWND — chrome is drawn on the form).</summary>

        /// <summary>MaterialForm-style content inset — explicit bounds, not Form.Padding (native TabControl ignores padding during live resize).</summary>
        public void SetMaterialContent(Control content, int leftGutter, int sidePad = 3)
        {
            _materialContent = content;
            _contentLeftGutter = leftGutter;
            _contentSidePad = sidePad;
            TitleLeadingGutterWidth = leftGutter;
            Padding = Padding.Empty;

            if (content == null) return;

            content.Dock = DockStyle.None;
            if (content.Parent != this)
            {
                content.Parent?.Controls.Remove(content);
                Controls.Add(content);
            }
            content.SendToBack();

            if (IsHandleCreated)
                LayoutMaterialContent();
        }

        /// <summary>Legacy name — calls <see cref="SetMaterialContent"/>.</summary>
        public void ApplyMaterialContentPadding(int leftGutter, int sidePad = 3)
        {
            SetMaterialContent(_materialContent, leftGutter, sidePad);
        }

        /// <summary>Keep the main content HWND strictly below the painted title bar on every size change.</summary>
        private void LayoutMaterialContent()
        {
            if (_materialContent == null || _materialContent.IsDisposed || Disposing || IsDisposed)
                return;

            int left = Math.Max(_contentLeftGutter, _contentSidePad);
            int top = TitleBarHeight;
            int w = Math.Max(0, ClientSize.Width - left - _contentSidePad);
            int h = Math.Max(0, ClientSize.Height - top - _contentSidePad);

            var b = _materialContent.Bounds;
            if (b.X != left || b.Y != top || b.Width != w || b.Height != h)
                _materialContent.SetBounds(left, top, w, h);
        }

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



        private bool HasThemePicker =>
            !string.IsNullOrEmpty(TitleBarThemeValue) || !string.IsNullOrEmpty(TitleBarThemeText);

        private string GetThemeDisplayValue()
        {
            if (!string.IsNullOrWhiteSpace(TitleBarThemeValue))
                return TitleBarThemeValue.Trim();
            string t = TitleBarThemeText ?? string.Empty;
            const string prefix = "Theme: ";
            if (t.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return t.Substring(prefix.Length).Trim();
            return t.Trim();
        }

        private Rectangle ThemeButtonRectFor(int barWidth)
        {
            if (!HasThemePicker)
                return Rectangle.Empty;

            string value = GetThemeDisplayValue();
            if (string.IsNullOrEmpty(value))
                return Rectangle.Empty;

            int valueW = TextRenderer.MeasureText(
                value,
                SupeyTheme.BodyFont,
                new Size(int.MaxValue, ThemeComboHeight),
                TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width;

            int innerW = ThemeLabelInnerWidth + 1 + 10 + valueW + ThemeArrowWidth + 14;
            int w = Math.Max(ThemeMinWidth, Math.Min(innerW, barWidth - ButtonWidth * 3 - 56));
            int h = ThemeComboHeight;
            int x = Math.Max(TitleLeadingGutterWidth + 8, barWidth - ButtonWidth * 3 - w - 8);
            int y = 0;

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

            // MaterialSkin MaterialForm — strip the system non-client frame (removes the white top line).

            if (m.Msg == WM_NCCALCSIZE)

                return;



            if (m.Msg == WM_NCACTIVATE)

            {

                m.Result = (IntPtr)(-1);

                return;

            }



            if (m.Msg == WM_GETMINMAXINFO)

            {

                base.WndProc(ref m);

                AdjustMaximizedBounds(m.HWnd, m.LParam);

                return;

            }



            if (m.Msg == WM_ENTERSIZEMOVE)

                _inSizeMove = true;

            else if (m.Msg == WM_EXITSIZEMOVE)

            {

                _inSizeMove = false;

                LayoutMaterialContent();

                LiveResizeEnded?.Invoke(this, EventArgs.Empty);

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

                LayoutMaterialContent();

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



        private static void DrawMaterialBodyBorder(Graphics g, int w, int h, int bodyTop)

        {

            using (var borderPen = new Pen(SupeyTheme.Divider, 1))

            {

                g.DrawLine(borderPen, 0, bodyTop, 0, h - 2);

                g.DrawLine(borderPen, w - 1, bodyTop, w - 1, h - 2);

                g.DrawLine(borderPen, 0, h - 1, w - 1, h - 1);

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



        /// <summary>MaterialForm routes WM_MOUSEMOVE over child controls so edge-resize cursors still work.</summary>

        private void OnGlobalMouseMove(object sender, MouseEventArgs e)

        {

            if (IsDisposed || !IsHandleCreated || !Visible || WindowState == FormWindowState.Minimized)

                return;



            var client = PointToClient(e.Location);

            if (client.X < 0 || client.Y < 0 || client.X >= ClientSize.Width || client.Y >= ClientSize.Height)

                return;



            OnMouseMove(new MouseEventArgs(MouseButtons.None, 0, client.X, client.Y, 0));

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

            LayoutMaterialContent();

            if (IsHandleCreated && !Disposing && !IsDisposed)

                OnLayoutTitleBarControls();

        }



        protected virtual void OnLayoutTitleBarControls() { }



        protected override void OnPaint(PaintEventArgs e)

        {

            if (DesignMode)

            {

                base.OnPaint(e);

                return;

            }



            var g = e.Graphics;

            g.Clear(SupeyTheme.SurfaceBase);



            int w = Math.Max(1, ClientSize.Width);

            int h = Math.Max(1, ClientSize.Height);



            using (var header = new SolidBrush(SupeyTheme.SurfaceHeader))

                g.FillRectangle(header, 0, 0, w, TitleBarHeight);



            PaintTitleBarChrome(g, w);



            if (h > TitleBarHeight)

            {

                int bodyTop = TitleBarHeight;

                int bodyH = h - bodyTop;

                int gutterW = TitleLeadingGutterWidth > 0 ? TitleLeadingGutterWidth : 0;

                if (gutterW > 0)

                {

                    using (var gutter = new SolidBrush(SupeyTheme.SurfaceHeader))

                        g.FillRectangle(gutter, 0, bodyTop, gutterW, bodyH);

                }



                DrawMaterialBodyBorder(g, w, h, bodyTop);

            }



            base.OnPaint(e);

        }



        private void PaintTitleBarChrome(Graphics g, int barWidth)

        {

            var bar = new Rectangle(0, 0, Math.Max(1, barWidth), TitleBarHeight);



            using (var divider = new Pen(SupeyTheme.Divider))

                g.DrawLine(divider, 0, TitleBarHeight - 1, bar.Width, TitleBarHeight - 1);



            DrawTitle(g, barWidth);

            DrawNavMenuButton(g);

            DrawThemeButton(g, barWidth);

            DrawWindowButtons(g, barWidth);

        }



        private void DrawThemeButton(Graphics g, int barWidth)
        {
            var r = ThemeButtonRectFor(barWidth);
            if (r.IsEmpty)
                return;

            string value = GetThemeDisplayValue();
            bool hot = _themeBtnHot || _themePickerOpen;
            Color bg = hot ? SupeyTheme.SurfaceElevated : SupeyTheme.Surface;
            Color border = _themePickerOpen
                ? SupeyTheme.AccentPrimary
                : hot ? SupeyTheme.BorderSubtle : SupeyTheme.Divider;

            using (var bgBrush = new SolidBrush(bg))
                g.FillRectangle(bgBrush, r);
            using (var pen = new Pen(border, 1f))
                g.DrawRectangle(pen, r.X, r.Y, r.Width - 1, r.Height - 1);

            var labelRect = new Rectangle(r.Left + 10, r.Top, ThemeLabelInnerWidth, r.Height);
            TextRenderer.DrawText(g, "Theme", SupeyTheme.CaptionFont, labelRect,
                SupeyTheme.TextMuted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

            int divX = r.Left + 10 + ThemeLabelInnerWidth;
            using (var divPen = new Pen(SupeyTheme.Divider, 1f))
                g.DrawLine(divPen, divX, r.Top + 7, divX, r.Bottom - 7);

            var valueRect = new Rectangle(
                divX + 10,
                r.Top,
                Math.Max(0, r.Width - ThemeArrowWidth - (divX - r.Left) - 10),
                r.Height);
            TextRenderer.DrawText(g, value, SupeyTheme.BodyFont, valueRect,
                SupeyTheme.TextPrimary,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter
                | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

            DrawThemeDropdownArrow(g, r, hot);
        }

        private static void DrawThemeDropdownArrow(Graphics g, Rectangle comboBounds, bool hot)
        {
            var state = g.Save();
            try
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                int cy = comboBounds.Top + comboBounds.Height / 2;
                int ax = comboBounds.Right - 14;
                Color arrowColor = hot ? SupeyTheme.AccentPrimary : SupeyTheme.TextSecondary;
                using (var brush = new SolidBrush(arrowColor))
                {
                    g.FillPolygon(brush, new[]
                    {
                        new Point(ax - 5, cy - 2),
                        new Point(ax + 5, cy - 2),
                        new Point(ax, cy + 3),
                    });
                }
            }
            finally
            {
                g.Restore(state);
            }
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



    /// <summary>MaterialForm-style filter so <see cref="SupeyForm"/> receives mouse-move over child HWNDs.</summary>

    internal sealed class SupeyMouseMessageFilter : IMessageFilter

    {

        private const int WM_MOUSEMOVE = 0x0200;



        public event MouseEventHandler MouseMove;



        public bool PreFilterMessage(ref Message m)

        {

            if (m.Msg == WM_MOUSEMOVE && MouseMove != null)

            {

                int x = Control.MousePosition.X, y = Control.MousePosition.Y;

                MouseMove(null, new MouseEventArgs(MouseButtons.None, 0, x, y, 0));

            }

            return false;

        }

    }

}
