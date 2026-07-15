using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>Human-readable Trip Scout change text (alert bar, list expand rows).</summary>
    internal static class TripScoutChangeFormat
    {
        private static readonly Dictionary<string, string> FieldLabels =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "driver", "Driver" },
                { "client", "Client" },
                { "sched_pu_iso", "Sched PU" },
                { "sched_do_iso", "Sched DO" },
                { "actual_pu_iso", "Actual PU" },
                { "actual_do_iso", "Actual DO" },
                { "status", "Status" },
                { "alerts", "Alerts" },
                { "pu_address", "PU address" },
                { "do_address", "DO address" },
                { "miles", "Miles" },
            };

        private static readonly Dictionary<string, string> TagHeadlines =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "cancelled", "Cancelled" },
                { "driver_changed", "Driver changed" },
                { "time_changed", "Time changed" },
                { "address_changed", "Address changed" },
                { "status_changed", "Status changed" },
                { "early_dropoff", "Drop-off before sched" },
                { "late_dropoff", "Drop-off after sched" },
                { "noshow", "No-show" },
                { "completed", "Completed" },
                { "added", "Trip added" },
                { "removed_from_wr", "Trip removed" },
            };

        /// <summary>Before → after lines for the change (primary alert text).</summary>
        public static string FormatDiff(HiatmeAiClient.TripScoutChangeRow row)
        {
            if (row == null)
                return "Trip updated";

            string kind = (row.Kind ?? "").Trim().ToLowerInvariant();
            if (kind == "added")
                return "Trip added on WellRyde";
            if (kind == "removed")
                return "Trip removed from WellRyde";

            var parts = FormatFieldDiffParts(row.Fields);
            if (parts.Count > 0)
                return string.Join("   ·   ", parts);

            string summary = (row.Summary ?? "").Trim();
            if (summary.Length > 0)
                return summary;

            return FormatHeadline(row) ?? "Trip updated";
        }

        /// <summary>Short category label from tags (expand row badge).</summary>
        public static string FormatHeadline(HiatmeAiClient.TripScoutChangeRow row)
        {
            if (row == null)
                return null;

            if (row.Tags != null)
            {
                foreach (var tag in row.Tags)
                {
                    if (string.IsNullOrWhiteSpace(tag))
                        continue;
                    string key = tag.Trim();
                    if (TagHeadlines.TryGetValue(key, out string label))
                        return label;
                }
            }

            string kind = (row.Kind ?? "").Trim().ToLowerInvariant();
            if (kind == "added")
                return "Trip added";
            if (kind == "removed")
                return "Trip removed";
            return null;
        }

        /// <summary>Trip identity line under the diff (#, client, times, driver).</summary>
        public static string FormatTripContext(
            HiatmeAiClient.TripScoutChangeRow row,
            WRDownloadedTrip trip)
        {
            var parts = new List<string>();

            string num = (row?.TripNo ?? trip?.TripNumber ?? "").Trim();
            if (num.Length > 0)
                parts.Add("#" + num);

            string client = (trip?.ClientName ?? row?.Client ?? "").Trim();
            if (client.Length > 0)
                parts.Add(client);

            string pu = FormatTimeOnly(trip?.PUTime ?? "");
            if (!string.IsNullOrWhiteSpace(pu))
                parts.Add("PU " + pu.Trim());

            string dot = FormatTimeOnly(trip?.DOTime ?? "");
            if (!string.IsNullOrWhiteSpace(dot))
                parts.Add("DO " + dot.Trim());

            string driver = (trip?.DriverName ?? row?.Driver ?? "").Trim();
            if (driver.Length > 0)
                parts.Add("Driver " + driver);

            return string.Join("  ·  ", parts);
        }

        public static string FormatDetectedAt(double? ts)
        {
            if (ts == null || ts.Value <= 0)
                return "";
            try
            {
                var dt = DateTimeOffset.FromUnixTimeSeconds((long)ts.Value).LocalDateTime;
                return "Detected " + dt.ToString("h:mm tt", CultureInfo.CurrentCulture);
            }
            catch
            {
                return "";
            }
        }

        private static List<string> FormatFieldDiffParts(IList<HiatmeAiClient.TripScoutChangeFieldRow> fields)
        {
            var parts = new List<string>();
            if (fields == null || fields.Count == 0)
                return parts;

            foreach (var change in fields)
            {
                if (change == null || string.IsNullOrWhiteSpace(change.Field))
                    continue;

                string name = change.Field.Trim();
                string label = FieldLabels.TryGetValue(name, out string mapped)
                    ? mapped
                    : name.Replace('_', ' ');

                string before = FormatFieldValue(name, change.Before);
                string after = FormatFieldValue(name, change.After);
                if (string.Equals(before, after, StringComparison.Ordinal))
                    continue;

                parts.Add(label + ": " + before + "  →  " + after);
            }

            return parts;
        }

        private static string FormatFieldValue(string fieldName, object value)
        {
            string text = ValueToString(value);
            if (string.IsNullOrWhiteSpace(text))
                return "—";

            if (fieldName != null && fieldName.EndsWith("_iso", StringComparison.OrdinalIgnoreCase))
            {
                string formatted = FormatIsoTime(text);
                if (!string.IsNullOrWhiteSpace(formatted))
                    return formatted;
            }

            if (string.Equals(fieldName, "driver", StringComparison.OrdinalIgnoreCase)
                && IsUnassignedDriver(text))
                return "Unassigned";

            return text;
        }

        private static string FormatIsoTime(string iso)
        {
            if (string.IsNullOrWhiteSpace(iso))
                return "";

            if (DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt)
                || DateTime.TryParse(iso, CultureInfo.CurrentCulture, DateTimeStyles.None, out dt))
            {
                return dt.ToString("h:mm tt", CultureInfo.CurrentCulture).TrimStart('0');
            }

            int tIdx = iso.IndexOf('T');
            if (tIdx >= 0 && tIdx + 1 < iso.Length)
            {
                string tail = iso.Substring(tIdx + 1);
                int dot = tail.IndexOf('.');
                if (dot >= 0)
                    tail = tail.Substring(0, dot);
                if (DateTime.TryParseExact(tail, "HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
                    return dt.ToString("h:mm tt", CultureInfo.CurrentCulture).TrimStart('0');
            }

            return iso;
        }

        private static string ValueToString(object value)
        {
            if (value == null)
                return "";
            if (value is string s)
                return s.Trim();
            return Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim() ?? "";
        }

        private static bool IsUnassignedDriver(string driver)
        {
            string d = (driver ?? "").Trim().ToLowerInvariant();
            return d.Length == 0
                || d == "unassigned"
                || d == "none"
                || d == "n/a"
                || d == "na"
                || d == "—"
                || d == "-";
        }

        private static string FormatTimeOnly(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return "";
            return raw.Trim();
        }
    }
}
