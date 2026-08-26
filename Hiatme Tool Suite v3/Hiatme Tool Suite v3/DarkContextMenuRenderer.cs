using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Dark Supey context menu with a fixed layout grid — icons, labels, and arrows land on the
    /// same columns in every row instead of letting WinForms guess from per-item padding.
    /// </summary>
    internal sealed class DarkContextMenuRenderer : ToolStripProfessionalRenderer
    {
        public static Color Background => SupeyTheme.SurfaceElevated;
        public static Color Border => SupeyTheme.BorderSubtle;
        public static Color Separator => SupeyTheme.Divider;
        public static Color ForeColor => SupeyTheme.TextPrimary;
        public static Color DisabledForeColor => SupeyTheme.TextMuted;

        /// <summary>Vertical padding inside each row (font height + this × 2 ≈ row height).</summary>
        public const int RowPadV = 5;

        /// <summary>Horizontal inset from the menu edge to the icon column.</summary>
        public const int EdgePadH = 8;

        /// <summary>Width reserved for the icon column; glyph is centered inside it.</summary>
        public const int IconColumn = 22;

        public const int IconSize = 16;

        /// <summary>Gap between the icon column and the label.</summary>
        public const int TextGap = 8;

        /// <summary>Right reserve for submenu chevrons.</summary>
        public const int ArrowColumn = 18;

        /// <summary>Where item text starts — every label aligns here.</summary>
        public static int TextLeft => EdgePadH + IconColumn + TextGap;

        private const int HoverInset = 4;
        private const int HoverRadius = 4;

        public DarkContextMenuRenderer()
            : base(new SupeyColorTable())
        {
            RoundedEdges = false;
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using (var bg = new SolidBrush(Background))
                e.Graphics.FillRectangle(bg, e.AffectedBounds);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using (var pen = new Pen(Border))
            {
                var r = e.AffectedBounds;
                r.Width -= 1;
                r.Height -= 1;
                e.Graphics.DrawRectangle(pen, r);
            }
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            var item = e.Item;
            var menuItem = item as ToolStripMenuItem;
            bool isOpenParent = menuItem != null && menuItem.DropDown != null && menuItem.DropDown.Visible;
            bool highlighted = item.Enabled && (item.Selected || isOpenParent);
            bool isChecked = menuItem != null && menuItem.Checked && item.Enabled;

            if (highlighted || isChecked)
            {
                var pill = new Rectangle(
                    HoverInset,
                    1,
                    item.Width - (HoverInset * 2),
                    item.Height - 2);
                if (pill.Width > 0 && pill.Height > 0)
                {
                    var g = e.Graphics;
                    g.SmoothingMode = SmoothingMode.AntiAlias;

                    if (highlighted)
                    {
                        using (var path = RoundedRect(pill, HoverRadius))
                        using (var brush = new SolidBrush(SupeyTheme.AccentPrimary))
                            g.FillPath(brush, path);
                    }
                    else
                    {
                        using (var path = RoundedRect(pill, HoverRadius))
                        using (var brush = new SolidBrush(Blend(Background, SupeyTheme.AccentPrimary, 0.14)))
                            g.FillPath(brush, path);
                    }

                    g.SmoothingMode = SmoothingMode.Default;
                }
            }

            // WinForms skips OnRenderItemImage when ShowImageMargin is false — paint here instead.
            DrawItemIcon(e);
        }

        protected override void OnRenderItemImage(ToolStripItemImageRenderEventArgs e)
        {
            // Icons are drawn in OnRenderMenuItemBackground so they always appear.
        }

        private static void DrawItemIcon(ToolStripItemRenderEventArgs e)
        {
            var image = e.Item.Image;
            if (image == null)
                return;

            var dest = IconBounds(e.Item);
            Color tint = !e.Item.Enabled
                ? DisabledForeColor
                : e.Item.Selected ? SupeyTheme.OnAccentText : SupeyTheme.TextSecondary;
            DrawTinted(e.Graphics, image, dest, tint);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            var menuItem = e.Item as ToolStripMenuItem;
            bool isOpenParent = menuItem != null && menuItem.DropDown != null && menuItem.DropDown.Visible;
            bool hasArrow = menuItem != null && menuItem.HasDropDownItems;

            int right = e.Item.Width - EdgePadH - (hasArrow ? ArrowColumn : 0);
            e.TextRectangle = new Rectangle(TextLeft, 0, Math.Max(0, right - TextLeft), e.Item.Height);
            e.TextFormat = TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix
                | TextFormatFlags.EndEllipsis;

            if (!e.Item.Enabled)
                e.TextColor = DisabledForeColor;
            else if (e.Item.Selected || isOpenParent)
                e.TextColor = SupeyTheme.OnAccentText;
            else
                e.TextColor = ForeColor;

            if (e.TextFont == null || e.TextFont == Control.DefaultFont)
                e.TextFont = SupeyTheme.BodyFont;

            base.OnRenderItemText(e);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            int left = TextLeft;
            int right = e.Item.Width - EdgePadH;
            if (right <= left)
            {
                left = EdgePadH;
                right = e.Item.Width - EdgePadH;
            }

            using (var pen = new Pen(Separator))
            {
                int y = e.Item.Height / 2;
                e.Graphics.DrawLine(pen, left, y, right, y);
            }
        }

        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
        {
            // Layout is handled in OnRenderItemImage — no separate margin strip.
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            bool live = e.Item.Enabled && e.Item.Selected;
            e.ArrowColor = !e.Item.Enabled
                ? DisabledForeColor
                : live ? SupeyTheme.OnAccentText : SupeyTheme.TextSecondary;

            int w = 8;
            int h = 10;
            int x = e.Item.Width - EdgePadH - w;
            int y = (e.Item.Height - h) / 2;
            e.ArrowRectangle = new Rectangle(x, y, w, h);
            base.OnRenderArrow(e);
        }

        /// <summary>Pixel box where every menu glyph is drawn — centered in the icon column.</summary>
        public static Rectangle IconBounds(ToolStripItem item)
        {
            int x = EdgePadH + (IconColumn - IconSize) / 2;
            int y = (item.Height - IconSize) / 2;
            return new Rectangle(x, y, IconSize, IconSize);
        }

        private static void DrawTinted(Graphics g, Image image, Rectangle dest, Color color)
        {
            using (var attrs = new ImageAttributes())
            {
                attrs.SetRemapTable(new[]
                {
                    new ColorMap { OldColor = Color.White, NewColor = color },
                    new ColorMap { OldColor = Color.FromArgb(255, 255, 255), NewColor = color },
                });
                g.DrawImage(
                    image,
                    dest,
                    0,
                    0,
                    image.Width,
                    image.Height,
                    GraphicsUnit.Pixel,
                    attrs);
            }
        }

        private static GraphicsPath RoundedRect(Rectangle r, int radius)
        {
            int d = radius * 2;
            var path = new GraphicsPath();
            if (d <= 0 || r.Width <= d || r.Height <= d)
            {
                path.AddRectangle(r);
                return path;
            }

            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static Color Blend(Color a, Color b, double amountB)
        {
            amountB = amountB < 0d ? 0d : amountB > 1d ? 1d : amountB;
            double amountA = 1d - amountB;
            return Color.FromArgb(
                (int)(a.R * amountA + b.R * amountB),
                (int)(a.G * amountA + b.G * amountB),
                (int)(a.B * amountA + b.B * amountB));
        }

        private sealed class SupeyColorTable : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground => Background;
            public override Color ImageMarginGradientBegin => Background;
            public override Color ImageMarginGradientMiddle => Background;
            public override Color ImageMarginGradientEnd => Background;
            public override Color MenuBorder => Border;
            public override Color MenuItemBorder => Color.Transparent;
            public override Color MenuItemSelected => Background;
            public override Color MenuItemSelectedGradientBegin => Background;
            public override Color MenuItemSelectedGradientEnd => Background;
            public override Color MenuItemPressedGradientBegin => Background;
            public override Color MenuItemPressedGradientMiddle => Background;
            public override Color MenuItemPressedGradientEnd => Background;
            public override Color SeparatorDark => Separator;
            public override Color SeparatorLight => Separator;
            public override Color MenuStripGradientBegin => Background;
            public override Color MenuStripGradientEnd => Background;
        }
    }
}
