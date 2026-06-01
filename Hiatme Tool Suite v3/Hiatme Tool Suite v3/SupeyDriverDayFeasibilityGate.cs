using System;
using System.Collections.Generic;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Hard pass/fail on a finished driver plan: whole shift must fit PU/DO windows with sequenced deadheads.
    /// Infeasible plans are quarantined (trips to reserves) so BUILD does not ship impossible days.
    /// </summary>
    internal static class SupeyDriverDayFeasibilityGate
    {
        private const double ConcurrentPuMaxMeters = 2500.0;
        private const double ConcurrentPuMaxMinutesApart = 3.0;

        internal sealed class DriverDayVerdict
        {
            public bool Feasible { get; set; } = true;
            public List<string> Failures { get; } = new List<string>();
        }

        internal static DriverDayVerdict Evaluate(SupeyDriverPlan plan, bool requireSequencedDeadheads)
        {
            var verdict = new DriverDayVerdict();
            if (plan?.Groups == null || plan.Groups.Count == 0)
                return verdict;

            string driver = plan.Driver?.Name ?? "Driver";

            AppendSheetOrderFailures(plan, verdict);
            AppendConcurrentPickupFailures(plan, verdict);

            if (plan.DeadHeads.Count > 0)
                AppendSimulationFailures(plan, driver, verdict);
            else if (requireSequencedDeadheads)
            {
                verdict.Feasible = false;
                verdict.Failures.Add(driver + ": road sequencing missing — cannot verify full day.");
            }

            return verdict;
        }

        internal static void ApplyToSchedule(SupeyScheduleResult result)
        {
            if (result?.DriverPlans == null) return;

            var quarantined = new List<SupeyDriverPlan>();
            foreach (var plan in result.DriverPlans)
            {
                if (plan?.Groups == null || plan.Groups.Count == 0) continue;

                bool needRoad = plan.TotalMeters >= 500 && plan.DeadHeads.Count > 0;
                var verdict = Evaluate(plan, requireSequencedDeadheads: needRoad);
                if (verdict.Feasible) continue;

                quarantined.Add(plan);
                string name = plan.Driver?.Name ?? "Driver";
                string detail = string.Join("; ", verdict.Failures);
                result.InfeasibleDriverNames.Add(name);
                result.BuildWarnings.Insert(0, new SupeyWarning(
                    SupeyWarningKind.BuildDiagnostic,
                    "",
                    "Day infeasible",
                    name + " — schedule rejected (cannot meet PU/DO windows in order): " + detail));
            }

            if (quarantined.Count == 0) return;

            result.HasInfeasibleDriverRejection = true;
            foreach (var plan in quarantined)
                QuarantineDriverPlan(plan, result);
        }

        private static void QuarantineDriverPlan(SupeyDriverPlan plan, SupeyScheduleResult result)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var existing in result.Reserves)
            {
                string tn = existing?.TripNumber ?? "";
                if (tn.Length > 0) seen.Add(tn);
            }

            foreach (var g in plan.Groups)
            {
                if (g?.Trips == null) continue;
                foreach (var t in g.Trips)
                {
                    if (t == null) continue;
                    string tn = (t.TripNumber ?? "").Trim();
                    if (tn.Length > 0 && !seen.Add(tn)) continue;
                    result.Reserves.Add(t);
                }
            }

            plan.Groups.Clear();
            plan.DeadHeads.Clear();
            plan.Warnings.Clear();
            plan.TripTimings.Clear();
            plan.TotalMeters = 0;
            plan.TotalDriveSeconds = 0;
            plan.FirstPickup = null;
            plan.LastDropoff = null;
            plan.ReleaseTimeOfDay = null;
        }

        private static void AppendSheetOrderFailures(SupeyDriverPlan plan, DriverDayVerdict verdict)
        {
            foreach (var msg in SupeyScheduleOrderAudit.DescribeViolations(plan))
            {
                if (msg.IndexOf("impossible on sheet", StringComparison.OrdinalIgnoreCase) < 0
                    && msg.IndexOf("before Group", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                verdict.Feasible = false;
                verdict.Failures.Add(msg);
            }
        }

        private static void AppendConcurrentPickupFailures(SupeyDriverPlan plan, DriverDayVerdict verdict)
        {
            for (int i = 0; i < plan.Groups.Count; i++)
            {
                var a = plan.Groups[i];
                if (a == null) continue;
                var tA = SupeyClusterTimeSplit.MinPickupTime(a);
                var geoA = SupeyScheduleAlgorithm.FirstPickupGeoPublic(a);

                for (int j = i + 1; j < plan.Groups.Count; j++)
                {
                    var b = plan.Groups[j];
                    if (b == null) continue;
                    var tB = SupeyClusterTimeSplit.MinPickupTime(b);
                    if (Math.Abs((tA - tB).TotalMinutes) > ConcurrentPuMaxMinutesApart)
                        continue;

                    var geoB = SupeyScheduleAlgorithm.FirstPickupGeoPublic(b);
                    double meters = HaversineMeters(geoA, geoB);
                    if (meters <= ConcurrentPuMaxMeters) continue;

                    verdict.Feasible = false;
                    verdict.Failures.Add(
                        "Group " + a.GroupNumber + " and Group " + b.GroupNumber
                        + " both need first PU around " + SupeyTripTimes.FormatTimeOfDay(tA)
                        + " but pickups are " + (meters / 1609.34).ToString("0.0") + " mi apart");
                }
            }
        }

        private static void AppendSimulationFailures(
            SupeyDriverPlan plan,
            string driver,
            DriverDayVerdict verdict)
        {
            var shiftStart = plan.Driver.ParseShiftStart() ?? TimeSpan.Zero;
            var shiftEnd = plan.Driver.ParseShiftEnd();
            var current = shiftStart;

            for (int i = 0; i < plan.Groups.Count; i++)
            {
                var g = plan.Groups[i];
                if (g == null || g.Trips.Count == 0) continue;

                double dhSec = i < plan.DeadHeads.Count ? plan.DeadHeads[i].DurationSeconds : 0;
                var arrivalAtFirstPU = current.Add(TimeSpan.FromSeconds(dhSec));

                if (i == 0)
                {
                    double puCap = SupeyScheduleAlgorithm.LegPuLateCapMinutesPublic(g);
                    var scheduledFirstPu = SupeyClusterTimeSplit.MinPickupTime(g);
                    if (arrivalAtFirstPU > scheduledFirstPu.Add(TimeSpan.FromMinutes(puCap)))
                    {
                        verdict.Feasible = false;
                        verdict.Failures.Add(
                            "Group " + g.GroupNumber + ": first PU scheduled "
                            + SupeyTripTimes.FormatTimeOfDay(scheduledFirstPu)
                            + ", projected arrival " + SupeyTripTimes.FormatTimeOfDay(arrivalAtFirstPU));
                    }
                }

                if (!ClusterPickupTourFits(g, arrivalAtFirstPU, verdict))
                    verdict.Feasible = false;

                var (feas, groupEnd, worstIdx, worstMin) =
                    SupeyScheduleAlgorithm.ProjectClusterFeasibilityPublic(g, arrivalAtFirstPU);
                if (!feas && worstIdx >= 0 && worstIdx < g.Trips.Count && worstMin > 0)
                {
                    var trip = g.Trips[worstIdx];
                    double cap = SupeyTripTimingPolicy.DoLateCapMinutes(trip);
                    double displayMin = Math.Max(0, worstMin - cap);
                    if (displayMin > 0)
                    {
                        verdict.Feasible = false;
                        verdict.Failures.Add(
                            "Group " + g.GroupNumber + " — "
                            + (trip.ClientFullName ?? trip.TripNumber ?? "rider")
                            + " misses DO by " + displayMin.ToString("0") + " min");
                    }
                }

                if (shiftEnd.HasValue && groupEnd > shiftEnd.Value)
                {
                    verdict.Feasible = false;
                    verdict.Failures.Add(
                        "Group " + g.GroupNumber + " ends " + SupeyTripTimes.FormatTimeOfDay(groupEnd)
                        + " (shift ends " + SupeyTripTimes.FormatTimeOfDay(shiftEnd.Value) + ")");
                }

                current = groupEnd;
            }
        }

        private static bool ClusterPickupTourFits(
            SupeyTripCluster g,
            TimeSpan arrivalAtFirstPU,
            DriverDayVerdict verdict)
        {
            var visit = SupeyClusterDisplayOrder.PickupVisitIndices(g);
            if (visit.Count == 0) return true;

            TimeSpan clock = arrivalAtFirstPU;
            int first = visit[0];
            if (!SupeyDispatchDriveClock.FitsPickupWindow(g, first, clock))
            {
                SupeyDispatchDriveClock.PickupWindow(g, first, out TimeSpan sched, out _, out TimeSpan latest);
                verdict.Failures.Add(
                    "Group " + g.GroupNumber + " — "
                    + (g.Trips[first].ClientFullName ?? g.Trips[first].TripNumber ?? "rider")
                    + " PU " + SupeyTripTimes.FormatTimeOfDay(sched)
                    + " missed (arrive " + SupeyTripTimes.FormatTimeOfDay(clock)
                    + ", latest " + SupeyTripTimes.FormatTimeOfDay(latest) + ")");
                return false;
            }

            clock = SupeyDispatchDriveClock.AfterPickup(g, first, clock);
            for (int step = 1; step < visit.Count; step++)
            {
                if (step < g.PickupLegSeconds.Count)
                    clock = clock.Add(TimeSpan.FromSeconds(g.PickupLegSeconds[step]));
                int idx = visit[step];
                if (!SupeyDispatchDriveClock.FitsPickupWindow(g, idx, clock))
                {
                    SupeyDispatchDriveClock.PickupWindow(g, idx, out TimeSpan sched, out _, out TimeSpan latest);
                    verdict.Failures.Add(
                        "Group " + g.GroupNumber + " — "
                        + (g.Trips[idx].ClientFullName ?? g.Trips[idx].TripNumber ?? "rider")
                        + " PU " + SupeyTripTimes.FormatTimeOfDay(sched)
                        + " missed (arrive " + SupeyTripTimes.FormatTimeOfDay(clock)
                        + ", latest " + SupeyTripTimes.FormatTimeOfDay(latest) + ")");
                    return false;
                }
                clock = SupeyDispatchDriveClock.AfterPickup(g, idx, clock);
            }
            return true;
        }

        private static double HaversineMeters(GeoPoint a, GeoPoint b)
        {
            const double R = 6371000.0;
            double lat1 = a.Lat * Math.PI / 180.0;
            double lat2 = b.Lat * Math.PI / 180.0;
            double dLat = (b.Lat - a.Lat) * Math.PI / 180.0;
            double dLng = (b.Lng - a.Lng) * Math.PI / 180.0;
            double h = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                     + Math.Cos(lat1) * Math.Cos(lat2) * Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
            return R * 2 * Math.Atan2(Math.Sqrt(h), Math.Sqrt(1 - h));
        }
    }
}
