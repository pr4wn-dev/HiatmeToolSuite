using System;
using System.Drawing;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Compatibility-shaped button: a <see cref="SupeyButton"/> that also exposes the handful of
    /// MaterialButton members our Designer files and dialogs set (<c>Type</c>, <c>Density</c>,
    /// <c>UseAccentColor</c>, <c>HighEmphasis</c>, <c>Depth</c>, <c>MouseState</c>, <c>Icon</c>).
    /// Those map onto the owner-painted <see cref="SupeyButton.Variant"/> so the existing layout code
    /// compiles and renders correctly without MaterialSkin. New code can just use <see cref="SupeyButton"/>
    /// and set <see cref="SupeyButton.Kind"/> directly.
    /// </summary>
    internal class SupeyMaterialButton : SupeyButton
    {
        public enum MaterialButtonType { Text, Outlined, Contained }
        public enum MaterialButtonDensity { Default, Dense }

        private MaterialButtonType _type = MaterialButtonType.Contained;
        private bool _useAccent;
        private bool _highEmphasis;

        public MaterialButtonType Type
        {
            get => _type;
            set { _type = value; Recompute(); }
        }

        public bool UseAccentColor
        {
            get => _useAccent;
            set { _useAccent = value; Recompute(); }
        }

        public bool HighEmphasis
        {
            get => _highEmphasis;
            set { _highEmphasis = value; Recompute(); }
        }

        /// <summary>Accepted for Designer compatibility; the Supey skin has a single density.</summary>
        public MaterialButtonDensity Density { get; set; } = MaterialButtonDensity.Default;

        /// <summary>Accepted for Designer compatibility (MaterialSkin elevation); unused by the flat skin.</summary>
        public int Depth { get; set; }

        /// <summary>Accepted for Designer compatibility (MaterialSkin tracked mouse state); unused.</summary>
        public SupeyMouseState MouseState { get; set; } = SupeyMouseState.OUT;

        /// <summary>Accepted for Designer compatibility (ButtonBase.AutoSizeMode); unused by the flat skin.</summary>
        public AutoSizeMode AutoSizeMode { get; set; } = AutoSizeMode.GrowOnly;

        /// <summary>Accepted for Designer compatibility (ButtonBase.UseVisualStyleBackColor); unused.</summary>
        public bool UseVisualStyleBackColor { get; set; }

        /// <summary>Optional leading icon (stored for compatibility; flat buttons are text-only today).</summary>
        public Image Icon { get; set; }

        /// <summary>
        /// Text color for non-accent (Outlined/Ghost/Secondary) buttons. Maps to <see cref="Control.ForeColor"/>,
        /// which the Supey Outlined/Ghost variants paint with. Mirrors MaterialButton.NoAccentTextColor.
        /// </summary>
        public System.Drawing.Color NoAccentTextColor
        {
            get => ForeColor;
            set { ForeColor = value; Invalidate(); }
        }

        private void Recompute()
        {
            switch (_type)
            {
                case MaterialButtonType.Text:
                    Kind = Variant.Ghost;
                    break;
                case MaterialButtonType.Outlined:
                    Kind = Variant.Outlined;
                    break;
                case MaterialButtonType.Contained:
                default:
                    Kind = (_useAccent || _highEmphasis) ? Variant.Primary : Variant.Secondary;
                    break;
            }
        }
    }

    /// <summary>Mirror of MaterialSkin's MouseState enum so Designer assignments keep compiling.</summary>
    public enum SupeyMouseState { OUT, HOVER, DOWN }
}
