using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>Parses Modivcare / Logisticare full delimited trip download text into <see cref="MCDownloadedTrip"/> rows.</summary>
    internal static class ModivcareDelimitedTripParser
    {
        private static readonly Regex CsvSplit = new Regex(",(?=(?:[^\"]*\"[^\"]*\")*(?![^\"]*\"))");

        public static List<MCDownloadedTrip> ParseDownloadText(string data)
        {
            var list = new List<MCDownloadedTrip>();
            if (string.IsNullOrWhiteSpace(data))
                return list;

            Dictionary<string, int> headerMap = null;
            using (var reader = new StringReader(data))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    var fields = SplitFields(line);
                    if (fields.Count < 10)
                        continue;

                    if (headerMap == null && LooksLikeHeaderRow(fields))
                    {
                        headerMap = BuildHeaderMap(fields);
                        continue;
                    }

                    if (!TryParseTrip(fields, headerMap, out MCDownloadedTrip trip))
                        continue;

                    list.Add(trip);
                }
            }

            return list;
        }

        private static bool TryParseTrip(
            IReadOnlyList<string> fields,
            Dictionary<string, int> headerMap,
            out MCDownloadedTrip trip)
        {
            trip = null;
            if (fields == null || fields.Count < 10)
                return false;

            string tripNumber = FirstNonEmpty(
                FieldByHeader(headerMap, fields, "trip #", "trip number", "tripid", "trip id", "tripidleg"),
                Get(fields, 1)).Replace(" ", "");
            if (string.IsNullOrWhiteSpace(tripNumber))
                return false;

            // Header / junk rows sometimes match trip # pattern loosely — require a service date.
            string date = FirstNonEmpty(
                FieldByHeader(headerMap, fields, "date of service", "service date", "trip date", "date"),
                Get(fields, 2));
            if (string.IsNullOrWhiteSpace(date))
                return false;

            trip = new MCDownloadedTrip
            {
                TripNumber = tripNumber,
                Date = date,
                ClientFullName = FirstNonEmpty(
                    FieldByHeader(headerMap, fields, "member name", "client name", "recipient", "member"),
                    Get(fields, 4)),
                PUStreet = FirstNonEmpty(
                    FieldByHeader(headerMap, fields, "pick up address", "pickup address", "pu address", "pick up street"),
                    Get(fields, 7)),
                PUCity = FirstNonEmpty(
                    FieldByHeader(headerMap, fields, "pick up city", "pickup city", "pu city"),
                    Get(fields, 10)),
                PUTelephone = FirstNonEmpty(
                    FieldByHeader(headerMap, fields, "pick up phone", "pickup phone", "pu phone", "pickup phone number"),
                    Get(fields, 13)),
                PUTime = TripTemplateCsvValidator.NormalizeTimeField(FirstNonEmpty(
                    FieldByHeader(headerMap, fields, "pick up time", "pickup time", "pu time", "scheduled pick up"),
                    Get(fields, 14))),
                DOStreet = FirstNonEmpty(
                    FieldByHeader(headerMap, fields, "drop off address", "dropoff address", "do address", "drop off street"),
                    Get(fields, 16)),
                DOCITY = FirstNonEmpty(
                    FieldByHeader(headerMap, fields, "drop off city", "dropoff city", "do city"),
                    Get(fields, 19)),
                DOTelephone = FirstNonEmpty(
                    FieldByHeader(headerMap, fields, "drop off phone", "dropoff phone", "do phone", "drop off phone number"),
                    Get(fields, 22)),
                SchedDOTime = TripTemplateCsvValidator.NormalizeTimeField(Get(fields, 23)),
                DOTime = TripTemplateCsvValidator.NormalizeTimeField(FirstNonEmpty(
                    FieldByHeader(headerMap, fields, "appointment time", "appt time", "do time", "drop off time"),
                    Get(fields, 24))),
                Age = FirstNonEmpty(
                    FieldByHeader(headerMap, fields, "age", "member age"),
                    Get(fields, 25)),
                Miles = FirstNonEmpty(
                    FieldByHeader(headerMap, fields, "miles", "mileage"),
                    Get(fields, 33)),
                Comments = FirstNonEmpty(
                    FieldByHeader(headerMap, fields, "comments", "notes", "trip notes"),
                    Get(fields, 34)),
            };

            return true;
        }

        private static bool LooksLikeHeaderRow(IReadOnlyList<string> fields)
        {
            int hits = 0;
            foreach (var raw in fields)
            {
                string norm = NormalizeHeader(raw);
                if (norm.Length == 0)
                    continue;
                if (norm.Contains("trip") && (norm.Contains("number") || norm.Contains("#") || norm.Contains("id")))
                    hits++;
                if (norm.Contains("member") || norm.Contains("client") || norm.Contains("recipient"))
                    hits++;
                if (norm.Contains("pick") && norm.Contains("phone"))
                    hits++;
                if (norm.Contains("drop") && norm.Contains("phone"))
                    hits++;
                if (norm.Contains("date") && norm.Contains("service"))
                    hits++;
            }

            return hits >= 2;
        }

        private static Dictionary<string, int> BuildHeaderMap(IReadOnlyList<string> fields)
        {
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < fields.Count; i++)
            {
                string norm = NormalizeHeader(fields[i]);
                if (norm.Length == 0)
                    continue;
                if (!map.ContainsKey(norm))
                    map[norm] = i;
            }

            return map;
        }

        private static string FieldByHeader(
            Dictionary<string, int> headerMap,
            IReadOnlyList<string> fields,
            params string[] headerNeedles)
        {
            if (headerMap == null || headerMap.Count == 0 || headerNeedles == null)
                return "";

            foreach (var needle in headerNeedles)
            {
                string key = NormalizeHeader(needle);
                if (headerMap.TryGetValue(key, out int exact))
                    return Get(fields, exact);

                foreach (var kv in headerMap)
                {
                    if (kv.Key.Contains(key) || key.Contains(kv.Key))
                        return Get(fields, kv.Value);
                }
            }

            return "";
        }

        private static List<string> SplitFields(string line)
        {
            var fields = new List<string>();
            if (string.IsNullOrEmpty(line))
                return fields;

            foreach (string part in CsvSplit.Split(line))
            {
                string v = part ?? "";
                if (v.Length >= 2 && v[0] == '"' && v[v.Length - 1] == '"')
                    v = v.Substring(1, v.Length - 2).Replace("\"\"", "\"");
                fields.Add(v.Trim());
            }

            return fields;
        }

        private static string Get(IReadOnlyList<string> fields, int index)
        {
            if (fields == null || index < 0 || index >= fields.Count)
                return "";
            return (fields[index] ?? "").Replace("\"", "").Trim();
        }

        private static string FirstNonEmpty(string a, string b)
        {
            a = (a ?? "").Trim();
            if (a.Length > 0)
                return a;
            return (b ?? "").Trim();
        }

        private static string NormalizeHeader(string raw) =>
            Regex.Replace((raw ?? "").Trim().ToLowerInvariant(), @"[^a-z0-9#]+", " ").Trim();
    }
}
