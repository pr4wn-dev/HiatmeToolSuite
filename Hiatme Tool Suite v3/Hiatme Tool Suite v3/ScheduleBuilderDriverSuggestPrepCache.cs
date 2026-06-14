using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>Reuses intra-group OSRM leg metrics when a group's trip set + tour order is unchanged.</summary>
    internal sealed class ScheduleBuilderDriverSuggestPrepCache
    {
        private readonly ConcurrentDictionary<string, ClusterLegMetrics> _byFingerprint =
            new ConcurrentDictionary<string, ClusterLegMetrics>(StringComparer.Ordinal);

        internal static string Fingerprint(SupeyTripCluster g)
        {
            if (g?.Trips == null || g.Trips.Count == 0)
                return "";

            var tripPart = string.Join("|",
                g.Trips.Select(t => (t?.TripNumber ?? "").Trim()));
            var pu = g.PickupOrder != null ? string.Join(",", g.PickupOrder) : "";
            var dof = g.DropoffOrder != null ? string.Join(",", g.DropoffOrder) : "";
            return tripPart + "@" + pu + "/" + dof;
        }

        internal bool TryApply(SupeyTripCluster g)
        {
            string fp = Fingerprint(g);
            if (fp.Length == 0)
                return false;
            if (!_byFingerprint.TryGetValue(fp, out var m))
                return false;

            g.PickupLegSeconds.Clear();
            g.PickupLegSeconds.AddRange(m.PickupLegSeconds);
            g.DropoffLegSeconds.Clear();
            g.DropoffLegSeconds.AddRange(m.DropoffLegSeconds);
            g.IntraClusterMeters = m.IntraClusterMeters;
            g.IntraClusterDriveSeconds = m.IntraClusterDriveSeconds;
            g.TailDriveSeconds = m.TailDriveSeconds;
            g.IsStraightLineFallback = m.IsStraightLineFallback;
            return true;
        }

        internal void Store(SupeyTripCluster g)
        {
            string fp = Fingerprint(g);
            if (fp.Length == 0)
                return;

            _byFingerprint[fp] = new ClusterLegMetrics
            {
                PickupLegSeconds = g.PickupLegSeconds.ToList(),
                DropoffLegSeconds = g.DropoffLegSeconds.ToList(),
                IntraClusterMeters = g.IntraClusterMeters,
                IntraClusterDriveSeconds = g.IntraClusterDriveSeconds,
                TailDriveSeconds = g.TailDriveSeconds,
                IsStraightLineFallback = g.IsStraightLineFallback,
            };
        }

        internal async Task PrewarmDriverGroupsAsync(
            IList<SupeyTripCluster> groups,
            Dictionary<string, GeoPoint> pickupByTrip,
            Dictionary<string, GeoPoint> dropoffByTrip,
            CancellationToken token)
        {
            if (groups == null)
                return;

            foreach (var g in groups)
            {
                if (g == null)
                    continue;
                ScheduleBuilderPreviewGroups.ApplyGeocodes(g, pickupByTrip, dropoffByTrip);
                await ScheduleBuilderDriverSuggestRouting.PrepareClusterForFeasibilityAsync(
                    g, this, token).ConfigureAwait(false);
            }
        }

        private sealed class ClusterLegMetrics
        {
            public List<double> PickupLegSeconds { get; set; } = new List<double>();
            public List<double> DropoffLegSeconds { get; set; } = new List<double>();
            public double IntraClusterMeters { get; set; }
            public double IntraClusterDriveSeconds { get; set; }
            public double TailDriveSeconds { get; set; }
            public bool IsStraightLineFallback { get; set; }
        }
    }

    internal static class ScheduleBuilderDriverSuggestRouting
    {
        private static readonly SemaphoreSlim OsrmTableGate = new SemaphoreSlim(1, 1);

        internal static async Task PrepareClusterForFeasibilityAsync(
            SupeyTripCluster c,
            ScheduleBuilderDriverSuggestPrepCache prepCache,
            CancellationToken token)
        {
            if (c == null)
                return;

            SupeyClusterRouting.ApplyManualEditTour(c);

            if (c.Trips.Count >= 2)
                ApplySuggestFeasibilityTour(c);

            if (prepCache != null && prepCache.TryApply(c))
                return;

            c.PickupLegSeconds.Clear();
            c.DropoffLegSeconds.Clear();
            c.IntraClusterMeters = 0;
            c.IntraClusterDriveSeconds = 0;
            c.TailDriveSeconds = 0;

            if (c.Trips.Count >= 2)
            {
                await OsrmTableGate.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    if (await SupeyClusterOsrmTable.TryBindClusterAsync(c, token).ConfigureAwait(false))
                    {
                        if (SupeyClusterOsrmTable.Current?.TryApplyTourMetrics(c) == true)
                        {
                            prepCache?.Store(c);
                            return;
                        }
                    }
                }
                finally
                {
                    SupeyClusterOsrmTable.Clear();
                    OsrmTableGate.Release();
                }
            }

            if (c.PickupOrder.Count > 1)
            {
                for (int step = 1; step < c.PickupOrder.Count; step++)
                {
                    var from = GetTripPickup(c, c.PickupOrder[step - 1]);
                    var to = GetTripPickup(c, c.PickupOrder[step]);
                    var leg = await SupeyOsrmLegs.GetLegAsync(from, to, token).ConfigureAwait(false);
                    double sec = leg.Seconds > 0 ? leg.Seconds : EstimateLegSeconds(from, to);
                    c.PickupLegSeconds.Add(sec);
                    c.IntraClusterMeters += leg.Meters > 0 ? leg.Meters : StraightMeters(from, to);
                }
            }

            if (c.DropoffOrder.Count > 0)
            {
                GeoPoint from = c.PickupOrder.Count > 0
                    ? GetTripPickup(c, c.PickupOrder[c.PickupOrder.Count - 1])
                    : default;
                for (int i = 0; i < c.DropoffOrder.Count; i++)
                {
                    var to = GetTripDropoff(c, c.DropoffOrder[i]);
                    var leg = await SupeyOsrmLegs.GetLegAsync(from, to, token).ConfigureAwait(false);
                    double sec = leg.Seconds > 0 ? leg.Seconds : EstimateLegSeconds(from, to);
                    c.DropoffLegSeconds.Add(sec);
                    c.IntraClusterMeters += leg.Meters > 0 ? leg.Meters : StraightMeters(from, to);
                    from = to;
                }
            }

            if (c.IntraClusterMeters <= 0 && c.Trips.Count == 1)
            {
                var pu = GetTripPickup(c, 0);
                var dof = GetTripDropoff(c, 0);
                c.IntraClusterMeters = StraightMeters(pu, dof);
            }

            prepCache?.Store(c);
        }

        private static void ApplySuggestFeasibilityTour(SupeyTripCluster c)
        {
            if (c == null || c.Trips.Count < 2)
                return;

            var puOrder = Enumerable.Range(0, c.Trips.Count)
                .OrderBy(i => SupeyTripTimes.TryParsePU(c.Trips[i]) ?? TimeSpan.MaxValue)
                .ThenBy(i => (c.Trips[i]?.TripNumber ?? "").Trim(), StringComparer.OrdinalIgnoreCase)
                .ToList();
            SupeyClusterRouting.ApplyOrdersPublic(
                c, puOrder, SupeyClusterRouting.BuildDeadlineDropoffOrderPublic(c));
        }

        private static GeoPoint GetTripPickup(SupeyTripCluster c, int tripIdx)
        {
            if (tripIdx >= 0 && tripIdx < c.PickupPoints.Count)
                return c.PickupPoints[tripIdx];
            return default;
        }

        private static GeoPoint GetTripDropoff(SupeyTripCluster c, int tripIdx)
        {
            if (tripIdx >= 0 && tripIdx < c.DropoffPoints.Count)
                return c.DropoffPoints[tripIdx];
            return default;
        }

        private static double EstimateLegSeconds(GeoPoint from, GeoPoint to) =>
            StraightMeters(from, to) / 12.0;

        private static double StraightMeters(GeoPoint a, GeoPoint b)
        {
            if (!SupeyOsrmLegs.IsRoutable(a) || !SupeyOsrmLegs.IsRoutable(b))
                return 0;
            const double R = 6371000;
            double dLat = (b.Lat - a.Lat) * Math.PI / 180;
            double dLng = (b.Lng - a.Lng) * Math.PI / 180;
            double x = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                + Math.Cos(a.Lat * Math.PI / 180) * Math.Cos(b.Lat * Math.PI / 180)
                * Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
            return R * 2 * Math.Atan2(Math.Sqrt(x), Math.Sqrt(1 - x)) * 1.25;
        }
    }
}
