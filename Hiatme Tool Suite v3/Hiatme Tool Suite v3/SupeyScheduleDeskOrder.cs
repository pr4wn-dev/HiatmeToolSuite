using System;
using System.Collections.Generic;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>Row layout: PU-time desk sort before routing; road visit order after routing.</summary>
    internal static class SupeyScheduleDeskOrder
    {
        /// <summary>Sort non-routed trip rows by scheduled PU (before OSRM builds PickupOrder).</summary>
        internal static void ApplyDeskRowSortToPlan(SupeyDriverPlan plan)
        {
            if (plan?.Groups == null) return;
            foreach (var g in plan.Groups)
            {
                if (g == null || g.Trips.Count == 0) continue;
                if (g.RoadTourOptimized && SupeyClusterRouting.IsValidVisitOrder(g.PickupOrder, g.Trips.Count))
                    continue;
                if (g.Trips.Count == 1)
                {
                    SupeyClusterRouting.ApplyOrdersForSingleTrip(g);
                    continue;
                }
                SupeyTripClusterGeo.PadListsToTripCount(g);
                if (!SupeyClusterDisplayOrder.TripsRowsInPuOrder(g)
                    || SupeyClusterTimeSplit.NeedsChronologicalPickup(g))
                    SupeyClusterDisplayOrder.ReorderTripRowsToPickupVisitOrder(g);
            }
        }

        /// <summary>Align trip rows with road PickupOrder so list, export, and route notes match.</summary>
        internal static void SyncDisplayRowsToRoadOrder(SupeyDriverPlan plan)
        {
            if (plan?.Groups == null) return;
            foreach (var g in plan.Groups)
            {
                if (g == null || g.Trips.Count < 2) continue;
                if (!g.RoadTourOptimized
                    && !SupeyClusterRouting.IsValidVisitOrder(g.PickupOrder, g.Trips.Count))
                    continue;
                SupeyClusterDisplayOrder.ReorderTripRowsToPickupVisitOrder(g);
            }
        }

        internal static void ApplyToPlan(SupeyDriverPlan plan)
        {
            ApplyDeskRowSortToPlan(plan);
            SyncDisplayRowsToRoadOrder(plan);
            if (plan?.Groups == null) return;
            foreach (var g in plan.Groups)
            {
                if (g == null || g.Trips.Count < 2) continue;
                EnsureRoutingOrders(g);
            }
        }

        /// <summary>Sync dropoff deadline order only — pickup visit order comes from OSRM tour.</summary>
        internal static void EnsureRoutingOrders(SupeyTripCluster g)
        {
            int n = g.Trips.Count;
            if (n <= 1) return;
            var dof = SupeyClusterRouting.BuildDeadlineDropoffOrderPublic(g);
            g.DropoffOrder.Clear();
            g.DropoffOrder.AddRange(dof);
        }
    }
}
