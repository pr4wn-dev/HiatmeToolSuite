using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Owns the active <see cref="SupeyThemePalette"/> and the set of built-in presets, persists the
    /// user's choice, and raises <see cref="ThemeChanged"/> so live UI can retint without a restart.
    /// </summary>
    internal static class SupeyThemeManager
    {
        /// <summary>Fired after <see cref="Current"/> changes. Handlers should recolor and invalidate.</summary>
        public static event EventHandler ThemeChanged;

        public const int MaxLevel = SupeyThemeGenerator.MaxLevel;
        public const int ThemesPerLevel = SupeyThemeGenerator.ThemesPerLevel;

        private static SupeyThemePalette _current = BuildBlackLime();
        private const int PaletteGeneratorVersion = 2;
        private static int _cachedGeneratorVersion;

        private static readonly Dictionary<string, SupeyThemePalette> GeneratedCache =
            new Dictionary<string, SupeyThemePalette>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The palette every <see cref="SupeyTheme"/> reference resolves against.</summary>
        public static SupeyThemePalette Current => _current;

        /// <summary>The palette that was active immediately before the last apply.</summary>
        public static SupeyThemePalette Previous { get; private set; } = BuildBlackLime();

        /// <summary>Hand-tuned starter presets (level 0).</summary>
        public static IReadOnlyList<SupeyThemePalette> ClassicPresets { get; } = new List<SupeyThemePalette>
        {
            BuildBlackLime(),
            BuildMidnight(),
            BuildGraphite(),
            BuildSlate(),
        };

        /// <summary>Legacy alias.</summary>
        public static IReadOnlyList<SupeyThemePalette> BuiltInPresets => ClassicPresets;

        public static IEnumerable<string> ClassicPresetNames => ClassicPresets.Select(p => p.Name);

        public static IEnumerable<string> PresetNames => ClassicPresetNames;

        private static string ConfigDir => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "hiatme_config");
        private static string ConfigPath => Path.Combine(ConfigDir, "theme.json");

        public static void LoadSavedOrDefault()
        {
            string saved = ReadSavedName();
            var match = ResolveTheme(saved) ?? ClassicPresets[0];
            _current = match;
        }

        public static void Apply(string name)
        {
            var match = ResolveTheme(name);
            if (match == null)
                return;
            ApplyPalette(match);
        }

        public static void Apply(int level, int index)
        {
            if (level <= 0)
                return;
            ApplyPalette(GetGenerated(level, index));
        }

        public static SupeyThemePalette GetGenerated(int level, int index)
        {
            if (_cachedGeneratorVersion != PaletteGeneratorVersion)
            {
                GeneratedCache.Clear();
                _cachedGeneratorVersion = PaletteGeneratorVersion;
            }

            string key = ThemeKey(level, index);
            if (!GeneratedCache.TryGetValue(key, out var palette))
            {
                palette = SupeyThemeGenerator.Generate(level, index);
                GeneratedCache[key] = palette;
            }
            return palette;
        }

        public static IEnumerable<SupeyThemePalette> GetThemesForLevel(int level)
        {
            if (level <= 0)
            {
                foreach (var p in ClassicPresets)
                    yield return p;
                yield break;
            }

            for (int i = 0; i < ThemesPerLevel; i++)
                yield return GetGenerated(level, i);
        }

        private static void ApplyPalette(SupeyThemePalette match)
        {
            if (ReferenceEquals(match, _current))
                return;
            Previous = _current;
            _current = match;
            SaveName(match.Name);
            ThemeChanged?.Invoke(null, EventArgs.Empty);
        }

        private static SupeyThemePalette ResolveTheme(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;

            var classic = ClassicPresets.FirstOrDefault(p =>
                string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            if (classic != null)
                return classic;

            var cached = GeneratedCache.Values.FirstOrDefault(p =>
                string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            if (cached != null)
                return cached;

            if (TryParseLevelFromName(name, out int level))
            {
                for (int i = 0; i < ThemesPerLevel; i++)
                {
                    var candidate = GetGenerated(level, i);
                    if (string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase))
                        return candidate;
                }
            }

            return null;
        }

        internal static bool TryParseLevelFromName(string name, out int level)
        {
            level = 0;
            if (string.IsNullOrWhiteSpace(name) || name[0] != 'L')
                return false;

            int i = 1;
            while (i < name.Length && char.IsDigit(name[i]))
                i++;
            if (i <= 1)
                return false;

            return int.TryParse(name.Substring(1, i - 1), out level) && level >= 1 && level <= MaxLevel;
        }

        private static string ThemeKey(int level, int index) => level.ToString("D2") + ":" + index;

        private static string ReadSavedName()
        {
            try
            {
                if (!File.Exists(ConfigPath))
                    return null;
                string text = File.ReadAllText(ConfigPath);
                int i = text.IndexOf("\"Theme\"", StringComparison.OrdinalIgnoreCase);
                if (i < 0)
                    return null;
                int colon = text.IndexOf(':', i);
                if (colon < 0)
                    return null;
                int firstQuote = text.IndexOf('"', colon);
                if (firstQuote < 0)
                    return null;
                int secondQuote = text.IndexOf('"', firstQuote + 1);
                if (secondQuote < 0)
                    return null;
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
            }
        }

        // ── Classic presets ─────────────────────────────────────────────────────

        private static SupeyThemePalette BuildBlackLime()
        {
            return new SupeyThemePalette
            {
                Name = "Black & Lime",
                Level = 0,
                LoginBackgroundKey = "classic-black-lime",
                TextPrimary = Color.FromArgb(230, 235, 222),
                TextSecondary = Color.FromArgb(176, 184, 162),
                TextMuted = Color.FromArgb(128, 136, 114),
                ListText = Color.FromArgb(216, 226, 200),
                ListHeaderText = Color.FromArgb(188, 198, 168),
                ListSelectedText = Color.FromArgb(248, 252, 238),
                ListGridLine = Color.FromArgb(47, 47, 47),
            };
        }

        private static SupeyThemePalette BuildMidnight()
        {
            return new SupeyThemePalette
            {
                Name = "Midnight",
                Level = 0,
                LoginBackgroundKey = "classic-midnight",
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
                ListGridLine = Color.FromArgb(38, 45, 62),
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
                Level = 0,
                LoginBackgroundKey = "classic-graphite",
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
                ListGridLine = Color.FromArgb(48, 48, 52),
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
                Level = 0,
                LoginBackgroundKey = "classic-slate",
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
                ListGridLine = Color.FromArgb(44, 52, 55),
                ListSelected = Color.FromArgb(46, 132, 116),
                ListSelectedText = Color.FromArgb(246, 250, 249),
                ListText = Color.FromArgb(222, 229, 229),
            };
        }
    }

    /// <summary>
    /// Loads per-theme login background PNGs from <c>Resources/login_backgrounds/</c> beside the EXE.
    /// </summary>
    internal static class SupeyLoginBackgroundManager
    {
        private static readonly Dictionary<string, Image> Cache =
            new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);

        private static Image _appliedImage;

        public static string ResolveKey(SupeyThemePalette palette)
        {
            if (palette == null)
                return null;
            if (!string.IsNullOrWhiteSpace(palette.LoginBackgroundKey))
                return palette.LoginBackgroundKey.Trim();
            if (palette.Level <= 0)
                return ClassicKeyFromName(palette.Name);
            return ThemeKey(palette.Level, palette.Index);
        }

        public static string ThemeKey(int level, int index)
            => "L" + level.ToString("D2") + "-" + index.ToString("D2");

        public static string ClassicKeyFromName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "classic-black-lime";
            string n = name.ToLowerInvariant();
            if (n.Contains("midnight")) return "classic-midnight";
            if (n.Contains("graphite")) return "classic-graphite";
            if (n.Contains("slate")) return "classic-slate";
            return "classic-black-lime";
        }

        public static Image GetImage(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return null;

            if (Cache.TryGetValue(key, out var cached))
                return cached;

            string path = Path.Combine(BackgroundDirectory, key + ".png");
            if (!File.Exists(path))
                return null;

            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    var img = Image.FromStream(fs);
                    Cache[key] = img;
                    return img;
                }
            }
            catch
            {
                return null;
            }
        }

        public static void ApplyToPictureBox(PictureBox pictureBox, SupeyThemePalette palette, Image fallback = null)
        {
            if (pictureBox == null || pictureBox.IsDisposed)
                return;

            string key = ResolveKey(palette);
            Image next = GetImage(key) ?? fallback;
            if (next == null)
                return;

            if (!ReferenceEquals(pictureBox.Image, next))
            {
                if (ReferenceEquals(pictureBox.Image, _appliedImage))
                    pictureBox.Image = null;
                pictureBox.Image = next;
                _appliedImage = next;
            }

            pictureBox.Invalidate();
        }

        public static string BackgroundDirectory =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "login_backgrounds");

        public static void ClearCache()
        {
            foreach (var img in Cache.Values)
            {
                try { img?.Dispose(); } catch { }
            }
            Cache.Clear();
            _appliedImage = null;
        }
    }
}
