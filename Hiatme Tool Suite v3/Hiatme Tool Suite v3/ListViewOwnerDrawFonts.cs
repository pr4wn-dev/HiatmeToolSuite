using System.Drawing;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Fonts used by <c>listView_DrawColumnHeader</c> / <c>listView_DrawSubItem</c> and by
    /// <see cref="ListViewMinWidthEnforcer"/> so column widths match painted text (not <see cref="ListView.Font"/>).
    /// </summary>
    internal static class ListViewOwnerDrawFonts
    {
        public static readonly Font Cell = new Font("Ariel", 10f, FontStyle.Regular);
        public static readonly Font Header = new Font("Archivo Medium", 11f, FontStyle.Regular);
    }
}
