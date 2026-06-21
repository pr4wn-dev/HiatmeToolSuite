using System;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Hides the native tab headers of the main <c>hiatmeTabControl</c> so navigation is driven
    /// entirely by the custom <see cref="SupeyDrawer"/> rail (mirroring how MaterialSkin's drawer
    /// drove a header-less <c>MaterialTabControl</c>). The tab pages fill the whole control; the
    /// gray Win32 tab row never paints.
    ///
    /// All existing tab wiring — SelectedIndexChanged, dynamic add/remove of pages — keeps working
    /// untouched, since we only suppress the header chrome, not the control.
    /// </summary>
    /// <summary>
    /// Legacy attach helper — prefer <see cref="SupeyTabControl"/> which owns this behavior.
    /// </summary>
    internal static class SupeyTabStrip
    {
        private const int TCM_ADJUSTRECT = 0x1328;

        public static void Attach(TabControl tc)
        {
            if (tc is SupeyTabControl stc)
            {
                stc.RefreshAfterRestore();
                return;
            }
            if (tc == null) return;

            tc.SizeMode = TabSizeMode.Fixed;
            tc.ItemSize = new System.Drawing.Size(0, 1);
            tc.Appearance = TabAppearance.FlatButtons;
            tc.BackColor = SupeyTheme.SurfaceBase;

            var hider = new HeaderHider();
            if (tc.IsHandleCreated)
                hider.AssignHandle(tc.Handle);
            else
                tc.HandleCreated += (s, e) => hider.AssignHandle(tc.Handle);
            tc.HandleDestroyed += (s, e) => { try { hider.ReleaseHandle(); } catch { } };

            SupeyThemeManager.ThemeChanged += (s, e) =>
            {
                if (tc.IsDisposed) return;
                try { tc.BackColor = SupeyTheme.SurfaceBase; tc.Invalidate(); } catch { }
            };
        }

        private sealed class HeaderHider : NativeWindow
        {
            protected override void WndProc(ref Message m)
            {
                if (m.Msg == TCM_ADJUSTRECT)
                {
                    m.Result = (IntPtr)1;
                    return;
                }
                base.WndProc(ref m);
            }
        }
    }
}
