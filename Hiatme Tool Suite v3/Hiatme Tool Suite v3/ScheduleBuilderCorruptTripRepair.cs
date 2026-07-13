using System;
using System.Collections.Generic;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Fixes schedule trips whose fields were corrupted by the Modivcare header fuzzy-match bug
    /// (Age→Date "A", State→PUTime "ME", Gender→DOCity "F"/"M") by copying fields from a
    /// fresh Modivcare download matched on trip #.
    /// </summary>
    internal static class ScheduleBuilderCorruptTripRepair
    {
        /// <summary>
        /// Walk every preview trip; when it looks corrupt and a healthier Modivcare row exists
        /// for the same trip #, overwrite the bad schedule fields in place.
        /// </summary>
        public static int RepairPreviewFromDownload(
            IDictionary<string, List<ScheduleBuilderPreviewLine>> linesByTab,
            IEnumerable<MCDownloadedTrip> downloaded,
            ISet<string> repairedKeys = null)
        {
            if (linesByTab == null || downloaded == null)
                return 0;

            var healthyByKey = BuildHealthyDownloadLookup(downloaded);
            if (healthyByKey.Count == 0)
                return 0;

            if (repairedKeys == null)
                repairedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            int repaired = 0;

            foreach (var kv in linesByTab)
            {
                var lines = kv.Value;
                if (lines == null)
                    continue;

                foreach (var line in lines)
                {
                    if (line?.Kind != ScheduleBuilderPreviewLine.LineKind.Trip || line.Trip == null)
                        continue;

                    if (!TryRepairTrip(line.Trip, healthyByKey, repairedKeys))
                        continue;

                    repaired++;
                }
            }

            return repaired;
        }

        /// <summary>Also repair trip objects that live only in reserve bucket lists / MCTripList.</summary>
        public static int RepairTripListFromDownload(
            IEnumerable<MCDownloadedTrip> trips,
            IEnumerable<MCDownloadedTrip> downloaded,
            ISet<string> alreadyRepairedKeys = null)
        {
            if (trips == null || downloaded == null)
                return 0;

            var healthyByKey = BuildHealthyDownloadLookup(downloaded);
            if (healthyByKey.Count == 0)
                return 0;

            var repairedKeys = alreadyRepairedKeys
                ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            int repaired = 0;
            foreach (var trip in trips)
            {
                if (!TryRepairTrip(trip, healthyByKey, repairedKeys))
                    continue;
                repaired++;
            }

            return repaired;
        }

        public static bool LooksCorrupt(MCDownloadedTrip trip)
        {
            if (trip == null)
                return false;

            // Strong signals from the known spill pattern.
            if (ModivcareDelimitedTripParser.LooksLikeAgeOrGenderToken(trip.Date)
                || ModivcareDelimitedTripParser.LooksLikeUsStateCode(trip.PUTime)
                || ModivcareDelimitedTripParser.LooksLikeAgeOrGenderToken(trip.DOCITY)
                || ModivcareDelimitedTripParser.LooksLikeAgeOrGenderToken(trip.ClientFullName)
                || ModivcareDelimitedTripParser.LooksLikeAgeOrGenderToken(trip.PUStreet)
                || ModivcareDelimitedTripParser.LooksLikeAgeOrGenderToken(trip.DOStreet)
                || ModivcareDelimitedTripParser.LooksLikeAgeOrGenderToken(trip.DOTime))
            {
                return true;
            }

            return ModivcareDelimitedTripParser.TripFieldHealthScore(trip) < 2;
        }

        private static bool TryRepairTrip(
            MCDownloadedTrip trip,
            IReadOnlyDictionary<string, MCDownloadedTrip> healthyByKey,
            ISet<string> repairedKeys)
        {
            if (trip == null || !LooksCorrupt(trip))
                return false;

            string key = ScheduleBuilderReroutedTrips.TripNumberKey(trip.TripNumber);
            if (key.Length == 0)
                return false;

            if (!healthyByKey.TryGetValue(key, out MCDownloadedTrip source) || source == null)
                return false;

            if (ModivcareDelimitedTripParser.TripFieldHealthScore(source)
                <= ModivcareDelimitedTripParser.TripFieldHealthScore(trip))
            {
                return false;
            }

            trip.RepairCorruptScheduleFieldsFrom(source);
            // Count unique trip #s; still overwrite every object instance that shares the key.
            return repairedKeys.Add(key);
        }

        private static Dictionary<string, MCDownloadedTrip> BuildHealthyDownloadLookup(
            IEnumerable<MCDownloadedTrip> downloaded)
        {
            var map = new Dictionary<string, MCDownloadedTrip>(StringComparer.OrdinalIgnoreCase);
            if (downloaded == null)
                return map;

            foreach (var trip in downloaded)
            {
                if (trip == null || LooksCorrupt(trip))
                    continue;

                string key = ScheduleBuilderReroutedTrips.TripNumberKey(trip.TripNumber);
                if (key.Length == 0)
                    continue;

                if (!map.TryGetValue(key, out MCDownloadedTrip existing)
                    || ModivcareDelimitedTripParser.TripFieldHealthScore(trip)
                        > ModivcareDelimitedTripParser.TripFieldHealthScore(existing))
                {
                    map[key] = trip;
                }
            }

            return map;
        }
    }
}
