using System.Drawing;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Fonts used by owner-draw ListViews and <see cref="ListViewMinWidthEnforcer"/>.
    /// Follows the active theme so generated palettes can change list typography.
    /// </summary>
    internal static class ListViewOwnerDrawFonts
    {
        public static Font Cell => SupeyTheme.ListCellFont;
        public static Font Header => SupeyTheme.ListHeaderFont;
    }
}
