using System;
using System.Collections.Generic;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>Pickup-visit order for trip rows in UI, export, and driver sheets.</summary>
    internal static class SupeyClusterDisplayOrder
    {
        /// <summary>True when <see cref="SupeyTripCluster.Trips"/> row order matches ascending scheduled PU.</summary>
        internal static bool TripsRowsInPuOrder(SupeyTripCluster c)
        {
            if (c?.Trips == null || c.Trips.Count < 2) return true;
            TimeSpan prev = TimeSpan.MinValue;
            foreach (var t in c.Trips)
            {
                var pu = SupeyTripTimes.TryParsePU(t);
                if (!pu.HasValue) continue;
                if (prev != TimeSpan.MinValue && pu.Value < prev)
                    return false;
                prev = pu.Value;
            }
            return true;
        }

        /// <summary>Row indices in scheduled PU order (for list/export — not OSRM visit order).</summary>
        private static List<int> PuTimeRowVisit(SupeyTripCluster c)
        {
            int n = c.Trips.Count;
            return new List<int>(SupeyClusterRouting.BuildPickupOrderByPuTimePublic(c));
        }

        /// <summary>Indices into <see cref="SupeyTripCluster.Trips"/> for UI row walk (always clock PU when rows are sorted).</summary>
        public static IReadOnlyList<int> PickupVisitIndices(SupeyTripCluster c)
        {
            if (c == null || c.Trips.Count == 0)
                return Array.Empty<int>();

            int n = c.Trips.Count;
            if (!TripsRowsInPuOrder(c) || SupeyClusterTimeSplit.NeedsChronologicalPickup(c))
                return PuTimeRowVisit(c);

            var row = new List<int>(n);
            for (int i = 0; i < n; i++) row.Add(i);
            return row;
        }

        /// <summary>
        /// Puts <see cref="SupeyTripCluster.Trips"/> in scheduled PU order for grids/export.
        /// Remaps <see cref="SupeyTripCluster.PickupOrder"/> / <see cref="SupeyTripCluster.DropoffOrder"/> so OSRM legs stay valid.
        /// </summary>
        public static void ReorderTripRowsToPickupVisitOrder(SupeyTripCluster c)
        {
            if (c == null || c.Trips.Count <= 1)
            {
                if (c != null && c.Trips.Count == 1)
                    SupeyClusterRouting.ApplyOrdersForSingleTrip(c);
                return;
            }

            int n = c.Trips.Count;
            if (TripsRowsInPuOrder(c) && !SupeyClusterTimeSplit.NeedsChronologicalPickup(c))
                return;

            SupeyTripClusterGeo.PadListsToTripCount(c);

            var visit = PuTimeRowVisit(c);
            if (visit.Count != n)
                return;

            var seen = new bool[n];
            for (int i = 0; i < n; i++)
            {
                int idx = visit[i];
                if (idx < 0 || idx >= n || seen[idx])
                    return;
                seen[idx] = true;
            }

            bool identityVisit = true;
            for (int i = 0; i < n; i++)
            {
                if (visit[i] != i) { identityVisit = false; break; }
            }
            if (identityVisit)
                return;

            var newTrips = new List<MCDownloadedTrip>(n);
            var newPu = new List<GeoPoint>(n);
            var newDo = new List<GeoPoint>(n);
            for (int step = 0; step < n; step++)
            {
                int old = visit[step];
                newTrips.Add(c.Trips[old]);
                newPu.Add(old < c.PickupPoints.Count ? c.PickupPoints[old] : new GeoPoint(0, 0));
                newDo.Add(old < c.DropoffPoints.Count ? c.DropoffPoints[old] : new GeoPoint(0, 0));
            }

            var oldToNew = new int[n];
            for (int newRow = 0; newRow < n; newRow++)
                oldToNew[visit[newRow]] = newRow;

            c.Trips.Clear();
            c.Trips.AddRange(newTrips);
            c.PickupPoints.Clear();
            c.PickupPoints.AddRange(newPu);
            c.DropoffPoints.Clear();
            c.DropoffPoints.AddRange(newDo);

            RemapVisitOrderIndices(c.PickupOrder, oldToNew, n);
            RemapVisitOrderIndices(c.DropoffOrder, oldToNew, n);
            c.DropoffLegSeconds.Clear();
            c.PickupLegSeconds.Clear();
        }

        private static void RemapVisitOrderIndices(List<int> order, int[] oldToNew, int n)
        {
            if (order == null || order.Count != n || oldToNew == null || oldToNew.Length != n)
                return;
            var remapped = new List<int>(n);
            foreach (int oldIdx in order)
            {
                if (oldIdx < 0 || oldIdx >= n) return;
                remapped.Add(oldToNew[oldIdx]);
            }
            if (remapped.Count != n) return;
            order.Clear();
            order.AddRange(remapped);
        }

        public static void ReorderPlanTripRows(SupeyDriverPlan plan)
        {
            if (plan?.Groups == null) return;
            foreach (var g in plan.Groups)
            {
                if (g != null && g.Trips.Count > 0)
                    ReorderTripRowsToPickupVisitOrder(g);
            }
        }
    }
}
