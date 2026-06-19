using System.Drawing;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Accent (green) call-to-action button. Historically this overrode MaterialButton to paint dark
    /// text on the bright lime fill for contrast; that behavior is now native to
    /// <see cref="SupeyButton.Variant.Primary"/> (it paints <see cref="SupeyTheme.OnAccentText"/>),
    /// so this is just a <see cref="SupeyMaterialButton"/> that defaults to the Primary look. Kept as a
    /// distinct type so existing call sites and Designer fields compile unchanged.
    /// </summary>
    internal class DarkOnAccentMaterialButton : SupeyMaterialButton
    {
        public DarkOnAccentMaterialButton()
        {
            Kind = Variant.Primary;
        }

        /// <summary>Retained for source compatibility; SupeyButton already picks readable on-accent text.</summary>
        public Color OverrideTextColor { get; set; } = Color.FromArgb(20, 20, 20);
    }
}
