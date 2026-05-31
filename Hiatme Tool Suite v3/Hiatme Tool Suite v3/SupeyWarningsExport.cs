using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>Paste-friendly warnings dump for Cursor / desk review.</summary>
    internal static class SupeyWarningsExport
    {
        public static string Build(
            DateTime serviceDate,
            SupeyScheduleResult result,
            string buildEngine,
            HiatmeBuildStats stats,
            int tripsLoaded,
            IList<SupeyDriverProfile> rosterForBuild = null)
        {
            if (result == null) return "";
            var sb = new StringBuilder();
            sb.AppendLine("# Hiatme schedule warnings — paste into Cursor");
            sb.AppendLine("Service date: " + serviceDate.ToString("yyyy-MM-dd"));
            sb.AppendLine(SupeyBuildEngineLabel.Describe(buildEngine));
            AppendBuildOptions(sb, result);
            int onScreen = HiatmeAiScheduleMapper.CountAssignedTrips(result);
            int reserves = result.TotalReserveCount;
            int loaded = tripsLoaded > 0 ? tripsLoaded : onScreen + reserves;

            if (stats != null)
            {
                sb.AppendLine("Trips loaded: " + (tripsLoaded > 0 ? tripsLoaded.ToString() : "?"));
                sb.AppendLine("On drivers (screen): " + onScreen + " / " + loaded);
                if (stats.TripsAssigned > 0 && stats.TripsAssigned != onScreen)
                {
                    sb.AppendLine("Server remainder placed: " + stats.TripsAssigned + " / "
                        + stats.TripsTotal + " (template locks merged after)");
                }
                sb.AppendLine("In reserves: " + reserves);
                int accounted = onScreen + reserves;
                if (loaded > 0 && accounted != loaded)
                    sb.AppendLine("Not on screen (geo/other): " + Math.Max(0, loaded - accounted));
                sb.AppendLine("Missing PU+DO geocode: " + stats.NoGeoCount);
                sb.AppendLine("Groups unplaced (had geo): " + stats.UnassignedGroupsCount);
                sb.AppendLine("Ride-share groups formed: " + stats.ClusterCount);
                if (stats.TripsWithCoords > 0)
                    sb.AppendLine("Trips with coordinates: " + stats.TripsWithCoords);
                if (stats.GeocodedNew > 0)
                    sb.AppendLine("New geocodes this run: " + stats.GeocodedNew);
            }
            else
            {
                int assigned = HiatmeAiScheduleMapper.CountAssignedTrips(result);
                sb.AppendLine("Trips loaded: " + (tripsLoaded > 0 ? tripsLoaded.ToString() : "?"));
                sb.AppendLine("On drivers: " + assigned);
                sb.AppendLine("In reserves: " + result.Reserves.Count);
                sb.AppendLine("Warning rows: " + result.WarningCount);
            }
            sb.AppendLine();

            AppendRosterAndBuildLoad(sb, rosterForBuild, result);
            AppendWarningsByKind(sb, result);
            AppendReserveSample(sb, result);
            return sb.ToString();
        }

        private static void AppendBuildOptions(StringBuilder sb, SupeyScheduleResult result)
        {
            if (result?.BuildOptions != null)
                result.BuildOptions.AppendTo(sb);
            else
            {
                sb.AppendLine("## BUILD options (enabled for this build)");
                sb.AppendLine("(not recorded — run BUILD again, then copy warnings)");
                sb.AppendLine();
            }
        }

        /// <summary>Drivers checked for BUILD (from Supey roster) plus what the build assigned.</summary>
        private static void AppendRosterAndBuildLoad(StringBuilder sb, IList<SupeyDriverProfile> roster, SupeyScheduleResult result)
        {
            sb.AppendLine("## Drivers for this BUILD (from Supey roster)");
            sb.AppendLine(
                "Shift = roster shift start–end (HH:mm). Empty shift = no floor/ceiling in the builder.");
            sb.AppendLine(
                "Driver | Cap | Shift | Home | Vehicle | Trips | Grps | Release | Drv warn | Drive");
            var planByName = new Dictionary<string, SupeyDriverPlan>(StringComparer.OrdinalIgnoreCase);
            if (result.DriverPlans != null)
            {
                foreach (var p in result.DriverPlans)
                {
                    if (p?.Driver == null || string.IsNullOrWhiteSpace(p.Driver.Name)) continue;
                    planByName[p.Driver.Name] = p;
                }
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (roster != null)
            {
                foreach (var d in roster)
                {
                    if (d == null || string.IsNullOrWhiteSpace(d.Name)) continue;
                    if (!seen.Add(d.Name)) continue;
                    planByName.TryGetValue(d.Name, out var plan);
                    AppendDriverRow(sb, d, plan);
                }
            }
            if (result.DriverPlans != null)
            {
                foreach (var p in result.DriverPlans)
                {
                    var d = p.Driver;
                    if (d == null || string.IsNullOrWhiteSpace(d.Name) || !seen.Add(d.Name)) continue;
                    AppendDriverRow(sb, d, p);
                }
            }
            if (seen.Count == 0)
                sb.AppendLine("(no drivers in roster)");
            int noRoad = 0;
            if (result?.DriverPlans != null)
            {
                foreach (var p in result.DriverPlans)
                {
                    if (p?.Groups == null || p.Groups.Count == 0) continue;
                    if (p.TotalDriveSeconds <= 0 || p.TotalMeters < 500) noRoad++;
                }
            }
            if (noRoad > 0)
                sb.AppendLine("Drivers without post-build road miles: " + noRoad
                    + " (release/drive column —) — see Post-build warnings.");
            sb.AppendLine();
        }

        private static void AppendDriverRow(StringBuilder sb, SupeyDriverProfile d, SupeyDriverPlan plan)
        {
            int trips = 0;
            int groups = 0;
            string release = "—";
            int warn = 0;
            string drive = "—";
            if (plan != null)
            {
                if (plan.Groups != null)
                {
                    groups = plan.Groups.Count;
                    foreach (var g in plan.Groups)
                        trips += g?.Trips?.Count ?? 0;
                }
                if (plan.ReleaseTimeOfDay.HasValue)
                    release = SupeyTripTimes.FormatTimeOfDay(plan.ReleaseTimeOfDay.Value);
                warn = plan.Warnings?.Count ?? 0;
                if (plan.TotalDriveSeconds > 0)
                    drive = SupeyTripTimes.FormatHoursMinutesFromSeconds(plan.TotalDriveSeconds)
                        + " / " + SupeyTripTimes.FormatMiles(plan.TotalMeters);
            }

            string vehicle = (d.VehicleLabel ?? "").Trim();
            if (vehicle.Length == 0) vehicle = "—";

            sb.Append(Sanitize(d.Name)).Append(" | ")
              .Append(d.CapacityPassengers).Append(" | ")
              .Append(Sanitize(FormatShift(d))).Append(" | ")
              .Append(Sanitize(d.FormatHomeOneLine())).Append(" | ")
              .Append(Sanitize(vehicle)).Append(" | ")
              .Append(trips).Append(" | ")
              .Append(groups).Append(" | ")
              .Append(release).Append(" | ")
              .Append(warn).Append(" | ")
              .AppendLine(drive);
        }

        private static string FormatShift(SupeyDriverProfile d)
        {
            if (d == null) return "—";
            string a = (d.ShiftStart ?? "").Trim();
            string b = (d.ShiftEnd ?? "").Trim();
            if (a.Length == 0 && b.Length == 0) return "(none)";
            if (a.Length > 0 && b.Length > 0) return a + "–" + b;
            return a.Length > 0 ? a : b;
        }

        private static string Sanitize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
        }

        private static void AppendWarningsByKind(StringBuilder sb, SupeyScheduleResult result)
        {
            var all = CollectWarnings(result);
            if (all.Count == 0)
            {
                sb.AppendLine("## Warnings");
                sb.AppendLine("(none)");
                return;
            }

            sb.AppendLine("## Warnings (" + all.Count + " rows)");
            var byKind = all.GroupBy(x => x.Warning.Kind)
                .OrderByDescending(g => g.Count());
            sb.AppendLine("### By kind");
            foreach (var g in byKind)
            {
                sb.AppendLine("- " + FormatKind(g.Key) + ": " + g.Count());
            }
            sb.AppendLine();

            sb.AppendLine("### Detail (Kind | Trip | Driver | Message)");
            foreach (var entry in all.OrderBy(x => FormatKind(x.Warning.Kind))
                .ThenBy(x => x.Scope)
                .ThenBy(x => x.Warning.TripNumber))
            {
                var w = entry.Warning;
                sb.Append("- ").Append(FormatKind(w.Kind)).Append(" | ");
                sb.Append(string.IsNullOrEmpty(w.TripNumber) ? "—" : w.TripNumber).Append(" | ");
                sb.Append(entry.Scope ?? "—").Append(" | ");
                sb.AppendLine((w.Detail ?? "").Replace("\r", " ").Replace("\n", " "));
            }
            sb.AppendLine();
        }

        private static void AppendReserveSample(StringBuilder sb, SupeyScheduleResult result)
        {
            if (result == null || result.TotalReserveCount == 0) return;
            sb.AppendLine("## Reserve trip numbers (" + result.TotalReserveCount + " total)");
            if (result.ReservesReroute.Count > 0)
            {
                sb.AppendLine("### Reroute (out-of-service areas) — " + result.ReservesReroute.Count);
                AppendTripNumberList(sb, result.ReservesReroute, 40);
            }
            if (result.Reserves.Count > 0)
            {
                sb.AppendLine("### Need driver — " + result.Reserves.Count);
                AppendTripNumberList(sb, result.Reserves, 80);
            }
            sb.AppendLine();
        }

        private static void AppendTripNumberList(StringBuilder sb, List<MCDownloadedTrip> trips, int maxList)
        {
            if (trips == null || trips.Count == 0) return;
            int n = 0;
            foreach (var t in trips)
            {
                if (t == null) continue;
                string tn = (t.TripNumber ?? "").Trim();
                if (string.IsNullOrEmpty(tn)) continue;
                if (n > 0) sb.Append(", ");
                if (n > 0 && n % 8 == 0) sb.AppendLine().Append("  ");
                sb.Append(tn);
                n++;
                if (n >= maxList)
                {
                    sb.AppendLine();
                    sb.AppendLine("  … +" + (trips.Count - maxList) + " more (see Reserves in Supey).");
                    break;
                }
            }
            if (n > 0 && n < maxList) sb.AppendLine();
        }

        private static List<WarningEntry> CollectWarnings(SupeyScheduleResult result)
        {
            var list = new List<WarningEntry>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var w in result.BuildWarnings)
            {
                if (w == null) continue;
                if (!seen.Add(WarningDedupeKey(w))) continue;
                string scope = string.IsNullOrWhiteSpace(w.DriverName) ? "Build" : w.DriverName;
                list.Add(new WarningEntry(w, scope));
            }
            foreach (var p in result.DriverPlans)
            {
                string name = p.Driver?.Name ?? "(driver)";
                if (p.Warnings == null) continue;
                foreach (var w in p.Warnings)
                {
                    if (w == null) continue;
                    if (!seen.Add(WarningDedupeKey(w))) continue;
                    list.Add(new WarningEntry(w, name));
                }
            }
            return list;
        }

        private static string WarningDedupeKey(SupeyWarning w) =>
            SupeyWarningsUtil.DedupeKey(w);

        private static string FormatKind(SupeyWarningKind k)
        {
            switch (k)
            {
                case SupeyWarningKind.MissingGeo: return "Geo";
                case SupeyWarningKind.UnassignedToReserves: return "Reserve";
                case SupeyWarningKind.OutOfServiceArea: return "Reroute";
                case SupeyWarningKind.LateArrival: return "LateDO";
                case SupeyWarningKind.TightArrival: return "Tight";
                case SupeyWarningKind.LateNextPickup: return "LatePU";
                case SupeyWarningKind.StraightLineFallback: return "OSRM";
                case SupeyWarningKind.OutsideShift: return "Shift";
                case SupeyWarningKind.DriverHomeUnresolvable: return "HomeGeo";
                case SupeyWarningKind.RouteFailure: return "RouteFail";
                case SupeyWarningKind.BuildDiagnostic: return "Build";
                default: return k.ToString();
            }
        }

        private sealed class WarningEntry
        {
            public SupeyWarning Warning { get; }
            public string Scope { get; }
            public WarningEntry(SupeyWarning w, string scope)
            {
                Warning = w;
                Scope = scope;
            }
        }
    }
}
