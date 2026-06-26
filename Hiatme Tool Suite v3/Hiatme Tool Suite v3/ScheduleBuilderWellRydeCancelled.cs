using System;
using System.Collections.Generic;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>Match schedule preview trips to cancelled rows on the WellRyde trip list.</summary>
    internal static class ScheduleBuilderWellRydeCancelled
    {
        public static bool IsCancelledStatus(string status)
        {
            if (string.IsNullOrWhiteSpace(status))
                return false;
            return status.Equals("Cancelled", StringComparison.OrdinalIgnoreCase)
                || status.Equals("Suspended", StringComparison.OrdinalIgnoreCase);
        }

        public static string NormalizeTripKey(string tripNumber)
        {
            return ScheduleBuilderPreviewDrag.TripLegKey(
                WellRydeFilterDataParser.FormatTripIdForScheduleMatch((tripNumber ?? "").Replace(" ", "")));
        }

        public static HashSet<string> CollectCancelledTripKeys(IEnumerable<WRDownloadedTrip> trips)
        {
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (trips == null)
                return keys;

            foreach (var trip in trips)
            {
                if (trip == null || !IsCancelledStatus(trip.Status))
                    continue;
                string key = NormalizeTripKey(trip.TripNumber);
                if (key.Length > 0)
                    keys.Add(key);
            }

            return keys;
        }

        public static bool TripKeySetContains(ISet<string> keys, string tripNumber)
        {
            if (keys == null || keys.Count == 0)
                return false;
            string key = NormalizeTripKey(tripNumber);
            return key.Length > 0 && keys.Contains(key);
        }

        public static void ApplyCancelledFlagsToPreview(
            IDictionary<string, List<ScheduleBuilderPreviewLine>> linesByTab,
            ISet<string> cancelledKeys)
        {
            if (linesByTab == null)
                return;

            bool anyCancelled = cancelledKeys != null && cancelledKeys.Count > 0;
            foreach (var kv in linesByTab)
            {
                var lines = kv.Value;
                if (lines == null)
                    continue;
                foreach (var line in lines)
                {
                    if (line?.Kind != ScheduleBuilderPreviewLine.LineKind.Trip || line.Trip == null)
                        continue;
                    line.CancelledOnWellRyde = anyCancelled
                        && !line.ReroutedOnModivcare
                        && TripKeySetContains(cancelledKeys, line.Trip.TripNumber);
                }
            }
        }

        public static int CountMarkedOnPreview(IDictionary<string, List<ScheduleBuilderPreviewLine>> linesByTab)
        {
            if (linesByTab == null)
                return 0;

            int count = 0;
            foreach (var kv in linesByTab)
            {
                var lines = kv.Value;
                if (lines == null)
                    continue;
                foreach (var line in lines)
                {
                    if (line?.Kind == ScheduleBuilderPreviewLine.LineKind.Trip
                        && line.Trip != null
                        && line.CancelledOnWellRyde)
                        count++;
                }
            }

            return count;
        }
    }
}
