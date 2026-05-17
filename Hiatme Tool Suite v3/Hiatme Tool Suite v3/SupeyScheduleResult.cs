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

        /// <summary>Trips that the algorithm couldn't place (missing geo, no feasible driver, etc).</summary>
        public List<MCDownloadedTrip> Reserves { get; } = new List<MCDownloadedTrip>();

        /// <summary>Build-level warnings that aren't tied to a specific driver (e.g. a roster home that won't geocode).</summary>
        public List<SupeyWarning> BuildWarnings { get; } = new List<SupeyWarning>();

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

        public int WarningCount
        {
            get
            {
                int n = BuildWarnings.Count;
                foreach (var p in DriverPlans) n += p.Warnings.Count;
                return n;
            }
        }

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
