using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>OSRM-backed drive legs and pickup-sequence checks for schedule BUILD.</summary>
    internal sealed partial class SupeyScheduleAlgorithm
    {
        private readonly struct DriveLeg
        {
            public readonly double Seconds;
            public readonly double Meters;
            public readonly bool FromOsrm;

            public DriveLeg(double seconds, double meters, bool fromOsrm)
            {
                Seconds = seconds;
                Meters = meters;
                FromOsrm = fromOsrm;
            }

            public static DriveLeg Unavailable => new DriveLeg(0, 0, false);
        }

        private static async Task<DriveLeg> GetDriveLegAsync(GeoPoint from, GeoPoint to, CancellationToken token)
        {
            var leg = await SupeyOsrmLegs.GetLegAsync(from, to, token).ConfigureAwait(false);
            return leg.Ok ? new DriveLeg(leg.Seconds, leg.Meters, true) : DriveLeg.Unavailable;
        }

        private async Task<bool> EnsureClusterOsrmAsync(SupeyTripCluster cluster, CancellationToken token)
        {
            if (cluster == null || cluster.Trips.Count == 0) return false;

            if (cluster.PickupOrder.Count == 0 || cluster.DropoffOrder.Count == 0)
                await SupeyClusterRouting.OptimizeClusterTourAsync(cluster, token).ConfigureAwait(false);

            await PopulateClusterPolylineAsync(cluster, token).ConfigureAwait(false);
            return cluster.Trips.Count == 1
                || (!cluster.IsStraightLineFallback && cluster.IntraClusterDriveSeconds > 0);
        }

        private async Task<bool> IsPickupSequenceFeasibleAsync(SupeyTripCluster c, CancellationToken token)
        {
            if (c == null || c.Trips.Count <= 1) return true;
            if (c.PickupPoints == null || c.PickupPoints.Count < c.Trips.Count)
                return false;

            var order = SupeyClusterRouting.IsValidVisitOrder(c.PickupOrder, c.Trips.Count)
                ? new List<int>(c.PickupOrder)
                : SupeyClusterRouting.BuildPickupOrderByPuTimePublic(c);
            if (order.Count == 0) return false;

            int firstIdx = order[0];
            var firstPu = SupeyDeskScheduleTiming.ScheduledPickupForBuild(c.Trips[firstIdx]);
            if (firstPu == TimeSpan.Zero)
                firstPu = SupeyTripTimes.TryParsePU(c.Trips[firstIdx]) ?? c.EarliestPickup;

            var current = firstPu;
            GeoPoint pos = c.PickupPoints[firstIdx];

            for (int step = 1; step < order.Count; step++)
            {
                int idx = order[step];
                var leg = await GetDriveLegAsync(pos, c.PickupPoints[idx], token).ConfigureAwait(false);
                if (!leg.FromOsrm) return false;

                current = current.Add(TimeSpan.FromSeconds(leg.Seconds));
                var scheduled = SupeyDeskScheduleTiming.ScheduledPickupForBuild(c.Trips[idx]);
                if (scheduled == TimeSpan.Zero)
                    scheduled = SupeyTripTimes.TryParsePU(c.Trips[idx]) ?? c.EarliestPickup;

                if (current < scheduled)
                    current = scheduled;

                double puCap = LegPuLateCapMinutes(c) + 2.0;
                if (current > scheduled.Add(TimeSpan.FromMinutes(puCap)))
                    return false;

                pos = c.PickupPoints[idx];
            }

            return true;
        }

        private async Task<List<SupeyTripCluster>> EnforcePickupSequenceFeasibilityAsync(
            List<SupeyTripCluster> clusters,
            CancellationToken token)
        {
            if (clusters == null || clusters.Count == 0)
                return clusters ?? new List<SupeyTripCluster>();

            var output = new List<SupeyTripCluster>();
            foreach (var c in clusters)
            {
                token.ThrowIfCancellationRequested();
                if (c.Trips.Count <= 1)
                {
                    output.Add(c);
                    continue;
                }

                await EnsureClusterOsrmAsync(c, token).ConfigureAwait(false);
                if (await IsPickupSequenceFeasibleAsync(c, token).ConfigureAwait(false))
                {
                    output.Add(c);
                    continue;
                }

                var parts = SupeyClusterRouting.SplitClusterForAssignment(c);
                if (parts.Count <= 1)
                {
                    output.Add(c);
                    continue;
                }

                foreach (var part in parts)
                {
                    SyncClusterMetadataFromTrips(part);
                    await SupeyClusterRouting.OptimizeClusterTourAsync(part, token).ConfigureAwait(false);
                    await PopulateClusterPolylineAsync(part, token).ConfigureAwait(false);
                    output.Add(part);
                }
            }

            return output;
        }

        private async Task<List<SupeyTripCluster>> ApplyMorningHubMergesAsync(
            List<SupeyTripCluster> clusters,
            int capacityFloor,
            CancellationToken token)
        {
            var list = clusters;
            foreach (string hub in new[] { "FALCON", "646", "MINOT", "CROSS", "MANLEY" })
                list = SupeyClusterRouting.MergeMorningHubClusters(list, capacityFloor, hub);

            return await EnforcePickupSequenceFeasibilityAsync(list, token).ConfigureAwait(false);
        }

        private async Task<DriveLeg> GetDeadheadToClusterAsync(
            SupeyDriverPlan plan,
            SupeyTripCluster cluster,
            CancellationToken token)
        {
            var (_, loc) = await ProjectedLastEventAsync(plan, token).ConfigureAwait(false);
            int firstPu = FirstPickupIndex(cluster);
            if (firstPu < 0 || firstPu >= cluster.PickupPoints.Count)
                return DriveLeg.Unavailable;
            return await GetDriveLegAsync(loc, cluster.PickupPoints[firstPu], token).ConfigureAwait(false);
        }
    }
}
