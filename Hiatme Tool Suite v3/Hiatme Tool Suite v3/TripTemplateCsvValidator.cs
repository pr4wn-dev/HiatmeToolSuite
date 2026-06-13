using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Shared validation for Modivcare-style trip CSV rows (14 columns A–N) used by the template wizard and schedule builder.
    /// </summary>
    internal static class TripTemplateCsvValidator
    {
        private static readonly string[] ColumnLabels =
        {
            "Trip # (column A)",
            "Service date (column B)",
            "Client name (column C)",
            "PU street (column D)",
            "PU city (column E)",
            "PU phone (column F)",
            "PU time (column G)",
            "DO street (column H)",
            "DO city (column I)",
            "DO phone (column J)",
            "DO time (column K)",
            "Age (column L)",
            "Miles (column M)",
            "Comments (column N)",
        };

        private static readonly Regex TripTimeLike = new Regex(
            @"^\s*\d{1,2}:\d{2}(:\d{2})?(\s*[AP]M)?\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex TripTimeExtract = new Regex(
            @"(\d{1,2}:\d{2}(?::\d{2})?(?:\s*[AP]M)?)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>Modivcare trip # pattern, e.g. 1-8008-A.</summary>
        private static readonly Regex ModivcareTripNumber = new Regex(
            @"^\d+\s*-\s*\d+(\s*-\s*[ABCabc])?\s*$",
            RegexOptions.CultureInvariant);

        public static bool IsLikelyModivcareTripNumber(string tripNumber)
        {
            if (string.IsNullOrWhiteSpace(tripNumber))
                return false;
            return ModivcareTripNumber.IsMatch(tripNumber.Replace("\"", "").Trim());
        }

        /// <summary>Dispatcher label in column A (e.g. GAS UP) with no trip fields in B–N.</summary>
        public static bool IsDispatcherNoteRow(string[] rowValues)
        {
            if (rowValues == null || rowValues.Length == 0)
                return false;

            string Cell(int i) => i < rowValues.Length ? (rowValues[i] ?? "").Replace("\"", "").Trim() : "";
            string trip = Cell(0);
            if (string.IsNullOrEmpty(trip) || IsLikelyModivcareTripNumber(trip))
                return false;

            for (int i = 1; i < 14; i++)
            {
                if (!string.IsNullOrWhiteSpace(Cell(i)))
                    return false;
            }

            return true;
        }

        public static string ColumnLetterFromIndex(int index0)
        {
            if (index0 < 0 || index0 > 25)
                return "?";
            return ((char)('A' + index0)).ToString();
        }

        public sealed class CellIssue
        {
            public CellIssue(int columnIndex0, string message)
            {
                ColumnIndex0 = columnIndex0;
                Message = message ?? "";
            }

            public int ColumnIndex0 { get; }
            public string ColumnLetter => ColumnLetterFromIndex(ColumnIndex0);
            public string FieldLabel => ColumnIndex0 >= 0 && ColumnIndex0 < ColumnLabels.Length
                ? ColumnLabels[ColumnIndex0]
                : "Column " + ColumnLetter;

            public string Message { get; }

            public string FormatForUser(string tabName, int lineOneBased)
            {
                return "Tab \"" + tabName + "\", CSV line " + lineOneBased + ", " + FieldLabel + " (" + ColumnLetter + "): " + Message;
            }
        }

        /// <summary>True when the first cell looks like a header row, not a trip.</summary>
        public static bool IsLikelyHeaderRow(string[] rowValues)
        {
            if (rowValues == null || rowValues.Length == 0)
                return false;
            var a = (rowValues[0] ?? "").Replace("\"", "").Trim();
            if (a.Length == 0)
                return false;
            if (a.Equals("Trip", StringComparison.OrdinalIgnoreCase))
                return true;
            if (a.Equals("Trip#", StringComparison.OrdinalIgnoreCase) || a.Equals("Trip #", StringComparison.OrdinalIgnoreCase))
                return true;
            if (a.IndexOf("trip", StringComparison.OrdinalIgnoreCase) >= 0 &&
                (a.IndexOf("number", StringComparison.OrdinalIgnoreCase) >= 0 || a.IndexOf('#') >= 0))
                return true;
            return false;
        }

        /// <summary>Dispatcher instruction row (no trip #, has note text in any column).</summary>
        public static bool IsTemplateInstructionRow(string[] rowValues)
        {
            if (rowValues == null || rowValues.Length == 0)
                return false;

            string Cell(int i) => i < rowValues.Length ? (rowValues[i] ?? "").Replace("\"", "").Trim() : "";
            if (!IsPlaceholderTripNumber(Cell(0)) && !string.IsNullOrWhiteSpace(Cell(0)))
                return false;

            for (int i = 0; i < 14; i++)
            {
                string c = Cell(i);
                if (string.IsNullOrWhiteSpace(c) || c.Equals("NaT", StringComparison.OrdinalIgnoreCase))
                    continue;
                return true;
            }
            return false;
        }

        /// <summary>Combined text from an instruction/gap row (display only).</summary>
        public static string ExtractInstructionText(string[] rowValues)
        {
            if (rowValues == null) return "";
            var parts = new List<string>();
            for (int i = 0; i < 14; i++)
            {
                string c = i < rowValues.Length ? (rowValues[i] ?? "").Replace("\"", "").Trim() : "";
                if (c.Length > 0) parts.Add(c);
            }
            return string.Join(" ", parts).Trim();
        }

        /// <summary>Intentional blank spacer or instruction row in a driver template.</summary>
        public static bool IsTemplateGapRow(string[] rowValues)
        {
            if (rowValues == null || rowValues.Length == 0)
                return true;

            if (IsDispatcherNoteRow(rowValues))
                return true;

            if (IsTemplateInstructionRow(rowValues))
                return true;

            string Cell(int i) => i < rowValues.Length ? (rowValues[i] ?? "").Replace("\"", "").Trim() : "";

            if (ScheduleBuilderGapMeta.RowHasGapMarker(rowValues))
                return true;

            if (!IsPlaceholderTripNumber(Cell(0)) && !string.IsNullOrEmpty(Cell(0)))
                return false;

            for (int i = 1; i < 14; i++)
            {
                string c = Cell(i);
                if (string.IsNullOrEmpty(c))
                    continue;
                if (IsPlaceholderTripNumber(c) || c.Equals("NaT", StringComparison.OrdinalIgnoreCase))
                    continue;
                return false;
            }

            return true;
        }

        /// <summary>Blank Excel export rows (NaT/nan) — not a real Modivcare trip.</summary>
        public static bool IsPlaceholderTripNumber(string tripNumber)
        {
            if (string.IsNullOrWhiteSpace(tripNumber))
                return true;
            string t = tripNumber.Replace("\"", "").Trim();
            if (t.Length == 0)
                return true;
            if (t.Equals("NaT", StringComparison.OrdinalIgnoreCase))
                return true;
            if (t.Equals("nan", StringComparison.OrdinalIgnoreCase)
                || t.Equals("none", StringComparison.OrdinalIgnoreCase))
                return true;
            if (t.IndexOf("nan", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            return false;
        }

        /// <summary>Validate one 14-column trip row. Empty trip number → no checks (caller skips or treats as blank line).</summary>
        public static IReadOnlyList<CellIssue> ValidateTripRow(string[] rowValues)
        {
            var issues = new List<CellIssue>();
            if (rowValues == null || rowValues.Length < 14)
                return issues;

            if (IsLikelyHeaderRow(rowValues))
                return issues;

            string Cell(int i) => (rowValues[i] ?? "").Replace("\"", "").Trim();

            string trip = Cell(0);
            if (string.IsNullOrEmpty(trip))
            {
                for (int i = 1; i < 14; i++)
                {
                    if (!string.IsNullOrWhiteSpace(Cell(i)))
                    {
                        issues.Add(new CellIssue(0,
                            "Trip number is empty but other columns have text — put the Modivcare trip # in column A, or remove the stray line."));
                        return issues;
                    }
                }

                return issues;
            }

            string date = Cell(1);
            string client = Cell(2);
            string puTime = NormalizeTimeField(Cell(6));
            string doTime = NormalizeTimeField(Cell(10));
            string miles = Cell(12);

            if (string.IsNullOrEmpty(date))
                issues.Add(new CellIssue(1, "Service date is missing. Use column B with a real date (e.g. 5/13/2026)."));
            else if (!IsPlausibleServiceDate(date))
                issues.Add(new CellIssue(1,
                    "Value \"" + date + "\" is not a recognized date. Try M/d/yyyy with slashes, or your Windows short date format."));

            if (string.IsNullOrEmpty(client))
                issues.Add(new CellIssue(2, "Client name is empty — column C should match the Modivcare client for this trip."));

            if (!string.IsNullOrEmpty(puTime) && !LooksLikeTripTime(puTime))
                issues.Add(new CellIssue(6,
                    "Value \"" + puTime + "\" does not look like a PU time. Use something like 8:30 AM or 14:00."));

            if (!string.IsNullOrEmpty(doTime) && !LooksLikeTripTime(doTime))
                issues.Add(new CellIssue(10,
                    "Value \"" + doTime + "\" does not look like a DO time. Use something like 2:15 PM or 14:15."));

            if (!string.IsNullOrEmpty(miles))
            {
                var m = miles.Replace(",", "").Trim();
                if (!double.TryParse(m, NumberStyles.Any, CultureInfo.InvariantCulture, out _) &&
                    !double.TryParse(m, NumberStyles.Any, CultureInfo.CurrentCulture, out _))
                    issues.Add(new CellIssue(12,
                        "Value \"" + miles + "\" is not a valid number for miles — use digits (e.g. 12 or 12.5)."));
            }

            return issues;
        }

        private static bool LooksLikeTripTime(string s)
        {
            return !string.IsNullOrWhiteSpace(s) && TripTimeLike.IsMatch(s);
        }

        /// <summary>Strip accidental trailing text from Excel cells (e.g. "12:00:00 AM fsfser3" → "12:00:00 AM").</summary>
        public static string NormalizeTimeField(string raw)
        {
            var s = (raw ?? "").Replace("\"", "").Trim();
            if (s.Length == 0)
                return "";
            if (LooksLikeTripTime(s))
                return s;
            var m = TripTimeExtract.Match(s);
            if (m.Success && LooksLikeTripTime(m.Groups[1].Value.Trim()))
                return m.Groups[1].Value.Trim();
            return s;
        }

        private static bool IsPlausibleServiceDate(string raw)
        {
            var s = (raw ?? "").Trim();
            if (s.Length == 0)
                return false;

            if (DateTime.TryParse(s, CultureInfo.CurrentCulture, DateTimeStyles.None, out _))
                return true;
            if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                return true;

            if (s.Contains("/"))
            {
                var parts = s.Split(new[] { '/' }, StringSplitOptions.None);
                if (parts.Length >= 3)
                {
                    var yRaw = parts[2].Trim();
                    if (yRaw.Length > 4)
                        yRaw = yRaw.Substring(0, 4);
                    if (int.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var mo) &&
                        int.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var da) &&
                        int.TryParse(yRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var yr))
                    {
                        try
                        {
                            if (yr < 100)
                                yr += 2000;
                            var dt = new DateTime(yr, mo, da);
                            return dt.Year >= 2000 && dt.Year <= 2100;
                        }
                        catch
                        {
                            return false;
                        }
                    }
                }

                return false;
            }

            return false;
        }
    }
}
