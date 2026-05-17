using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using GMap.NET.WindowsForms;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Marker tooltip used by <see cref="DriverLocationMapForm"/>. Owns its own draw routine so we
    /// can lay out a bold title line (driver name) on top of muted detail rows, add a colored
    /// "live status" accent stripe on the left edge, and drop a soft shadow underneath.
    /// </summary>
    /// <remarks>
    /// Fonts are owned by the parent form and passed in by reference; this class never disposes
    /// them so the auto-refresh loop can rebuild a tooltip on every render without GDI churn.
    /// </remarks>
    internal sealed class DriverMarkerToolTip : GMapToolTip
    {
        private static readonly Color BackgroundColor = Color.FromArgb(245, 40, 40, 40);
        private static readonly Color BorderColor = Color.FromArgb(110, 110, 110);
        private static readonly Color TitleColor = Color.White;
        private static readonly Color DetailColor = Color.Gainsboro;
        private static readonly Color DividerColor = Color.FromArgb(80, 80, 80);

        private const int AccentWidth = 4;
        private const int OuterPadX = 14;
        private const int OuterPadY = 12;
        private const int TextLeftGap = 12;
        private const int LineSpacing = 4;
        private const int TitleToDividerGap = 6;
        private const int DividerToDetailGap = 8;

        public string Title { get; set; } = "";
        public List<string> Detail { get; set; } = new List<string>();
        public Color AccentColor { get; set; } = Color.FromArgb(76, 175, 80);

        private readonly Font _titleFont;
        private readonly Font _detailFont;

        public DriverMarkerToolTip(GMapMarker marker, Font titleFont, Font detailFont) : base(marker)
        {
            _titleFont = titleFont;
            _detailFont = detailFont;
            // Default GMapToolTip places the box right at the marker; nudge it up-right so the
            // pushpin shape and the box don't fight over the same pixels.
            Offset = new Point(16, -8);
        }

        public override void OnRender(Graphics g)
        {
            string title = Title ?? "";
            var detail = Detail ?? new List<string>();

            var titleSize = string.IsNullOrEmpty(title)
                ? Size.Empty
                : Size.Ceiling(g.MeasureString(title, _titleFont));

            int detailMaxWidth = 0;
            int detailHeight = 0;
            var detailLineHeights = new List<int>(detail.Count);
            foreach (var d in detail)
            {
                var s = Size.Ceiling(g.MeasureString(string.IsNullOrEmpty(d) ? " " : d, _detailFont));
                detailLineHeights.Add(s.Height);
                detailHeight += s.Height + LineSpacing;
                if (s.Width > detailMaxWidth) detailMaxWidth = s.Width;
            }
            if (detail.Count > 0) detailHeight -= LineSpacing;

            int contentWidth = System.Math.Max(titleSize.Width, detailMaxWidth);
            int contentHeight = titleSize.Height;
            bool hasDivider = !string.IsNullOrEmpty(title) && detail.Count > 0;
            if (hasDivider) contentHeight += TitleToDividerGap + 1 + DividerToDetailGap;
            else if (detail.Count > 0 && string.IsNullOrEmpty(title)) contentHeight = 0;
            contentHeight += detailHeight;

            int leftPad = AccentWidth + TextLeftGap;
            int boxWidth = leftPad + contentWidth + OuterPadX;
            int boxHeight = OuterPadY + contentHeight + OuterPadY;

            var pos = Marker.ToolTipPosition;
            pos.Offset(Offset.X, Offset.Y);
            var rect = new Rectangle(pos, new Size(boxWidth, boxHeight));

            // Snapshot + restore Graphics state so we don't disturb subsequent overlay drawing.
            var prevSmoothing = g.SmoothingMode;
            var prevHint = g.TextRenderingHint;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            // Soft shadow — three offset rectangles with decreasing alpha approximate a blurred
            // drop shadow without an offscreen buffer. Drawn before the box so the box masks the
            // alpha overlap on its own footprint.
            for (int d = 4; d >= 1; d--)
            {
                int alpha = 56 / d;
                using (var sh = new SolidBrush(Color.FromArgb(alpha, 0, 0, 0)))
                    g.FillRectangle(sh, new Rectangle(rect.X + d, rect.Y + d, rect.Width, rect.Height));
            }

            using (var bg = new SolidBrush(BackgroundColor))
                g.FillRectangle(bg, rect);

            using (var accent = new SolidBrush(AccentColor))
                g.FillRectangle(accent, new Rectangle(rect.X, rect.Y, AccentWidth, rect.Height));

            using (var border = new Pen(BorderColor, 1f))
                g.DrawRectangle(border, new Rectangle(rect.X, rect.Y, rect.Width - 1, rect.Height - 1));

            int textX = rect.X + leftPad;
            int y = rect.Y + OuterPadY;

            if (!string.IsNullOrEmpty(title))
            {
                using (var titleBrush = new SolidBrush(TitleColor))
                    g.DrawString(title, _titleFont, titleBrush, textX, y);
                y += titleSize.Height;
            }

            if (hasDivider)
            {
                y += TitleToDividerGap;
                using (var div = new Pen(DividerColor, 1f))
                    g.DrawLine(div, textX, y, rect.Right - OuterPadX, y);
                y += 1 + DividerToDetailGap;
            }

            if (detail.Count > 0)
            {
                using (var detailBrush = new SolidBrush(DetailColor))
                {
                    for (int i = 0; i < detail.Count; i++)
                    {
                        g.DrawString(detail[i] ?? "", _detailFont, detailBrush, textX, y);
                        y += detailLineHeights[i] + LineSpacing;
                    }
                }
            }

            g.SmoothingMode = prevSmoothing;
            g.TextRenderingHint = prevHint;
        }
    }
}
