using System;
using System.Text.RegularExpressions;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Match Modivcare download rows to schedule / reroute rows — same rules as Analyzer
    /// (<see cref="WellRydeFilterDataParser.FormatTripIdForScheduleMatch"/>) plus glued-leg fix.
    /// </summary>
    internal static class ScheduleBuilderModivcareTripMatch
    {
        public static string NormalizeTripNumber(string tripNumber) =>
            ModivcareDelimitedTripParser.NormalizeStoredTripNumber(tripNumber);

        /// <summary>Same comparison used for reroute registry, preview lines, and leg suffixes.</summary>
        public static bool TripNumbersMatch(string tripNumberA, string tripNumberB) =>
            ScheduleBuilderPreviewDrag.TripLegKeysMatch(tripNumberA, tripNumberB);

        /// <summary>Fallback when trip # cells differ but row is clearly the same trip.</summary>
        public static bool TripDetailsMatch(MCDownloadedTrip a, MCDownloadedTrip b)
        {
            if (a == null || b == null)
                return false;
            if (ScheduleBuilderPreviewDrag.TripEquals(a, b))
                return true;
            if (TripNumbersMatch(a.TripNumber, b.TripNumber))
                return true;

            string clientA = NormalizeClient(a);
            string clientB = NormalizeClient(b);
            if (clientA.Length == 0 || !string.Equals(clientA, clientB, StringComparison.OrdinalIgnoreCase))
                return false;

            string puA = NormalizePuTime(a.PUTime);
            string puB = NormalizePuTime(b.PUTime);
            if (puA.Length == 0 || !string.Equals(puA, puB, StringComparison.OrdinalIgnoreCase))
                return false;

            // Same client + PU time but different trip # formats — require same base id when both have one.
            string baseA = TripBaseId(a.TripNumber);
            string baseB = TripBaseId(b.TripNumber);
            if (baseA.Length > 0 && baseB.Length > 0)
                return string.Equals(baseA, baseB, StringComparison.OrdinalIgnoreCase);

            return true;
        }

        public static string TripBaseId(string tripNumber)
        {
            string normalized = NormalizeTripNumber(tripNumber);
            if (normalized.Length == 0)
                return "";
            string withoutPrefix = ScheduleBuilderPreviewDrag.NormalizeTripNumberKey(normalized);
            return SupeyScheduleAlgorithm.TripPartnerBase(withoutPrefix);
        }

        public static string DetailKey(MCDownloadedTrip trip)
        {
            if (trip == null)
                return "";
            string client = NormalizeClient(trip);
            string pu = NormalizePuTime(trip.PUTime);
            string baseId = TripBaseId(trip.TripNumber);
            if (client.Length == 0 && pu.Length == 0 && baseId.Length == 0)
                return "";
            return client + "|" + pu + "|" + baseId;
        }

        private static string NormalizeClient(MCDownloadedTrip trip)
        {
            string client = (trip.ClientFullName ?? "").Trim();
            if (client.Length == 0)
                client = ((trip.ClientFirstName ?? "") + " " + (trip.ClientLastName ?? "")).Trim();
            client = Regex.Replace(client, @"\s+", " ");
            return client.ToUpperInvariant();
        }

        private static string NormalizePuTime(string puTime) =>
            TripTemplateCsvValidator.NormalizeTimeField(puTime ?? "").Trim().ToUpperInvariant();
    }
}
