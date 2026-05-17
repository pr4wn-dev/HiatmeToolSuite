using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using MaterialSkin;
using MaterialSkin.Controls;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// <see cref="MaterialButton"/> tweak that paints the label in a dark color when the button is
    /// rendered with the bright Lime accent fill. The stock MaterialButton always uses white text on
    /// Contained-accent buttons, which fails contrast against MaterialSkin's Lime700 accent — the
    /// "ASSIGN" label looked washed out in the Trip Scout driver picker. We let the base class draw
    /// the fill / shadow / ripple as usual, then mask the text region with the accent color and
    /// redraw the label using <see cref="OverrideTextColor"/>.
    /// </summary>
    /// <remarks>
    /// Only kicks in for <see cref="MaterialButtonType.Contained"/> + <see cref="MaterialButton.UseAccentColor"/> = true.
    /// Other styles fall through to the base implementation untouched.
    /// </remarks>
    internal class DarkOnAccentMaterialButton : MaterialButton
    {
        /// <summary>Color used for the label. Defaults to a near-black for max contrast on Lime700.</summary>
        public Color OverrideTextColor { get; set; } = Color.FromArgb(20, 20, 20);

        // Cached bold derivative of the control's Font so we don't allocate a Font on every paint.
        // Invalidated whenever Font changes; disposed on control disposal.
        private Font _boldFont;

        public DarkOnAccentMaterialButton()
        {
            // Repaint when Enabled flips so the cursor/visuals stay in sync. Visuals stay bright
            // green in both states (per UX request); the disabled cue is the standard disabled
            // cursor + the host form's tooltip explaining why the button is currently inert.
            EnabledChanged += (s, e) => Invalidate();
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            _boldFont?.Dispose();
            _boldFont = null;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _boldFont?.Dispose();
                _boldFont = null;
            }
            base.Dispose(disposing);
        }

        private Font GetBoldFont()
        {
            if (_boldFont == null)
            {
                _boldFont = new Font(Font, FontStyle.Bold);
            }
            return _boldFont;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            if (Type != MaterialButtonType.Contained) return;
            if (!UseAccentColor) return;
            if (string.IsNullOrEmpty(Text)) return;

            Color accentFill = SafeAccentColor();
            string label = ApplyCasing(Text);
            Font font = GetBoldFont();
            const TextFormatFlags textFlags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                | TextFormatFlags.SingleLine | TextFormatFlags.GlyphOverhangPadding;

            if (!Enabled)
            {
                // The user wants accent buttons to keep the same bright green look in both
                // states — no muting on disabled. We repaint the entire surface ourselves so
                // MaterialSkin's stock dim-grey disabled fill is fully covered. A 4px rounded
                // path matches MaterialSkin's Contained button shape so corners line up.
                var fullRect = new Rectangle(0, 0, Width, Height);
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var path = CreateRoundedPath(fullRect, 4))
                using (var bg = new SolidBrush(accentFill))
                {
                    e.Graphics.FillPath(bg, path);
                }
                TextRenderer.DrawText(e.Graphics, label, font, fullRect, OverrideTextColor, textFlags);
                return;
            }

            // Enabled: keep MaterialSkin's full Contained look (ripple, hover, shadow) and just
            // overlay the label region with a fresh accent fill + dark bold text so the
            // white-on-lime default doesn't kill contrast.
            Size measured = TextRenderer.MeasureText(label, font);
            var textRect = new Rectangle(
                (Width - measured.Width) / 2,
                (Height - measured.Height) / 2,
                measured.Width,
                measured.Height);
            // Inflate so we fully cover the anti-aliased edges of the original white text.
            textRect.Inflate(3, 2);

            using (var bg = new SolidBrush(accentFill))
            {
                e.Graphics.FillRectangle(bg, textRect);
            }
            TextRenderer.DrawText(e.Graphics, label, font, textRect, OverrideTextColor, textFlags);
        }

        /// <summary>
        /// Builds a rounded-rectangle <see cref="GraphicsPath"/> with corner radius
        /// <paramref name="radius"/>. MaterialSkin's Contained buttons use a 4px corner radius;
        /// matching that here keeps the disabled fill from looking like a sharp-cornered shim.
        /// </summary>
        private static GraphicsPath CreateRoundedPath(Rectangle bounds, int radius)
        {
            var path = new GraphicsPath();
            int d = radius * 2;
            if (d <= 0 || bounds.Width <= d || bounds.Height <= d)
            {
                path.AddRectangle(bounds);
                return path;
            }
            path.AddArc(bounds.X, bounds.Y, d, d, 180f, 90f);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270f, 90f);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0f, 90f);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90f, 90f);
            path.CloseFigure();
            return path;
        }

        /// <summary>Mirrors how the base button uppercases its label so our overlay matches what was painted.</summary>
        private string ApplyCasing(string s)
        {
            switch (CharacterCasing)
            {
                case CharacterCasingEnum.Upper: return s.ToUpper();
                case CharacterCasingEnum.Lower: return s.ToLower();
                default: return s;
            }
        }

        private static Color SafeAccentColor()
        {
            try
            {
                return MaterialSkinManager.Instance?.ColorScheme?.AccentColor ?? Color.YellowGreen;
            }
            catch
            {
                return Color.YellowGreen;
            }
        }
    }
}
