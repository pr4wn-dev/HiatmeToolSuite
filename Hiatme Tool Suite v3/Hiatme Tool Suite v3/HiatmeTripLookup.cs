using System;
using System.Collections.Generic;
using System.Linq;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>Resolves AI trip_number strings to loaded <see cref="MCDownloadedTrip"/> rows.</summary>
    internal static class HiatmeTripLookup
    {
        public static Dictionary<string, MCDownloadedTrip> Build(IEnumerable<MCDownloadedTrip> trips)
        {
            var map = new Dictionary<string, MCDownloadedTrip>(StringComparer.OrdinalIgnoreCase);
            if (trips == null) return map;

            foreach (var t in trips)
            {
                if (t == null || string.IsNullOrWhiteSpace(t.TripNumber)) continue;
                foreach (var key in TripKeys(t.TripNumber))
                {
                    if (!map.ContainsKey(key))
                        map[key] = t;
                }
            }
            return map;
        }

        public static bool TryResolve(
            string tripRef,
            IReadOnlyDictionary<string, MCDownloadedTrip> map,
            out MCDownloadedTrip trip)
        {
            trip = null;
            if (map == null || string.IsNullOrWhiteSpace(tripRef)) return false;
            foreach (var key in TripKeys(tripRef))
            {
                if (map.TryGetValue(key, out trip) && trip != null)
                    return true;
            }
            return false;
        }

        /// <summary>All lookup keys for a trip id (raw, trimmed, WellRyde-short form).</summary>
        public static IEnumerable<string> TripKeys(string tripNumber)
        {
            if (string.IsNullOrWhiteSpace(tripNumber)) yield break;
            var raw = tripNumber.Trim();
            yield return raw;
            var compact = raw.Replace(" ", "");
            if (!string.Equals(compact, raw, StringComparison.Ordinal))
                yield return compact;
            var formatted = WellRydeFilterDataParser.FormatTripIdForScheduleMatch(compact);
            if (!string.IsNullOrEmpty(formatted) && !string.Equals(formatted, compact, StringComparison.OrdinalIgnoreCase))
                yield return formatted;
        }
    }
}
