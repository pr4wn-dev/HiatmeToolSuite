using System;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>Maps trip endpoints to 1-based stop numbers along a cluster's driven route.</summary>
    internal static class SupeyRouteStopNumbers
    {
        /// <summary>
        /// Stop order follows the optimized tour: <see cref="SupeyTripCluster.PickupOrder"/>
        /// then <see cref="SupeyTripCluster.DropoffOrder"/> from <see cref="SupeyClusterRouting.OptimizeClusterTour"/>.
        /// </summary>
        internal static int ForEndpoint(SupeyTripCluster cluster, bool isPickup, int tripIndex)
        {
            if (cluster == null || tripIndex < 0)
                return 0;

            int nPu = cluster.PickupOrder.Count > 0
                ? cluster.PickupOrder.Count
                : cluster.PickupPoints.Count;
            int nDo = cluster.DropoffOrder.Count > 0
                ? cluster.DropoffOrder.Count
                : cluster.DropoffPoints.Count;

            if (isPickup)
            {
                for (int step = 0; step < nPu; step++)
                {
                    int idx = cluster.PickupOrder.Count > 0 ? cluster.PickupOrder[step] : step;
                    if (idx == tripIndex)
                        return step + 1;
                }
                return tripIndex + 1;
            }

            for (int step = 0; step < nDo; step++)
            {
                int idx = cluster.DropoffOrder.Count > 0 ? cluster.DropoffOrder[step] : step;
                if (idx == tripIndex)
                    return nPu + step + 1;
            }
            return nPu + tripIndex + 1;
        }

        internal static int TotalStops(SupeyTripCluster cluster)
        {
            if (cluster == null) return 0;
            int nPu = cluster.PickupOrder.Count > 0
                ? cluster.PickupOrder.Count
                : cluster.PickupPoints.Count;
            int nDo = cluster.DropoffOrder.Count > 0
                ? cluster.DropoffOrder.Count
                : cluster.DropoffPoints.Count;
            return nPu + nDo;
        }
    }
}
