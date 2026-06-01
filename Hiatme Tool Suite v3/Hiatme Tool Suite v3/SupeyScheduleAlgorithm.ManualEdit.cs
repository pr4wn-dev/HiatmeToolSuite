using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Hiatme_Tool_Suite_v3
{
    internal sealed partial class SupeyScheduleAlgorithm
    {
        internal void FingerprintClusterPublic(SupeyTripCluster c) => FingerprintCluster(c);

        /// <summary>
        /// Updates cluster metadata without re-sorting <see cref="SupeyTripCluster.Trips"/>.
        /// Used after manual drag so preview line order is preserved for routing.
        /// </summary>
        internal void SyncClusterMetadataPublic(SupeyTripCluster c) => SyncClusterMetadataFromTrips(c);

        private static void SyncClusterMetadataFromTrips(SupeyTripCluster c)
        {
            if (c == null || c.Trips.Count == 0) return;

            c.PickupCentroid = Centroid(c.PickupPoints);
            c.DropoffCentroid = Centroid(c.DropoffPoints);

            TimeSpan? earliest = null;
            TimeSpan? latest = null;
            bool allA = true;
            foreach (var t in c.Trips)
            {
                var pu = SupeyDeskScheduleTiming.ScheduledPickupForBuild(t);
                if (pu > TimeSpan.Zero)
                {
                    if (!earliest.HasValue || pu < earliest.Value) earliest = pu;
                    if (!latest.HasValue || pu > latest.Value) latest = pu;
                }
                if (DetectLeg(t.TripNumber) != 'A') allA = false;
            }
            if (earliest.HasValue) c.EarliestPickup = earliest.Value;
            if (latest.HasValue) c.LatestPickup = latest.Value;
            c.IsAllALeg = allA;
        }

        internal async Task PopulateClusterPolylinePublicAsync(SupeyTripCluster c, CancellationToken token) =>
            await PopulateClusterPolylineAsync(c, token).ConfigureAwait(false);

        internal async Task SequenceDriverPublicAsync(SupeyDriverPlan plan, CancellationToken token) =>
            await SequenceDriverAsync(plan, token).ConfigureAwait(false);

        /// <summary>Corridor + PU/DO window feasibility — same ordering used during BUILD assign.</summary>
        internal async Task OrderDriverDayGroupsPublicAsync(SupeyDriverPlan plan, CancellationToken token)
        {
            if (plan?.Groups == null || plan.Groups.Count < 2) return;
            await OrderDriverGroupsCorridorAsync(plan, token).ConfigureAwait(false);
            SupeyClusterTimeSplit.RenumberGroupsPublic(plan);
        }

        internal void EvaluateWarningsAndTimingsPublic(SupeyDriverPlan plan)
        {
            plan.Warnings.Clear();
            plan.TripTimings.Clear();
            EvaluateWarnings(plan);
            PopulateTripTimings(plan);
        }

        private static void PopulateTripTimings(SupeyDriverPlan plan)
        {
            if (plan == null || plan.Groups.Count == 0) return;

            var shiftStart = plan.Driver.ParseShiftStart() ?? TimeSpan.Zero;
            var current = shiftStart;

            for (int i = 0; i < plan.Groups.Count; i++)
            {
                var g = plan.Groups[i];
                double dhSec = i < plan.DeadHeads.Count ? plan.DeadHeads[i].DurationSeconds : 0;
                var arrivalAtFirstPU = current.Add(TimeSpan.FromSeconds(dhSec));
                var startAtLastPU = ComputeStartAtLastPU(g, arrivalAtFirstPU);

                var visit = SupeyClusterDisplayOrder.PickupVisitIndices(g);
                var clock = arrivalAtFirstPU > g.EffectiveEarliestPickup
                    ? arrivalAtFirstPU : g.EffectiveEarliestPickup;
                for (int step = 0; step < visit.Count; step++)
                {
                    int tripIdx = visit[step];
                    if (tripIdx < 0 || tripIdx >= g.Trips.Count) continue;
                    string tn = (g.Trips[tripIdx].TripNumber ?? "").Trim();
                    if (tn.Length == 0) continue;

                    if (step > 0 && step < g.PickupLegSeconds.Count)
                        clock = clock.Add(TimeSpan.FromSeconds(g.PickupLegSeconds[step]));
                    plan.TripTimings[tn] = new SupeyTripProjectedTiming
                    {
                        EstPu = SupeyDispatchDriveClock.AfterPickup(g, tripIdx, clock)
                    };
                    clock = SupeyDispatchDriveClock.AfterPickup(g, tripIdx, clock);
                }

                var (_, groupEnd, _, _) = ProjectClusterFeasibility(g, arrivalAtFirstPU);
                current = groupEnd;
            }
        }

    }
}
