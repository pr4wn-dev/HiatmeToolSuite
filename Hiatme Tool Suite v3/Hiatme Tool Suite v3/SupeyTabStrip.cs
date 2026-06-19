using System;
using System.Drawing;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Owner-draws an existing <see cref="TabControl"/> (the main <c>hiatmeTabControl</c>) so its tab
    /// bar matches the Supey skin instead of MaterialSkin / native gray: a flat themed header strip,
    /// muted labels with the active tab in bright text, the tab's icon from the control's ImageList,
    /// and a lime accent underline under the selected tab.
    ///
    /// This attaches to the control in place (no reparenting / headerless hacks), so all existing tab
    /// wiring — SelectedIndexChanged, dynamic add/remove of pages — keeps working untouched. It also
    /// re-themes itself when the active preset changes.
    /// </summary>
    internal static class SupeyTabStrip
    {
        private const int TabHeight = 34;

        public static void Attach(TabControl tc)
        {
            if (tc == null) return;

            tc.DrawMode = TabDrawMode.OwnerDrawFixed;
            tc.SizeMode = TabSizeMode.Normal;
            tc.Alignment = TabAlignment.Top;
            tc.ItemSize = new Size(46, TabHeight);
            tc.Padding = new Point(16, 6);
            tc.BackColor = SupeyTheme.SurfaceHeader;

            tc.DrawItem -= DrawTab;
            tc.DrawItem += DrawTab;

            // Repaint the whole bar when the theme switches.
            SupeyThemeManager.ThemeChanged += (s, e) =>
            {
                if (tc.IsDisposed) return;
                try
                {
                    tc.BackColor = SupeyTheme.SurfaceHeader;
                    tc.Invalidate();
                }
                catch { }
            };
        }

        private static void DrawTab(object sender, DrawItemEventArgs e)
        {
            var tc = sender as TabControl;
            if (tc == null || e.Index < 0 || e.Index >= tc.TabPages.Count) return;

            var g = e.Graphics;
            TabPage page = tc.TabPages[e.Index];
            Rectangle rect = tc.GetTabRect(e.Index);
            bool selected = e.Index == tc.SelectedIndex;

            // Header background — selected tab reads one step darker (toward the page surface) so it
            // connects visually with the content below it.
            Color bg = selected ? SupeyTheme.SurfaceBase : SupeyTheme.SurfaceHeader;
            using (var b = new SolidBrush(bg))
                g.FillRectangle(b, rect);

            int x = rect.Left + 12;
            int centerY = rect.Top + rect.Height / 2;

            // Icon from the shared tab ImageList, if this page has one assigned.
            if (tc.ImageList != null)
            {
                int imgIndex = ResolveImageIndex(tc, page);
                if (imgIndex >= 0 && imgIndex < tc.ImageList.Images.Count)
                {
                    var img = tc.ImageList.Images[imgIndex];
                    int iconY = centerY - (tc.ImageList.ImageSize.Height / 2);
                    g.DrawImage(img, x, iconY, tc.ImageList.ImageSize.Width, tc.ImageList.ImageSize.Height);
                    x += tc.ImageList.ImageSize.Width + 8;
                }
            }

            // Label.
            Color textColor = selected ? SupeyTheme.TextPrimary : SupeyTheme.TextSecondary;
            var textRect = new Rectangle(x, rect.Top, rect.Right - x - 8, rect.Height);
            TextRenderer.DrawText(g, page.Text ?? "", tc.Font, textRect, textColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine
                | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

            // Lime accent underline on the active tab.
            if (selected)
            {
                using (var accent = new SolidBrush(SupeyTheme.AccentPrimary))
                    g.FillRectangle(accent, rect.Left, rect.Bottom - 3, rect.Width, 3);
            }
        }

        private static int ResolveImageIndex(TabControl tc, TabPage page)
        {
            if (page.ImageIndex >= 0) return page.ImageIndex;
            if (!string.IsNullOrEmpty(page.ImageKey) && tc.ImageList != null)
                return tc.ImageList.Images.IndexOfKey(page.ImageKey);
            return -1;
        }
    }
}
