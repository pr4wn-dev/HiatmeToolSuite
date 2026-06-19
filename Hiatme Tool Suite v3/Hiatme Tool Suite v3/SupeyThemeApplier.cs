using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Live-recolors a control tree when the theme switches. Most of the app sets colors imperatively
    /// from <see cref="SupeyTheme"/> (e.g. <c>card.BackColor = SupeyTheme.Surface</c>). Because
    /// <see cref="SupeyTheme"/> is now dynamic, those controls keep the OLD color value after a switch.
    ///
    /// Rather than re-run every per-tab theming method (heavy, easy to break), we build a map from the
    /// previous palette's colors to the current palette's colors and walk the tree, remapping any
    /// BackColor / ForeColor that matches a known previous-theme color. This only touches controls
    /// that were themed (a coincidental match is harmless), and owner-drawn surfaces (ListViews,
    /// SupeyButton, SupeyCard, the tab strip) repaint themselves from the live palette on Invalidate.
    /// </summary>
    internal static class SupeyThemeApplier
    {
        public static void Recolor(Control root)
        {
            if (root == null) return;
            var prev = SupeyThemeManager.Previous;
            var cur = SupeyThemeManager.Current;
            if (prev == null || cur == null) return;

            var map = BuildMap(prev, cur);
            RecolorTree(root, map);
        }

        private static Dictionary<int, Color> BuildMap(SupeyThemePalette a, SupeyThemePalette b)
        {
            var m = new Dictionary<int, Color>();
            void Add(Color from, Color to)
            {
                int key = from.ToArgb();
                if (!m.ContainsKey(key)) m[key] = to;
            }

            Add(a.SurfaceBase, b.SurfaceBase);
            Add(a.Surface, b.Surface);
            Add(a.SurfaceElevated, b.SurfaceElevated);
            Add(a.SurfaceHeader, b.SurfaceHeader);
            Add(a.SurfaceStatusBar, b.SurfaceStatusBar);
            Add(a.Divider, b.Divider);
            Add(a.BorderSubtle, b.BorderSubtle);
            Add(a.TextPrimary, b.TextPrimary);
            Add(a.TextSecondary, b.TextSecondary);
            Add(a.TextMuted, b.TextMuted);
            Add(a.TextLink, b.TextLink);
            Add(a.AccentPrimary, b.AccentPrimary);
            Add(a.AccentStripe, b.AccentStripe);
            Add(a.SuccessText, b.SuccessText);
            Add(a.WarnText, b.WarnText);
            Add(a.ErrorText, b.ErrorText);
            Add(a.ListBody, b.ListBody);
            Add(a.ListBodyAlt, b.ListBodyAlt);
            Add(a.ListHeader, b.ListHeader);
            Add(a.ListHeaderText, b.ListHeaderText);
            Add(a.ListGrid, b.ListGrid);
            Add(a.ListSelected, b.ListSelected);
            Add(a.ListSelectedText, b.ListSelectedText);
            Add(a.ListText, b.ListText);
            return m;
        }

        private static void RecolorTree(Control c, Dictionary<int, Color> map)
        {
            if (c == null) return;

            if (map.TryGetValue(c.BackColor.ToArgb(), out Color nb))
                c.BackColor = nb;
            if (map.TryGetValue(c.ForeColor.ToArgb(), out Color nf))
                c.ForeColor = nf;

            foreach (Control child in c.Controls)
                RecolorTree(child, map);

            c.Invalidate();
        }
    }
}
