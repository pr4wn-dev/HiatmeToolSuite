using System;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Header-less <see cref="TabControl"/> — same core technique as MaterialSkin's
    /// <c>MaterialTabControl</c> (<c>TCM_ADJUSTRECT</c> swallowed, zero-height tabs). Tab-button
    /// chrome is owner-drawn as a flat theme fill so Win32 never flashes white/system tabs on
    /// maximize or restore. Drawer icons use a separate <see cref="ImageList"/> reference.
    /// </summary>
    public class SupeyTabControl : TabControl
    {
        private const int TCM_ADJUSTRECT = 0x1328;
        private const int TCM_SETITEMSIZE = 0x1329;
        private const int TCM_LAYOUT = 0x130B;
        [StructLayout(LayoutKind.Sequential)]
        private struct SIZE { public int cx; public int cy; }

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, ref SIZE lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hwnd, string pszSubAppName, string pszSubIdList);

        public SupeyTabControl()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
            EnableDoubleBuffering(this);

            Multiline = false;
            SizeMode = TabSizeMode.Fixed;
            ItemSize = new Size(0, 1);
            Appearance = TabAppearance.FlatButtons;
            DrawMode = TabDrawMode.OwnerDrawFixed;
            BackColor = SupeyTheme.SurfaceBase;

            DrawItem += DrawTabHeader;

            SupeyThemeManager.ThemeChanged += OnThemeChanged;
        }

        /// <summary>
        /// Call once after wiring <see cref="SupeyDrawer"/> with the tab icon list.
        /// </summary>
        public void DetachImageListForDrawer()
        {
            if (!DesignMode)
                ImageList = null;
        }

        /// <summary>Relayout native tab chrome after restore-from-minimized only.</summary>
        public void RefreshAfterRestore()
        {
            if (!IsHandleCreated || IsDisposed || DesignMode) return;
            ApplyHeaderlessNativeLayout();
            try { Invalidate(true); } catch { }
        }

        private void ApplyHeaderlessNativeLayout()
        {
            if (DesignMode || !IsHandleCreated || IsDisposed || Disposing) return;
            try
            {
                var sz = new SIZE { cx = 0, cy = 1 };
                SendMessage(Handle, TCM_SETITEMSIZE, 0, ref sz);
                SendMessage(Handle, TCM_LAYOUT, IntPtr.Zero, IntPtr.Zero);
            }
            catch { }
        }

        private void OnThemeChanged(object sender, EventArgs e)
        {
            if (IsDisposed) return;
            BackColor = SupeyTheme.SurfaceBase;
            Invalidate(true);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DrawItem -= DrawTabHeader;
                SupeyThemeManager.ThemeChanged -= OnThemeChanged;
            }
            base.Dispose(disposing);
        }

        protected override void OnCreateControl()
        {
            if (!DesignMode)
            {
                Multiline = false;
                SizeMode = TabSizeMode.Fixed;
                ItemSize = new Size(0, 1);
                DrawMode = TabDrawMode.OwnerDrawFixed;
            }
            base.OnCreateControl();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (DesignMode) return;
            try { SetWindowTheme(Handle, string.Empty, string.Empty); } catch { }
            ApplyHeaderlessNativeLayout();
        }

        protected override void OnControlAdded(ControlEventArgs e)
        {
            base.OnControlAdded(e);
            if (DesignMode || !(e.Control is TabPage page)) return;
            page.BackColor = SupeyTheme.SurfaceBase;
        }

        private void DrawTabHeader(object sender, DrawItemEventArgs e)
        {
            if (DesignMode || e.Index < 0) return;
            using (var fill = new SolidBrush(SupeyTheme.SurfaceBase))
                e.Graphics.FillRectangle(fill, e.Bounds);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == TCM_ADJUSTRECT && !DesignMode)
            {
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
    }
}
