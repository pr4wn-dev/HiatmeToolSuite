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
    /// Cluster size is capped at the smallest <see cref="SupeyDriverProfile.CapacityPassengers"/>
    /// among the rostered drivers, so we never form a group that is impossible to serve. A trip
    /// that doesn't fit any cluster ends up in a singleton cluster of its own.
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

        /// <summary>Hardest DO appointment across the cluster — used as the deadline that scoring must beat.</summary>
        public System.TimeSpan HardestDropoff { get; set; }

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
    }
}
