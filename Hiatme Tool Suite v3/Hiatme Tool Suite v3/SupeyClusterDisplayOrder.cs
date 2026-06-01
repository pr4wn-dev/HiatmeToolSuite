using System;

using System.Collections.Generic;



namespace Hiatme_Tool_Suite_v3

{

    /// <summary>

    /// Trip row order for grids, export, timing, and route notes — one visit sequence per cluster.

    /// After road routing, rows match <see cref="SupeyTripCluster.PickupOrder"/>; desk-only groups use PU time.

    /// </summary>

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



        /// <summary>Indices into <see cref="SupeyTripCluster.Trips"/> in dispatch visit order (pickups).</summary>

        public static IReadOnlyList<int> PickupVisitIndices(SupeyTripCluster c)

        {

            if (c == null || c.Trips.Count == 0)

                return Array.Empty<int>();

            return ResolvePickupVisit(c);

        }



        /// <summary>

        /// Physical row order for <see cref="SupeyTripCluster.Trips"/> / geo lists to match visit order.

        /// Remaps <see cref="SupeyTripCluster.PickupOrder"/> and <see cref="SupeyTripCluster.DropoffOrder"/> after moves.

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

            var visit = ResolvePickupVisit(c);

            if (visit.Count != n)

                return;



            if (RowsAlreadyInVisitOrder(c, visit))

            {

                NormalizeVisitOrdersAfterRowSync(c);

                return;

            }



            SupeyTripClusterGeo.PadListsToTripCount(c);



            var seen = new bool[n];

            for (int i = 0; i < n; i++)

            {

                int idx = visit[i];

                if (idx < 0 || idx >= n || seen[idx])

                    return;

                seen[idx] = true;

            }



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

            NormalizeVisitOrdersAfterRowSync(c);

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



        /// <summary>True when trip rows follow the same sequence as <paramref name="visit"/>.</summary>

        internal static bool RowsAlreadyInVisitOrder(SupeyTripCluster c, IReadOnlyList<int> visit)

        {

            if (c?.Trips == null || visit == null || c.Trips.Count != visit.Count)

                return false;

            for (int i = 0; i < visit.Count; i++)

            {

                if (visit[i] != i)

                    return false;

            }

            return true;

        }



        /// <summary>

        /// For road-routed groups: rows should be 0..n-1 and PickupOrder matches row walk.

        /// For desk groups: rows should be ascending PU time.

        /// </summary>

        internal static bool ClusterRowsNeedReorder(SupeyTripCluster g)

        {

            if (g?.Trips == null || g.Trips.Count < 2) return false;

            var visit = ResolvePickupVisit(g);

            return !RowsAlreadyInVisitOrder(g, visit);

        }



        private static List<int> ResolvePickupVisit(SupeyTripCluster c)

        {

            int n = c.Trips.Count;

            if (n <= 1)

                return new List<int> { 0 };



            if (SupeyClusterTimeSplit.NeedsChronologicalPickup(c))

                return PuTimeRowVisit(c);



            if (SupeyClusterRouting.IsValidVisitOrder(c.PickupOrder, n))

                return new List<int>(c.PickupOrder);



            if (!TripsRowsInPuOrder(c))

                return PuTimeRowVisit(c);



            var row = new List<int>(n);

            for (int i = 0; i < n; i++) row.Add(i);

            return row;

        }



        private static List<int> PuTimeRowVisit(SupeyTripCluster c)

        {

            int n = c.Trips.Count;

            return new List<int>(SupeyClusterRouting.BuildPickupOrderByPuTimePublic(c));

        }



        private static void NormalizeVisitOrdersAfterRowSync(SupeyTripCluster c)

        {

            int n = c.Trips.Count;

            if (n <= 1) return;

            if (SupeyClusterRouting.IsValidVisitOrder(c.PickupOrder, n)

                && RowsAlreadyInVisitOrder(c, c.PickupOrder))

            {

                var identity = new List<int>(n);

                for (int i = 0; i < n; i++) identity.Add(i);

                c.PickupOrder.Clear();

                c.PickupOrder.AddRange(identity);

            }

            SupeyClusterRouting.NormalizeVisitOrders(c);

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

    }

}


