using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>Infers service date from saved schedule paths and trip CSV date column.</summary>
    internal static class ScheduleBuilderLoadDateResolver
    {
        private static readonly Regex ExportNameRegex = new Regex(
            @"Schedule\s+for\s+(?<month>[A-Za-z]+)\s+(?<day>\d{1,2})\s+(?<year>\d{4})",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static void ApplyResolvedDate(ScheduleBuilderLoadResult load, string pickedFilePath)
        {
            if (load == null)
                return;

            if (TryResolve(pickedFilePath, load.AllTrips, out DateTime date, out string source))
            {
                load.ServiceDate = date.Date;
                load.ServiceDateSource = source;
            }
        }

        public static bool TryResolve(
            string pickedFilePath,
            IEnumerable<MCDownloadedTrip> trips,
            out DateTime date,
            out string source)
        {
            date = default;
            source = "";

            if (!string.IsNullOrWhiteSpace(pickedFilePath))
            {
                if (TryParseExportLabel(Path.GetFileNameWithoutExtension(pickedFilePath), out date))
                {
                    source = "file name";
                    return true;
                }

                string parent = Path.GetFileName(Path.GetDirectoryName(pickedFilePath) ?? "");
                if (TryParseExportLabel(parent, out date))
                {
                    source = "folder name";
                    return true;
                }
            }

            if (TryResolveFromTripDates(trips, out date))
            {
                source = "trip dates in file";
                return true;
            }

            return false;
        }

        public static bool TryParseExportLabel(string label, out DateTime date)
        {
            date = default;
            if (string.IsNullOrWhiteSpace(label))
                return false;

            var m = ExportNameRegex.Match(label.Trim());
            if (!m.Success)
                return false;

            string s = m.Groups["month"].Value + " " + m.Groups["day"].Value + ", " + m.Groups["year"].Value;
            return DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
        }

        public static bool TryParseTripServiceDate(string raw, out DateTime date)
        {
            date = default;
            var s = (raw ?? "").Trim();
            if (s.Length == 0)
                return false;

            if (DateTime.TryParse(s, CultureInfo.CurrentCulture, DateTimeStyles.None, out date))
                return IsReasonableServiceYear(date.Year);

            if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
                return IsReasonableServiceYear(date.Year);

            if (s.Contains("/"))
            {
                var parts = s.Split(new[] { '/' }, StringSplitOptions.None);
                if (parts.Length >= 3
                    && int.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int mo)
                    && int.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int da))
                {
                    var yRaw = parts[2].Trim();
                    if (yRaw.Length > 4)
                        yRaw = yRaw.Substring(0, 4);
                    if (int.TryParse(yRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int yr))
                    {
                        if (yr < 100)
                            yr += 2000;
                        try
                        {
                            date = new DateTime(yr, mo, da);
                            return IsReasonableServiceYear(date.Year);
                        }
                        catch
                        {
                            return false;
                        }
                    }
                }
            }

            return false;
        }

        private static bool TryResolveFromTripDates(IEnumerable<MCDownloadedTrip> trips, out DateTime date)
        {
            date = default;
            var counts = new Dictionary<DateTime, int>();
            foreach (var trip in trips ?? Enumerable.Empty<MCDownloadedTrip>())
            {
                if (trip == null)
                    continue;
                if (!TryParseTripServiceDate(trip.Date, out DateTime d))
                    continue;
                d = d.Date;
                counts[d] = counts.TryGetValue(d, out int n) ? n + 1 : 1;
            }

            if (counts.Count == 0)
                return false;

            var best = counts.OrderByDescending(kv => kv.Value).First();
            date = best.Key;
            return true;
        }

        private static bool IsReasonableServiceYear(int year) =>
            year >= 2000 && year <= 2100;
    }
}
