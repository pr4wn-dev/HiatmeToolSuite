using System.Drawing;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// <see cref="ToolStripProfessionalRenderer"/> that follows the active <see cref="SupeyTheme"/> —
    /// same surface/text/accent ladder as combos and ListViews. Apply with
    /// <c>contextMenuStrip.Renderer = new DarkContextMenuRenderer();</c>.
    /// </summary>
    internal sealed class DarkContextMenuRenderer : ToolStripProfessionalRenderer
    {
        public static Color Background => SupeyTheme.SurfaceElevated;
        public static Color Border => SupeyTheme.BorderSubtle;
        public static Color HoverFill => SupeyTheme.ListSelected;
        public static Color Separator => SupeyTheme.Divider;
        public static Color ForeColor => SupeyTheme.TextPrimary;
        public static Color DisabledForeColor => SupeyTheme.TextMuted;

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
            var rect = new Rectangle(2, 0, item.Width - 4, item.Height);
            Color fill = Background;
            if (item is ToolStripMenuItem menuItem && menuItem.Checked && item.Enabled)
                fill = Blend(Background, SupeyTheme.AccentPrimary, 0.22);
            if (item.Selected && item.Enabled)
                fill = HoverFill;
            using (var brush = new SolidBrush(fill))
                e.Graphics.FillRectangle(brush, rect);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            if (e.Item is ToolStripMenuItem menuItem && menuItem.Checked && e.Item.Enabled)
                e.TextColor = SupeyTheme.ListSelectedText;
            else
                e.TextColor = e.Item.Enabled ? ForeColor : DisabledForeColor;
            if (e.TextFont == null || e.TextFont == Control.DefaultFont)
                e.TextFont = SupeyTheme.BodyFont;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            using (var pen = new Pen(Separator))
            {
                int y = e.Item.Height / 2;
                e.Graphics.DrawLine(pen, 4, y, e.Item.Width - 4, y);
            }
        }

        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
        {
            using (var bg = new SolidBrush(Background))
                e.Graphics.FillRectangle(bg, e.AffectedBounds);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = e.Item.Selected && e.Item.Enabled
                ? SupeyTheme.ListSelectedText
                : SupeyTheme.TextSecondary;
            base.OnRenderArrow(e);
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
            public override Color MenuItemBorder => HoverFill;
            public override Color MenuItemSelected => HoverFill;
            public override Color MenuItemSelectedGradientBegin => HoverFill;
            public override Color MenuItemSelectedGradientEnd => HoverFill;
            public override Color SeparatorDark => Separator;
            public override Color SeparatorLight => Separator;
            public override Color MenuStripGradientBegin => Background;
            public override Color MenuStripGradientEnd => Background;
        }
    }
}
