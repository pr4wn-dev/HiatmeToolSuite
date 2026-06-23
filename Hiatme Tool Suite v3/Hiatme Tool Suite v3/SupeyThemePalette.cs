using System.Drawing;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// A single named color/typography palette. This is the data behind <see cref="SupeyTheme"/>:
    /// the static facade simply forwards every <c>SupeyTheme.X</c> reference to the matching field
    /// on <see cref="SupeyThemeManager.Current"/>, so swapping the active palette instantly retints
    /// the whole app. Fonts are carried here too (presets share the same family today, but keeping
    /// them on the palette means a future "large text" / "compact" preset is a drop-in).
    ///
    /// Colors are grouped exactly like the original SupeyTheme ladder: surfaces (base -> elevated ->
    /// header -> status bar), borders/dividers, text, semantic accents, and a separate ListView
    /// sub-palette so owner-drawn lists keep their own identity.
    /// </summary>
    internal sealed class SupeyThemePalette
    {
        public string Name = "Black & Lime";

        /// <summary>0 = classic preset; 1+ = generated Tetris-style level.</summary>
        public int Level;

        public int Index;

        public bool IsGenerated;

        /// <summary>Filename stem under Resources/login_backgrounds/ (no extension).</summary>
        public string LoginBackgroundKey;

        // ── Surfaces ─────────────────────────────────────────────────────────────
        public Color SurfaceBase = Color.FromArgb(24, 24, 24);
        public Color Surface = Color.FromArgb(32, 32, 32);
        public Color SurfaceElevated = Color.FromArgb(40, 40, 40);
        public Color SurfaceHeader = Color.FromArgb(28, 28, 28);
        public Color SurfaceStatusBar = Color.FromArgb(20, 20, 20);

        // ── Borders / dividers ───────────────────────────────────────────────────
        public Color Divider = Color.FromArgb(56, 56, 56);
        public Color BorderSubtle = Color.FromArgb(64, 64, 64);

        // ── Text ─────────────────────────────────────────────────────────────────
        public Color TextPrimary = Color.FromArgb(232, 232, 232);
        public Color TextSecondary = Color.FromArgb(176, 176, 176);
        public Color TextMuted = Color.FromArgb(128, 128, 128);
        public Color TextLink = Color.FromArgb(120, 180, 240);

        // ── Accents (semantic) ───────────────────────────────────────────────────
        public Color AccentPrimary = Color.FromArgb(140, 200, 80);
        public Color AccentStripe = Color.FromArgb(120, 170, 70);
        public Color SuccessText = Color.FromArgb(140, 200, 120);
        public Color WarnText = Color.FromArgb(230, 180, 90);
        public Color ErrorText = Color.FromArgb(220, 110, 110);

        // ── ListView sub-palette ─────────────────────────────────────────────────
        public Color ListBody = Color.FromArgb(36, 36, 36);
        public Color ListBodyAlt = Color.FromArgb(40, 40, 40);
        public Color ListHeader = Color.FromArgb(26, 26, 26);
        public Color ListHeaderText = Color.FromArgb(200, 200, 200);
        public Color ListGrid = Color.FromArgb(48, 48, 48);
        /// <summary>Owner-draw cell grid hairline; <see cref="Color.Empty"/> = auto from ListBody + Divider.</summary>
        public Color ListGridLine = Color.Empty;
        public Color ListSelected = Color.FromArgb(56, 110, 168);
        public Color ListSelectedText = Color.FromArgb(245, 245, 245);
        public Color ListText = Color.FromArgb(225, 225, 225);

        /// <summary>Owner-draw list cell font; falls back to <see cref="BodyFont"/> when null.</summary>
        public Font ListCellFont;

        /// <summary>Owner-draw list header font; falls back to <see cref="SubHeaderFont"/> when null.</summary>
        public Font ListHeaderFont;

        // ── Typography (shared across presets unless a preset overrides) ──────────
        public Font HeaderFont = new Font("Segoe UI Semibold", 10f);
        public Font SubHeaderFont = new Font("Segoe UI Semibold", 9.5f);
        public Font BodyFont = new Font("Segoe UI", 9.5f);
        public Font CaptionFont = new Font("Segoe UI", 9f);
        public Font MonoFont = new Font("Consolas", 9.5f);

        /// <summary>
        /// Convenience accent used when a control wants a readable on-accent foreground.
        /// Lime / amber accents read best with near-black text; blue accents with white.
        /// </summary>
        public Color OnAccentText
        {
            get
            {
                double luminance = (0.299 * AccentPrimary.R + 0.587 * AccentPrimary.G + 0.114 * AccentPrimary.B) / 255.0;
                return luminance > 0.55 ? Color.FromArgb(18, 18, 18) : Color.FromArgb(245, 245, 245);
            }
        }
    }
}
