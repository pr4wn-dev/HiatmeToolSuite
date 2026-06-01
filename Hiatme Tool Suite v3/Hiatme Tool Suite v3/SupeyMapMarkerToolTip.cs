using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using GMap.NET.WindowsForms;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Dark-themed GMap marker hover card for Supey / Schedule Builder maps.
    /// </summary>
    internal sealed class SupeyMapMarkerToolTip : GMapToolTip
    {
        private static readonly Color BackgroundColor = Color.FromArgb(250, 34, 38, 34);
        private static readonly Color BorderColor = SupeyTheme.BorderSubtle;
        private static readonly Color TitleColor = SupeyTheme.TextPrimary;
        private static readonly Color DetailColor = SupeyTheme.TextSecondary;
        private static readonly Color DividerColor = SupeyTheme.Divider;

        private const int AccentWidth = 4;
        private const int OuterPadX = 14;
        private const int OuterPadY = 11;
        private const int TextLeftGap = 12;
        private const int LineSpacing = 3;
        private const int TitleToDividerGap = 6;
        private const int DividerToDetailGap = 7;

        public string Title { get; set; } = "";
        public List<string> Detail { get; set; } = new List<string>();
        public Color AccentColor { get; set; } = SupeyTheme.AccentStripe;

        private readonly Font _titleFont;
        private readonly Font _detailFont;

        public SupeyMapMarkerToolTip(GMapMarker marker, Font titleFont, Font detailFont) : base(marker)
        {
            _titleFont = titleFont;
            _detailFont = detailFont;
            Offset = new Point(14, -10);
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

            var prevSmoothing = g.SmoothingMode;
            var prevHint = g.TextRenderingHint;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            for (int d = 4; d >= 1; d--)
            {
                int alpha = 48 / d;
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
