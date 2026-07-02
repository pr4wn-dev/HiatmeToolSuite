using System.Drawing;
using System.Drawing.Drawing2D;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>16×16 glyph — Modivcare download into Reserves (accent + = new trip).</summary>
    internal static class ScheduleBuilderModivcareNewTripsIcon
    {
        private const int Size = 16;

        public static Bitmap Create(bool enabled)
        {
            Color ink = enabled ? SupeyTheme.TextPrimary : SupeyTheme.TextMuted;
            Color accent = enabled ? SupeyTheme.AccentPrimary : SupeyTheme.TextMuted;

            var bmp = new Bitmap(Size, Size);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(Color.Transparent);

                using (var pen = new Pen(ink, 1.55f)
                {
                    StartCap = LineCap.Round,
                    EndCap = LineCap.Round,
                    LineJoin = LineJoin.Round,
                })
                {
                    // Inbox tray
                    g.DrawArc(pen, 2.5f, 9.5f, 3.5f, 3.5f, 90, 90);
                    g.DrawLine(pen, 4.25f, 9.5f, 4.25f, 12.5f);
                    g.DrawLine(pen, 4.25f, 12.5f, 11.75f, 12.5f);
                    g.DrawArc(pen, 10f, 9.5f, 3.5f, 3.5f, 0, 90);

                    // Arrow shaft
                    g.DrawLine(pen, 8f, 3.5f, 8f, 8.5f);
                    // Arrow head
                    g.DrawLines(pen, new[]
                    {
                        new PointF(5.5f, 6.5f),
                        new PointF(8f, 9.5f),
                        new PointF(10.5f, 6.5f),
                    });
                }

                // New-trip accent badge
                using (var fill = new SolidBrush(accent))
                using (var plus = new Pen(SupeyTheme.OnAccentText, 1.35f)
                {
                    StartCap = LineCap.Round,
                    EndCap = LineCap.Round,
                })
                {
                    g.FillEllipse(fill, 10.5f, 10.5f, 4.5f, 4.5f);
                    g.DrawLine(plus, 12f, 11.8f, 12f, 13.7f);
                    g.DrawLine(plus, 11.1f, 12.75f, 12.9f, 12.75f);
                }
            }

            return bmp;
        }
    }
}
