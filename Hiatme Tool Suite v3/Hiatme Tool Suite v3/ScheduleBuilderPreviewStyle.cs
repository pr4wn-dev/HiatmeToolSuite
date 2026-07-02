using System;
using System.Drawing;

namespace Hiatme_Tool_Suite_v3
{
    internal static class ScheduleBuilderPreviewStyle
    {
        public static Color RouteHeaderBackColor(Color groupColor)
        {
            int r = Math.Max(0, groupColor.R - 48);
            int g = Math.Max(0, groupColor.G - 48);
            int b = Math.Max(0, groupColor.B - 48);
            return Color.FromArgb(255, r, g, b);
        }

        public static Color ContrastText(Color background)
        {
            int lum = (background.R * 299 + background.G * 587 + background.B * 114) / 1000;
            return lum < 128 ? Color.White : Color.FromArgb(28, 28, 28);
        }

        /// <summary>Reserve section title bars — always white so every bucket matches.</summary>
        public static Color ReserveSectionHeaderText => Color.White;

        /// <summary>Trip rows rerouted on Modivcare (list + workbook fill).</summary>
        public static Color ReroutedTripBackColor => Color.FromArgb(132, 88, 36);

        public static Color ReroutedTripSelectedBackColor => Color.FromArgb(158, 108, 44);

        /// <summary>WellRyde Cancelled / Suspended — rose (distinct from reroute red and amber band).</summary>
        public static Color CancelledTripBackColor => Color.FromArgb(168, 72, 98);

        public static Color CancelledTripSelectedBackColor => Color.FromArgb(190, 88, 115);
    }
}
