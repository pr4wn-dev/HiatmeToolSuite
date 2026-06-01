using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Hiatme_Tool_Suite_v3
{
    internal sealed partial class SupeyScheduleAlgorithm
    {
        /// <summary>Template-only BUILD: locks + gaps from CSV order; remainder to reserves.</summary>
        public async Task<SupeyScheduleResult> BuildFromTemplateLocksAsync(
            DateTime serviceDate,
            IList<MCDownloadedTrip> trips,
            IList<SupeyDriverProfile> drivers,
            SupeyTemplateMatchResult match,
            IDictionary<string, string> userLocks,
            IProgress<string> progress,
            CancellationToken token)
        {
            var result = new SupeyScheduleResult { ServiceDate = serviceDate.Date };
            if (userLocks != null)
                foreach (var kv in userLocks)
                    if (!result.Locks.ContainsKey(kv.Key))
                        result.Locks[kv.Key] = kv.Value;
            if (match?.Locks != null)
                foreach (var kv in match.Locks)
                    if (!result.Locks.ContainsKey(kv.Key))
                        result.Locks[kv.Key] = kv.Value;

            if (match?.Warnings != null)
                result.BuildWarnings.AddRange(match.Warnings);

            trips = trips ?? new List<MCDownloadedTrip>();
            drivers = drivers ?? new List<SupeyDriverProfile>();

            progress?.Report("Template build: geocoding…");
            var tripGeo = await GeocodeTripsAndDriversAsync(trips, drivers, result, progress, token)
                .ConfigureAwait(false);

            var assigned = new HashSet<MCDownloadedTrip>();

            foreach (var d in drivers)
            {
                token.ThrowIfCancellationRequested();
                if (d == null || string.IsNullOrWhiteSpace(d.Name)) continue;
                if (match == null
                    || !match.OrderedSlotsByRosterDriver.TryGetValue(d.Name, out var slots)
                    || slots == null || slots.Count == 0)
                    continue;

                var plan = new SupeyDriverPlan { Driver = d };
                plan.TemplateDisplaySlots = new List<SupeyTemplateSlot>(slots);

                var homeGeo = await ResolveDriverHomeAsync(d, result, token).ConfigureAwait(false);
                if (homeGeo.HasValue)
                    plan.HomeGeo = homeGeo.Value;

                SupeyTripCluster currentCluster = null;
                int groupNum = 0;

                void FlushCluster()
                {
                    if (currentCluster == null || currentCluster.Trips.Count == 0)
                    {
                        currentCluster = null;
                        return;
                    }
                    groupNum++;
                    currentCluster.GroupNumber = groupNum;
                    currentCluster.GroupColor = SupeyGroupPalette.For(groupNum);
                    plan.Groups.Add(currentCluster);
                    currentCluster = null;
                }

                foreach (var slot in slots)
                {
                    if (slot.Kind == SupeyTemplateSlot.SlotKind.Gap)
                    {
                        FlushCluster();
                        continue;
                    }

                    if (!slot.IsMatched || slot.MatchedLiveTrip == null)
                        continue;

                    var t = slot.MatchedLiveTrip;
                    if (result.ReservesReroute.Contains(t)
                        || result.ReservesWillCalls.Contains(t)
                        || result.Reserves.Contains(t))
                        continue;

                    if (SupeyWillCallPickup.IsPickupWillCall(t))
                    {
                        SupeyReserveBuckets.AddToReserves(result, t);
                        continue;
                    }

                    if (!tripGeo.TryGetValue(t, out var g) || !g.Complete)
                    {
                        SupeyReserveBuckets.AddToReserves(result, t);
                        continue;
                    }

                    if (currentCluster == null)
                        currentCluster = new SupeyTripCluster();

                    currentCluster.Trips.Add(t);
                    currentCluster.PickupPoints.Add(g.Pickup.GetValueOrDefault());
                    currentCluster.DropoffPoints.Add(g.Dropoff.GetValueOrDefault());
                    assigned.Add(t);
                }

                FlushCluster();
                plan.TemplateSeedGroupCount = plan.Groups.Count;

                for (int gi = 0; gi < plan.Groups.Count; gi++)
                {
                    var cluster = plan.Groups[gi];
                    SyncClusterMetadataFromTrips(cluster);
                    await PopulateClusterPolylineAsync(cluster, token).ConfigureAwait(false);
                }

                if (plan.Groups.Count > 0)
                    result.DriverPlans.Add(plan);
            }

            progress?.Report("Template build: sequencing drivers…");
            int seqDone = 0;
            foreach (var plan in result.DriverPlans)
            {
                token.ThrowIfCancellationRequested();
                // Keep groups in template slot order (do not corridor-reorder).
                await SequenceDriverAsync(plan, token).ConfigureAwait(false);
                EvaluateWarnings(plan);
                seqDone++;
                progress?.Report("Sequenced " + seqDone + " / " + result.DriverPlans.Count + " driver(s)…");
            }

            foreach (var t in trips)
            {
                if (assigned.Contains(t)) continue;
                if (result.Reserves.Contains(t) || result.ReservesReroute.Contains(t)
                    || result.ReservesWillCalls.Contains(t))
                    continue;
                SupeyReserveBuckets.AddToReserves(result, t);
            }

            SupeyWillCallPickup.EnforceOnResult(result, trips);
            progress?.Report("Template build complete.");
            return result;
        }

        private async Task<GeoPoint?> ResolveDriverHomeAsync(
            SupeyDriverProfile d,
            SupeyScheduleResult result,
            CancellationToken token)
        {
            var p = await AddressGeocoder.ResolveWithFallbacksAsync(
                d.HomeStreet, d.HomeCity, d.HomeState, d.HomeZip, "us", token).ConfigureAwait(false);
            return p;
        }

        private async Task<Dictionary<MCDownloadedTrip, SupeyTripGeo>> GeocodeTripsAndDriversAsync(
            IList<MCDownloadedTrip> trips,
            IList<SupeyDriverProfile> drivers,
            SupeyScheduleResult result,
            IProgress<string> progress,
            CancellationToken token)
        {
            AddressGeocoder.ResetCounters();
            var tripGeo = new Dictionary<MCDownloadedTrip, SupeyTripGeo>();
            int done = 0;
            foreach (var t in trips)
            {
                token.ThrowIfCancellationRequested();
                string ooa = SupeyOutOfArea.MatchTrip(t);
                if (ooa != null)
                {
                    SupeyReserveBuckets.AddToReserves(result, t);
                    tripGeo[t] = new SupeyTripGeo();
                    continue;
                }

                var geo = new SupeyTripGeo
                {
                    Pickup = await AddressGeocoder.ResolveTripEndpointAsync(t.PUStreet, t.PUCity, token)
                        .ConfigureAwait(false),
                    Dropoff = await AddressGeocoder.ResolveTripEndpointAsync(t.DOStreet, t.DOCITY, token)
                        .ConfigureAwait(false),
                };
                tripGeo[t] = geo;
                done++;
                if ((done % 10) == 0)
                    progress?.Report("Geocoding " + done + " / " + trips.Count + "…");
            }

            return tripGeo;
        }
    }
}
