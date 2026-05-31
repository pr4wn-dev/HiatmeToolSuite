using System;

using System.Collections.Generic;



namespace Hiatme_Tool_Suite_v3

{

    internal static class SupeyWarningsUtil

    {

        internal static bool IsDriverTimingWarning(SupeyWarning w)

        {

            if (w == null) return false;

            if (w.Kind == SupeyWarningKind.LateArrival

                || w.Kind == SupeyWarningKind.TightArrival

                || w.Kind == SupeyWarningKind.LateNextPickup)

                return true;

            string d = w.Detail ?? "";

            return d.IndexOf("may miss DO appt", StringComparison.OrdinalIgnoreCase) >= 0

                || d.IndexOf("may arrive at first PU", StringComparison.OrdinalIgnoreCase) >= 0;

        }



        internal static string DedupeKey(SupeyWarning w)
        {
            if (w == null) return "";
            // Same LateDO line often appears once with a trip # and once without (build vs driver).
            if (IsDriverTimingWarning(w))
                return ((int)w.Kind) + "|" + (w.Detail ?? "").Trim();
            return ((int)w.Kind) + "|" + (w.TripNumber ?? "") + "|" + (w.Detail ?? "");
        }



        internal static int CountUnique(SupeyScheduleResult result)

        {

            if (result == null) return 0;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            int n = 0;

            foreach (var w in result.BuildWarnings)

            {

                if (w == null || !seen.Add(DedupeKey(w))) continue;

                n++;

            }

            foreach (var p in result.DriverPlans)

            {

                if (p?.Warnings == null) continue;

                foreach (var w in p.Warnings)

                {

                    if (w == null || !seen.Add(DedupeKey(w))) continue;

                    n++;

                }

            }

            return n;

        }



        internal static void StripTimingFromBuild(SupeyScheduleResult result)

        {

            if (result?.BuildWarnings == null) return;

            result.BuildWarnings.RemoveAll(w => IsDriverTimingWarning(w));

        }



        internal static void ClearAllDriverWarnings(SupeyScheduleResult result)

        {

            if (result?.DriverPlans == null) return;

            foreach (var p in result.DriverPlans)

            {

                if (p == null) continue;

                p.Warnings.Clear();

                p.TripTimings.Clear();

            }

        }

    }

}


