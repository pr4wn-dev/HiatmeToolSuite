using System;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Header-less, dark-themed <see cref="TabControl"/> for the main app shell. Navigation icons
    /// live on <see cref="SupeyDrawer"/> only — this control clears <see cref="ImageList"/> so
    /// Win32 never paints tab buttons, swallows <c>TCM_ADJUSTRECT</c>, and masks any residual strip
    /// after minimize/restore.
    /// </summary>
    public class SupeyTabControl : TabControl
    {
        private const int TCM_ADJUSTRECT = 0x1328;
        private const int TCM_LAYOUT = 0x130B;
        private const int WM_ERASEBKGND = 0x0014;
        private const int WM_PAINT = 0x000F;

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        private FormWindowState _ownerLastState = FormWindowState.Normal;

        public SupeyTabControl()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            EnableDoubleBuffering(this);

            Multiline = false;
            SizeMode = TabSizeMode.Fixed;
            ItemSize = new Size(0, 0);
            Appearance = TabAppearance.FlatButtons;
            BackColor = SupeyTheme.SurfaceBase;

            SupeyThemeManager.ThemeChanged += OnThemeChanged;
        }

        /// <summary>
        /// Detach the image list from the TabControl so Win32 does not paint tab-button icons.
        /// Pass the same list to <see cref="SupeyDrawerHost"/> for the left drawer rail.
        /// </summary>
        public void DetachImageListForDrawer()
        {
            if (DesignMode) return;
            ImageList = null;
            ApplyHeaderlessLayout();
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

        protected override void OnCreateControl()
        {
            ApplyHeaderlessLayout();
            base.OnCreateControl();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyHeaderlessLayout();

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

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            SyncTabPageBounds();
            CoverTabStrip();
        }

        private void ApplyHeaderlessLayout()
        {
            if (DesignMode) return;
            Multiline = false;
            SizeMode = TabSizeMode.Fixed;
            ItemSize = new Size(0, 0);
            Appearance = TabAppearance.FlatButtons;
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

        internal void RefreshAfterRestore()
        {
            if (!IsHandleCreated || IsDisposed || DesignMode) return;
            try { BeginInvoke(new Action(RefreshAfterRestoreCore)); } catch { }
        }

        private void RefreshAfterRestoreCore()
        {
            if (!IsHandleCreated || IsDisposed || DesignMode) return;

            ApplyHeaderlessLayout();

            try { SendMessage(Handle, TCM_LAYOUT, IntPtr.Zero, IntPtr.Zero); } catch { }

            SyncTabPageBounds();
            CoverTabStrip();
            Invalidate(true);
            Update();
        }

        /// <summary>Win32 tab pages keep designer offsets (e.g. y=39) until forced to DisplayRectangle.</summary>
        private void SyncTabPageBounds()
        {
            if (DesignMode || !IsHandleCreated) return;
            var area = DisplayRectangle;
            if (area.Width <= 0 || area.Height <= 0) return;

            foreach (TabPage page in TabPages)
            {
                if (page.IsDisposed) continue;
                try
                {
                    page.SetBounds(area.X, area.Y, area.Width, area.Height, BoundsSpecified.All);
                }
                catch { }
            }
        }

        private int TabStripHeight
        {
            get
            {
                if (!IsHandleCreated) return 0;
                return Math.Max(0, DisplayRectangle.Top);
            }
        }

        private void CoverTabStrip()
        {
            if (DesignMode || !IsHandleCreated) return;

            int h = TabStripHeight;
            if (h <= 0)
            {
                // Even when DisplayRectangle.Top is 0, Win32 can still paint a 1–3px row after restore.
                h = 3;
            }

            try
            {
                using (var g = CreateGraphics())
                using (var fill = new SolidBrush(SupeyTheme.SurfaceBase))
                    g.FillRectangle(fill, 0, 0, ClientSize.Width, h);
            }
            catch { }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == TCM_ADJUSTRECT && !DesignMode)
            {
                // Reserve zero height for tab buttons — pages use the full client rect.
                var rc = new RECT
                {
                    Left = 0,
                    Top = 0,
                    Right = Math.Max(0, ClientSize.Width),
                    Bottom = Math.Max(0, ClientSize.Height),
                };
                Marshal.StructureToPtr(rc, m.LParam, false);
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

            bool wasPaint = m.Msg == WM_PAINT;
            base.WndProc(ref m);

            if (wasPaint && !DesignMode)
                CoverTabStrip();
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
