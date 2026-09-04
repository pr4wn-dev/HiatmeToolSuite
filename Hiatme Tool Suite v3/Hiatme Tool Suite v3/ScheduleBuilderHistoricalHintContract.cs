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

        /// <summary>Corridor this trip runs, as "pu city|do city". Empty when unknown.</summary>
        public string Corridor { get; set; } = "";

        /// <summary>
        /// The whole fleet runs this leg late, so its scheduled travel time is short.
        /// No driver choice fixes that, so outcome history on this corridor says less
        /// about the driver than usual.
        /// </summary>
        public bool CorridorUnderbuilt { get; set; }

        /// <summary>
        /// Whether placing this trip on each driver would be named late. Ranking only;
        /// never shown as a builder warning. Empty when the server had no model or the
        /// request failed — ranking then works as it did before.
        /// </summary>
        public Dictionary<string, ScheduleBuilderForecastCall> ForecastByDriver { get; set; } =
            new Dictionary<string, ScheduleBuilderForecastCall>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Server placement ranks for this trip on each driver. Only populated when the
        /// trust gate says ready; otherwise Suggest Driver ranks as it did before.
        /// </summary>
        public bool PlacementReady { get; set; }

        public Dictionary<string, ScheduleBuilderPlacementRank> PlacementByDriver { get; set; } =
            new Dictionary<string, ScheduleBuilderPlacementRank>(StringComparer.OrdinalIgnoreCase);
    }

    internal sealed class ScheduleBuilderForecastCall
    {
        public string DriverName { get; set; } = "";
        public double PredictedLate { get; set; }
        public bool Called { get; set; }
        public List<string> Why { get; set; } = new List<string>();
    }

    internal sealed class ScheduleBuilderPlacementRank
    {
        public string DriverName { get; set; } = "";
        public int Rank { get; set; }
        public bool Feasible { get; set; } = true;
        public double Cost { get; set; }
        public List<string> Reasons { get; set; } = new List<string>();
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

        /// <summary>
        /// How this pairing has gone against what it should have, from -1 to +1.
        /// Positive means this driver does better on this leg than their own record
        /// and the leg's difficulty predict; negative means worse.
        ///
        /// This is deliberately not "how good is this driver". Every driver works a
        /// full day, so a score that only ranks drivers cannot change who gets a trip,
        /// it can only move work off the slower van onto someone else. The server has
        /// already removed each driver's own baseline, so what is left trades legs
        /// between drivers rather than starving one.
        ///
        /// Already shrunk toward zero on thin samples, so a two-trip history reads
        /// near zero rather than as a strong opinion.
        /// </summary>
        public double CorridorAffinity { get; set; }
        public int CorridorTrips { get; set; }

        /// <summary>Same measure, for this driver carrying this client.</summary>
        public double ClientAffinity { get; set; }
        public int ClientTrips { get; set; }

        /// <summary>
        /// Whether outcome history exists at all for this driver on this pairing. When
        /// false the ranker falls back to counting past runs, which is what it did
        /// before outcomes were available.
        /// </summary>
        public bool HasOutcomeHistory => CorridorTrips > 0 || ClientTrips > 0;
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
