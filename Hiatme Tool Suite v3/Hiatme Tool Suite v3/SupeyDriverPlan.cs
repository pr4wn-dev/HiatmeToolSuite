using System;
using System.Collections.Generic;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Travel between two known points in a driver's day — home -&gt; first cluster start, cluster
    /// end -&gt; next cluster start, last cluster end -&gt; home. Includes the snap-to-street polyline
    /// when OSRM cooperated; falls back to a straight line when it didn't.
    /// </summary>
    internal sealed class SupeyDeadHeadSegment
    {
        public GeoPoint From { get; set; }
        public GeoPoint To { get; set; }
        public double DistanceMeters { get; set; }
        public double DurationSeconds { get; set; }
        public List<GeoPoint> Polyline { get; } = new List<GeoPoint>();
        public bool IsStraightLineFallback { get; set; }

        /// <summary>Human-readable label, e.g. "Home → Group 1" or "Group 2 → Group 3".</summary>
        public string Label { get; set; } = "";
    }

    /// <summary>
    /// Everything needed to render one driver's day in the preview UI: the assigned clusters
    /// (chronological), the dead-head connectors, totals, and any warnings the algorithm raised.
    /// Callers also get a <see cref="ReleaseTimeOfDay"/> so the dispatcher can sort by "who can
    /// go home first" — which is the optimization target the user explicitly called out.
    /// </summary>
    internal sealed class SupeyDriverPlan
    {
        public SupeyDriverProfile Driver { get; set; }
        public GeoPoint? HomeGeo { get; set; }

        /// <summary>Clusters in pickup order. May be empty (driver got nothing this build).</summary>
        public List<SupeyTripCluster> Groups { get; } = new List<SupeyTripCluster>();

        /// <summary>
        /// Connectors before / between / after groups. Always one more than <c>Groups.Count</c>
        /// when populated: <c>Home→g[0]</c>, <c>g[i].end→g[i+1].start</c> for each pair, and
        /// <c>g[last].end→Home</c>. Empty when no groups are assigned.
        /// </summary>
        public List<SupeyDeadHeadSegment> DeadHeads { get; } = new List<SupeyDeadHeadSegment>();

        public TimeSpan? FirstPickup { get; set; }
        public TimeSpan? LastDropoff { get; set; }

        /// <summary>"Done at home" time = LastDropoff + travel time of final dead-head back to home.</summary>
        public TimeSpan? ReleaseTimeOfDay { get; set; }

        /// <summary>Total miles for the day (intra-cluster + dead-head). Stored as meters, formatted on render.</summary>
        public double TotalMeters { get; set; }

        /// <summary>Total drive seconds for the day (intra-cluster + dead-head).</summary>
        public double TotalDriveSeconds { get; set; }

        public List<SupeyWarning> Warnings { get; } = new List<SupeyWarning>();

        public int RiderCount
        {
            get
            {
                int n = 0;
                foreach (var g in Groups) n += g.RiderCount;
                return n;
            }
        }
    }
}
