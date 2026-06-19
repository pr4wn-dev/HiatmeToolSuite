using System.Drawing;
using MaterialSkin;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Bridges the Supey palette into MaterialSkin's global <see cref="MaterialSkinManager"/> so any
    /// control still rendered by MaterialSkin (cards, buttons, the app bar, dialogs we haven't
    /// migrated yet) follows the active theme instead of MaterialSkin's fixed Grey800 / BlueGrey.
    ///
    /// MaterialSkin's <see cref="ColorScheme"/> takes <see cref="Primary"/> / <see cref="Accent"/>
    /// enum values, but those enums are just <c>uint</c> 0xAARRGGBB colors — so we cast our own
    /// palette colors straight in. Call <see cref="Init"/> once at startup; it applies immediately
    /// and re-applies whenever the user switches themes.
    /// </summary>
    internal static class SupeyMaterialSkinBridge
    {
        private static bool _hooked;

        /// <summary>Apply the current palette now and re-apply on every future theme switch.</summary>
        public static void Init()
        {
            ApplyToManager();
            if (!_hooked)
            {
                SupeyThemeManager.ThemeChanged += (s, e) => ApplyToManager();
                _hooked = true;
            }
        }

        /// <summary>Configure a manager instance (DARK + palette colors). Safe to call repeatedly.</summary>
        public static void ApplyTo(MaterialSkinManager mgr)
        {
            if (mgr == null) return;
            mgr.Theme = MaterialSkinManager.Themes.DARK;
            mgr.ColorScheme = BuildColorScheme();

            // CRITICAL: MaterialSkin's DARK BackgroundColor is hardcoded to (80,80,80) gray and is NOT
            // derived from the ColorScheme, so the bridge above can't recolor it. With this flag ON
            // (the library default) the manager walks every managed form and *stamps* that 80,80,80
            // onto all child controls' BackColor — overriding the palette surfaces we set. Turning it
            // off lets our own theming (Form1.ThemeUntouchedMaterialChrome + per-tab Apply* + the live
            // SupeyThemeApplier recolor) own every background instead of MaterialSkin's fixed gray.
            mgr.EnforceBackcolorOnAllComponents = false;
        }

        private static void ApplyToManager()
        {
            try
            {
                ApplyTo(MaterialSkinManager.Instance);
            }
            catch
            {
                // MaterialSkin may not be initialized yet on the very first call; the per-form
                // ApplyTo calls will pick up the scheme when each MaterialForm registers.
            }
        }

        /// <summary>Map the active Supey palette onto a MaterialSkin color scheme.</summary>
        public static ColorScheme BuildColorScheme()
        {
            var p = SupeyThemeManager.Current;
            // App bar / primary chrome reads as the header surface; the dark/light variants frame it.
            Primary primary = ToPrimary(p.SurfaceHeader);
            Primary darkPrimary = ToPrimary(p.SurfaceStatusBar);
            Primary lightPrimary = ToPrimary(p.SurfaceElevated);
            Accent accent = ToAccent(p.AccentPrimary);
            return new ColorScheme(primary, darkPrimary, lightPrimary, accent, TextShade.WHITE);
        }

        private static Primary ToPrimary(Color c) => (Primary)ToUInt(c);
        private static Accent ToAccent(Color c) => (Accent)ToUInt(c);

        private static uint ToUInt(Color c) =>
            (uint)Color.FromArgb(255, c.R, c.G, c.B).ToArgb();
    }
}
