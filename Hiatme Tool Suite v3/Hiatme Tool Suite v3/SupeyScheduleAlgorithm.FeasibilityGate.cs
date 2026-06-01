using System;
using System.Collections.Generic;

namespace Hiatme_Tool_Suite_v3
{
    internal sealed partial class SupeyScheduleAlgorithm
    {
        internal static (bool feasible, TimeSpan end, int worstTripIdx, double worstMinutes)
            ProjectClusterFeasibilityPublic(SupeyTripCluster c, TimeSpan arrivalAtFirstPU) =>
            ProjectClusterFeasibility(c, arrivalAtFirstPU);

        internal static double LegPuLateCapMinutesPublic(SupeyTripCluster cluster) =>
            LegPuLateCapMinutes(cluster);

        internal static GeoPoint FirstPickupGeoPublic(SupeyTripCluster c) => FirstPickupGeo(c);
    }
}
