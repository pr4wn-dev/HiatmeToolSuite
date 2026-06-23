using System;
using System.Drawing;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Procedural Supey palettes — Tetris-style levels get more saturated, contrasty, and typographically wild.
    /// Each (level, index) pair is deterministic so names/colors persist across sessions.
    /// </summary>
    internal static class SupeyThemeGenerator
    {
        public const int ThemesPerLevel = 8;
        public const int MaxLevel = 30;

        private static readonly string[] HueNames =
        {
            "Cyan", "Azure", "Violet", "Magenta", "Rose", "Coral", "Amber", "Lime",
            "Jade", "Teal", "Ice", "Gold", "Plum", "Rust", "Mint", "Fuchsia",
        };

        private static readonly string[] MoodNames =
        {
            "Calm", "Deep", "Glow", "Pulse", "Neon", "Arcade", "Hyper", "Chaos",
            "Prism", "Voltage", "Laser", "Rave", "Glitch", "Super", "Ultra", "Omega",
        };

        public static SupeyThemePalette Generate(int level, int index)
        {
            level = Math.Max(1, Math.Min(MaxLevel, level));
            index = ((index % ThemesPerLevel) + ThemesPerLevel) % ThemesPerLevel;

            double chaos = Math.Min(1d, (level - 1) / (double)(MaxLevel - 1));
            double hue = (level * 41.7 + index * 53.3) % 360d;
            double accentHue = hue;
            double listHue = (hue + 18 + level * 2.3) % 360d;
            double altHue = (hue + 140 + index * 11 + level * 4) % 360d;

            double surfaceSat = 0.12 + chaos * 0.35;
            double surfaceLit = 0.05 + (index % 3) * 0.012;
            double accentSat = 0.42 + chaos * 0.52;
            double accentLit = 0.42 + (1 - chaos) * 0.12 - (index % 2) * 0.04;

            Color surfaceBase = FromHsl(hue, surfaceSat, surfaceLit);
            Color surface = FromHsl(hue, surfaceSat + 0.04, surfaceLit + 0.035);
            Color surfaceElevated = FromHsl(hue, surfaceSat + 0.06, surfaceLit + 0.07);
            Color surfaceHeader = FromHsl(hue, surfaceSat + 0.02, surfaceLit + 0.015);
            Color surfaceStatus = FromHsl(hue, surfaceSat, Math.Max(0.02, surfaceLit - 0.02));

            Color accent = FromHsl(accentHue, accentSat, accentLit);
            Color accentStripe = FromHsl(accentHue, accentSat, Math.Max(0.2, accentLit - 0.12));
            Color accentAlt = FromHsl(altHue, accentSat * 0.85, accentLit);

            Color listBody = FromHsl(listHue, surfaceSat + 0.08 + chaos * 0.12, surfaceLit + 0.055);
            Color listBodyAlt = FromHsl(listHue, surfaceSat + 0.1, surfaceLit + 0.08);
            Color listHeader = FromHsl(listHue, surfaceSat + 0.03, surfaceLit + 0.02);
            Color listGrid = FromHsl(listHue, surfaceSat + 0.18, surfaceLit + 0.14);
            Color listSelected = Blend(accent, FromHsl(accentHue, accentSat * 0.7, 0.22), 0.55 + chaos * 0.25);

            bool lightSurfaces = surfaceBase.GetBrightness() > 0.42;
            Color textPrimary = lightSurfaces
                ? FromHsl(hue, 0.25, 0.12)
                : FromHsl(hue, 0.08, 0.88 + chaos * 0.08);
            Color textSecondary = lightSurfaces
                ? FromHsl(hue, 0.18, 0.22)
                : FromHsl(hue, 0.1, 0.68);
            Color textMuted = lightSurfaces
                ? FromHsl(hue, 0.12, 0.32)
                : FromHsl(hue, 0.08, 0.48);

            ApplyTypography(level, chaos, index, out Font headerFont, out Font subHeaderFont,
                out Font bodyFont, out Font captionFont, out Font monoFont,
                out Font listCellFont, out Font listHeaderFont);

            string name = BuildName(level, index, hue, chaos);

            return new SupeyThemePalette
            {
                Name = name,
                Level = level,
                Index = index,
                IsGenerated = true,
                LoginBackgroundKey = SupeyLoginBackgroundManager.ThemeKey(level, index),
                SurfaceBase = surfaceBase,
                Surface = surface,
                SurfaceElevated = surfaceElevated,
                SurfaceHeader = surfaceHeader,
                SurfaceStatusBar = surfaceStatus,
                Divider = FromHsl(hue, surfaceSat + 0.14, surfaceLit + 0.12),
                BorderSubtle = FromHsl(hue, surfaceSat + 0.16, surfaceLit + 0.16),
                TextPrimary = textPrimary,
                TextSecondary = textSecondary,
                TextMuted = textMuted,
                TextLink = Blend(accentAlt, Color.White, 0.35),
                AccentPrimary = accent,
                AccentStripe = accentStripe,
                SuccessText = FromHsl((hue + 95) % 360, 0.55, 0.58),
                WarnText = FromHsl((hue + 35) % 360, 0.7, 0.58),
                ErrorText = FromHsl((hue + 350) % 360, 0.65, 0.55),
                ListBody = listBody,
                ListBodyAlt = listBodyAlt,
                ListHeader = listHeader,
                ListHeaderText = textSecondary,
                ListGrid = listGrid,
                ListGridLine = Color.Empty,
                ListSelected = listSelected,
                ListSelectedText = textPrimary,
                ListText = textPrimary,
                HeaderFont = headerFont,
                SubHeaderFont = subHeaderFont,
                BodyFont = bodyFont,
                CaptionFont = captionFont,
                MonoFont = monoFont,
                ListCellFont = listCellFont,
                ListHeaderFont = listHeaderFont,
            };
        }

        public static string BuildName(int level, int index, double hue, double chaos)
        {
            string hueName = HueNames[(int)((hue / 360d) * HueNames.Length) % HueNames.Length];
            string mood = MoodNames[Math.Min(MoodNames.Length - 1, index + (int)(chaos * 4))];
            return $"L{level:D2} · {mood} {hueName}";
        }

        private static void ApplyTypography(int level, double chaos, int index,
            out Font headerFont, out Font subHeaderFont, out Font bodyFont, out Font captionFont,
            out Font monoFont, out Font listCellFont, out Font listHeaderFont)
        {
            string bodyFamily = "Segoe UI";
            string headerFamily = "Segoe UI Semibold";
            float bodySize = 9.5f;
            float headerSize = 10f;
            FontStyle bodyStyle = FontStyle.Regular;
            FontStyle headerStyle = FontStyle.Regular;

            if (level >= 4)
            {
                bodyFamily = "Corbel";
                headerFamily = "Corbel";
                headerStyle = FontStyle.Bold;
            }
            if (level >= 8)
            {
                bodyFamily = "Trebuchet MS";
                headerFamily = "Trebuchet MS";
                bodySize = 9.75f;
            }
            if (level >= 14)
            {
                bodyFamily = "Century Gothic";
                headerFamily = "Century Gothic";
                bodySize = 10f;
                headerSize = 10.5f;
                if (index % 2 == 0)
                    bodyStyle = FontStyle.Italic;
            }
            if (level >= 20)
            {
                bodyFamily = "Franklin Gothic Medium";
                headerFamily = "Franklin Gothic Medium";
                bodySize = 10.25f + (float)(chaos * 1.5f);
                headerSize = 11f + (float)(chaos * 1.25f);
                headerStyle = FontStyle.Bold;
            }
            if (level >= 26)
            {
                bodyFamily = "Comic Sans MS";
                headerFamily = "Impact";
                bodySize = 10.5f + (index % 3) * 0.25f;
                headerSize = 11.5f;
            }

            bodyFont = new Font(bodyFamily, bodySize, bodyStyle);
            headerFont = new Font(headerFamily, headerSize, headerStyle);
            subHeaderFont = new Font(headerFamily, headerSize - 0.5f, headerStyle);
            captionFont = new Font(bodyFamily, Math.Max(8f, bodySize - 0.5f), bodyStyle);
            monoFont = new Font("Consolas", bodySize, level >= 12 ? FontStyle.Bold : FontStyle.Regular);
            listCellFont = new Font(bodyFamily, bodySize, bodyStyle);
            listHeaderFont = new Font(headerFamily, Math.Max(8.75f, bodySize - 0.25f), headerStyle);
        }

        private static Color FromHsl(double h, double s, double l)
        {
            h = ((h % 360d) + 360d) % 360d;
            s = Clamp(s, 0d, 1d);
            l = Clamp(l, 0d, 1d);

            double c = (1 - Math.Abs(2 * l - 1)) * s;
            double x = c * (1 - Math.Abs((h / 60d) % 2 - 1));
            double m = l - c / 2;

            double r, g, b;
            if (h < 60) { r = c; g = x; b = 0; }
            else if (h < 120) { r = x; g = c; b = 0; }
            else if (h < 180) { r = 0; g = c; b = x; }
            else if (h < 240) { r = 0; g = x; b = c; }
            else if (h < 300) { r = x; g = 0; b = c; }
            else { r = c; g = 0; b = x; }

            return Color.FromArgb(
                (int)((r + m) * 255),
                (int)((g + m) * 255),
                (int)((b + m) * 255));
        }

        private static Color Blend(Color a, Color b, double amountB)
        {
            amountB = Clamp(amountB, 0d, 1d);
            double amountA = 1d - amountB;
            return Color.FromArgb(
                (int)(a.R * amountA + b.R * amountB),
                (int)(a.G * amountA + b.G * amountB),
                (int)(a.B * amountA + b.B * amountB));
        }

        private static double Clamp(double v, double min, double max)
            => v < min ? min : v > max ? max : v;
    }
}
