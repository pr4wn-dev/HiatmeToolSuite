using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Sole writer for <see cref="SupeyTripCluster.PickupOrder"/> during BUILD / post-build routing.
    /// Dispatch doctrine (whole group, every rider):
    /// <list type="number">
    ///   <item><b>Road order first</b> — visit pickups/drops along the drive (no passing someone to
    ///         hit a sheet time, then doubling back).</item>
    ///   <item><b>Windows, not clock sort</b> — each PU/DO must land in early/late window; timing
    ///         flexes within the window (on-time, a few min early/late) so the chain works.</item>
    ///   <item><b>Shortest feasible OSRM tour</b> — among road orders that satisfy all windows,
    ///         pick shortest miles (see <see cref="SupeyDispatchDriveClock"/>).</item>
    /// </list>
    /// Desk row sort is display-only; it does not define the road tour.
    /// </summary>
    internal static class SupeyClusterRouteBuilder
    {
        public const string PipelineTag = "ClusterRouteBuilder-v28";

        /// <summary>
        /// Force visit order by appointment clock only when PUs are this far apart in the same town
        /// (e.g. Greene 6:45 before Lewiston 7:40). Closer stops use drive-through + windows only.
        /// </summary>
        public const int PickupPrecedenceMinutes = 20;

        public static Task ApplyRoadRouteAsync(
            SupeyTripCluster cluster,
            CancellationToken token,
            GeoPoint? vanPositionBeforeGroup) =>
            SupeyClusterRouting.ApplyClusterRoadRouteInternalAsync(
                cluster, token, vanPositionBeforeGroup);
    }
}
