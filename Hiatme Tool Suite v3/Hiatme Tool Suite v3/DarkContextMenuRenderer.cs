using System.Drawing;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// <see cref="ToolStripProfessionalRenderer"/> tuned to match the dark trip listviews —
    /// body fill <c>RGB(70,70,70)</c>, text white, hover/selection Royal Blue (same as
    /// <c>listView_DrawItem</c>'s focus highlight), separators a faint gray. Apply by setting
    /// <c>contextMenuStrip.Renderer = new DarkContextMenuRenderer();</c>.
    /// </summary>
    internal sealed class DarkContextMenuRenderer : ToolStripProfessionalRenderer
    {
        public static readonly Color Background = Color.FromArgb(70, 70, 70);
        public static readonly Color Border = Color.FromArgb(35, 35, 35);
        public static readonly Color HoverFill = Color.RoyalBlue;
        public static readonly Color Separator = Color.FromArgb(96, 96, 96);
        public static readonly Color ForeColor = Color.White;
        public static readonly Color DisabledForeColor = Color.FromArgb(150, 150, 150);

        public DarkContextMenuRenderer()
            : base(new DarkColorTable())
        {
            RoundedEdges = false;
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using (var bg = new SolidBrush(Background))
            {
                e.Graphics.FillRectangle(bg, e.AffectedBounds);
            }
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using (var pen = new Pen(Border))
            {
                var r = e.AffectedBounds;
                r.Width -= 1; r.Height -= 1;
                e.Graphics.DrawRectangle(pen, r);
            }
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            var item = e.Item;
            var rect = new Rectangle(2, 0, item.Width - 4, item.Height);
            Color fill = item.Selected && item.Enabled ? HoverFill : Background;
            using (var brush = new SolidBrush(fill))
            {
                e.Graphics.FillRectangle(brush, rect);
            }
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? ForeColor : DisabledForeColor;
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
            {
                e.Graphics.FillRectangle(bg, e.AffectedBounds);
            }
        }

        private sealed class DarkColorTable : ProfessionalColorTable
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
