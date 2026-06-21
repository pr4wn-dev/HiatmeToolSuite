using System;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Header-less, dark-themed <see cref="TabControl"/> for the main app shell. Swallows
    /// <c>TCM_ADJUSTRECT</c> so tab pages fill the entire client area (no Win32 tab-button row),
    /// erases with the theme palette (never system white), and forces a layout refresh after the
    /// owner form restores from minimized so native tab buttons do not flash in the top-left corner.
    /// </summary>
    public class SupeyTabControl : TabControl
    {
        private const int TCM_ADJUSTRECT = 0x1328;
        private const int TCM_LAYOUT = 0x130B;
        private const int WM_ERASEBKGND = 0x0014;

        private FormWindowState _ownerLastState = FormWindowState.Normal;

        public SupeyTabControl()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            EnableDoubleBuffering(this);

            Multiline = true;
            SizeMode = TabSizeMode.Fixed;
            ItemSize = new Size(0, 1);
            Appearance = TabAppearance.FlatButtons;
            BackColor = SupeyTheme.SurfaceBase;

            SupeyThemeManager.ThemeChanged += OnThemeChanged;
        }

        private void OnThemeChanged(object sender, EventArgs e)
        {
            if (IsDisposed) return;
            BackColor = SupeyTheme.SurfaceBase;
            foreach (TabPage page in TabPages)
            {
                if (page.IsDisposed) continue;
                try { page.BackColor = SupeyTheme.SurfaceBase; } catch { }
            }
            Invalidate(true);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                SupeyThemeManager.ThemeChanged -= OnThemeChanged;
            base.Dispose(disposing);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            var owner = FindForm();
            if (owner == null || DesignMode) return;

            owner.Resize += OwnerFormResize;
            owner.Activated += OwnerFormActivated;
            _ownerLastState = owner.WindowState;
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            var owner = FindForm();
            if (owner != null)
            {
                owner.Resize -= OwnerFormResize;
                owner.Activated -= OwnerFormActivated;
            }
            base.OnHandleDestroyed(e);
        }

        protected override void OnControlAdded(ControlEventArgs e)
        {
            base.OnControlAdded(e);
            if (DesignMode || !(e.Control is TabPage page)) return;
            page.BackColor = SupeyTheme.SurfaceBase;
        }

        private void OwnerFormResize(object sender, EventArgs e)
        {
            var owner = FindForm();
            if (owner == null) return;
            if (_ownerLastState == FormWindowState.Minimized && owner.WindowState != FormWindowState.Minimized)
                RefreshAfterRestore();
            _ownerLastState = owner.WindowState;
        }

        private void OwnerFormActivated(object sender, EventArgs e)
        {
            var owner = FindForm();
            if (owner == null || owner.WindowState == FormWindowState.Minimized) return;
            RefreshAfterRestore();
        }

        /// <summary>
        /// After minimize/restore Win32 briefly lays out the tab-button row (icons flash top-left)
        /// before our adjust-rect takes effect — force a layout pass and repaint.
        /// </summary>
        internal void RefreshAfterRestore()
        {
            if (!IsHandleCreated || IsDisposed || DesignMode) return;

            // Defer one UI tick so we repaint after Win32's default restore layout (tab icons flash).
            try { BeginInvoke(new Action(RefreshAfterRestoreCore)); } catch { }
        }

        private void RefreshAfterRestoreCore()
        {
            if (!IsHandleCreated || IsDisposed || DesignMode) return;

            try
            {
                SendMessage(Handle, TCM_LAYOUT, IntPtr.Zero, IntPtr.Zero);
            }
            catch { }

            Invalidate(true);
            Update();
        }

        protected override void WndProc(ref Message m)
        {
            // MaterialTabControl pattern: swallow adjust-rect so pages use the full client rect.
            if (m.Msg == TCM_ADJUSTRECT && !DesignMode)
            {
                m.Result = (IntPtr)1;
                return;
            }

            if (m.Msg == WM_ERASEBKGND && !DesignMode)
            {
                using (var g = Graphics.FromHdc(m.WParam))
                using (var fill = new SolidBrush(SupeyTheme.SurfaceBase))
                    g.FillRectangle(fill, ClientRectangle);
                m.Result = (IntPtr)1;
                return;
            }

            base.WndProc(ref m);
        }

        private static void EnableDoubleBuffering(Control c)
        {
            try
            {
                typeof(Control).InvokeMember(
                    "DoubleBuffered",
                    BindingFlags.SetProperty | BindingFlags.Instance | BindingFlags.NonPublic,
                    null, c, new object[] { true });
            }
            catch { }
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    }
}
