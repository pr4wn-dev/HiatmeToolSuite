using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Owns the active <see cref="SupeyThemePalette"/> and the set of built-in presets, persists the
    /// user's choice, and raises <see cref="ThemeChanged"/> so live UI can retint without a restart.
    ///
    /// The whole app reads colors through the <see cref="SupeyTheme"/> static facade, which forwards
    /// to <see cref="Current"/>. So the flow is: pick a preset -> <see cref="Apply(string)"/> swaps
    /// <see cref="Current"/>, saves the name, and fires <see cref="ThemeChanged"/> -> subscribers
    /// (forms, custom controls) recolor + invalidate.
    /// </summary>
    internal static class SupeyThemeManager
    {
        /// <summary>Fired after <see cref="Current"/> changes. Handlers should recolor and invalidate.</summary>
        public static event EventHandler ThemeChanged;

        private static SupeyThemePalette _current = BuildBlackLime();

        /// <summary>The palette every <see cref="SupeyTheme"/> reference resolves against.</summary>
        public static SupeyThemePalette Current => _current;

        /// <summary>
        /// The palette that was active immediately before the last <see cref="Apply(string)"/>.
        /// Live recoloring uses this to remap controls whose colors were set imperatively from the
        /// old palette onto the matching new-palette color.
        /// </summary>
        public static SupeyThemePalette Previous { get; private set; } = BuildBlackLime();

        /// <summary>All selectable presets, in display order.</summary>
        public static IReadOnlyList<SupeyThemePalette> BuiltInPresets { get; } = new List<SupeyThemePalette>
        {
            BuildBlackLime(),
            BuildMidnight(),
            BuildGraphite(),
            BuildSlate(),
        };

        public static IEnumerable<string> PresetNames => BuiltInPresets.Select(p => p.Name);

        private static string ConfigDir => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "hiatme_config");
        private static string ConfigPath => Path.Combine(ConfigDir, "theme.json");

        /// <summary>Read the saved preset name (if any) and make it active. Call once at startup.</summary>
        public static void LoadSavedOrDefault()
        {
            string saved = ReadSavedName();
            var match = FindByName(saved) ?? BuiltInPresets[0];
            _current = match;
            // No event here: this runs before the UI exists; the first paint uses Current directly.
        }

        /// <summary>Switch to the named preset, persist the choice, and notify listeners.</summary>
        public static void Apply(string name)
        {
            var match = FindByName(name);
            if (match == null) return;
            if (ReferenceEquals(match, _current)) return;
            Previous = _current;
            _current = match;
            SaveName(match.Name);
            ThemeChanged?.Invoke(null, EventArgs.Empty);
        }

        private static SupeyThemePalette FindByName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            return BuiltInPresets.FirstOrDefault(p =>
                string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        private static string ReadSavedName()
        {
            try
            {
                if (!File.Exists(ConfigPath)) return null;
                // Tiny hand-rolled read so we don't depend on a serializer for one field.
                string text = File.ReadAllText(ConfigPath);
                int i = text.IndexOf("\"Theme\"", StringComparison.OrdinalIgnoreCase);
                if (i < 0) return null;
                int colon = text.IndexOf(':', i);
                if (colon < 0) return null;
                int firstQuote = text.IndexOf('"', colon);
                if (firstQuote < 0) return null;
                int secondQuote = text.IndexOf('"', firstQuote + 1);
                if (secondQuote < 0) return null;
                return text.Substring(firstQuote + 1, secondQuote - firstQuote - 1);
            }
            catch
            {
                return null;
            }
        }

        private static void SaveName(string name)
        {
            try
            {
                Directory.CreateDirectory(ConfigDir);
                File.WriteAllText(ConfigPath, "{\n  \"Theme\": \"" + name.Replace("\"", "") + "\"\n}\n");
            }
            catch
            {
                // Persistence is best-effort; a read-only install just won't remember the choice.
            }
        }

        // ── Built-in presets ─────────────────────────────────────────────────────

        private static SupeyThemePalette BuildBlackLime()
        {
            // The original Supey look: near-black charcoal surfaces with a lime call-to-action.
            return new SupeyThemePalette { Name = "Black & Lime" };
        }

        private static SupeyThemePalette BuildMidnight()
        {
            return new SupeyThemePalette
            {
                Name = "Midnight",
                SurfaceBase = Color.FromArgb(15, 18, 28),
                Surface = Color.FromArgb(22, 26, 38),
                SurfaceElevated = Color.FromArgb(30, 35, 50),
                SurfaceHeader = Color.FromArgb(17, 21, 32),
                SurfaceStatusBar = Color.FromArgb(11, 14, 22),
                Divider = Color.FromArgb(44, 52, 72),
                BorderSubtle = Color.FromArgb(54, 64, 88),
                TextPrimary = Color.FromArgb(228, 233, 242),
                TextSecondary = Color.FromArgb(165, 176, 198),
                TextMuted = Color.FromArgb(120, 130, 152),
                TextLink = Color.FromArgb(120, 180, 240),
                AccentPrimary = Color.FromArgb(80, 162, 245),
                AccentStripe = Color.FromArgb(70, 140, 212),
                ListBody = Color.FromArgb(24, 29, 42),
                ListBodyAlt = Color.FromArgb(30, 36, 51),
                ListHeader = Color.FromArgb(16, 20, 30),
                ListHeaderText = Color.FromArgb(190, 200, 218),
                ListGrid = Color.FromArgb(40, 48, 66),
                ListSelected = Color.FromArgb(48, 108, 182),
                ListSelectedText = Color.FromArgb(245, 248, 252),
                ListText = Color.FromArgb(220, 227, 238),
            };
        }

        private static SupeyThemePalette BuildGraphite()
        {
            return new SupeyThemePalette
            {
                Name = "Graphite",
                SurfaceBase = Color.FromArgb(22, 22, 24),
                Surface = Color.FromArgb(30, 30, 33),
                SurfaceElevated = Color.FromArgb(40, 40, 44),
                SurfaceHeader = Color.FromArgb(25, 25, 28),
                SurfaceStatusBar = Color.FromArgb(16, 16, 18),
                Divider = Color.FromArgb(58, 58, 62),
                BorderSubtle = Color.FromArgb(66, 66, 70),
                TextPrimary = Color.FromArgb(234, 232, 228),
                TextSecondary = Color.FromArgb(180, 176, 168),
                TextMuted = Color.FromArgb(132, 128, 122),
                TextLink = Color.FromArgb(236, 182, 110),
                AccentPrimary = Color.FromArgb(236, 170, 72),
                AccentStripe = Color.FromArgb(210, 150, 60),
                SuccessText = Color.FromArgb(150, 200, 120),
                WarnText = Color.FromArgb(236, 184, 92),
                ErrorText = Color.FromArgb(222, 112, 104),
                ListBody = Color.FromArgb(34, 34, 37),
                ListBodyAlt = Color.FromArgb(40, 40, 44),
                ListHeader = Color.FromArgb(25, 25, 28),
                ListHeaderText = Color.FromArgb(202, 198, 190),
                ListGrid = Color.FromArgb(50, 50, 54),
                ListSelected = Color.FromArgb(150, 108, 40),
                ListSelectedText = Color.FromArgb(250, 246, 238),
                ListText = Color.FromArgb(226, 223, 216),
            };
        }

        private static SupeyThemePalette BuildSlate()
        {
            return new SupeyThemePalette
            {
                Name = "Slate",
                SurfaceBase = Color.FromArgb(20, 24, 26),
                Surface = Color.FromArgb(28, 33, 36),
                SurfaceElevated = Color.FromArgb(37, 43, 47),
                SurfaceHeader = Color.FromArgb(23, 28, 30),
                SurfaceStatusBar = Color.FromArgb(14, 18, 20),
                Divider = Color.FromArgb(50, 58, 62),
                BorderSubtle = Color.FromArgb(60, 70, 74),
                TextPrimary = Color.FromArgb(228, 234, 234),
                TextSecondary = Color.FromArgb(168, 180, 182),
                TextMuted = Color.FromArgb(122, 134, 136),
                TextLink = Color.FromArgb(110, 200, 200),
                AccentPrimary = Color.FromArgb(70, 200, 170),
                AccentStripe = Color.FromArgb(60, 175, 150),
                ListBody = Color.FromArgb(31, 37, 40),
                ListBodyAlt = Color.FromArgb(37, 44, 48),
                ListHeader = Color.FromArgb(22, 27, 29),
                ListHeaderText = Color.FromArgb(194, 204, 205),
                ListGrid = Color.FromArgb(46, 54, 58),
                ListSelected = Color.FromArgb(46, 132, 116),
                ListSelectedText = Color.FromArgb(246, 250, 249),
                ListText = Color.FromArgb(222, 229, 229),
            };
        }
    }
}
