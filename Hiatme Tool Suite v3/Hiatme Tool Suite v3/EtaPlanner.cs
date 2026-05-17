using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Builds a chain of physical stops the driver still has to make today (drop offs for riders
    /// already on board, then pickups + dropoffs for scheduled trips, in scheduled-time order),
    /// then asks <see cref="AddressGeocoder"/> + <see cref="RouteEstimator"/> for ideal driving
    /// times along that chain. The result is a per-trip ETA the UI can stamp on each card.
    /// </summary>
    /// <remarks>
    /// We deliberately do NOT try to re-optimize the driver's manifest — drivers re-sequence in
    /// the field constantly, and our only signal is scheduled time. Time-sort is a defensible
    /// "if the driver follows the schedule" estimate; it's clearly labeled as approximate in
    /// the UI so dispatch can sanity-check against the live position on the map.
    /// </remarks>
    internal static class EtaPlanner
    {
        /// <summary>Per-stop dwell time we pad into the chain (loading/unloading is rarely instant).</summary>
        private static readonly TimeSpan ServicePerStop = TimeSpan.FromMinutes(3);

        public enum FailReason
        {
            None,
            NoAddress,
            ChainBroken,
            RoutingUnavailable,
        }

        public sealed class EtaInfo
        {
            /// <summary>Wall-clock time we expect the driver to arrive at this trip's relevant stop.</summary>
            public DateTime EstimatedLocalTime;
            /// <summary>Total time-from-now to that stop (driving + service buffers along the way).</summary>
            public TimeSpan FromNow;
            /// <summary>Label that makes sense for the trip's phase ("Pickup ETA" or "Dropoff ETA").</summary>
            public string Label;
        }

        public sealed class Result
        {
            /// <summary>Trip key → ETA. Use <see cref="GetTripKey"/> to look up a card's trip.</summary>
            public Dictionary<string, EtaInfo> EtaByTrip { get; } =
                new Dictionary<string, EtaInfo>(StringComparer.Ordinal);

            /// <summary>Trip key → why we couldn't compute an ETA. Same key scheme as <see cref="EtaByTrip"/>.</summary>
            public Dictionary<string, FailReason> FailureByTrip { get; } =
                new Dictionary<string, FailReason>(StringComparer.Ordinal);

            public int ComputedCount;
            public int SkippedCount;
            /// <summary>Human-readable summary for the toast/status line, or empty.</summary>
            public string DiagnosticMessage = "";
        }

        /// <summary>
        /// Stable identity key for a trip across UI controls and planner output. Falls back through
        /// uuid → trip number → hash so two cards never collide.
        /// </summary>
        public static string GetTripKey(WRDownloadedTrip trip)
        {
            if (trip == null) return "null";
            if (!string.IsNullOrWhiteSpace(trip.TripUUID)) return "uuid:" + trip.TripUUID.Trim();
            if (!string.IsNullOrWhiteSpace(trip.TripNumber)) return "num:" + trip.TripNumber.Trim();
            return "ref:" + trip.GetHashCode().ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Estimate per-trip arrival times. Caller passes the driver's current GPS, the trips
        /// assigned to that driver for the day, and a cancellation token (the form cancels on
        /// close so a long Nominatim queue doesn't fire the callback after dispose).
        /// </summary>
        public static async Task<Result> EstimateAsync(
            GeoPoint driverPosition,
            DateTime nowLocal,
            IEnumerable<WRDownloadedTrip> driverTrips,
            CancellationToken token = default)
        {
            var result = new Result();
            if (driverTrips == null) return result;

            // 1. Build the chronological stop list. Each row knows whether it's a "target" — i.e.
            //    a card we'll stamp an ETA on. Non-target rows are still part of the chain (so
            //    the driver's drive time accumulates correctly) but they don't get a UI badge.
            var stops = new List<StopRow>();
            foreach (var trip in driverTrips)
            {
                if (trip == null) continue;
                var (phase, _, _) = TripPhaseClassifier.Classify(trip);
                switch (phase)
                {
                    case TripPhase.Completed:
                    case TripPhase.Cancelled:
                    case TripPhase.AtDropoff:
                        // AtDropoff: driver is at the DO right now, dropoff in flight; no further stop
                        // for this trip's chain. Same reasoning for completed/cancelled — done.
                        continue;

                    case TripPhase.Scheduled:
                        // Both PU and DO still need to happen. PU is the dispatch-relevant target.
                        AddIfTimed(stops, trip, StopKind.Pickup, isTarget: true,
                            label: "Pickup ETA",
                            timeStr: trip.PUTime, street: trip.PUStreet, city: trip.PUCity);
                        AddIfTimed(stops, trip, StopKind.Dropoff, isTarget: false,
                            label: "Dropoff ETA",
                            timeStr: trip.DOTime, street: trip.DOStreet, city: trip.DOCITY);
                        break;

                    case TripPhase.OnBoard:
                    case TripPhase.AtPickup:
                    case TripPhase.Unknown:
                        // Rider is in the vehicle (or about to be); the only stop left for this
                        // trip is the dropoff, and that's the dispatch-relevant ETA.
                        AddIfTimed(stops, trip, StopKind.Dropoff, isTarget: true,
                            label: "Dropoff ETA",
                            timeStr: trip.DOTime, street: trip.DOStreet, city: trip.DOCITY);
                        break;
                }
            }

            if (stops.Count == 0)
            {
                result.DiagnosticMessage = "No upcoming stops for this driver.";
                return result;
            }

            stops.Sort((a, b) => a.SortTime.CompareTo(b.SortTime));

            // 2. Geocode every stop (cached + rate-limited). The first failure ends the routable
            //    prefix — beyond that we can't compute ETAs because we don't know where the
            //    driver detours to between resolved stops.
            var resolved = new GeoPoint?[stops.Count];
            int firstFailureIdx = -1;
            for (int i = 0; i < stops.Count; i++)
            {
                if (token.IsCancellationRequested) return result;
                resolved[i] = await AddressGeocoder.ResolveAsync(stops[i].Street, stops[i].City, token).ConfigureAwait(false);
                if (!resolved[i].HasValue && firstFailureIdx < 0)
                {
                    firstFailureIdx = i;
                }
            }

            int chainEnd = firstFailureIdx < 0 ? stops.Count : firstFailureIdx;

            // 3. If the chain has no resolvable prefix at all, bail with a useful diagnostic.
            if (chainEnd == 0)
            {
                MarkAllAfter(stops, 0, FailReason.NoAddress, result);
                result.DiagnosticMessage = "Could not geocode the next stop's address.";
                return result;
            }

            // 4. Single OSRM call for the resolvable prefix.
            var coords = new List<GeoPoint>(chainEnd + 1) { driverPosition };
            for (int i = 0; i < chainEnd; i++) coords.Add(resolved[i].Value);

            var routeResult = await RouteEstimator.GetCumulativeDurationsAsync(coords, token).ConfigureAwait(false);
            if (!routeResult.Ok)
            {
                MarkAllAfter(stops, 0, FailReason.RoutingUnavailable, result);
                // Surface the actual reason so the user sees "HTTP 414 — too long" or
                // "OSRM throttled us" instead of a generic "service unavailable".
                result.DiagnosticMessage = routeResult.ErrorMessage ?? "Routing service is unavailable.";
                return result;
            }
            var cumulative = routeResult.Durations;

            // 5. Stamp ETAs on the routable, target-flagged stops. Service buffer is added per
            //    upstream stop the driver has to pass through (j prior stops × 3 min dwell).
            for (int j = 0; j < chainEnd; j++)
            {
                var stop = stops[j];
                if (!stop.IsTarget) continue;

                double driveSeconds = cumulative[j];
                double bufferSeconds = j * ServicePerStop.TotalSeconds;
                double total = driveSeconds + bufferSeconds;

                string key = GetTripKey(stop.Trip);
                result.EtaByTrip[key] = new EtaInfo
                {
                    EstimatedLocalTime = nowLocal.AddSeconds(total),
                    FromNow = TimeSpan.FromSeconds(total),
                    Label = stop.Label,
                };
                result.ComputedCount++;
            }

            // 6. Mark anything past the chain break as "couldn't route" so the UI can label it.
            if (firstFailureIdx >= 0)
            {
                MarkAllAfter(stops, firstFailureIdx, FailReason.ChainBroken, result);
                if (string.IsNullOrEmpty(result.DiagnosticMessage))
                    result.DiagnosticMessage = "Some addresses couldn't be geocoded; later trips skipped.";
            }

            return result;
        }

        private static void MarkAllAfter(List<StopRow> stops, int fromIdx, FailReason reason, Result result)
        {
            for (int i = fromIdx; i < stops.Count; i++)
            {
                var stop = stops[i];
                if (!stop.IsTarget) continue;
                string key = GetTripKey(stop.Trip);
                if (result.EtaByTrip.ContainsKey(key)) continue; // already computed (shouldn't happen, but safe)
                result.FailureByTrip[key] = reason;
                result.SkippedCount++;
            }
        }

        private static void AddIfTimed(List<StopRow> stops, WRDownloadedTrip trip, StopKind kind,
            bool isTarget, string label, string timeStr, string street, string city)
        {
            // Stops with no time anchor still get added so they don't drop out of the chain entirely
            // — but they sort to the bottom so they don't pollute the early ordering.
            DateTime sortTime;
            if (!DateTime.TryParse(timeStr, CultureInfo.CurrentCulture,
                    DateTimeStyles.None, out sortTime))
            {
                sortTime = DateTime.MaxValue;
            }
            stops.Add(new StopRow
            {
                Trip = trip,
                Kind = kind,
                Street = street,
                City = city,
                SortTime = sortTime,
                IsTarget = isTarget,
                Label = label,
            });
        }

        private enum StopKind { Pickup, Dropoff }

        private sealed class StopRow
        {
            public WRDownloadedTrip Trip;
            public StopKind Kind;
            public string Street;
            public string City;
            public DateTime SortTime;
            public bool IsTarget;
            public string Label;
        }
    }
}
