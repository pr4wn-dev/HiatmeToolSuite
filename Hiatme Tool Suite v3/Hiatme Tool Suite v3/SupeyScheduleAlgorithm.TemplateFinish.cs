using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Hiatme_Tool_Suite_v3
{
    internal sealed partial class SupeyScheduleAlgorithm
    {
        /// <summary>
        /// Weekday templates first (desk row order), then Supey assigns leftovers using the same
        /// desk person/place timing rules as a scratch <see cref="BuildAsync"/> run.
        /// </summary>
        public async Task<SupeyScheduleResult> BuildTemplateThenFinishAsync(
            DateTime serviceDate,
            IList<MCDownloadedTrip> trips,
            IList<SupeyDriverProfile> drivers,
            SupeyTemplateMatchResult match,
            IDictionary<string, string> userLocks,
            IProgress<string> progress,
            CancellationToken token)
        {
            var result = new SupeyScheduleResult { ServiceDate = serviceDate.Date };
            if (FrequentRiders == null)
                FrequentRiders = SupeyFrequentRiders.Load();
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
            await OsrmBootstrap.EnsureForBuildAsync(HiatmeAiSettings.Load(), progress, token)
                .ConfigureAwait(false);
            SupeyOsrmLegs.BeginBuildSession();

            progress?.Report("Template + finish: geocoding…");
            var tripGeo = await GeocodeTripsAndDriversAsync(trips, drivers, result, progress, token)
                .ConfigureAwait(false);

            var planByDriver = new Dictionary<string, SupeyDriverPlan>(StringComparer.OrdinalIgnoreCase);
            foreach (var d in drivers)
            {
                token.ThrowIfCancellationRequested();
                if (d == null || string.IsNullOrWhiteSpace(d.Name)) continue;
                var plan = new SupeyDriverPlan { Driver = d };
                var homeGeo = await ResolveDriverHomeAsync(d, result, token).ConfigureAwait(false);
                if (homeGeo.HasValue)
                    plan.HomeGeo = homeGeo.Value;
                planByDriver[d.Name] = plan;
                result.DriverPlans.Add(plan);
            }

            var assigned = new HashSet<MCDownloadedTrip>();
            progress?.Report("Template + finish: applying template locks…");

            if (match?.OrderedSlotsByRosterDriver != null)
            {
                foreach (var kv in match.OrderedSlotsByRosterDriver)
                {
                    token.ThrowIfCancellationRequested();
                    if (!planByDriver.TryGetValue(kv.Key, out var plan)) continue;
                    var slots = kv.Value;
                    if (slots == null || slots.Count == 0) continue;
                    plan.TemplateDisplaySlots = new List<SupeyTemplateSlot>(slots);

                    SupeyTripCluster currentCluster = null;
                    int groupNum = plan.Groups.Count;

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

                    for (int gi = 0; gi < plan.Groups.Count; gi++)
                    {
                        var cluster = plan.Groups[gi];
                        SyncClusterMetadataFromTrips(cluster);
                        await SupeyClusterRouting.OptimizeClusterTourAsync(cluster, token).ConfigureAwait(false);
                        await PopulateClusterPolylineAsync(cluster, token).ConfigureAwait(false);
                    }
                }
            }

            int templatePlaced = assigned.Count;
            progress?.Report("Template locks placed " + templatePlaced + " trip(s); finishing remainder…");

            await AssignRemainingTripsForFinishAsync(
                result, trips, tripGeo, assigned, progress, token).ConfigureAwait(false);

            progress?.Report("Template + finish: sequencing all drivers…");
            int seqDone = 0;
            foreach (var plan in result.DriverPlans)
            {
                token.ThrowIfCancellationRequested();
                if (plan.Groups.Count == 0) continue;
                await SequenceDriverAsync(plan, token).ConfigureAwait(false);
                EvaluateWarnings(plan);
                seqDone++;
                progress?.Report("Sequenced " + seqDone + " driver(s) with trips…");
            }

            foreach (var t in trips)
            {
                if (assigned.Contains(t)) continue;
                if (result.Reserves.Contains(t) || result.ReservesReroute.Contains(t)
                    || result.ReservesWillCalls.Contains(t))
                    continue;
                SupeyReserveBuckets.AddToReserves(result, t);
            }

            progress?.Report("Template + finish build complete.");
            return result;
        }

        private async Task AssignRemainingTripsForFinishAsync(
            SupeyScheduleResult result,
            IList<MCDownloadedTrip> trips,
            Dictionary<MCDownloadedTrip, SupeyTripGeo> tripGeo,
            HashSet<MCDownloadedTrip> assigned,
            IProgress<string> progress,
            CancellationToken token)
        {
            var routable = new List<MCDownloadedTrip>();
            foreach (var t in trips)
            {
                if (assigned.Contains(t)) continue;
                if (result.Reserves.Contains(t) || result.ReservesReroute.Contains(t)
                    || result.ReservesWillCalls.Contains(t))
                    continue;
                if (!tripGeo.TryGetValue(t, out var g) || !g.Complete) continue;
                if (!SupeyTripTimes.TryParsePU(t).HasValue) continue;
                routable.Add(t);
            }

            if (routable.Count == 0)
            {
                progress?.Report("No remaining trips to assign.");
                return;
            }

            var driverPlans = result.DriverPlans;
            var drivers = new List<SupeyDriverProfile>();
            foreach (var p in driverPlans)
                if (p?.Driver != null) drivers.Add(p.Driver);

            int capacityFloor = ResolveCapacityFloor(drivers);
            var hintsForCluster = UseTemplateHints ? Hints : null;

            progress?.Report("Finish: clustering " + routable.Count + " remaining trip(s)…");
            var clusters = await ClusterTripsAsync(routable, tripGeo, capacityFloor, token, hintsForCluster)
                .ConfigureAwait(false);
            for (int i = 0; i < clusters.Count; i++)
            {
                clusters[i].GroupNumber = i + 1;
                clusters[i].GroupColor = SupeyGroupPalette.For(i + 1);
            }

            progress?.Report("Finish: routing " + clusters.Count + " new group(s)…");
            foreach (var c in clusters)
            {
                token.ThrowIfCancellationRequested();
                SyncClusterMetadataFromTrips(c);
                await SupeyClusterRouting.OptimizeClusterTourAsync(c, token).ConfigureAwait(false);
                await PopulateClusterPolylineAsync(c, token).ConfigureAwait(false);
            }

            int beforeHub = clusters.Count;
            clusters = SupeyClusterRouting.MergeHouseholdClusters(clusters, capacityFloor);
            clusters = await ApplyMorningHubMergesAsync(clusters, capacityFloor, token).ConfigureAwait(false);
            if (clusters.Count != beforeHub)
            {
                foreach (var c in clusters)
                {
                    SyncClusterMetadataFromTrips(c);
                    await SupeyClusterRouting.OptimizeClusterTourAsync(c, token).ConfigureAwait(false);
                    await PopulateClusterPolylineAsync(c, token).ConfigureAwait(false);
                }
            }

            clusters = await SupeyClusterRouting.SplitInefficientClustersAsync(clusters, token)
                .ConfigureAwait(false);
            foreach (var c in clusters)
            {
                SyncClusterMetadataFromTrips(c);
                await SupeyClusterRouting.OptimizeClusterTourAsync(c, token).ConfigureAwait(false);
                await PopulateClusterPolylineAsync(c, token).ConfigureAwait(false);
            }

            var remaining = new List<SupeyTripCluster>(clusters);
            double avgRiders = AverageRiderLoad(driverPlans);
            remaining.Sort(CompareClustersForCoveragePriority);

            progress?.Report("Finish: assigning remaining groups (desk timing)…");
            await AssignMorningHubWavesAsync(remaining, driverPlans, avgRiders, token).ConfigureAwait(false);
            foreach (var cluster in remaining.ToArray())
            {
                token.ThrowIfCancellationRequested();
                if (!remaining.Contains(cluster)) continue;
                await TryAssignClusterAsync(cluster, remaining, driverPlans, result, progress, token, splitDepth: 0)
                    .ConfigureAwait(false);
            }

            await PolishAssignmentsAsync(driverPlans, token).ConfigureAwait(false);
            await ImproveCoverageAsync(result, driverPlans, tripGeo, capacityFloor, hintsForCluster, progress, token)
                .ConfigureAwait(false);
            await ConsolidateAsync(driverPlans, token).ConfigureAwait(false);

            foreach (var plan in driverPlans)
            {
                RenumberDriverGroups(plan);
                foreach (var g in plan.Groups)
                    foreach (var t in g.Trips)
                        assigned.Add(t);
            }

            progress?.Report("Finish: placed " + (assigned.Count) + " total trip(s) on drivers.");
        }

        private static void RenumberDriverGroups(SupeyDriverPlan plan)
        {
            if (plan?.Groups == null) return;
            for (int i = 0; i < plan.Groups.Count; i++)
            {
                plan.Groups[i].GroupNumber = i + 1;
                plan.Groups[i].GroupColor = SupeyGroupPalette.For(i + 1);
            }
        }
    }
}
