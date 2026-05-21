using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json.Linq;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>Paste-friendly schedule dump for Cursor / desk review (roster, rules, groups, coords).</summary>
    internal static class SupeyScheduleReviewExport
    {
        public static string Build(
            DateTime serviceDate,
            IList<SupeyDriverProfile> roster,
            SupeyScheduleResult result,
            JObject rulesContext)
        {
            if (result == null) return "";
            var sb = new StringBuilder();
            sb.AppendLine("# Hiatme schedule — paste into Cursor for review");
            sb.AppendLine("Service date: " + serviceDate.ToString("yyyy-MM-dd"));
            sb.AppendLine("Drivers with trips: " + CountDriversWithTrips(result)
                + " · Reserves: " + result.Reserves.Count
                + " · Warnings: " + result.WarningCount);
            if (result.FleetActiveSeconds > 0)
            {
                sb.Append("Fleet: ")
                  .Append(SupeyTripTimes.FormatHoursMinutesFromSeconds(result.FleetActiveSeconds))
                  .Append(" · ")
                  .Append(SupeyTripTimes.FormatMiles(result.FleetMeters))
                  .AppendLine();
            }
            sb.AppendLine();

            AppendRoster(sb, roster, result);
            AppendRules(sb, rulesContext);
            AppendWarnings(sb, result);
            sb.AppendLine("---");
            sb.AppendLine();

            foreach (var plan in result.DriverPlans)
                AppendDriver(sb, plan, includeCoords: true);
            if (result.Reserves.Count > 0)
                AppendReserves(sb, result);
            return sb.ToString();
        }

        private static int CountDriversWithTrips(SupeyScheduleResult result)
        {
            int n = 0;
            foreach (var p in result.DriverPlans)
                if (p.Groups.Count > 0) n++;
            return n;
        }

        private static void AppendRoster(StringBuilder sb, IList<SupeyDriverProfile> roster, SupeyScheduleResult result)
        {
            sb.AppendLine("## Roster (checked for BUILD)");
            sb.AppendLine("Driver\tCapacity\tShift\tHome");
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (roster != null)
            {
                foreach (var d in roster)
                {
                    if (d == null || string.IsNullOrWhiteSpace(d.Name)) continue;
                    if (!seen.Add(d.Name)) continue;
                    sb.Append(Sanitize(d.Name)).Append('\t')
                      .Append(d.CapacityPassengers).Append('\t')
                      .Append(Sanitize(FormatShift(d))).Append('\t')
                      .Append(Sanitize(d.FormatHomeOneLine()))
                      .AppendLine();
                }
            }
            foreach (var plan in result.DriverPlans)
            {
                var d = plan.Driver;
                if (d == null || string.IsNullOrWhiteSpace(d.Name) || !seen.Add(d.Name)) continue;
                sb.Append(Sanitize(d.Name)).Append('\t')
                  .Append(d.CapacityPassengers).Append('\t')
                  .Append(Sanitize(FormatShift(d))).Append('\t')
                  .Append(Sanitize(d.FormatHomeOneLine()))
                  .AppendLine(" (from plan)");
            }
            sb.AppendLine();
        }

        private static string FormatShift(SupeyDriverProfile d)
        {
            string a = (d.ShiftStart ?? "").Trim();
            string b = (d.ShiftEnd ?? "").Trim();
            if (a.Length == 0 && b.Length == 0) return "";
            if (a.Length > 0 && b.Length > 0) return a + "–" + b;
            return a.Length > 0 ? a : b;
        }

        private static void AppendRules(StringBuilder sb, JObject rulesContext)
        {
            sb.AppendLine("## Accepted rules (from server at BUILD)");
            if (rulesContext == null)
            {
                sb.AppendLine("(none — panel was offline or pre-review skipped)");
                sb.AppendLine();
                return;
            }
            foreach (var a in rulesContext["hard_avoidances"] as JArray ?? new JArray())
            {
                sb.Append("- Do NOT assign ").Append(a["client"]).Append(" to ").Append(a["driver"]);
                if (!string.IsNullOrWhiteSpace(a["reason"]?.ToString()))
                    sb.Append(" — ").Append(a["reason"]);
                sb.AppendLine();
            }
            foreach (var p in rulesContext["preferred_pairings"] as JArray ?? new JArray())
            {
                sb.Append("- Prefer ").Append(p["client"]).Append(" on ").Append(p["driver"]);
                if (!string.IsNullOrWhiteSpace(p["reason"]?.ToString()))
                    sb.Append(" — ").Append(p["reason"]);
                sb.AppendLine();
            }
            foreach (var lp in rulesContext["driver_load_preferences"] as JArray ?? new JArray())
            {
                sb.Append("- Lighter clusters for ").Append(lp["driver"])
                  .Append(" (≤").Append(lp["max_cluster_riders"] ?? "4").Append(" riders)");
                if (!string.IsNullOrWhiteSpace(lp["reason"]?.ToString()))
                    sb.Append(" — ").Append(lp["reason"]);
                sb.AppendLine();
            }
            if ((rulesContext["hard_avoidances"] as JArray)?.Count == 0
                && (rulesContext["preferred_pairings"] as JArray)?.Count == 0
                && (rulesContext["driver_load_preferences"] as JArray)?.Count == 0)
                sb.AppendLine("(no structured rules in context)");
            sb.AppendLine();
        }

        private static void AppendWarnings(StringBuilder sb, SupeyScheduleResult result)
        {
            if (result.WarningCount == 0) return;
            sb.AppendLine("## Warnings");
            if (result.BuildWarnings != null)
            {
                foreach (var w in result.BuildWarnings)
                {
                    if (w == null) continue;
                    sb.Append("- [build] ").AppendLine(Sanitize(w.Detail ?? ""));
                }
            }
            foreach (var plan in result.DriverPlans)
            {
                if (plan.Warnings == null) continue;
                foreach (var w in plan.Warnings)
                {
                    if (w == null) continue;
                    sb.Append("- ").Append(Sanitize(plan.Driver?.Name ?? "driver"))
                      .Append(": ").AppendLine(Sanitize(w.Detail ?? ""));
                }
            }
            sb.AppendLine();
        }

        private static void AppendDriver(StringBuilder sb, SupeyDriverPlan plan, bool includeCoords)
        {
            string driverName = plan.Driver?.Name ?? "(driver)";
            if (plan.Groups.Count == 0)
            {
                sb.Append("=== ").Append(driverName).AppendLine(" === — no trips assigned");
                sb.AppendLine();
                return;
            }

            int riders = plan.RiderCount;
            int groups = plan.Groups.Count;
            sb.Append("=== ").Append(driverName).Append(" === (")
              .Append(riders).Append(" trip").Append(riders == 1 ? "" : "s")
              .Append(", ").Append(groups).Append(" group").Append(groups == 1 ? "" : "s");
            sb.Append(" · order G");
            for (int i = 0; i < plan.Groups.Count; i++)
            {
                if (i > 0) sb.Append(" → ");
                sb.Append(plan.Groups[i].GroupNumber);
            }
            if (plan.FirstPickup.HasValue)
                sb.Append(" · first PU ").Append(SupeyTripTimes.FormatTimeOfDay(plan.FirstPickup.Value));
            if (plan.LastDropoff.HasValue)
                sb.Append(" · last DO ").Append(SupeyTripTimes.FormatTimeOfDay(plan.LastDropoff.Value));
            if (plan.ReleaseTimeOfDay.HasValue)
                sb.Append(" · release ").Append(SupeyTripTimes.FormatTimeOfDay(plan.ReleaseTimeOfDay.Value));
            sb.Append(" · ").Append(SupeyTripTimes.FormatHoursMinutesFromSeconds(plan.TotalDriveSeconds))
              .Append(" / ").Append(SupeyTripTimes.FormatMiles(plan.TotalMeters));
            if (plan.DeadHeads != null && plan.DeadHeads.Count > 0)
            {
                sb.AppendLine();
                sb.Append("Deadheads: ");
                for (int i = 0; i < plan.DeadHeads.Count; i++)
                {
                    if (i > 0) sb.Append(" | ");
                    var dh = plan.DeadHeads[i];
                    sb.Append(Sanitize(dh.Label ?? ""))
                      .Append(" ").Append(SupeyTripTimes.FormatMiles(dh.DistanceMeters));
                }
            }
            sb.AppendLine(")");

            if (includeCoords)
                sb.AppendLine("Grp\tTrip #\tClient\tPU Time\tPU Street\tPU City\tPU lat\tPU lng\tDO Time\tDO Street\tDO City\tDO lat\tDO lng\tMiles");
            else
                sb.AppendLine("Grp\tTrip #\tClient\tPU Time\tPU Street\tPU City\tDO Time\tDO Street\tDO City\tMiles");

            foreach (var g in plan.Groups)
            {
                sb.Append(g.GroupNumber).Append("\tRoute\t")
                  .Append(Sanitize(SupeyRouteNoteFormatter.Format(g)))
                  .AppendLine();
                for (int ti = 0; ti < g.Trips.Count; ti++)
                {
                    var t = g.Trips[ti];
                    sb.Append(g.GroupNumber).Append('\t')
                      .Append(Sanitize(t.TripNumber)).Append('\t')
                      .Append(Sanitize(t.ClientFullName)).Append('\t')
                      .Append(Sanitize(t.PUTime)).Append('\t')
                      .Append(Sanitize(t.PUStreet)).Append('\t')
                      .Append(Sanitize(t.PUCity));
                    if (includeCoords && ti < g.PickupPoints.Count)
                    {
                        sb.Append('\t').Append(g.PickupPoints[ti].Lat.ToString("F5", System.Globalization.CultureInfo.InvariantCulture))
                          .Append('\t').Append(g.PickupPoints[ti].Lng.ToString("F5", System.Globalization.CultureInfo.InvariantCulture));
                    }
                    else if (includeCoords)
                    {
                        sb.Append("\t\t");
                    }
                    sb.Append('\t').Append(Sanitize(t.DOTime)).Append('\t')
                      .Append(Sanitize(t.DOStreet)).Append('\t')
                      .Append(Sanitize(t.DOCITY));
                    if (includeCoords && ti < g.DropoffPoints.Count)
                    {
                        sb.Append('\t').Append(g.DropoffPoints[ti].Lat.ToString("F5", System.Globalization.CultureInfo.InvariantCulture))
                          .Append('\t').Append(g.DropoffPoints[ti].Lng.ToString("F5", System.Globalization.CultureInfo.InvariantCulture));
                    }
                    else if (includeCoords)
                    {
                        sb.Append("\t\t");
                    }
                    sb.Append('\t').Append(Sanitize(t.Miles))
                      .AppendLine();
                }
            }
            sb.AppendLine();
        }

        private static void AppendReserves(StringBuilder sb, SupeyScheduleResult result)
        {
            sb.Append("=== RESERVES === (")
              .Append(result.Reserves.Count)
              .Append(" trip").Append(result.Reserves.Count == 1 ? "" : "s")
              .AppendLine(")");
            sb.AppendLine("Trip #\tClient\tPU Time\tPU Street\tPU City\tDO Time\tDO Street\tDO City\tMiles");
            foreach (var t in result.Reserves)
            {
                sb.Append(Sanitize(t.TripNumber)).Append('\t')
                  .Append(Sanitize(t.ClientFullName)).Append('\t')
                  .Append(Sanitize(t.PUTime)).Append('\t')
                  .Append(Sanitize(t.PUStreet)).Append('\t')
                  .Append(Sanitize(t.PUCity)).Append('\t')
                  .Append(Sanitize(t.DOTime)).Append('\t')
                  .Append(Sanitize(t.DOStreet)).Append('\t')
                  .Append(Sanitize(t.DOCITY)).Append('\t')
                  .Append(Sanitize(t.Miles))
                  .AppendLine();
            }
            sb.AppendLine();
        }

        private static string Sanitize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\t", " ").Replace("\r", " ").Replace("\n", " ");
        }
    }
}
