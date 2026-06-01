using System;
using System.Collections.Generic;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Top-level output of one Supey schedule build. Hands the UI everything it needs to render
    /// the preview ListView, the map workspace, the per-driver stats, and the warnings modal,
    /// plus a <see cref="Locks"/> dictionary that subsequent Rebuilds will respect.
    /// </summary>
    /// <remarks>
    /// Locks are <c>tripNumber → driverName</c> entries created when the user manually moves a
    /// trip via the context menu or drag-and-drop. The next Rebuild treats each locked trip as
    /// a hard pre-assignment, falling back to "violate the lock and warn loudly" only if honoring
    /// it would break a hard constraint (capacity overrun, impossible deadline, etc).
    /// </remarks>
    internal sealed class SupeyScheduleResult
    {
        public DateTime BuiltAtLocal { get; set; } = DateTime.Now;
        public DateTime ServiceDate { get; set; }

        /// <summary>Drivers actually selected for this build, in the order they were checked in the roster.</summary>
        public List<SupeyDriverPlan> DriverPlans { get; } = new List<SupeyDriverPlan>();

        /// <summary>Trips that need a driver (unassigned, missing geo on assignable legs, etc).</summary>
        public List<MCDownloadedTrip> Reserves { get; } = new List<MCDownloadedTrip>();

        /// <summary>Will-call trips (PU time 00:00) — held in reserves, not auto-assigned.</summary>
        public List<MCDownloadedTrip> ReservesWillCalls { get; } = new List<MCDownloadedTrip>();

        /// <summary>Out-of-service-area trips — reroute to Modivcare; not auto-assigned on BUILD.</summary>
        public List<MCDownloadedTrip> ReservesReroute { get; } = new List<MCDownloadedTrip>();

        /// <summary>Template BUILD metadata for mode-aware status lines (null when templates not used).</summary>
        public SupeyTemplateBuildMeta TemplateBuild { get; set; }

        /// <summary>Toolbar/settings at BUILD time — included in warnings paste for AI review.</summary>
        public SupeyBuildOptionsSnapshot BuildOptions { get; set; }

        /// <summary>All reserve buckets combined (for counts and clipboard).</summary>
        public int TotalReserveCount => Reserves.Count + ReservesWillCalls.Count + ReservesReroute.Count;

        /// <summary>Build-level warnings that aren't tied to a specific driver (e.g. a roster home that won't geocode).</summary>
        public List<SupeyWarning> BuildWarnings { get; } = new List<SupeyWarning>();

        /// <summary>True when one or more driver days were rejected after full-shift simulation (trips moved to reserves).</summary>
        public bool HasInfeasibleDriverRejection { get; set; }

        /// <summary>Drivers whose on-screen plan was quarantined — day could not meet PU/DO windows in order.</summary>
        public List<string> InfeasibleDriverNames { get; } = new List<string>();

        /// <summary>Trip numbers that already received a reserve failure warning (dedupes Pass C re-cluster).</summary>
        internal HashSet<string> ReserveWarnedTripNumbers { get; } =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Manual locks honored by this build: trip number → driver name. Survives across rebuilds.</summary>
        public Dictionary<string, string> Locks { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Total active driving seconds across the whole fleet (intra-group + dead-head).</summary>
        public double FleetActiveSeconds
        {
            get
            {
                double sum = 0;
                foreach (var p in DriverPlans) sum += p.TotalDriveSeconds;
                return sum;
            }
        }

        public double FleetMeters
        {
            get
            {
                double sum = 0;
                foreach (var p in DriverPlans) sum += p.TotalMeters;
                return sum;
            }
        }

        public int WarningCount => SupeyWarningsUtil.CountUnique(this);

        /// <summary>Earliest <see cref="SupeyDriverPlan.ReleaseTimeOfDay"/> across the fleet (null if none).</summary>
        public TimeSpan? EarliestRelease
        {
            get
            {
                TimeSpan? earliest = null;
                foreach (var p in DriverPlans)
                {
                    if (!p.ReleaseTimeOfDay.HasValue) continue;
                    if (!earliest.HasValue || p.ReleaseTimeOfDay.Value < earliest.Value)
                        earliest = p.ReleaseTimeOfDay;
                }
                return earliest;
            }
        }
    }
}
