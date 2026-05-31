using System;
using System.Text.RegularExpressions;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Desk timing tiers for Supey BUILD — mirrors scoreboard strict rules for clinics and
    /// lenient windows used when scoring driver actuals (<see cref="McTripTimingRules"/>).
    /// SCHEDULES FOR 2026 note rows inform Minot/618/program vs Falcon/Manley strict drops.
    /// </summary>
    internal static class SupeyTripTimingPolicy
    {
        public enum TimingTier
        {
            /// <summary>A-leg to dialysis / medical appointment hub — DO at or before appt.</summary>
            StrictClinic,
            /// <summary>Day program, residential hub, 618 Main, Minot drop window, etc.</summary>
            ProgramFlexible,
            /// <summary>B/C return; no appointment deadline (00:00 DO ignored).</summary>
            ReturnRide,
        }

        private static readonly Regex ProgramCommentHint = new Regex(
            @"(?i)\b(day\s*hab|dayhab|program|residential|sheltered|workshop|adult\s*day)\b",
            RegexOptions.Compiled);

        private static readonly Regex CannotDropBefore = new Regex(
            @"CANNOT\s+BE\s+DROPPED\s+OFF\s+BEFORE",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static TimingTier TierFor(MCDownloadedTrip t)
        {
            if (t == null) return TimingTier.ReturnRide;
            char leg = SupeyScheduleAlgorithm.DetectLegPublic(t.TripNumber);
            if (leg != 'A') return TimingTier.ReturnRide;

            string comments = t.Comments ?? "";
            if (ProgramCommentHint.IsMatch(comments))
                return TimingTier.ProgramFlexible;
            if (comments.IndexOf("DR APPT", StringComparison.OrdinalIgnoreCase) >= 0
                || comments.IndexOf("DOCTOR", StringComparison.OrdinalIgnoreCase) >= 0)
                return TimingTier.StrictClinic;

            string doStreet = NormalizeStreet(t.DOStreet);
            string doCity = NormalizeStreet(t.DOCITY);

            if (IsProgramDropHub(doStreet, doCity))
                return TimingTier.ProgramFlexible;

            // Minot: sheets use "cannot drop before" windows — program-flexible, not strict clinic.
            if (IsMinotClinicDrop(doStreet)
                || CannotDropBefore.IsMatch(comments))
                return TimingTier.ProgramFlexible;

            if (IsClinicDropHub(doStreet, doCity))
                return TimingTier.StrictClinic;

            var pu = SupeyTripTimes.TryParsePU(t);
            var appt = SupeyTripTimes.TryParseDO(t);
            if (pu.HasValue && appt.HasValue && (appt.Value - pu.Value).TotalMinutes <= 90)
                return TimingTier.StrictClinic;

            return TimingTier.ProgramFlexible;
        }

        public static double PuLateCapMinutes(MCDownloadedTrip t) =>
            McTripTimingRules.PuLateMaxMinutes(t?.TripNumber ?? "");

        public static double ExtraCoveragePuSlackMinutes(MCDownloadedTrip t) =>
            TierFor(t) == TimingTier.ProgramFlexible ? 6.0 : 0.0;

        public static double DoLateCapMinutes(MCDownloadedTrip t)
        {
            switch (TierFor(t))
            {
                case TimingTier.StrictClinic:
                    return McTripTimingRules.DoLateMaxMinutes;
                case TimingTier.ProgramFlexible:
                    return McTripTimingRules.LenientDoLateMinMinutes;
                default:
                    return McTripTimingRules.LenientDoLateMinMinutes;
            }
        }

        public static double DoLateCapMinutesForCluster(SupeyTripCluster c)
        {
            if (c == null || c.Trips.Count == 0) return McTripTimingRules.DoLateMaxMinutes;
            double cap = McTripTimingRules.LenientDoLateMinMinutes;
            foreach (var t in c.Trips)
            {
                double tripCap = DoLateCapMinutes(t);
                if (tripCap < cap) cap = tripCap;
            }
            return cap;
        }

        public static bool ClusterHasStrictClinicAppointment(SupeyTripCluster c)
        {
            if (c == null) return false;
            foreach (var t in c.Trips)
            {
                if (TierFor(t) == TimingTier.StrictClinic && SupeyTripTimes.TryParseDO(t).HasValue)
                    return true;
            }
            return false;
        }

        private static string NormalizeStreet(string s) =>
            (s ?? "").Trim().ToUpperInvariant().Replace(".", "").Replace(",", "");

        private static bool IsMinotClinicDrop(string street) =>
            street.IndexOf("MINOT", StringComparison.OrdinalIgnoreCase) >= 0;

        private static bool IsClinicDropHub(string street, string city)
        {
            if (street.Length == 0) return false;
            if (street.IndexOf("FALCON", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (street.IndexOf("MANLEY", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (street.IndexOf("646 MAIN", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (street.IndexOf("63 BROAD", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (street.IndexOf("23 CROSS", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (street.IndexOf("CROSS ST", StringComparison.OrdinalIgnoreCase) >= 0
                && city.IndexOf("AUBURN", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (street.IndexOf("CENTRE ST", StringComparison.OrdinalIgnoreCase) >= 0
                && city.IndexOf("BATH", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        private static bool IsProgramDropHub(string street, string city)
        {
            if (street.IndexOf("618 MAIN", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (street.IndexOf("20 EAST", StringComparison.OrdinalIgnoreCase) >= 0
                || (street.IndexOf("EAST AVE", StringComparison.OrdinalIgnoreCase) >= 0
                    && city.IndexOf("LEWISTON", StringComparison.OrdinalIgnoreCase) >= 0)) return true;
            return false;
        }
    }
}
