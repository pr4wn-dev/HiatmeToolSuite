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
    }
}
