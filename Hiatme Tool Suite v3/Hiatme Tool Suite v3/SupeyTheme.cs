using System.Drawing;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Central palette + typography for the Supey dispatch tab. Every panel, header,
    /// toolbar, status strip and side dock pulls its surface / border / text colors from
    /// here. The motivation is consistency: previously the tab used a half-dozen ad-hoc
    /// shades of gray (28, 30, 33, 35, 40, 45, 50, 55, 60, 70) which made the layout feel
    /// noisy and amateur. We now have a small named ladder of surfaces (base → elevated →
    /// header → divider) plus a separate ListView palette that's intentionally NOT mixed
    /// in so the owner-drawn trip / driver lists keep their existing identity.
    /// </summary>
    internal static class SupeyTheme
    {
        // ── Surfaces (lightest = closest to the user) ────────────────────────────
        /// <summary>Whole-tab background (behind the splitters).</summary>
        public static readonly Color SurfaceBase = Color.FromArgb(24, 24, 24);
        /// <summary>Standard panel surface (drivers/AI/info content area).</summary>
        public static readonly Color Surface = Color.FromArgb(32, 32, 32);
        /// <summary>Elevated cards inside a panel (status row, prompt box, etc).</summary>
        public static readonly Color SurfaceElevated = Color.FromArgb(40, 40, 40);
        /// <summary>Section header bars (collapsible-panel title strip, toolbar).</summary>
        public static readonly Color SurfaceHeader = Color.FromArgb(28, 28, 28);
        /// <summary>Bottom status strip — slightly darker than panels for separation.</summary>
        public static readonly Color SurfaceStatusBar = Color.FromArgb(20, 20, 20);

        // ── Borders / dividers ───────────────────────────────────────────────────
        /// <summary>1px hair-line that separates regions (toolbar bottom, panel edges).</summary>
        public static readonly Color Divider = Color.FromArgb(56, 56, 56);
        /// <summary>Subtle 1px inside a card (text inputs, status pills).</summary>
        public static readonly Color BorderSubtle = Color.FromArgb(64, 64, 64);

        // ── Text ─────────────────────────────────────────────────────────────────
        public static readonly Color TextPrimary = Color.FromArgb(232, 232, 232);
        public static readonly Color TextSecondary = Color.FromArgb(176, 176, 176);
        public static readonly Color TextMuted = Color.FromArgb(128, 128, 128);
        public static readonly Color TextLink = Color.FromArgb(120, 180, 240);

        // ── Accents (semantic) ───────────────────────────────────────────────────
        /// <summary>Primary accent — used for the active/important call-to-action.</summary>
        public static readonly Color AccentPrimary = Color.FromArgb(140, 200, 80);
        /// <summary>Subtle accent strip on collapsed/active section headers.</summary>
        public static readonly Color AccentStripe = Color.FromArgb(120, 170, 70);
        public static readonly Color SuccessText = Color.FromArgb(140, 200, 120);
        public static readonly Color WarnText = Color.FromArgb(230, 180, 90);
        public static readonly Color ErrorText = Color.FromArgb(220, 110, 110);

        // ── ListView palette (intentionally separate from the surface ladder) ───
        // The Supey tab's listviews used to live at #464646 / #333333 (a flat
        // mid-gray that read as "Visual Studio 2005 placeholder"). They now sit
        // one notch elevated above the panel surface — dark enough to feel like
        // primary content, bright enough that the per-row data is the brightest
        // thing on screen.
        /// <summary>ListView body fill (rows). Slightly elevated above Surface.</summary>
        public static readonly Color ListBody = Color.FromArgb(36, 36, 36);
        /// <summary>Optional zebra-stripe row color, one shade brighter than <see cref="ListBody"/>.</summary>
        public static readonly Color ListBodyAlt = Color.FromArgb(40, 40, 40);
        /// <summary>Column header bar background.</summary>
        public static readonly Color ListHeader = Color.FromArgb(26, 26, 26);
        /// <summary>Column header label color — slightly muted from primary text.</summary>
        public static readonly Color ListHeaderText = Color.FromArgb(200, 200, 200);
        /// <summary>1px grid line between cells / rows.</summary>
        public static readonly Color ListGrid = Color.FromArgb(48, 48, 48);
        /// <summary>Selected-row background — a muted blue that's clearly distinct
        /// from the green accent (which we reserve for primary actions and "checked").</summary>
        public static readonly Color ListSelected = Color.FromArgb(56, 110, 168);
        /// <summary>Selected-row text — bright white for max contrast on the muted blue.</summary>
        public static readonly Color ListSelectedText = Color.FromArgb(245, 245, 245);
        /// <summary>Default body text color inside ListView cells.</summary>
        public static readonly Color ListText = Color.FromArgb(225, 225, 225);

        // ── Typography (named so we never sprinkle font(name, sz) literals) ──────
        /// <summary>Section header text — collapsible-panel titles, toolbar labels.</summary>
        public static readonly Font HeaderFont = new Font("Segoe UI Semibold", 10f);
        /// <summary>Sub-header / group title inside a panel.</summary>
        public static readonly Font SubHeaderFont = new Font("Segoe UI Semibold", 9.5f);
        /// <summary>Body labels, prompt help, status text.</summary>
        public static readonly Font BodyFont = new Font("Segoe UI", 9.5f);
        /// <summary>Smaller caption text — side notes, footers.</summary>
        public static readonly Font CaptionFont = new Font("Segoe UI", 9f);
        /// <summary>Monospace for the AI transcript.</summary>
        public static readonly Font MonoFont = new Font("Consolas", 9.5f);
    }
}
