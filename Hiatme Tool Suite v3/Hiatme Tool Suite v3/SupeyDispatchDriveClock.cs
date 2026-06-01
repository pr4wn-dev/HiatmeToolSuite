using System;
using System.Collections.Generic;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Simulates the van clock for every pickup/drop in a group (not just the first rider).
    /// Road order is chosen elsewhere (OSRM tour); here we only ask whether that order can
    /// be driven in time: wait if too early for a window, keep moving if already in-window,
    /// fail if too late. Any rider may be picked up early/on-time/slightly late inside window
    /// so the rest of the group and the drop run still work — never idle until sheet time.
    /// </summary>
    internal static class SupeyDispatchDriveClock
    {
        /// <summary>Real arrival at the stop; only wait if before earliest allowed PU.</summary>
        public static TimeSpan ArrivalAtPickup(SupeyTripCluster c, int idx, TimeSpan driveArrival) =>
            AfterPickup(c, idx, driveArrival);
        public static void PickupWindow(
            SupeyTripCluster c,
            int idx,
            out TimeSpan scheduled,
            out TimeSpan earliest,
            out TimeSpan latest)
        {
            var trip = c.Trips[idx];
            scheduled = ScheduledPickup(c, idx);
            double earlyMin = SupeyDeskScheduleTiming.EarlyPuAllowanceMinutesBeforeScheduledPu(trip);
            double lateMin = SupeyTripTimingPolicy.PuLateCapMinutes(trip)
                + SupeyTripTimingPolicy.ExtraCoveragePuSlackMinutes(trip);
            earliest = scheduled.Subtract(TimeSpan.FromMinutes(earlyMin));
            if (earliest < TimeSpan.Zero) earliest = TimeSpan.Zero;
            latest = scheduled.Add(TimeSpan.FromMinutes(lateMin));
        }

        public static bool FitsPickupWindow(SupeyTripCluster c, int idx, TimeSpan arrive)
        {
            PickupWindow(c, idx, out _, out TimeSpan earliest, out TimeSpan latest);
            return arrive >= earliest && arrive <= latest;
        }

        public static TimeSpan AfterPickup(SupeyTripCluster c, int idx, TimeSpan arrive)
        {
            PickupWindow(c, idx, out _, out TimeSpan earliest, out _);
            return arrive < earliest ? earliest : arrive;
        }

        public static TimeSpan ScheduledPickup(SupeyTripCluster c, int idx)
        {
            var t = SupeyDeskScheduleTiming.ScheduledPickupForBuild(c.Trips[idx]);
            if (t != TimeSpan.Zero) return t;
            return SupeyTripTimes.TryParsePU(c.Trips[idx]) ?? c.EarliestPickup;
        }

        public static void DropWindow(
            MCDownloadedTrip trip,
            out TimeSpan scheduled,
            out TimeSpan earliest,
            out TimeSpan latest)
        {
            scheduled = SupeyTripTimes.TryParseDO(trip) ?? TimeSpan.Zero;
            var commentEarliest = SupeyDeskScheduleTiming.EarliestDropoffForFeasibility(trip);
            double lateMin = SupeyTripTimingPolicy.DoLateCapMinutes(trip);
            if (lateMin < McTripTimingRules.LenientNaturalSlackMinutes)
                lateMin += McTripTimingRules.LenientNaturalSlackMinutes;

            earliest = scheduled;
            if (commentEarliest.HasValue)
                earliest = commentEarliest.Value > earliest ? commentEarliest.Value : earliest;

            if (SupeyTripTimingPolicy.TierFor(trip) == SupeyTripTimingPolicy.TimingTier.ProgramFlexible
                && scheduled > TimeSpan.Zero)
            {
                var programEarly = scheduled.Subtract(
                    TimeSpan.FromMinutes(McTripTimingRules.LenientDoEarlyMinMinutes));
                if (programEarly < earliest) earliest = programEarly;
                if (earliest < TimeSpan.Zero) earliest = TimeSpan.Zero;
            }

            latest = scheduled.Add(TimeSpan.FromMinutes(lateMin));
        }

        public static bool FitsDropWindow(MCDownloadedTrip trip, TimeSpan arrive)
        {
            DropWindow(trip, out _, out TimeSpan earliest, out TimeSpan latest);
            if (latest <= TimeSpan.Zero && earliest <= TimeSpan.Zero)
                return true;
            if (latest <= TimeSpan.Zero)
                return arrive >= earliest;
            return arrive >= earliest && arrive <= latest;
        }

        /// <summary>Wait only before &quot;cannot drop before&quot; / program early floor — not until scheduled if in window.</summary>
        public static TimeSpan AfterDropoff(MCDownloadedTrip trip, TimeSpan arrive)
        {
            DropWindow(trip, out _, out TimeSpan earliest, out _);
            return arrive < earliest ? earliest : arrive;
        }

        public static TimeSpan DepartureAfterLastPickup(
            SupeyTripCluster c,
            IReadOnlyList<int> puOrder,
            TimeSpan arrivalAtFirstStop)
        {
            if (c == null || puOrder == null || puOrder.Count == 0)
                return arrivalAtFirstStop;

            int first = puOrder[0];
            TimeSpan clock = ArrivalAtPickup(c, first, arrivalAtFirstStop);

            for (int step = 1; step < puOrder.Count; step++)
            {
                int idx = puOrder[step];
                clock = clock.Add(TimeSpan.FromSeconds(PickupLegSecondsOrEstimate(c, step)));
                if (!FitsPickupWindow(c, idx, clock))
                    return clock;
                clock = AfterPickup(c, idx, clock);
            }
            return clock;
        }

        public static (bool feasible, TimeSpan end, int worstLateTrip, double worstLateMinutes)
            ProjectDropRun(
            SupeyTripCluster c,
            TimeSpan departureAfterLastPickup)
        {
            bool feasible = true;
            int worstLateTrip = -1;
            double worstLateMinutes = 0;
            TimeSpan current = departureAfterLastPickup;

            if (c?.DropoffOrder == null)
                return (feasible, current, worstLateTrip, worstLateMinutes);

            for (int i = 0; i < c.DropoffOrder.Count; i++)
            {
                double legSec = i < c.DropoffLegSeconds.Count ? c.DropoffLegSeconds[i] : 0;
                current = current.Add(TimeSpan.FromSeconds(legSec));

                int tripIdx = c.DropoffOrder[i];
                var trip = c.Trips[tripIdx];
                DropWindow(trip, out TimeSpan sched, out _, out TimeSpan latest);

                if (sched <= TimeSpan.Zero && latest <= TimeSpan.Zero)
                    continue;

                if (!FitsDropWindow(trip, current))
                {
                    if (sched > TimeSpan.Zero && current > latest)
                    {
                        double overrun = (current - sched).TotalMinutes;
                        if (overrun > worstLateMinutes)
                        {
                            worstLateMinutes = overrun;
                            worstLateTrip = tripIdx;
                        }
                    }
                    feasible = false;
                }

                current = AfterDropoff(trip, current);
            }

            return (feasible, current, worstLateTrip, worstLateMinutes);
        }

        private static double PickupLegSecondsOrEstimate(SupeyTripCluster c, int step)
        {
            if (c.PickupLegSeconds != null && step < c.PickupLegSeconds.Count && c.PickupLegSeconds[step] > 0)
                return c.PickupLegSeconds[step];
            if (c.PickupOrder == null || c.PickupOrder.Count <= 1)
                return 0;
            double head = SupeyDeskScheduleTiming.HeadPickupSecondsForFeasibility(c);
            if (head <= 0 && c.IntraClusterDriveSeconds > c.TailDriveSeconds)
                head = c.IntraClusterDriveSeconds - c.TailDriveSeconds;
            return head / (c.PickupOrder.Count - 1);
        }
    }
}
