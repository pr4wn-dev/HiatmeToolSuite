using System;
using System.Collections.Generic;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Contract for historical-memory hints used by Suggest Driver ranking.
    /// Ranking-only: feasibility gates remain deterministic and cannot be overridden by this payload.
    /// </summary>
    internal sealed class ScheduleBuilderHistoricalHints
    {
        public string ServiceDate { get; set; } = "";
        public DateTime RetrievedUtc { get; set; } = DateTime.UtcNow;
        public List<ScheduleBuilderHistoricalTripHint> TripHints { get; set; } =
            new List<ScheduleBuilderHistoricalTripHint>();
        public List<ScheduleBuilderHistoricalDriverHint> DriverHints { get; set; } =
            new List<ScheduleBuilderHistoricalDriverHint>();
    }

    internal sealed class ScheduleBuilderHistoricalTripHint
    {
        public string TripNumber { get; set; } = "";
        public string ClientName { get; set; } = "";
        public List<string> PreferredDrivers { get; set; } = new List<string>();
        public string Rationale { get; set; } = "";
        public double Confidence01 { get; set; }
    }

    internal sealed class ScheduleBuilderHistoricalDriverHint
    {
        public string DriverName { get; set; } = "";
        public int ObservedDays { get; set; }
        public int ObservedTrips { get; set; }
        public List<string> FrequentClients { get; set; } = new List<string>();
        public List<string> TypicalWaveKeys { get; set; } = new List<string>();
    }

    internal sealed class ScheduleBuilderHistoricalHintQuery
    {
        public string ServiceDate { get; set; } = "";
        public string Weekday { get; set; } = "";
        public string TripNumber { get; set; } = "";
        public string ClientName { get; set; } = "";
        public int LimitDays { get; set; } = 30;
    }
}
