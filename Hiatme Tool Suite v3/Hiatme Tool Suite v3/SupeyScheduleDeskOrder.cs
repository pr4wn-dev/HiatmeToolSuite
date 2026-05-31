using System;
using System.Collections.Generic;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>Clock-based PU row order inside clusters for UI / export / feasibility.</summary>
    internal static class SupeyScheduleDeskOrder
    {
        /// <summary>Sort trip rows by scheduled PU only (does not change OSRM visit order).</summary>
        internal static void ApplyRowSortToPlan(SupeyDriverPlan plan)
        {
            if (plan?.Groups == null) return;
            foreach (var g in plan.Groups)
            {
                if (g == null || g.Trips.Count == 0) continue;
                if (g.Trips.Count == 1)
                {
                    SupeyClusterRouting.ApplyOrdersForSingleTrip(g);
                    continue;
                }
                SupeyTripClusterGeo.PadListsToTripCount(g);
                SupeyClusterDisplayOrder.ReorderTripRowsToPickupVisitOrder(g);
            }
        }

        internal static void ApplyToPlan(SupeyDriverPlan plan)
        {
            ApplyRowSortToPlan(plan);
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
