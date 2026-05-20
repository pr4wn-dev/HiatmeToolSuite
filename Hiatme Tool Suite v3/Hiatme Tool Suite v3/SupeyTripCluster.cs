using System.Collections.Generic;
using System.Drawing;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// One natural ride-share cluster: trips that pick up close together at close-enough times
    /// and drop off close together. Built in algorithm phase 2 (clustering) and refined in phase
    /// 3 (fingerprinting), then each cluster is scored as a unit against every driver.
    /// </summary>
    /// <remarks>
    /// Cluster size is capped at the largest rostered driver capacity (see
    /// <c>ResolveCapacityFloor</c> in <see cref="SupeyScheduleAlgorithm"/>). A trip that
    /// doesn't fit any cluster ends up in a singleton cluster of its own.
    /// </remarks>
    internal sealed class SupeyTripCluster
    {
        /// <summary>1-based group number; matches the swatch color shown in the preview / map.</summary>
        public int GroupNumber { get; set; }

        /// <summary>Cached palette color for this group — consistent across List, map, legend.</summary>
        public Color GroupColor { get; set; }

        public List<MCDownloadedTrip> Trips { get; } = new List<MCDownloadedTrip>();

        /// <summary>Geocoded PU points, in <see cref="Trips"/> order.</summary>
        public List<GeoPoint> PickupPoints { get; } = new List<GeoPoint>();

        /// <summary>Geocoded DO points, in <see cref="Trips"/> order.</summary>
        public List<GeoPoint> DropoffPoints { get; } = new List<GeoPoint>();

        /// <summary>Earliest scheduled PU time across the cluster — used to seed greedy assignment order.</summary>
        public System.TimeSpan EarliestPickup { get; set; }

        /// <summary>
        /// Latest scheduled PU time across the cluster. The driver almost always waits at this
        /// PU's location until the rider's pickup window opens (or arrives 29 min into the
        /// A-leg early-pickup window), so this — not <see cref="EarliestPickup"/> — is what
        /// determines when the drop-off leg actually starts.
        /// </summary>
        public System.TimeSpan LatestPickup { get; set; }

        /// <summary>Hardest DO appointment across the cluster — used as the deadline that scoring must beat.</summary>
        public System.TimeSpan HardestDropoff { get; set; }

        /// <summary>
        /// Set during fingerprinting. <c>true</c> iff every trip in the cluster is an A-leg —
        /// the only leg type the website's scoreboard lets the driver pick up early
        /// (<c>docs/TRIP_TIMING_RULES.md</c>). The dispatcher leans on this 29-min early window
        /// to compress otherwise-impossible morning loads, so the scheduler models it the same way.
        /// </summary>
        public bool IsAllALeg { get; set; }

        /// <summary>
        /// Drive seconds along the tail of the in-cluster route — the segment from the LAST PU
        /// through every DO to the final drop. Pulled from OSRM's per-leg durations during
        /// fingerprinting (or a proportional fallback when OSRM is unreachable).
        /// </summary>
        /// <remarks>
        /// Cluster-end time = max(arrival_at_last_PU, effective_last_pickup) + this. When the PU
        /// times span a wide window, the driver waits at the later PUs; only the tail drive
        /// actually consumes calendar time after that wait.
        /// </remarks>
        public double TailDriveSeconds { get; set; }

        /// <summary>
        /// Trip indices (into <see cref="Trips"/>) for pickup sequence after tour optimization.
        /// When empty, pickups are visited in <see cref="Trips"/> list order (PU-time sorted).
        /// </summary>
        public System.Collections.Generic.List<int> PickupOrder { get; }
            = new System.Collections.Generic.List<int>();

        /// <summary>
        /// Trip indices (into <see cref="Trips"/>) sorted by drop-off deadline — earliest
        /// deadline first, then refined by drive-distance when feasible. The driver drives
        /// through <see cref="PickupOrder"/> then these dropoffs.
        /// </summary>
        public System.Collections.Generic.List<int> DropoffOrder { get; }
            = new System.Collections.Generic.List<int>();

        /// <summary>Facility merge key used during clustering (A-leg = DO hub, B/C = PU hub).</summary>
        public string FacilityMergeKey { get; set; } = "";

        /// <summary>
        /// Per-leg drive seconds along the dropoff portion of the tour, aligned 1:1 with
        /// <see cref="DropoffOrder"/>. <c>DropoffLegSeconds[0]</c> = drive from the last PU
        /// to the first dropped rider; subsequent entries are drive between consecutive
        /// dropoffs. Lets feasibility checks compute when each rider is actually dropped
        /// off (instead of forcing the whole cluster to finish by the earliest deadline).
        /// </summary>
        public System.Collections.Generic.List<double> DropoffLegSeconds { get; }
            = new System.Collections.Generic.List<double>();

        /// <summary>
        /// Earliest moment the cluster can effectively begin when the dispatcher uses the A-leg
        /// early-pickup allowance: 29 min before <see cref="EarliestPickup"/> for all-A-leg
        /// clusters, otherwise the scheduled <see cref="EarliestPickup"/> (no early pickups
        /// permitted on B/C legs).
        /// </summary>
        public System.TimeSpan EffectiveEarliestPickup =>
            IsAllALeg ? EarliestPickup.Subtract(System.TimeSpan.FromMinutes(29.0)) : EarliestPickup;

        /// <summary>
        /// Symmetric to <see cref="EffectiveEarliestPickup"/> for the last rider — the earliest
        /// the driver can pick the last rider up and start the drop-off run.
        /// </summary>
        public System.TimeSpan EffectiveLatestPickup =>
            IsAllALeg ? LatestPickup.Subtract(System.TimeSpan.FromMinutes(29.0)) : LatestPickup;

        /// <summary>Centroid of <see cref="PickupPoints"/> (lat/lng mean). Used by home-affinity scoring.</summary>
        public GeoPoint PickupCentroid { get; set; }

        /// <summary>Centroid of <see cref="DropoffPoints"/>. Used by home-affinity scoring.</summary>
        public GeoPoint DropoffCentroid { get; set; }

        /// <summary>OSRM snap-to-street polyline through the in-cluster sequence (PU1, PU2, ..., DO1, DO2, ...).</summary>
        public List<GeoPoint> RoutePolyline { get; } = new List<GeoPoint>();

        /// <summary>Total in-cluster travel seconds (sum of leg durations).</summary>
        public double IntraClusterDriveSeconds { get; set; }

        /// <summary>Total in-cluster travel meters.</summary>
        public double IntraClusterMeters { get; set; }

        /// <summary>True when OSRM failed and we built the polyline by drawing straight lines.</summary>
        public bool IsStraightLineFallback { get; set; }

        /// <summary>Number of riders in the cluster (= <see cref="Trips"/>.Count). Used by capacity scoring.</summary>
        public int RiderCount => Trips.Count;

        /// <summary>
        /// Diagnostic tally of why each driver was rejected when this cluster was scored. Populated
        /// only when scoring fails to find a driver — surfaces in the Reserves warning so the user
        /// can see whether a cluster failed for capacity, shift, lateness, or DO-infeasibility
        /// reasons rather than a generic "no feasible driver". Cleared and rebuilt on every scoring
        /// attempt; harmless to leave populated even on a successful assignment.
        /// </summary>
        public SupeyClusterRejectionTally Rejections { get; }
            = new SupeyClusterRejectionTally();
    }

    /// <summary>
    /// Counts (and names) the drivers rejected for a given cluster, broken out by the constraint
    /// that disqualified them. Used to format a meaningful Reserves warning ("Group 6 — 5/5
    /// rejected: 3 shift-start, 2 PU late") instead of a flat "no feasible driver".
    /// </summary>
    internal sealed class SupeyClusterRejectionTally
    {
        public List<string> Capacity { get; } = new List<string>();
        public List<string> ShiftStart { get; } = new List<string>();
        public List<string> ShiftEnd { get; } = new List<string>();
        public List<string> PuLate { get; } = new List<string>();
        public List<string> DoInfeasible { get; } = new List<string>();
        public List<string> TimeConflict { get; } = new List<string>();
        public List<string> PolicyAvoid { get; } = new List<string>();

        /// <summary>
        /// Free-form note attached to the most-recent DO-infeasibility — names the specific rider
        /// whose deadline was missed, so the warning can say "Group 12 — DO infeasible for all
        /// drivers (TANCREL late by 4 min)" instead of just "DO infeasible".
        /// </summary>
        public string LateRiderNote { get; set; }

        public int TotalRejections =>
            Capacity.Count + ShiftStart.Count + ShiftEnd.Count
            + PuLate.Count + DoInfeasible.Count + TimeConflict.Count + PolicyAvoid.Count;

        public void Clear()
        {
            Capacity.Clear(); ShiftStart.Clear(); ShiftEnd.Clear();
            PuLate.Clear(); DoInfeasible.Clear(); TimeConflict.Clear();
            PolicyAvoid.Clear();
            LateRiderNote = null;
        }

        /// <summary>
        /// Formats the rejection breakdown into a single human-readable string for the warning.
        /// Returns the empty string if nothing was rejected (caller should fall back to the
        /// generic message).
        /// </summary>
        public string FormatBreakdown()
        {
            if (TotalRejections == 0) return "";
            var sb = new System.Text.StringBuilder();
            void Append(string label, List<string> names)
            {
                if (names.Count == 0) return;
                if (sb.Length > 0) sb.Append("; ");
                sb.Append(names.Count).Append(' ').Append(label);
                if (names.Count <= 3)
                {
                    sb.Append(" (").Append(string.Join(", ", names)).Append(')');
                }
            }
            Append("capacity", Capacity);
            Append("shift-start", ShiftStart);
            Append("shift-end", ShiftEnd);
            Append("PU late", PuLate);
            Append("time-conflict", TimeConflict);
            Append("DO infeasible", DoInfeasible);
            Append("policy avoid", PolicyAvoid);
            if (!string.IsNullOrEmpty(LateRiderNote))
                sb.Append(" — ").Append(LateRiderNote);
            return sb.ToString();
        }
    }
}
