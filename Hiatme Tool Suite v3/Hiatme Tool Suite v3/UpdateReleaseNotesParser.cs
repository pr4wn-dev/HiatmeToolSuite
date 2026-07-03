using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Hiatme_Tool_Suite_v3
{
    internal sealed class UpdateReleaseNoteItem
    {
        public string Section;
        public string Text;
    }

    /// <summary>Parses release-notes markdown/plain text into carousel slides.</summary>
    internal static class UpdateReleaseNotesParser
    {
        private static readonly Regex WhatsNewHeader = new Regex(
            @"^What's new(\s+in\s+[\d.]+)?\s*:?\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static List<UpdateReleaseNoteItem> Parse(string raw)
        {
            var items = new List<UpdateReleaseNoteItem>();
            if (string.IsNullOrWhiteSpace(raw))
            {
                items.Add(new UpdateReleaseNoteItem { Text = "No release notes provided." });
                return items;
            }

            string currentSection = null;
            foreach (string line in raw.Replace("\r\n", "\n").Split('\n'))
            {
                string t = line.Trim();
                if (t.Length == 0)
                    continue;
                if (WhatsNewHeader.IsMatch(t))
                    continue;

                if (TryParseBullet(t, out string bulletText))
                {
                    items.Add(new UpdateReleaseNoteItem
                    {
                        Section = currentSection,
                        Text = bulletText,
                    });
                    continue;
                }

                if (IsSectionHeader(t))
                {
                    currentSection = t.TrimEnd(':').Trim();
                    continue;
                }

                items.Add(new UpdateReleaseNoteItem
                {
                    Section = currentSection,
                    Text = t,
                });
            }

            if (items.Count == 0)
                items.Add(new UpdateReleaseNoteItem { Text = raw.Trim() });

            return items;
        }

        private static bool TryParseBullet(string line, out string text)
        {
            text = null;
            if (line.StartsWith("- ", StringComparison.Ordinal)
                || line.StartsWith("* ", StringComparison.Ordinal)
                || line.StartsWith("• ", StringComparison.Ordinal))
            {
                text = line.Substring(2).Trim();
                return text.Length > 0;
            }

            if ((line.StartsWith("-", StringComparison.Ordinal) || line.StartsWith("*", StringComparison.Ordinal))
                && line.Length > 1)
            {
                text = line.Substring(1).Trim();
                return text.Length > 0;
            }

            return false;
        }

        private static bool IsSectionHeader(string line)
        {
            if (line.EndsWith(":", StringComparison.Ordinal) && line.Length > 1 && line.Length < 80)
                return true;

            // Short title lines without sentence punctuation (e.g. "Schedule Builder", "Trip Scout UI polish")
            if (line.Length >= 80)
                return false;
            if (line.IndexOf('.') >= 0 || line.IndexOf('!') >= 0 || line.IndexOf('?') >= 0)
                return false;
            return line.IndexOf(' ') >= 0 || char.IsUpper(line[0]);
        }
    }
}
