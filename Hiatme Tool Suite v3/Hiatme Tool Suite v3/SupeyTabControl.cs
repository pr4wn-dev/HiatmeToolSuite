using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Header-less <see cref="TabControl"/> — same technique as MaterialSkin's
    /// <c>MaterialTabControl</c>: swallow <c>TCM_ADJUSTRECT</c> so tab pages fill the client
    /// area. Icons for the left drawer are kept on a separate <see cref="ImageList"/> reference
    /// (<see cref="DetachImageListForDrawer"/>) so Win32 does not paint tab buttons.
    /// </summary>
    public class SupeyTabControl : TabControl
    {
        private const int TCM_ADJUSTRECT = 0x1328;

        public SupeyTabControl()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            EnableDoubleBuffering(this);

            Multiline = false;
            SizeMode = TabSizeMode.Fixed;
            ItemSize = new Size(0, 1);
            Appearance = TabAppearance.FlatButtons;
            BackColor = SupeyTheme.SurfaceBase;

            SupeyThemeManager.ThemeChanged += OnThemeChanged;
        }

        /// <summary>
        /// Call once after <see cref="SupeyDrawerHost"/> copies the tab icons — clears
        /// <see cref="ImageList"/> so Win32 never draws tab-button chrome.
        /// </summary>
        public void DetachImageListForDrawer()
        {
            if (!DesignMode)
                ImageList = null;
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
                SupeyThemeManager.ThemeChanged -= OnThemeChanged;
            base.Dispose(disposing);
        }

        protected override void OnCreateControl()
        {
            if (!DesignMode)
            {
                Multiline = false;
                SizeMode = TabSizeMode.Fixed;
                ItemSize = new Size(0, 1);
            }
            base.OnCreateControl();
        }

        protected override void OnControlAdded(ControlEventArgs e)
        {
            base.OnControlAdded(e);
            if (DesignMode || !(e.Control is TabPage page)) return;
            page.BackColor = SupeyTheme.SurfaceBase;
        }

        protected override void WndProc(ref Message m)
        {
            // MaterialTabControl: swallow adjust-rect; tab pages use the full client rect.
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
