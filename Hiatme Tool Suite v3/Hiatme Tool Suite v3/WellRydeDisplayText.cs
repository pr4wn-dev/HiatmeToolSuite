using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Display-only casing for WellRyde ALL-CAPS strings in ListViews. Underlying
    /// <see cref="ListViewItem"/> / <see cref="ListViewItem.Tag"/> values are never modified —
    /// format at owner-draw paint time so portal submit paths keep raw server text.
    /// </summary>
    internal static class WellRydeDisplayText
    {
        private static readonly HashSet<string> SkipColumnHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Trip ID", "Trip #", "Trip", "Status", "PU Time", "DO Time", "Sched PU", "Sched DO",
            "Scheduled PU", "Scheduled DO", "Driver PU", "Driver DO", "Suggested PU", "Suggested DO",
            "Miles", "Mi", "Price", "Rate", "References", "Ref", "CoPay", "Co-Pay", "Billed",
            "Amount", "Date", "Created", "Count", "Failed", "Vehicle", "Signature", "Rider Call",
            "Call Time", "Capacity", "Shift", "Use", "Email", "Batch", "Total Billed",
            "Requires Attention", "Failed Trips", "Trip Count", "Create Date", "Created By",
        };

        private static readonly Regex TimePattern = new Regex(
            @"^\d{1,2}:\d{2}(\s*(AM|PM|am|pm))?$",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        /// <summary>
        /// Title-cases WellRyde ALL-CAPS labels (same rules as the Billing tab).
        /// </summary>
        public static string Format(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return value ?? string.Empty;

            string trimmed = value.Trim();
            bool hasLetter = trimmed.Any(char.IsLetter);
            bool hasLower = trimmed.Any(char.IsLower);
            if (!hasLetter || hasLower)
                return value;

            string display = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(
                trimmed.ToLower(CultureInfo.CurrentCulture));
            string[] tokens = display.Split(' ');
            for (int i = 0; i < tokens.Length; i++)
            {
                string token = tokens[i].Trim(',', '.', ';', ':', '#');
                if (string.Equals(token, "Ne", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(token, "Nw", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(token, "Se", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(token, "Sw", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(token, "Us", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(token, "Po", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(token, "Llc", StringComparison.OrdinalIgnoreCase))
                {
                    tokens[i] = tokens[i].Replace(token, token.ToUpperInvariant());
                }
            }

            return string.Join(" ", tokens);
        }

        /// <summary>Format one ListView cell for display; skips IDs, times, money, and numeric columns.</summary>
        public static string FormatListCell(ListView listView, int columnIndex, string raw)
        {
            if (ShouldSkipFormatting(listView, columnIndex, raw))
                return raw ?? string.Empty;
            return Format(raw);
        }

        private static bool ShouldSkipFormatting(ListView listView, int columnIndex, string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return true;

            if (listView != null && columnIndex >= 0 && columnIndex < listView.Columns.Count)
            {
                string header = NormalizeHeader(listView.Columns[columnIndex].Text);
                if (SkipColumnHeaders.Contains(header))
                    return true;
            }

            string t = raw.Trim();
            if (t.StartsWith("$", StringComparison.Ordinal))
                return true;
            if (TimePattern.IsMatch(t))
                return true;
            if (IsNumericOnly(t))
                return true;

            return false;
        }

        private static string NormalizeHeader(string header)
        {
            if (string.IsNullOrEmpty(header))
                return string.Empty;
            int colon = header.IndexOf(':');
            if (colon > 0)
                header = header.Substring(0, colon);
            return header.Trim();
        }

        private static bool IsNumericOnly(string t)
        {
            bool hasDigit = false;
            foreach (char c in t)
            {
                if (char.IsDigit(c))
                    hasDigit = true;
                else if (c != '.' && c != ',' && c != ' ' && c != '-')
                    return false;
            }
            return hasDigit;
        }
    }
}
