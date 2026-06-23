using System.Drawing;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Central palette + typography facade for the whole app. Historically these were fixed
    /// <c>static readonly</c> colors; they are now thin properties that forward to the active
    /// <see cref="SupeyThemeManager.Current"/> palette. Every existing <c>SupeyTheme.X</c> call site
    /// keeps working unchanged, but the values become dynamic — switch the preset in
    /// <see cref="SupeyThemeManager"/> and the next paint of every control picks up the new colors.
    ///
    /// The named ladder is unchanged: surfaces (base -> elevated -> header -> status bar), borders/
    /// dividers, text, semantic accents, and a separate ListView sub-palette so owner-drawn trip /
    /// driver lists keep their own identity.
    /// </summary>
    internal static class SupeyTheme
    {
        private static SupeyThemePalette P => SupeyThemeManager.Current;

        // ── Surfaces (lightest = closest to the user) ────────────────────────────
        public static Color SurfaceBase => P.SurfaceBase;
        public static Color Surface => P.Surface;
        public static Color SurfaceElevated => P.SurfaceElevated;
        public static Color SurfaceHeader => P.SurfaceHeader;
        public static Color SurfaceStatusBar => P.SurfaceStatusBar;

        // ── Borders / dividers ───────────────────────────────────────────────────
        public static Color Divider => P.Divider;
        public static Color BorderSubtle => P.BorderSubtle;

        // ── Text ─────────────────────────────────────────────────────────────────
        public static Color TextPrimary => P.TextPrimary;
        public static Color TextSecondary => P.TextSecondary;
        public static Color TextMuted => P.TextMuted;
        public static Color TextLink => P.TextLink;

        // ── Accents (semantic) ───────────────────────────────────────────────────
        public static Color AccentPrimary => P.AccentPrimary;
        public static Color AccentStripe => P.AccentStripe;
        public static Color SuccessText => P.SuccessText;
        public static Color WarnText => P.WarnText;
        public static Color ErrorText => P.ErrorText;

        /// <summary>Readable foreground to place on top of <see cref="AccentPrimary"/>.</summary>
        public static Color OnAccentText => P.OnAccentText;

        // ── ListView palette (intentionally separate from the surface ladder) ───
        public static Color ListBody => P.ListBody;
        public static Color ListBodyAlt => P.ListBodyAlt;
        public static Color ListHeader => P.ListHeader;
        public static Color ListHeaderText => P.ListHeaderText;
        public static Color ListGrid => P.ListGrid;
        /// <summary>Owner-draw cell grid lines — readable on ListBody but softer than legacy ListGrid.</summary>
        public static Color ListGridLine => ResolveListGridLine(P);
        public static Color ListSelected => P.ListSelected;
        public static Color ListSelectedText => P.ListSelectedText;
        public static Color ListText => P.ListText;

        // ── Typography ───────────────────────────────────────────────────────────
        public static Font HeaderFont => P.HeaderFont;
        public static Font SubHeaderFont => P.SubHeaderFont;
        public static Font BodyFont => P.BodyFont;
        public static Font CaptionFont => P.CaptionFont;
        public static Font MonoFont => P.MonoFont;

        private static Color ResolveListGridLine(SupeyThemePalette palette)
        {
            if (palette == null)
                return Color.FromArgb(42, 42, 44);
            if (!palette.ListGridLine.IsEmpty)
                return palette.ListGridLine;
            // Midpoint between row fill and theme divider — visible workbook grid, not flat ListGrid.
            return BlendColors(palette.ListBody, palette.Divider, 0.48);
        }

        internal static Color ListGridLineForPalette(SupeyThemePalette palette) => ResolveListGridLine(palette);

        /// <summary>Previous cell grid formula — used only for live theme-switch color remap.</summary>
        internal static Color LegacyListGridLineBlend(SupeyThemePalette palette)
            => BlendColors(palette.ListBody, palette.ListGrid, 0.36);

        private static Color BlendColors(Color from, Color to, double amountTo)
        {
            amountTo = amountTo < 0d ? 0d : amountTo > 1d ? 1d : amountTo;
            double amountFrom = 1d - amountTo;
            return Color.FromArgb(
                (int)((from.R * amountFrom) + (to.R * amountTo)),
                (int)((from.G * amountFrom) + (to.G * amountTo)),
                (int)((from.B * amountFrom) + (to.B * amountTo)));
        }
    }
}
