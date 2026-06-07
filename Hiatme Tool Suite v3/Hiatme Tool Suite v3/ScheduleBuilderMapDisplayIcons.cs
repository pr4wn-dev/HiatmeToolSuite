using System.Drawing;
using System.Drawing.Drawing2D;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>18×18 toolbar glyphs for Schedule Builder map display mode toggles.</summary>
    internal static class ScheduleBuilderMapDisplayIcons
    {
        private const int Size = 18;
        private static readonly Color Ink = Color.FromArgb(220, 220, 220);
        private static readonly Color Muted = Color.FromArgb(130, 130, 130);
        private static readonly Color Accent = Color.FromArgb(76, 175, 80);

        private static Bitmap _allTrips;
        private static Bitmap _selectedGroup;
        private static Bitmap _selectedTrips;

        public static Bitmap AllDriverTrips(bool selected) =>
            _allTrips ?? (_allTrips = BuildAllTrips());

        public static Bitmap SelectedGroup(bool selected) =>
            _selectedGroup ?? (_selectedGroup = BuildSelectedGroup());

        public static Bitmap SelectedTrips(bool selected) =>
            _selectedTrips ?? (_selectedTrips = BuildSelectedTrips());

        public static Color IconColor(bool selected) => selected ? Accent : Ink;

        private static Bitmap BuildAllTrips()
        {
            var bmp = new Bitmap(Size, Size);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using (var pen = new Pen(Ink, 1.6f))
                {
                    for (int i = 0; i < 3; i++)
                    {
                        int y = 3 + i * 5;
                        g.DrawLine(pen, 2, y, 16, y);
                    }
                }
                using (var pen = new Pen(Muted, 1.2f))
                {
                    g.DrawRectangle(pen, 2, 2, 14, 13);
                }
            }
            return bmp;
        }

        private static Bitmap BuildSelectedGroup()
        {
            var bmp = new Bitmap(Size, Size);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using (var fill = new SolidBrush(Ink))
                using (var line = new Pen(Muted, 1.2f))
                {
                    g.FillEllipse(fill, 2, 6, 5, 5);
                    g.FillEllipse(fill, 11, 2, 5, 5);
                    g.FillEllipse(fill, 11, 11, 5, 5);
                    g.DrawLine(line, 6, 8, 11, 5);
                    g.DrawLine(line, 6, 9, 11, 13);
                }
            }
            return bmp;
        }

        private static Bitmap BuildSelectedTrips()
        {
            var bmp = new Bitmap(Size, Size);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using (var fill = new SolidBrush(Ink))
                using (var ring = new Pen(Accent, 1.4f))
                {
                    g.FillEllipse(fill, 7, 2, 6, 6);
                    g.DrawLine(new Pen(Ink, 1.5f), 10, 8, 10, 14);
                    g.DrawEllipse(ring, 2, 10, 7, 7);
                }
            }
            return bmp;
        }
    }
}
