using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    internal static class HiatmeSchedulePatchApplier
    {
        public static HiatmePatchApplyResult Apply(
            HiatmeSchedulePatch patch,
            IList<MCDownloadedTrip> trips,
            IList<SupeyDriverProfile> roster,
            ref SupeyScheduleResult result,
            ListView driversListView,
            DateTime serviceDate)
        {
            var outcome = new HiatmePatchApplyResult { Ok = true };
            if (patch?.Actions == null || patch.Actions.Count == 0)
            {
                outcome.Summary = "No actions to apply.";
                return outcome;
            }

            var tripNums = new HashSet<string>(
                (trips ?? Enumerable.Empty<MCDownloadedTrip>())
                .Select(t => t?.TripNumber ?? "")
                .Where(x => !string.IsNullOrEmpty(x)),
                StringComparer.OrdinalIgnoreCase);

            var driverNames = new HashSet<string>(
                (roster ?? Enumerable.Empty<SupeyDriverProfile>())
                .Select(d => d?.Name ?? "")
                .Where(x => !string.IsNullOrEmpty(x)),
                StringComparer.OrdinalIgnoreCase);

            int applied = 0;

            foreach (var action in patch.Actions)
            {
                if (action == null || string.IsNullOrWhiteSpace(action.Op)) continue;
                string op = action.Op.Trim().ToLowerInvariant();

                if (op == "set_driver_selected")
                {
                    if (driversListView == null)
                    {
                        outcome.Errors.Add("set_driver_selected: no driver list");
                        continue;
                    }
                    if (!driverNames.Contains(action.DriverName ?? ""))
                    {
                        outcome.Errors.Add("Unknown driver: " + action.DriverName);
                        continue;
                    }
                    foreach (ListViewItem item in driversListView.Items)
                    {
                        var prof = item.Tag as SupeyDriverProfile;
                        if (prof == null) continue;
                        if (!string.Equals(prof.Name, action.DriverName, StringComparison.OrdinalIgnoreCase))
                            continue;
                        item.Checked = action.Selected;
                        applied++;
                        break;
                    }
                    continue;
                }

                if (op == "rebuild")
                {
                    outcome.ShouldRebuild = true;
                    outcome.RebuildUseTemplates = action.UseTemplates;
                    applied++;
                    continue;
                }

                if (op == "unlock_trip" || op == "move_to_reserves")
                {
                    string tn = action.TripNumber ?? "";
                    if (!tripNums.Contains(tn))
                    {
                        outcome.Errors.Add("Unknown trip: " + tn);
                        continue;
                    }
                    EnsureResult(ref result, serviceDate);
                    result.Locks.Remove(tn);
                    applied++;
                    continue;
                }

                if (op == "assign_trip" || op == "lock_trip")
                {
                    string tn = action.TripNumber ?? "";
                    string dn = action.DriverName ?? "";
                    if (!tripNums.Contains(tn))
                    {
                        outcome.Errors.Add("Unknown trip: " + tn);
                        continue;
                    }
                    if (!driverNames.Contains(dn))
                    {
                        outcome.Errors.Add("Unknown driver for " + tn + ": " + dn);
                        continue;
                    }
                    EnsureResult(ref result, serviceDate);
                    result.Locks[tn] = dn;
                    applied++;
                }
            }

            if (result != null && result.DriverPlans.Count + result.Reserves.Count >= 0)
                ApplyVisualTripMoves(patch, trips, roster, result);

            outcome.Ok = outcome.Errors.Count == 0 || applied > 0;
            outcome.Summary = "Applied " + applied + " action(s)."
                + (outcome.Errors.Count > 0 ? " " + outcome.Errors.Count + " skipped." : "");
            return outcome;
        }

        /// <summary>Move trips in the on-screen schedule so ListView updates without another BUILD.</summary>
        private static void ApplyVisualTripMoves(
            HiatmeSchedulePatch patch,
            IList<MCDownloadedTrip> trips,
            IList<SupeyDriverProfile> roster,
            SupeyScheduleResult result)
        {
            if (patch?.Actions == null || result == null) return;

            foreach (var action in patch.Actions)
            {
                if (action == null || string.IsNullOrWhiteSpace(action.Op)) continue;
                string op = action.Op.Trim().ToLowerInvariant();
                string tn = (action.TripNumber ?? "").Trim();
                if (string.IsNullOrEmpty(tn)) continue;

                var trip = FindTrip(trips, tn);
                if (trip == null) continue;

                if (op == "move_to_reserves")
                {
                    RemoveTripFromResult(result, tn);
                    if (!result.Reserves.Any(t => TripEquals(t?.TripNumber, tn)))
                        result.Reserves.Add(trip);
                    continue;
                }

                if (op == "assign_trip" || op == "lock_trip")
                {
                    string dn = (action.DriverName ?? "").Trim();
                    if (string.IsNullOrEmpty(dn)) continue;
                    RemoveTripFromResult(result, tn);
                    AddTripToDriverPlan(result, roster, trip, dn);
                }
            }
        }

        private static bool TripEquals(string a, string b) =>
            string.Equals(a ?? "", b ?? "", StringComparison.OrdinalIgnoreCase);

        private static MCDownloadedTrip FindTrip(IList<MCDownloadedTrip> trips, string tripNumber)
        {
            if (trips == null) return null;
            foreach (var t in trips)
            {
                if (t != null && TripEquals(t.TripNumber, tripNumber))
                    return t;
            }
            return null;
        }

        private static void RemoveTripFromResult(SupeyScheduleResult result, string tripNumber)
        {
            for (int i = result.Reserves.Count - 1; i >= 0; i--)
            {
                if (TripEquals(result.Reserves[i]?.TripNumber, tripNumber))
                    result.Reserves.RemoveAt(i);
            }
            foreach (var plan in result.DriverPlans)
            {
                foreach (var g in plan.Groups)
                {
                    for (int i = g.Trips.Count - 1; i >= 0; i--)
                    {
                        if (TripEquals(g.Trips[i]?.TripNumber, tripNumber))
                            g.Trips.RemoveAt(i);
                    }
                }
            }
        }

        private static void AddTripToDriverPlan(
            SupeyScheduleResult result,
            IList<SupeyDriverProfile> roster,
            MCDownloadedTrip trip,
            string driverName)
        {
            SupeyDriverPlan plan = null;
            foreach (var p in result.DriverPlans)
            {
                if (p?.Driver != null && TripEquals(p.Driver.Name, driverName))
                {
                    plan = p;
                    break;
                }
            }
            if (plan == null)
            {
                SupeyDriverProfile profile = null;
                if (roster != null)
                {
                    foreach (var d in roster)
                    {
                        if (d != null && TripEquals(d.Name, driverName))
                        {
                            profile = d;
                            break;
                        }
                    }
                }
                plan = new SupeyDriverPlan { Driver = profile ?? new SupeyDriverProfile { Name = driverName } };
                result.DriverPlans.Add(plan);
            }

            SupeyTripCluster cluster;
            if (plan.Groups.Count == 0)
            {
                cluster = new SupeyTripCluster
                {
                    GroupNumber = 1,
                    GroupColor = SupeyGroupPalette.For(1),
                };
                plan.Groups.Add(cluster);
            }
            else
            {
                cluster = plan.Groups[plan.Groups.Count - 1];
            }
            cluster.Trips.Add(trip);
        }

        private static void EnsureResult(ref SupeyScheduleResult result, DateTime serviceDate)
        {
            if (result != null) return;
            result = new SupeyScheduleResult { ServiceDate = serviceDate };
        }

        public static string DescribePatch(HiatmeSchedulePatch patch)
        {
            if (patch?.Actions == null || patch.Actions.Count == 0)
                return "(no actions)";
            var lines = new List<string>();
            foreach (var a in patch.Actions)
            {
                if (a == null) continue;
                if (a.Op == "rebuild")
                    lines.Add("• rebuild" + (a.UseTemplates ? " (templates)" : ""));
                else if (a.Op == "set_driver_selected")
                    lines.Add("• " + a.DriverName + " selected=" + a.Selected);
                else if (!string.IsNullOrEmpty(a.TripNumber))
                    lines.Add("• " + a.Op + " " + a.TripNumber
                        + (string.IsNullOrEmpty(a.DriverName) ? "" : " → " + a.DriverName));
            }
            return string.Join(Environment.NewLine, lines);
        }
    }
}
