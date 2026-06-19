using System;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Theme-driven replacement for MaterialLabel. A plain <see cref="Label"/> that pulls its colors
    /// from <see cref="SupeyTheme"/> and live-updates on theme switches, plus no-op shims for the
    /// MaterialLabel members our Designer files set (<c>Depth</c>, <c>MouseState</c>, <c>HighEmphasis</c>)
    /// so nothing has to be rewritten at the call site.
    /// </summary>
    internal class SupeyLabel : Label
    {
        private bool _highEmphasis = true;

        public SupeyLabel()
        {
            BackColor = System.Drawing.Color.Transparent;
            ForeColor = SupeyTheme.TextPrimary;
            Font = SupeyTheme.BodyFont;
            SupeyThemeManager.ThemeChanged += OnThemeChanged;
        }

        /// <summary>High emphasis = primary text color; low emphasis = secondary/muted.</summary>
        public bool HighEmphasis
        {
            get => _highEmphasis;
            set { _highEmphasis = value; ApplyEmphasis(); }
        }

        /// <summary>Accepted for Designer compatibility (MaterialSkin elevation); unused by the flat skin.</summary>
        public int Depth { get; set; }

        /// <summary>Accepted for Designer compatibility (MaterialSkin tracked mouse state); unused.</summary>
        public SupeyMouseState MouseState { get; set; } = SupeyMouseState.OUT;

        private void ApplyEmphasis()
        {
            ForeColor = _highEmphasis ? SupeyTheme.TextPrimary : SupeyTheme.TextSecondary;
        }

        private void OnThemeChanged(object sender, EventArgs e)
        {
            if (IsDisposed) return;
            ApplyEmphasis();
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                SupeyThemeManager.ThemeChanged -= OnThemeChanged;
            base.Dispose(disposing);
        }
    }
}
