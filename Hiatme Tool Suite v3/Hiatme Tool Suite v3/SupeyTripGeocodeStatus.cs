using System;
using System.Linq;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>Geocode health text for the Supey trip list Geo column.</summary>
    internal static class SupeyTripGeocodeStatus
    {
        public const string Ok = "OK";
        public const string CheckPin = "Check pin";
        public const string Pending = "…";

        public static string ForScheduledTrip(
            MCDownloadedTrip trip,
            SupeyTripCluster group,
            SupeyDriverPlan plan,
            int tripIndex)
        {
            if (trip == null) return "";
            bool hasPu = group != null && tripIndex >= 0 && tripIndex < group.PickupPoints.Count
                && IsRealPoint(group.PickupPoints[tripIndex]);
            bool hasDo = group != null && tripIndex >= 0 && tripIndex < group.DropoffPoints.Count
                && IsRealPoint(group.DropoffPoints[tripIndex]);
            if (!hasPu || !hasDo)
            {
                if (HasAnyAddress(trip)) return CheckPin;
                return "";
            }
            if (plan?.Warnings != null && plan.Warnings.Any(w =>
                    w != null &&
                    w.Kind == SupeyWarningKind.MissingGeo &&
                    string.Equals(w.TripNumber, trip.TripNumber, StringComparison.OrdinalIgnoreCase)))
                return CheckPin;
            if (plan?.Warnings != null && plan.Warnings.Any(w =>
                    w != null &&
                    w.Kind == SupeyWarningKind.StraightLineFallback &&
                    string.Equals(w.TripNumber, trip.TripNumber, StringComparison.OrdinalIgnoreCase)))
                return CheckPin;
            return Ok;
        }

        public static bool NeedsAttention(string label)
        {
            return string.Equals(label, CheckPin, StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasAnyAddress(MCDownloadedTrip t)
        {
            return !string.IsNullOrWhiteSpace(t.PUStreet) || !string.IsNullOrWhiteSpace(t.PUCity)
                || !string.IsNullOrWhiteSpace(t.DOStreet) || !string.IsNullOrWhiteSpace(t.DOCITY);
        }

        private static bool IsRealPoint(GeoPoint p) => !(p.Lat == 0 && p.Lng == 0);
    }
}
