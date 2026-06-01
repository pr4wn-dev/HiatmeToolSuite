using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Pure schedule-building heuristic for the Supey tab. Phases:
    /// 1. Geocode all trip PU/DO and selected driver homes.
    /// 2. Cluster trips into natural ride-share groups using time + PU/DO radius gates.
    /// 3. Fingerprint each cluster (centroids, deadlines, in-cluster polyline).
    /// 4. Score every (driver, cluster) pair under hard + soft constraints.
    /// 5. Greedy-assign clusters to lowest-cost feasible drivers (corridor + home bias).
    /// 5b. Pass C — swap groups for balance and retry reserve clusters as groups.
    /// 6. Order each driver's groups home → corridor → nearest-next (timing-feasible), then OSRM dead-heads.
    /// 7. Consolidation hill-climb — collapse late trips onto fewer drivers when it cuts fleet hours.
    /// 8. Reserves + warnings — anything unassigned plus per-driver feasibility re-checks.
    /// </summary>
    /// <remarks>
    /// Assignment, clustering, tours, and feasibility use OSRM legs (cached).
    /// straight-line fallback is display-only when OSRM is down.
    /// <para/>
    /// The user-stated optimization target is "minimize total fleet active-hours and miles, then
    /// release drivers as early as possible". We implement that via a <c>λ × activeWindowExtension</c>
    /// term — assigning a late cluster to a driver already working late is cheap; assigning it to
    /// an early driver is expensive, so late trips naturally consolidate.
    /// </remarks>
    internal sealed partial class SupeyScheduleAlgorithm
    {
        // PU/DO clustering gates, calibrated against real Hiatme dispatcher schedules from
        // 2026 (one Aaron-morning load: 6 riders picked up Greene/Lewiston/Auburn over 95
        // minutes, dropped at the Auburn dialysis clinic + a nearby Auburn appt). The
        // previous values caused that load to split into 6 solo trips.
        //
        // A-leg morning pattern (many PUs → one clinic): wide PU radius, tight DO radius.
        // B/C-leg afternoon pattern (one clinic → many homes): tight PU radius, wide DO
        // radius. Same numbers swapped — the geometry is symmetric. Without the leg-aware
        // DO radius the afternoon "single van leaves clinic, drops 4 people in different
        // towns" load can never cluster, because every home is > 4 mi from every other home.
        //
        // - PU radius: ~15.5 mi for A-leg / ~4 mi for B/C — A-leg sweeps a rural catchment
        //   into one clinic; B/C all leaves from the same clinic so the PU points are
        //   essentially identical.
        // - DO radius: ~4 mi for A-leg / ~15.5 mi for B/C — A-leg drops at a few clinics in
        //   the same metro area, B/C drops 4 riders across the same rural catchment they
        //   came from.
        // - Time window: 120 min — observed schedules cluster pickups spanning 1.5+ hours
        //   when they share destinations (early-bird dialysis pickups vs. mid-shift pickups).
        private const double PuClusterRadiusMetersALeg = 25000.0;
        private const double PuClusterRadiusMetersBcLeg = 6500.0;
        private const double DoClusterRadiusMetersALeg = 6500.0;
        private const double DoClusterRadiusMetersBcLeg = 25000.0;
        private const double ClusterTimeWindowMinutes = 120.0;
        internal static double ClusterTimeWindowMinutesPublic => ClusterTimeWindowMinutes;

        // Inside the radius gates, score candidate clusters with destination-dominant
        // weighting. Two trips going to the same clinic from 5 miles apart should cluster
        // before two trips picking up next door but going to different towns. Weight 3x makes
        // DO proximity outvote PU spread when both are within their gates.
        private const double DoScoringWeight = 3.0;

        // Tight-arrival threshold — under 5 min of slack to the appointment fires a warning.
        private const double TightArrivalSlackMinutes = 5.0;

        // Average street speed for the haversine-based cost matrix. ~30 mph in m/s.
        private const double AverageStreetSpeedMps = 13.4;

        // Trip-timing rules carried over from the Hiatme website (docs/TRIP_TIMING_RULES.md
        // and check_scoreboard_trips.php). These are the dispatcher-truth boundaries — the
        // scoreboard counts violations using exactly these numbers, so the schedule builder
        // should refuse to assign anything that already breaks them.
        //
        // Pickup lateness allowed (driver arrival after scheduled PU is "on time"):
        //   - A-leg : 0–14 min late.  15+ min late = LATE.
        //   - B/C   : 0–29 min late.  30+ min late = LATE.
        // Routing uses pass-through windows (see <see cref="SupeyDispatchDriveClock"/>): the van
        // keeps moving when arrival is already inside the allowed PU window.
        // Dropoff lateness allowed (cluster end after the hardest deadline):
        //   - All legs: 0 min. At-deadline-or-after counts as LATE.
        private const double ALegPuLateMaxMinutes = 14.0;
        private const double BcLegPuLateMaxMinutes = 29.0;
        private const double DoLateMaxMinutes = 0.0;

        // Pass A: cap extra PU slack when every driver shows time-conflict (minutes beyond normal A/B cap).
        private const double CoverageMaxPuSlackMinutes = 8.0;
        private static readonly TimeSpan MorningHubWindowStart = new TimeSpan(6, 30, 0);
        private static readonly TimeSpan MorningHubWindowEnd = new TimeSpan(9, 30, 0);
        /// <summary>Max drivers per round for dedicated morning clinic waves.</summary>
        private const int DedicatedMorningHubMaxDrivers = 4;

        // A-leg riders are allowed to be picked up up to 29 min EARLY (and dropped 29 min
        // early too). Real dispatchers lean on this hard — it's how a 6-rider cluster whose
        // PU times span 95 min on paper actually gets driven in ~30 min of road time. The
        // scheduler models the early-pickup window for A-leg-only clusters; B/C clusters
        // ignore it because "too early" on a return ride is a scoreboard violation.
        private const double ALegEarlyPickupMinutes = 29.0;

        // Score weights. Both terms come out in seconds-equivalent units so they're comparable.
        private const double HomeAffinityWeight = 0.3;
        private const double ActiveWindowWeight = 1.0;
        private const double TemplateHintBonusSeconds = 600.0; // 10 minutes of "credit" for matching a hint
        private const double HistoricalPairBonusSeconds = 240.0; // 4 minutes for clustering historical pairs
        /// <summary>Driver-agnostic nudge so daily regulars land on a van, not reserves.</summary>
        private const double FrequentRiderCoverageBonusSeconds = 480.0;
        private const double LegPairSameDriverBonusSeconds = 720.0;

        // Load-balance credit. Without it, "minimize fleet hours" piles every cluster onto
        // whoever the first cluster of the day landed on, and other drivers sit idle all day.
        // The credit nudges underloaded drivers ahead in tie-breaks but is capped so it can't
        // override a real cost difference.
        //  - Threshold: a driver is "underloaded" if they have < 25% of the average riders.
        //  - Per-rider credit: 90s per rider below the average.
        //  - Max credit: 600s (10 min) per cluster — enough to break ties, not enough to win
        //    a cluster that's a real bad fit.
        private const double UnderloadedThresholdFraction = 0.25;
        private const double UnderloadedCreditPerRiderSeconds = 90.0;
        private const double UnderloadedMaxCreditSeconds = 600.0;

        // Capacity floor — even if every driver has high capacity, never form a "cluster" larger
        // than this so we don't accidentally try to put 12 riders in one ride-share. Defensive.
        private const int AbsoluteCapacityFloor = 8;

        public SupeyTemplateHints Hints { get; set; }
        public bool UseTemplateHints { get; set; }

        /// <summary>Human-accepted rules from AIagent (hard avoidances, preferred pairings).</summary>
        public SupeyScheduleRules ScheduleRules { get; set; }

        /// <summary>Weekday-template daily regulars — coverage priority only, not driver locks.</summary>
        public SupeyFrequentRiders FrequentRiders { get; set; }

        public SupeyRouteCache RouteCache => SupeyOsrmLegs.SharedCache;

        /// <summary>
        /// Builds a schedule. Trips and drivers should already be filtered to "selected for this build".
        /// <paramref name="locks"/> is honored as <c>tripNumber → driverName</c> pre-assignments.
        /// </summary>
        public async Task<SupeyScheduleResult> BuildAsync(
            DateTime serviceDate,
            IList<MCDownloadedTrip> trips,
            IList<SupeyDriverProfile> drivers,
            IDictionary<string, string> locks,
            IProgress<string> progress,
            CancellationToken token)
        {
            var result = new SupeyScheduleResult { ServiceDate = serviceDate.Date };
            await OsrmBootstrap.EnsureForBuildAsync(HiatmeAiSettings.Load(), progress, token)
                .ConfigureAwait(false);
            SupeyOsrmLegs.BeginBuildSession();
            if (FrequentRiders == null)
                FrequentRiders = SupeyFrequentRiders.Load();
            if (locks != null)
                foreach (var kv in locks) result.Locks[kv.Key] = kv.Value;

            if (trips == null) trips = new List<MCDownloadedTrip>();
            if (drivers == null) drivers = new List<SupeyDriverProfile>();

            // -------- Phase 0: Geocode prefetch --------
            // Many trips share PU or DO addresses (e.g. 8 riders all going to the same dialysis
            // clinic). Without dedupe, the per-trip loop below would still resolve correctly
            // (cache deduplicates after the first hit) but the dispatcher couldn't see how many
            // *unique* new addresses needed Nominatim until the loop was nearly done. The
            // prefetch pass scans every PU/DO/driver-home, dedupes by normalized address key,
            // and reports "X cached, Y new" up front so the user knows exactly how long the
            // 1-req/sec Nominatim phase will take.
            //
            // After this pass the cache is warm; the per-trip loop in Phase 1 is pure cache
            // reads (microseconds per call).
            AddressGeocoder.ResetCounters();
            var seenKey = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var uniqueTripAddrs = new List<(string street, string city)>();
            var uniqueDriverHomes = new List<SupeyDriverProfile>();

            foreach (var t in trips)
            {
                if (TryAddUniqueTripAddr(seenKey, t.PUStreet, t.PUCity)) uniqueTripAddrs.Add((t.PUStreet, t.PUCity));
                if (TryAddUniqueTripAddr(seenKey, t.DOStreet, t.DOCITY)) uniqueTripAddrs.Add((t.DOStreet, t.DOCITY));
            }
            foreach (var d in drivers)
            {
                string dk = "drv|" + (d.HomeStreet ?? "").Trim().ToLowerInvariant() + "|" +
                    (d.HomeCity ?? "").Trim().ToLowerInvariant() + "|" + (d.HomeZip ?? "").Trim();
                if (seenKey.Add(dk)) uniqueDriverHomes.Add(d);
            }

            int alreadyCached = 0;
            int needFetch = 0;
            foreach (var addr in uniqueTripAddrs)
            {
                if (AddressGeocoder.IsCached(addr.street, addr.city, "ME", "", "us")) alreadyCached++;
                else needFetch++;
            }
            foreach (var d in uniqueDriverHomes)
            {
                if (AddressGeocoder.IsCached(d.HomeStreet, d.HomeCity, d.HomeState, d.HomeZip, "us")) alreadyCached++;
                else needFetch++;
            }
            int totalUnique = uniqueTripAddrs.Count + uniqueDriverHomes.Count;

            if (needFetch == 0)
            {
                progress?.Report("Geocode: all " + totalUnique + " unique addresses already cached.");
            }
            else
            {
                int estSeconds = (int)Math.Ceiling(needFetch * 1.1);
                progress?.Report("Geocode: " + totalUnique + " unique addresses (" + alreadyCached +
                    " cached, " + needFetch + " new — about " + estSeconds + "s).");
            }

            // -------- Phase 1: Geocode --------
            progress?.Report("Geocoding " + trips.Count + " trips and " + drivers.Count + " drivers...");
            var tripGeo = new Dictionary<MCDownloadedTrip, SupeyTripGeo>();
            int doneTrips = 0;
            foreach (var t in trips)
            {
                token.ThrowIfCancellationRequested();
                var geo = new SupeyTripGeo
                {
                    Pickup = await AddressGeocoder.ResolveTripEndpointAsync(t.PUStreet, t.PUCity, token).ConfigureAwait(false),
                    Dropoff = await AddressGeocoder.ResolveTripEndpointAsync(t.DOStreet, t.DOCITY, token).ConfigureAwait(false),
                };
                tripGeo[t] = geo;
                doneTrips++;
                if ((doneTrips % 5) == 0 || doneTrips == trips.Count)
                {
                    long hits = AddressGeocoder.CacheHits;
                    long misses = AddressGeocoder.CacheMisses;
                    progress?.Report("Geocoding trips " + doneTrips + " / " + trips.Count +
                        " (" + hits + " cached, " + misses + " new)");
                }
            }

            // One-line summary so the user can immediately see the cache hit-rate per build.
            // First build of the day will report low cache hits; subsequent builds should be
            // ~100% if the persistent cache is working.
            progress?.Report("Geocoded " + trips.Count + " trips: " +
                AddressGeocoder.CacheHits + " from cache, " +
                AddressGeocoder.CacheMisses + " new from Nominatim.");

            var driverHomeGeo = new Dictionary<SupeyDriverProfile, GeoPoint>();
            var validDrivers = new List<SupeyDriverProfile>();
            foreach (var d in drivers)
            {
                token.ThrowIfCancellationRequested();
                progress?.Report("Geocoding home for " + d.Name + "...");
                // Pin to US/state so Nominatim doesn't match the same street name in a different
                // state (e.g. there's an Auburn ME and an Auburn AL — without the state pin the
                // wrong one wins ~10% of the time on common names). Use the fallback resolver
                // because driver-home cities are hand-typed and prone to misspellings ("Lvermore
                // Falls" → no match, even though zip 04254 + ME would have resolved cleanly).
                var p = await AddressGeocoder.ResolveWithFallbacksAsync(d.HomeStreet,
                    d.HomeCity,
                    d.HomeState, d.HomeZip, "us",
                    token).ConfigureAwait(false);
                if (p.HasValue)
                {
                    driverHomeGeo[d] = p.Value;
                    validDrivers.Add(d);
                }
                else
                {
                    result.BuildWarnings.Add(new SupeyWarning(SupeyWarningKind.DriverHomeUnresolvable,
                        "", d.Name,
                        "Driver excluded from BUILD — home address missing or will not geocode: "
                        + (d.FormatHomeOneLine() ?? "(empty)") +
                        ". Fill home in roster (or uncheck for this run) and rebuild."));
                }
            }

            // Will calls (00:00 PU) and out-of-service-area trips never auto-assign.
            var routableTrips = new List<MCDownloadedTrip>(trips.Count);
            foreach (var t in trips)
            {
                if (SupeyWillCallPickup.IsInAnyReserveList(result, t)) continue;

                string ooa = SupeyOutOfArea.MatchTrip(t);
                if (ooa != null)
                {
                    SupeyReserveBuckets.AddToReserves(result, t);
                    result.BuildWarnings.Add(new SupeyWarning(
                        SupeyWarningKind.OutOfServiceArea,
                        t.TripNumber ?? "", "",
                        "Trip " + (t.TripNumber ?? "") + " (" + (t.ClientFullName ?? "") +
                        ") touches out-of-service area \"" + ooa +
                        "\" — reroute to Modivcare (not auto-assigned)."));
                    continue;
                }

                if (SupeyWillCallPickup.IsPickupWillCall(t))
                {
                    SupeyReserveBuckets.AddToReserves(result, t);
                    continue;
                }
            }

            // Trips that didn't geocode go straight to Reserves with a warning.
            foreach (var t in trips)
            {
                if (SupeyWillCallPickup.IsInAnyReserveList(result, t)) continue;
                if (!tripGeo[t].Complete)
                {
                    SupeyReserveBuckets.AddToReserves(result, t);
                    result.BuildWarnings.Add(new SupeyWarning(SupeyWarningKind.MissingGeo,
                        t.TripNumber ?? "", "",
                        "Could not place " + (tripGeo[t].MissingPickup ? "PU" : "") +
                        (tripGeo[t].MissingPickup && tripGeo[t].MissingDropoff ? " and " : "") +
                        (tripGeo[t].MissingDropoff ? "DO" : "") + " for trip " +
                        (t.TripNumber ?? "(no #)") + " (" + (t.ClientFullName ?? "") + ")."));
                    continue;
                }
                if (!SupeyTripTimes.TryParsePU(t).HasValue)
                {
                    // No PU time — can't sequence; treat as Reserves.
                    SupeyReserveBuckets.AddToReserves(result, t);
                    continue;
                }
                routableTrips.Add(t);
            }

            // -------- Phase 2: Cluster --------
            progress?.Report("Clustering " + routableTrips.Count + " trips...");
            int capacityFloor = ResolveCapacityFloor(validDrivers);
            var hintsForCluster = UseTemplateHints ? Hints : null;
            var clusters = await ClusterTripsAsync(routableTrips, tripGeo, capacityFloor, token, hintsForCluster)
                .ConfigureAwait(false);
            for (int i = 0; i < clusters.Count; i++)
            {
                clusters[i].GroupNumber = i + 1;
                clusters[i].GroupColor = SupeyGroupPalette.For(i + 1);
            }
            progress?.Report("Built " + clusters.Count + " group(s).");

            // -------- Phase 3: Fingerprint --------
            progress?.Report("Routing in-group geometry for " + clusters.Count + " group(s)...");
            int fingerprinted = 0;
            foreach (var c in clusters)
            {
                token.ThrowIfCancellationRequested();
                SyncClusterMetadataFromTrips(c);
                await SupeyClusterRouting.OptimizeClusterTourAsync(c, token).ConfigureAwait(false);
                await PopulateClusterPolylineAsync(c, token).ConfigureAwait(false);
                fingerprinted++;
                if ((fingerprinted % 3) == 0 || fingerprinted == clusters.Count)
                    progress?.Report("In-group routing " + fingerprinted + " / " + clusters.Count + "...");
            }

            int clusterCountBeforeMerge = clusters.Count;
            clusters = SupeyClusterRouting.MergeHouseholdClusters(clusters, capacityFloor);
            int beforeHubMerge = clusters.Count;
            clusters = await ApplyMorningHubMergesAsync(clusters, capacityFloor, token).ConfigureAwait(false);
            if (clusters.Count != beforeHubMerge)
            {
                progress?.Report("Merged " + (beforeHubMerge - clusters.Count) +
                    " morning clinic group(s); re-routing...");
                for (int i = 0; i < clusters.Count; i++)
                {
                    token.ThrowIfCancellationRequested();
                    clusters[i].RoutePolyline.Clear();
                    SyncClusterMetadataFromTrips(clusters[i]);
                    await SupeyClusterRouting.OptimizeClusterTourAsync(clusters[i], token).ConfigureAwait(false);
                    await PopulateClusterPolylineAsync(clusters[i], token).ConfigureAwait(false);
                }
            }
            if (clusters.Count != clusterCountBeforeMerge)
            {
                progress?.Report("Merged " + (clusterCountBeforeMerge - clusters.Count) +
                    " household group(s); re-routing...");
                for (int i = 0; i < clusters.Count; i++)
                {
                    token.ThrowIfCancellationRequested();
                    clusters[i].RoutePolyline.Clear();
                    SyncClusterMetadataFromTrips(clusters[i]);
                    await SupeyClusterRouting.OptimizeClusterTourAsync(clusters[i], token).ConfigureAwait(false);
                    await PopulateClusterPolylineAsync(clusters[i], token).ConfigureAwait(false);
                }
            }

            int clusterCountBeforeSplit = clusters.Count;
            clusters = await SupeyClusterRouting.SplitInefficientClustersAsync(clusters, token)
                .ConfigureAwait(false);
            if (clusters.Count != clusterCountBeforeSplit)
            {
                progress?.Report("Re-routing " + clusters.Count + " group(s) after mileage split...");
                for (int i = 0; i < clusters.Count; i++)
                {
                    token.ThrowIfCancellationRequested();
                    clusters[i].RoutePolyline.Clear();
                    SyncClusterMetadataFromTrips(clusters[i]);
                    await SupeyClusterRouting.OptimizeClusterTourAsync(clusters[i], token).ConfigureAwait(false);
                    await PopulateClusterPolylineAsync(clusters[i], token).ConfigureAwait(false);
                }
            }
            for (int i = 0; i < clusters.Count; i++)
            {
                clusters[i].GroupNumber = i + 1;
                clusters[i].GroupColor = SupeyGroupPalette.For(i + 1);
            }

            // -------- Phase 4 & 5: Score + Greedy assign --------
            progress?.Report("Assigning groups to drivers...");
            var driverPlans = new List<SupeyDriverPlan>(validDrivers.Count);
            foreach (var d in validDrivers)
                driverPlans.Add(new SupeyDriverPlan { Driver = d, HomeGeo = driverHomeGeo[d] });

            // Lock pre-pass: if a trip's lock points at a driver in this build, assign the entire
            // cluster containing that trip to the locked driver (capacity permitting). Locks that
            // point at drivers not in this build are silently ignored.
            var lockedClusters = new HashSet<SupeyTripCluster>();
            if (result.Locks.Count > 0)
            {
                foreach (var c in clusters)
                {
                    string lockedDriverName = null;
                    foreach (var t in c.Trips)
                    {
                        if (result.Locks.TryGetValue(t.TripNumber ?? "", out var dn))
                        {
                            lockedDriverName = dn;
                            break;
                        }
                    }
                    if (string.IsNullOrEmpty(lockedDriverName)) continue;

                    SupeyDriverPlan target = null;
                    foreach (var p in driverPlans)
                        if (string.Equals(p.Driver.Name, lockedDriverName, StringComparison.OrdinalIgnoreCase))
                        { target = p; break; }
                    if (target == null) continue;
                    if (c.RiderCount > target.Driver.CapacityPassengers) continue;

                    target.Groups.Add(c);
                    lockedClusters.Add(c);
                }
            }

            // Now greedy-assign the remaining clusters in earliest-PU order. Earliest first means a
            // later cluster always sees a partially-built schedule before deciding where to land.
            var remaining = new List<SupeyTripCluster>();
            foreach (var c in clusters)
                if (!lockedClusters.Contains(c)) remaining.Add(c);
            double avgRidersForPassA = AverageRiderLoad(driverPlans);
            remaining.Sort(CompareClustersForCoveragePriority);

            progress?.Report("Pass A: morning clinic hubs...");
            await AssignMorningHubWavesAsync(remaining, driverPlans, avgRidersForPassA, token)
                .ConfigureAwait(false);

            progress?.Report("Pass A: assigning groups for coverage...");
            foreach (var cluster in remaining.ToArray())
            {
                token.ThrowIfCancellationRequested();
                if (!remaining.Contains(cluster)) continue;
                await TryAssignClusterAsync(cluster, remaining, driverPlans, result, progress, token, splitDepth: 0)
                    .ConfigureAwait(false);
            }

            progress?.Report("Pass B: polishing assignments...");
            await PolishAssignmentsAsync(driverPlans, token).ConfigureAwait(false);

            progress?.Report("Pass C: improving coverage (group swaps + reserves)...");
            await ImproveCoverageAsync(result, driverPlans, tripGeo, capacityFloor, hintsForCluster, progress, token)
                .ConfigureAwait(false);

            // -------- Phase 6: Sequence each driver --------
            progress?.Report("Sequencing dead-heads for " + driverPlans.Count + " driver(s)...");
            int seqDone = 0;
            foreach (var plan in driverPlans)
            {
                token.ThrowIfCancellationRequested();
                await OrderDriverGroupsCorridorAsync(plan, token).ConfigureAwait(false);
                await SequenceDriverAsync(plan, token).ConfigureAwait(false);
                seqDone++;
                progress?.Report("Sequenced " + seqDone + " / " + driverPlans.Count + " driver(s)...");
            }

            // -------- Phase 7: Consolidation hill-climb (release-time aware) --------
            progress?.Report("Consolidating late trips for early release...");
            await ConsolidateAsync(driverPlans, token).ConfigureAwait(false);

            // -------- Phase 8: Final feasibility & warnings --------
            foreach (var plan in driverPlans)
            {
                token.ThrowIfCancellationRequested();
                EvaluateWarnings(plan);
            }

            foreach (var p in driverPlans) result.DriverPlans.Add(p);
            SupeyWillCallPickup.EnforceOnResult(result, trips);
            progress?.Report("Build complete.");
            return result;
        }

        // ----- Phase 2 helpers -----

        /// <summary>
        /// Returns the cluster size ceiling — clusters won't grow past this many riders. We
        /// cap at the LARGEST driver's capacity (not the smallest, as before) because a 6-seat
        /// van should be able to take a 6-rider cluster even if a 4-seat sedan exists. Per-
        /// driver capacity is still enforced at scoring time, so a 6-rider cluster simply
        /// won't be assigned to the 4-seat sedan. Capped at <see cref="AbsoluteCapacityFloor"/>
        /// as a sanity ceiling regardless.
        /// </summary>
        /// <summary>
        /// Adds a normalized (street, city) tuple to the seen set; returns true on first sight.
        /// Used by the Phase 0 geocode prefetch to count unique addresses without double-counting
        /// trips that share PU/DO with another trip in the same load.
        /// </summary>
        private static bool TryAddUniqueTripAddr(HashSet<string> seen, string street, string city)
        {
            string s = (street ?? "").Trim().ToLowerInvariant();
            string c = (city ?? "").Trim().ToLowerInvariant();
            if (s.Length == 0 && c.Length == 0) return false;
            return seen.Add("trip|" + s + "|" + c);
        }

        private static int ResolveCapacityFloor(IEnumerable<SupeyDriverProfile> drivers)
        {
            int floor = AbsoluteCapacityFloor;
            int max = 0;
            foreach (var d in drivers)
            {
                int c = d.CapacityPassengers;
                if (c < 1) c = 1;
                if (c > max) max = c;
            }
            if (max == 0) return floor;
            return Math.Min(floor, max);
        }

        private static async Task<List<SupeyTripCluster>> ClusterTripsAsync(
            List<MCDownloadedTrip> trips, Dictionary<MCDownloadedTrip, SupeyTripGeo> geo,
            int capacityFloor, CancellationToken token, SupeyTemplateHints hints,
            double? clusterTimeWindowMinutes = null)
        {
            double timeWindow = clusterTimeWindowMinutes ?? ClusterTimeWindowMinutes;
            var sorted = new List<MCDownloadedTrip>(trips);
            sorted.Sort((a, b) =>
            {
                var ta = SupeyDeskScheduleTiming.ScheduledPickupForBuild(a);
                var tb = SupeyDeskScheduleTiming.ScheduledPickupForBuild(b);
                if (ta == TimeSpan.Zero) ta = TimeSpan.MaxValue;
                if (tb == TimeSpan.Zero) tb = TimeSpan.MaxValue;
                return ta.CompareTo(tb);
            });

            var clusters = new List<SupeyTripCluster>();
            foreach (var t in sorted)
            {
                token.ThrowIfCancellationRequested();
                if (!geo.TryGetValue(t, out var g) || !g.Complete) continue;
                var puTimeOpt = SupeyTripTimes.TryParsePU(t);
                if (!puTimeOpt.HasValue) continue;
                var pu = g.Pickup.Value;
                var dro = g.Dropoff.Value;
                var puTime = SupeyDeskScheduleTiming.ScheduledPickupForBuild(t);
                if (puTime == TimeSpan.Zero) continue;
                char tripLeg = DetectLeg(t.TripNumber);
                bool tripIsA = tripLeg == 'A';
                string tripFacilityKey = SupeyClusterRouting.MergeKeyForTrip(t);

                SupeyTripCluster bestFit = null;
                double bestScore = double.MaxValue;
                foreach (var c in clusters)
                {
                    if (c.RiderCount >= capacityFloor) continue;
                    if (Math.Abs((c.EarliestPickup - puTime).TotalMinutes) > timeWindow) continue;

                    char clusterLeg = DetectLeg(c.Trips[0].TripNumber);
                    if ((clusterLeg == 'A') != tripIsA) continue;

                    // Facility-first: same clinic hub (A=DO, B/C=PU).
                    if (!string.Equals(c.FacilityMergeKey, tripFacilityKey, StringComparison.OrdinalIgnoreCase))
                        continue;

                    double puRadius = tripIsA ? PuClusterRadiusMetersALeg : PuClusterRadiusMetersBcLeg;
                    double doRadius = tripIsA ? DoClusterRadiusMetersALeg : DoClusterRadiusMetersBcLeg;
                    var puLeg = await SupeyOsrmLegs.GetLegAsync(pu, c.PickupCentroid, token).ConfigureAwait(false);
                    if (!puLeg.Ok || puLeg.Meters > puRadius) continue;
                    double puDist = puLeg.Meters;
                    var doLeg = await SupeyOsrmLegs.GetLegAsync(dro, c.DropoffCentroid, token).ConfigureAwait(false);
                    if (!doLeg.Ok || doLeg.Meters > doRadius) continue;
                    double doDist = doLeg.Meters;

                    double score = tripIsA
                        ? (puDist + DoScoringWeight * doDist)
                        : (DoScoringWeight * puDist + doDist);

                    if (hints != null)
                    {
                        foreach (var existing in c.Trips)
                        {
                            if (hints.RodeTogetherHistorically(
                                    existing.ClientFullName ?? "", t.ClientFullName ?? ""))
                            {
                                score -= HistoricalPairBonusSeconds / 10.0;
                                break;
                            }
                        }
                    }

                    if (score < bestScore) { bestScore = score; bestFit = c; }
                }

                if (bestFit != null)
                {
                    bestFit.Trips.Add(t);
                    bestFit.PickupPoints.Add(pu);
                    bestFit.DropoffPoints.Add(dro);
                    bestFit.PickupCentroid = Centroid(bestFit.PickupPoints);
                    bestFit.DropoffCentroid = Centroid(bestFit.DropoffPoints);
                    if (puTime < bestFit.EarliestPickup) bestFit.EarliestPickup = puTime;
                    if (puTime > bestFit.LatestPickup) bestFit.LatestPickup = puTime;
                    var doTime = SupeyTripTimes.TryParseDO(t);
                    if (doTime.HasValue && doTime.Value < bestFit.HardestDropoff) bestFit.HardestDropoff = doTime.Value;
                }
                else
                {
                    var c = new SupeyTripCluster
                    {
                        EarliestPickup = puTime,
                        LatestPickup = puTime,
                        HardestDropoff = SupeyTripTimes.TryParseDO(t) ?? puTime.Add(TimeSpan.FromMinutes(30)),
                        PickupCentroid = pu,
                        DropoffCentroid = dro,
                        FacilityMergeKey = tripFacilityKey,
                    };
                    c.Trips.Add(t);
                    c.PickupPoints.Add(pu);
                    c.DropoffPoints.Add(dro);
                    clusters.Add(c);
                }
            }

            return clusters;
        }

        // ----- Phase 3 helpers -----

        private static void FingerprintCluster(SupeyTripCluster c)
        {
            // Sort trips inside a cluster by PU time so the in-cluster waypoint sequence
            // (PU1 → PU2 → ... → DO1 → DO2 → ...) is well-defined.
            var indexed = new List<int>(c.Trips.Count);
            for (int i = 0; i < c.Trips.Count; i++) indexed.Add(i);
            indexed.Sort((a, b) =>
            {
                var ta = SupeyTripTimes.TryParsePU(c.Trips[a]) ?? TimeSpan.MaxValue;
                var tb = SupeyTripTimes.TryParsePU(c.Trips[b]) ?? TimeSpan.MaxValue;
                return ta.CompareTo(tb);
            });

            var trips = new List<MCDownloadedTrip>(c.Trips.Count);
            var pus = new List<GeoPoint>(c.Trips.Count);
            var dos = new List<GeoPoint>(c.Trips.Count);
            foreach (var i in indexed)
            {
                trips.Add(c.Trips[i]);
                pus.Add(c.PickupPoints[i]);
                dos.Add(c.DropoffPoints[i]);
            }
            c.Trips.Clear();
            c.Trips.AddRange(trips);
            c.PickupPoints.Clear();
            c.PickupPoints.AddRange(pus);
            c.DropoffPoints.Clear();
            c.DropoffPoints.AddRange(dos);

            c.PickupCentroid = Centroid(c.PickupPoints);
            c.DropoffCentroid = Centroid(c.DropoffPoints);

            // Re-affirm earliest/latest from the (now sorted) list so they stay in sync if the
            // cluster building phase happened before parsing finished, then derive the all-A-leg
            // flag that gates 29-min early-pickup compression.
            var firstPu = SupeyTripTimes.TryParsePU(c.Trips[0]);
            var lastPu = SupeyTripTimes.TryParsePU(c.Trips[c.Trips.Count - 1]);
            if (firstPu.HasValue) c.EarliestPickup = firstPu.Value;
            if (lastPu.HasValue) c.LatestPickup = lastPu.Value;

            bool allA = true;
            foreach (var t in c.Trips)
            {
                if (DetectLeg(t.TripNumber) != 'A') { allA = false; break; }
            }
            c.IsAllALeg = allA;

            // Drop-off order: sort trip indices by DO deadline so the rider with the strictest
            // appointment is dropped first. This is the dispatcher's "drop TANCREL at 7:30 on
            // the way to the 8:30 dialysis run" pattern; routing the tour PU1..PUn, then DOs
            // in deadline order lets feasibility check each rider's individual deadline
            // instead of forcing the whole cluster to finish by the earliest one.
            c.DropoffOrder.Clear();
            for (int i = 0; i < c.Trips.Count; i++) c.DropoffOrder.Add(i);
            c.DropoffOrder.Sort((a, b) =>
            {
                var da = SupeyTripTimes.TryParseDO(c.Trips[a]) ?? TimeSpan.MaxValue;
                var db = SupeyTripTimes.TryParseDO(c.Trips[b]) ?? TimeSpan.MaxValue;
                int cmp = da.CompareTo(db);
                return cmp != 0 ? cmp : a.CompareTo(b); // stable
            });

            c.PickupOrder.Clear();
            for (int i = 0; i < c.Trips.Count; i++) c.PickupOrder.Add(i);
        }

        /// <summary>First PU on the road tour when routed; else earliest scheduled PU before routing.</summary>
        private static int FirstPickupIndex(SupeyTripCluster c)
        {
            if (c == null || c.Trips.Count == 0) return 0;
            if (c.Trips.Count == 1) return 0;
            if (SupeyClusterRouting.IsValidVisitOrder(c.PickupOrder, c.Trips.Count))
                return c.PickupOrder[0];
            return 0;
        }

        private static GeoPoint LastDropoffPoint(SupeyTripCluster c)
        {
            if (c == null || c.Trips.Count == 0)
                return c?.DropoffCentroid ?? new GeoPoint(44.1004, -70.2148);
            if (c.DropoffOrder != null && c.DropoffOrder.Count > 0)
            {
                int idx = c.DropoffOrder[c.DropoffOrder.Count - 1];
                if (idx >= 0 && idx < c.DropoffPoints.Count)
                    return c.DropoffPoints[idx];
            }
            if (c.DropoffPoints.Count > 0)
                return c.DropoffPoints[c.DropoffPoints.Count - 1];
            return c.DropoffCentroid;
        }

        private async Task PopulateClusterPolylineAsync(SupeyTripCluster c, CancellationToken token)
        {
            int n = c.Trips.Count;
            var path = new List<GeoPoint>(c.PickupPoints.Count + c.DropoffPoints.Count);
            if (c.PickupOrder.Count == 0)
                for (int i = 0; i < n; i++) c.PickupOrder.Add(i);
            foreach (int idx in c.PickupOrder)
                path.Add(c.PickupPoints[idx]);
            for (int i = 0; i < c.DropoffOrder.Count; i++)
                path.Add(c.DropoffPoints[c.DropoffOrder[i]]);
            if (path.Count < 2) return;

            c.RoutePolyline.Clear();
            var route = await SupeyOsrmLegs.RouteAsync(path, token).ConfigureAwait(false);
            if (route.Ok && !route.IsStraightLineFallback)
            {
                c.RoutePolyline.AddRange(route.Polyline);
                c.IntraClusterDriveSeconds = route.TotalSeconds;
                c.IntraClusterMeters = route.TotalMeters;
                c.IsStraightLineFallback = false;
            }
            else
            {
                c.IntraClusterDriveSeconds = 0;
                c.IntraClusterMeters = 0;
                c.IsStraightLineFallback = true;
            }

            // Tail = drive from the LAST PU through every DO. Path is PU1..PUn, DO_order[0]
            // ..DO_order[n-1] → 2n-1 OSRM legs total. Tail is legs[n-1..end] (n legs: PUn →
            // first drop, then drop-to-drop). We also store per-leg seconds so per-rider
            // deadlines can be checked — DropoffLegSeconds[i] is the time from the previous
            // waypoint to the i-th drop in DropoffOrder.
            c.DropoffLegSeconds.Clear();
            if (n <= 1)
            {
                c.TailDriveSeconds = c.IntraClusterDriveSeconds;
                c.DropoffLegSeconds.Add(c.IntraClusterDriveSeconds);
            }
            else if (route.Ok && !route.IsStraightLineFallback
                && route.LegDurations != null && route.LegDurations.Count >= 2 * n - 1)
            {
                double tail = 0;
                for (int i = n - 1; i < route.LegDurations.Count; i++)
                {
                    tail += route.LegDurations[i];
                    c.DropoffLegSeconds.Add(route.LegDurations[i]);
                }
                c.TailDriveSeconds = tail;
            }
            else
            {
                c.TailDriveSeconds = c.IntraClusterDriveSeconds * ((double)n / (2 * n - 1));
                double perLeg = c.TailDriveSeconds / n;
                for (int i = 0; i < n; i++) c.DropoffLegSeconds.Add(perLeg);
            }

            await SupeyClusterRouting.ApplySharedDropHubLegTimesAsync(c, token).ConfigureAwait(false);
        }

        // ----- Phase 4 + 5 -----

        private const int MaxAssignmentSplitDepth = 2;
        private const double SplitOnRejectLateMinutes = 30.0;

        private async Task TryAssignClusterAsync(
            SupeyTripCluster cluster,
            List<SupeyTripCluster> remaining,
            List<SupeyDriverPlan> plans,
            SupeyScheduleResult result,
            IProgress<string> progress,
            CancellationToken token,
            int splitDepth)
        {
            var pick = await ScoreAndPickForCoverageAsync(cluster, plans, preferDriver: null, token)
                .ConfigureAwait(false);
            if (pick != null)
            {
                pick.Groups.Add(cluster);
                remaining.Remove(cluster);
                await TryAssignPartnerLegsOnDriverAsync(pick, cluster, remaining, plans, progress, token)
                    .ConfigureAwait(false);
                return;
            }

            if (splitDepth < MaxAssignmentSplitDepth && ShouldTrySplitCluster(cluster))
            {
                var parts = SupeyClusterRouting.SplitClusterForAssignment(cluster);
                if (parts.Count > 1)
                {
                    progress?.Report("Splitting group " + cluster.GroupNumber + " into " + parts.Count + " for coverage...");
                    remaining.Remove(cluster);
                    foreach (var sub in parts)
                    {
                        token.ThrowIfCancellationRequested();
                        SyncClusterMetadataFromTrips(sub);
                        await SupeyClusterRouting.OptimizeClusterTourAsync(sub, token).ConfigureAwait(false);
                        await PopulateClusterPolylineAsync(sub, token).ConfigureAwait(false);
                        remaining.Add(sub);
                        await TryAssignClusterAsync(sub, remaining, plans, result, progress, token, splitDepth + 1)
                            .ConfigureAwait(false);
                    }
                    return;
                }
            }

            ReserveCluster(cluster, result);
            remaining.Remove(cluster);
        }

        /// <summary>
        /// After a cluster lands, pull matching B/C (or A) partner legs still in <paramref name="remaining"/>
        /// onto the same driver when timing allows.
        /// </summary>
        private async Task TryAssignPartnerLegsOnDriverAsync(
            SupeyDriverPlan driver,
            SupeyTripCluster assigned,
            List<SupeyTripCluster> remaining,
            List<SupeyDriverPlan> plans,
            IProgress<string> progress,
            CancellationToken token)
        {
            if (driver == null || assigned == null || remaining.Count == 0) return;

            var partnerBases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var clientNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in assigned.Trips)
            {
                string pb = TripPartnerBase(t.TripNumber ?? "");
                if (!string.IsNullOrEmpty(pb)) partnerBases.Add(pb);
                string cn = SupeyFrequentRiders.NormalizeClient(t.ClientFullName);
                if (cn.Length >= 2) clientNames.Add(cn);
            }

            bool progressMade;
            do
            {
                progressMade = false;
                foreach (var candidate in remaining.ToArray())
                {
                    token.ThrowIfCancellationRequested();
                    if (!ClusterMatchesPartnerRequest(candidate, partnerBases, clientNames))
                        continue;

                    var partnerScore = await TryScoreDriverAsync(candidate, driver, AverageRiderLoad(plans),
                        recordRejections: false, assignmentMode: AssignmentCostMode.MaximizeCoverage, token)
                        .ConfigureAwait(false);
                    bool feasible = partnerScore.ok;
                    if (!feasible)
                    {
                        for (double slack = 1.0; slack <= CoverageMaxPuSlackMinutes + 4; slack += 1.0)
                        {
                            partnerScore = await TryScoreDriverAsync(candidate, driver, AverageRiderLoad(plans),
                                    recordRejections: false,
                                    assignmentMode: AssignmentCostMode.MaximizeCoverage, token,
                                    puLateGraceMinutes: slack).ConfigureAwait(false);
                            if (partnerScore.ok)
                            {
                                feasible = true;
                                break;
                            }
                        }
                    }
                    if (!feasible) continue;

                    SyncClusterMetadataFromTrips(candidate);
                    await SupeyClusterRouting.OptimizeClusterTourAsync(candidate, token).ConfigureAwait(false);
                    await PopulateClusterPolylineAsync(candidate, token).ConfigureAwait(false);
                    driver.Groups.Add(candidate);
                    remaining.Remove(candidate);
                    foreach (var t in candidate.Trips)
                    {
                        string pb = TripPartnerBase(t.TripNumber ?? "");
                        if (!string.IsNullOrEmpty(pb)) partnerBases.Add(pb);
                        string cn = SupeyFrequentRiders.NormalizeClient(t.ClientFullName);
                        if (cn.Length >= 2) clientNames.Add(cn);
                    }
                    progressMade = true;
                    break;
                }
            }
            while (progressMade);
        }

        private static bool ClusterMatchesPartnerRequest(
            SupeyTripCluster cluster,
            HashSet<string> partnerBases,
            HashSet<string> clientNames)
        {
            if (cluster == null) return false;
            foreach (var t in cluster.Trips)
            {
                string pb = TripPartnerBase(t.TripNumber ?? "");
                if (!string.IsNullOrEmpty(pb) && partnerBases.Contains(pb))
                    return true;
                string cn = SupeyFrequentRiders.NormalizeClient(t.ClientFullName);
                if (cn.Length >= 2 && clientNames.Contains(cn))
                {
                    char leg = DetectLeg(t.TripNumber);
                    if (leg == 'B' || leg == 'C') return true;
                }
            }
            return false;
        }

        private static void ReserveCluster(
            SupeyTripCluster cluster,
            SupeyScheduleResult result,
            HashSet<string> suppressWarningTripNumbers = null)
        {
            var warned = suppressWarningTripNumbers ?? result.ReserveWarnedTripNumbers;

            foreach (var t in cluster.Trips)
                SupeyReserveBuckets.AddToReserves(result, t);

            bool allTripsAlreadyWarned = true;
            foreach (var t in cluster.Trips)
            {
                string tripNum = t.TripNumber ?? "";
                if (string.IsNullOrEmpty(tripNum)) continue;
                if (!warned.Contains(tripNum))
                {
                    allTripsAlreadyWarned = false;
                    break;
                }
            }
            if (allTripsAlreadyWarned) return;

            string breakdown = cluster.Rejections.FormatBreakdown();
            string baseMsg = "Group " + cluster.GroupNumber + " (" + cluster.RiderCount + " rider" +
                (cluster.RiderCount == 1 ? "" : "s") + ", " + SupeyTripTimes.FormatTimeOfDay(cluster.EarliestPickup) +
                " PU) — no driver could take it; sent to Reserves.";
            if (!string.IsNullOrEmpty(breakdown))
                baseMsg += " Why: " + breakdown + ".";

            string warnTrip = cluster.Trips.Count > 0 ? (cluster.Trips[0].TripNumber ?? "") : "";
            result.BuildWarnings.Add(new SupeyWarning(SupeyWarningKind.UnassignedToReserves,
                warnTrip, "", baseMsg));

            foreach (var t in cluster.Trips)
            {
                string tripNum = t.TripNumber ?? "";
                if (!string.IsNullOrEmpty(tripNum))
                    warned.Add(tripNum);
            }
        }

        private static bool ShouldTrySplitCluster(SupeyTripCluster cluster)
        {
            if (cluster.RiderCount <= 1) return false;
            if (SupeyClusterRouting.ClusterSharesSinglePickupAddress(cluster)) return false;
            if (IsMorningClinicCluster(cluster) && SupeyTripTimingPolicy.ClusterHasStrictClinicAppointment(cluster))
            {
                if (cluster.Rejections.DoInfeasible.Count > 0
                    && ParseLateMinutes(cluster.Rejections.LateRiderNote) >= SplitOnRejectLateMinutes)
                    return true;
                return cluster.Rejections.DoInfeasible.Count >= 4;
            }
            if (cluster.Rejections.DoInfeasible.Count > 0 && ParseLateMinutes(cluster.Rejections.LateRiderNote) >= SplitOnRejectLateMinutes)
                return true;
            if (cluster.Rejections.DoInfeasible.Count >= 3) return true;
            if (cluster.Rejections.TimeConflict.Count >= 3 && cluster.RiderCount >= 2) return true;
            if (cluster.Rejections.TimeConflict.Count >= 4 && cluster.RiderCount >= 2) return true;
            return false;
        }

        private static bool IsMorningClinicCluster(SupeyTripCluster cluster)
        {
            if (cluster == null || cluster.Trips.Count == 0) return false;
            if (cluster.EarliestPickup < MorningHubWindowStart || cluster.EarliestPickup >= MorningHubWindowEnd)
                return false;
            if (DetectLeg(cluster.Trips[0].TripNumber) != 'A') return false;
            string hub = SupeyClusterRouting.CanonicalMorningDropHubKey(cluster.FacilityMergeKey ?? "");
            return hub.IndexOf("FALCON", StringComparison.OrdinalIgnoreCase) >= 0
                || hub.IndexOf("MANLEY", StringComparison.OrdinalIgnoreCase) >= 0
                || hub.IndexOf("MINOT", StringComparison.OrdinalIgnoreCase) >= 0
                || hub.IndexOf("646 MAIN", StringComparison.OrdinalIgnoreCase) >= 0
                || hub.IndexOf("618 MAIN", StringComparison.OrdinalIgnoreCase) >= 0
                || hub.IndexOf("CROSS", StringComparison.OrdinalIgnoreCase) >= 0
                || hub.IndexOf("63 BROAD", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Spread morning A-leg clinic loads across drivers before the main greedy pass.
        /// One cluster per hub per sweep so Manley does not consume every driver before Falcon/Minot/646.
        /// </summary>
        private async Task AssignMorningHubWavesAsync(
            List<SupeyTripCluster> remaining,
            List<SupeyDriverPlan> plans,
            double avgRiders,
            CancellationToken token)
        {
            var byHub = new Dictionary<string, List<SupeyTripCluster>>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in remaining)
            {
                if (c.EarliestPickup < MorningHubWindowStart || c.EarliestPickup >= MorningHubWindowEnd)
                    continue;
                if (c.Trips.Count == 0 || DetectLeg(c.Trips[0].TripNumber) != 'A')
                    continue;
                string hub = SupeyClusterRouting.CanonicalMorningDropHubKey(c.FacilityMergeKey ?? "");
                if (!byHub.TryGetValue(hub, out var list))
                {
                    list = new List<SupeyTripCluster>();
                    byHub[hub] = list;
                }
                list.Add(c);
            }
            if (byHub.Count == 0) return;

            var hubKeys = SortHubKeysByMorningPriority(byHub);
            var hubsServed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var driverLockedHub = new Dictionary<SupeyDriverPlan, string>();

            // Falcon / 646 / Minot before other morning hubs consume every driver slot.
            await SeedCriticalMorningHubsAsync(remaining, plans, avgRiders, byHub, hubKeys, hubsServed,
                driverLockedHub, token).ConfigureAwait(false);

            await AssignDedicatedMorningHubWaveAsync(remaining, plans, avgRiders, driverLockedHub,
                IsFalconMorningHub, token).ConfigureAwait(false);
            await AssignDedicatedMorningHubWaveAsync(remaining, plans, avgRiders, driverLockedHub,
                Is646MorningHub, token).ConfigureAwait(false);
            await AssignDedicatedMorningHubWaveAsync(remaining, plans, avgRiders, driverLockedHub,
                IsMinotMorningHub, token).ConfigureAwait(false);
            await AssignDedicatedMorningHubWaveAsync(remaining, plans, avgRiders, driverLockedHub,
                IsCrossMorningHub, token).ConfigureAwait(false);
            await AssignDedicatedMorningHubWaveAsync(remaining, plans, avgRiders, driverLockedHub,
                IsManleyMorningHub, token).ConfigureAwait(false);

            await SpreadMorningHubsDistinctDriverFirstAsync(remaining, plans, avgRiders, byHub, hubKeys,
                allowPuSlack: false, hubsServed, driverLockedHub, token).ConfigureAwait(false);
            await SpreadMorningHubsDistinctDriverFirstAsync(remaining, plans, avgRiders, byHub, hubKeys,
                allowPuSlack: true, hubsServed, driverLockedHub, token).ConfigureAwait(false);
            await SweepMorningHubsOncePerHubAsync(remaining, plans, avgRiders, byHub, hubKeys,
                allowPuSlack: false, token).ConfigureAwait(false);
            await SweepMorningHubsOncePerHubAsync(remaining, plans, avgRiders, byHub, hubKeys,
                allowPuSlack: true, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Assigns the earliest cluster for each hub that has not yet been served, preferring
        /// drivers not already locked to a different morning clinic hub.
        /// </summary>
        private async Task SpreadMorningHubsDistinctDriverFirstAsync(
            List<SupeyTripCluster> remaining,
            List<SupeyDriverPlan> plans,
            double avgRiders,
            Dictionary<string, List<SupeyTripCluster>> byHub,
            List<string> hubKeys,
            bool allowPuSlack,
            HashSet<string> hubsServed,
            Dictionary<SupeyDriverPlan, string> driverLockedHub,
            CancellationToken token)
        {
            bool progress;
            do
            {
                progress = false;
                foreach (string hubKey in hubKeys)
                {
                    token.ThrowIfCancellationRequested();
                    if (hubsServed.Contains(hubKey)) continue;
                    var cluster = EarliestRemainingClusterInHub(byHub[hubKey], remaining);
                    if (cluster == null) continue;

                    var pick = await PickForDistinctMorningHubAsync(cluster, plans, avgRiders, hubKey,
                        driverLockedHub, allowPuSlack, token).ConfigureAwait(false);
                    if (pick == null) continue;

                    pick.Groups.Add(cluster);
                    remaining.Remove(cluster);
                    if (!UsesDedicatedMorningHubWave(hubKey))
                        hubsServed.Add(hubKey);
                    if (!driverLockedHub.ContainsKey(pick))
                        driverLockedHub[pick] = hubKey;
                    progress = true;
                }
            }
            while (progress);
        }

        /// <summary>
        /// Spreads morning clinic clusters for one hub (Falcon, Manley, …) across several drivers
        /// before the general hub sweep marks the hub served after a single assignment.
        /// </summary>
        private async Task AssignDedicatedMorningHubWaveAsync(
            List<SupeyTripCluster> remaining,
            List<SupeyDriverPlan> plans,
            double avgRiders,
            Dictionary<SupeyDriverPlan, string> driverLockedHub,
            Func<string, bool> matchesHubKey,
            CancellationToken token)
        {
            var pool = CollectMorningHubClusters(remaining, matchesHubKey);
            if (pool.Count == 0) return;

            foreach (bool allowPuSlack in new[] { false, true })
            {
                bool progress;
                do
                {
                    progress = false;
                    var orderedDrivers = OrderPlansForDedicatedHubWave(plans, driverLockedHub, matchesHubKey);
                    int driversUsed = 0;
                    foreach (var plan in orderedDrivers)
                    {
                        token.ThrowIfCancellationRequested();
                        if (driversUsed >= DedicatedMorningHubMaxDrivers) break;
                        if (!PlanEligibleForDedicatedHubWave(plan, driverLockedHub, matchesHubKey))
                            continue;

                        SupeyTripCluster cluster = await PickEarliestFeasibleHubClusterAsync(
                            pool, remaining, plan, avgRiders, allowPuSlack, token).ConfigureAwait(false);
                        if (cluster == null) continue;

                        plan.Groups.Add(cluster);
                        remaining.Remove(cluster);
                        pool.Remove(cluster);
                        if (!driverLockedHub.ContainsKey(plan))
                            driverLockedHub[plan] = cluster.FacilityMergeKey ?? "";
                        driversUsed++;
                        progress = true;
                    }
                }
                while (progress && pool.Count > 0);
            }
        }

        private static List<SupeyTripCluster> CollectMorningHubClusters(
            List<SupeyTripCluster> remaining,
            Func<string, bool> matchesHubKey)
        {
            var list = new List<SupeyTripCluster>();
            foreach (var c in remaining)
            {
                if (!IsMorningHubCluster(c, matchesHubKey)) continue;
                list.Add(c);
            }
            list.Sort((a, b) => a.EarliestPickup.CompareTo(b.EarliestPickup));
            return list;
        }

        private static bool IsMorningHubCluster(SupeyTripCluster c, Func<string, bool> matchesHubKey)
        {
            if (c == null || c.Trips.Count == 0) return false;
            if (c.EarliestPickup < MorningHubWindowStart || c.EarliestPickup >= MorningHubWindowEnd)
                return false;
            if (DetectLeg(c.Trips[0].TripNumber) != 'A') return false;
            return matchesHubKey(SupeyClusterRouting.CanonicalMorningDropHubKey(c.FacilityMergeKey ?? ""));
        }

        private static bool IsFalconMorningHub(string hubKey) =>
            !string.IsNullOrEmpty(hubKey) &&
            hubKey.IndexOf("FALCON", StringComparison.OrdinalIgnoreCase) >= 0;

        private static bool IsManleyMorningHub(string hubKey) =>
            !string.IsNullOrEmpty(hubKey) &&
            hubKey.IndexOf("MANLEY", StringComparison.OrdinalIgnoreCase) >= 0;

        private static bool Is646MorningHub(string hubKey) =>
            !string.IsNullOrEmpty(hubKey) &&
            hubKey.IndexOf("646 MAIN", StringComparison.OrdinalIgnoreCase) >= 0;

        private static bool IsMinotMorningHub(string hubKey) =>
            !string.IsNullOrEmpty(hubKey) &&
            hubKey.IndexOf("MINOT", StringComparison.OrdinalIgnoreCase) >= 0;

        private static bool IsCrossMorningHub(string hubKey) =>
            !string.IsNullOrEmpty(hubKey) &&
            hubKey.IndexOf("CROSS", StringComparison.OrdinalIgnoreCase) >= 0;

        private static bool UsesDedicatedMorningHubWave(string hubKey) =>
            IsFalconMorningHub(hubKey) || Is646MorningHub(hubKey) ||
            IsMinotMorningHub(hubKey) || IsCrossMorningHub(hubKey) || IsManleyMorningHub(hubKey);

        private static List<SupeyDriverPlan> OrderPlansForDedicatedHubWave(
            List<SupeyDriverPlan> plans,
            Dictionary<SupeyDriverPlan, string> driverLockedHub,
            Func<string, bool> matchesHubKey)
        {
            var ordered = new List<SupeyDriverPlan>(plans);
            ordered.Sort((a, b) =>
            {
                int hubCmp = PlanHasDedicatedHubMorning(b, driverLockedHub, matchesHubKey)
                    .CompareTo(PlanHasDedicatedHubMorning(a, driverLockedHub, matchesHubKey));
                if (hubCmp != 0) return hubCmp;
                int openCmp = PlanEligibleForDedicatedHubWave(b, driverLockedHub, matchesHubKey)
                    .CompareTo(PlanEligibleForDedicatedHubWave(a, driverLockedHub, matchesHubKey));
                if (openCmp != 0) return openCmp;
                int shiftCmp = CompareShiftStart(a, b);
                if (shiftCmp != 0) return shiftCmp;
                int g = a.Groups.Count.CompareTo(b.Groups.Count);
                return g != 0 ? g : TotalRiders(a).CompareTo(TotalRiders(b));
            });
            return ordered;
        }

        private static bool PlanHasDedicatedHubMorning(
            SupeyDriverPlan plan,
            Dictionary<SupeyDriverPlan, string> driverLockedHub,
            Func<string, bool> matchesHubKey)
        {
            if (driverLockedHub.TryGetValue(plan, out string locked) && matchesHubKey(locked))
                return true;
            foreach (var g in plan.Groups)
            {
                if (matchesHubKey(g.FacilityMergeKey)) return true;
            }
            return false;
        }

        private static bool PlanEligibleForDedicatedHubWave(
            SupeyDriverPlan plan,
            Dictionary<SupeyDriverPlan, string> driverLockedHub,
            Func<string, bool> matchesHubKey)
        {
            if (plan.Groups.Count == 0) return true;
            if (driverLockedHub.TryGetValue(plan, out string locked))
                return matchesHubKey(locked);
            foreach (var g in plan.Groups)
            {
                if (!IsMorningHubCluster(g, matchesHubKey) && g.EarliestPickup < MorningHubWindowEnd)
                    return false;
            }
            return true;
        }

        private async Task<SupeyTripCluster> PickEarliestFeasibleHubClusterAsync(
            List<SupeyTripCluster> hubOrdered,
            List<SupeyTripCluster> remaining,
            SupeyDriverPlan plan,
            double avgRiders,
            bool allowPuSlack,
            CancellationToken token)
        {
            foreach (var c in hubOrdered)
            {
                if (!remaining.Contains(c)) continue;
                if (await PickLightestFeasibleDriverFromAsync(c, new List<SupeyDriverPlan> { plan }, avgRiders,
                        allowPuSlack, preferEarlyShift: true, token).ConfigureAwait(false) != null)
                    return c;
            }
            return null;
        }

        private static SupeyTripCluster EarliestRemainingClusterInHub(
            List<SupeyTripCluster> hubClusters,
            List<SupeyTripCluster> remaining)
        {
            SupeyTripCluster best = null;
            foreach (var c in hubClusters)
            {
                if (!remaining.Contains(c)) continue;
                if (best == null || c.EarliestPickup < best.EarliestPickup)
                    best = c;
            }
            return best;
        }

        private async Task<SupeyDriverPlan> PickForDistinctMorningHubAsync(
            SupeyTripCluster cluster,
            List<SupeyDriverPlan> plans,
            double avgRiders,
            string hubKey,
            Dictionary<SupeyDriverPlan, string> driverLockedHub,
            bool allowPuSlack,
            CancellationToken token)
        {
            var open = new List<SupeyDriverPlan>();
            var sameHub = new List<SupeyDriverPlan>();
            foreach (var p in plans)
            {
                if (!driverLockedHub.TryGetValue(p, out string locked))
                    open.Add(p);
                else if (string.Equals(locked, hubKey, StringComparison.OrdinalIgnoreCase))
                    sameHub.Add(p);
            }

            bool preferEarlyShift = cluster.EarliestPickup < new TimeSpan(8, 0, 0);
            var pick = await PickLightestFeasibleDriverFromAsync(cluster, open, avgRiders, allowPuSlack,
                preferEarlyShift, token).ConfigureAwait(false);
            if (pick != null) return pick;
            return await PickLightestFeasibleDriverFromAsync(cluster, sameHub, avgRiders, allowPuSlack,
                preferEarlyShift, token).ConfigureAwait(false);
        }

        private static List<string> SortHubKeysByMorningPriority(
            Dictionary<string, List<SupeyTripCluster>> byHub)
        {
            var hubKeys = new List<string>(byHub.Keys);
            hubKeys.Sort((ka, kb) =>
            {
                int tierCmp = MorningHubPriorityTier(ka).CompareTo(MorningHubPriorityTier(kb));
                if (tierCmp != 0) return tierCmp;
                int cmp = EarliestPickupInHub(byHub[ka]).CompareTo(EarliestPickupInHub(byHub[kb]));
                if (cmp != 0) return cmp;
                return TotalClusterRiders(byHub[kb]).CompareTo(TotalClusterRiders(byHub[ka]));
            });
            return hubKeys;
        }

        /// <summary>Lower tier = assigned earlier in morning hub waves (Falcon before Manley).</summary>
        private static int MorningHubPriorityTier(string hubKey)
        {
            if (string.IsNullOrEmpty(hubKey)) return 50;
            hubKey = SupeyClusterRouting.CanonicalMorningDropHubKey(hubKey);
            if (hubKey.IndexOf("FALCON", StringComparison.OrdinalIgnoreCase) >= 0) return 0;
            if (hubKey.IndexOf("63 BROAD", StringComparison.OrdinalIgnoreCase) >= 0) return 1;
            if (hubKey.IndexOf("646 MAIN", StringComparison.OrdinalIgnoreCase) >= 0) return 2;
            if (hubKey.IndexOf("MINOT", StringComparison.OrdinalIgnoreCase) >= 0) return 3;
            if (hubKey.IndexOf("CROSS", StringComparison.OrdinalIgnoreCase) >= 0) return 3;
            if (hubKey.IndexOf("MANLEY", StringComparison.OrdinalIgnoreCase) >= 0) return 4;
            return 5;
        }

        private static bool IsSeedPriorityMorningHub(string hubKey) =>
            MorningHubPriorityTier(hubKey) <= 3;

        /// <summary>
        /// Assigns the earliest cluster at each critical clinic hub while drivers are still open,
        /// so Falcon/646/Minot are not starved after Manley fills every timeline.
        /// </summary>
        private async Task SeedCriticalMorningHubsAsync(
            List<SupeyTripCluster> remaining,
            List<SupeyDriverPlan> plans,
            double avgRiders,
            Dictionary<string, List<SupeyTripCluster>> byHub,
            List<string> hubKeys,
            HashSet<string> hubsServed,
            Dictionary<SupeyDriverPlan, string> driverLockedHub,
            CancellationToken token)
        {
            foreach (string hubKey in hubKeys)
            {
                token.ThrowIfCancellationRequested();
                if (!IsSeedPriorityMorningHub(hubKey)) continue;
                if (!byHub.TryGetValue(hubKey, out var hubClusters)) continue;
                var cluster = EarliestRemainingClusterInHub(hubClusters, remaining);
                if (cluster == null) continue;

                var pick = await PickLightestFeasibleDriverAsync(cluster, plans, avgRiders, allowPuSlack: false,
                    token, preferEarlyShift: true).ConfigureAwait(false);
                if (pick == null)
                    pick = await PickLightestFeasibleDriverAsync(cluster, plans, avgRiders, allowPuSlack: true,
                        token, preferEarlyShift: true).ConfigureAwait(false);
                if (pick == null) continue;

                pick.Groups.Add(cluster);
                remaining.Remove(cluster);
                hubsServed.Add(hubKey);
                if (!driverLockedHub.ContainsKey(pick))
                    driverLockedHub[pick] = hubKey;
            }
        }

        private static TimeSpan EarliestPickupInHub(List<SupeyTripCluster> hubClusters)
        {
            TimeSpan t = TimeSpan.MaxValue;
            foreach (var c in hubClusters)
                if (c.EarliestPickup < t) t = c.EarliestPickup;
            return t;
        }

        private async Task SweepMorningHubsOncePerHubAsync(
            List<SupeyTripCluster> remaining,
            List<SupeyDriverPlan> plans,
            double avgRiders,
            Dictionary<string, List<SupeyTripCluster>> byHub,
            List<string> hubKeys,
            bool allowPuSlack,
            CancellationToken token)
        {
            bool anyAssigned;
            do
            {
                anyAssigned = false;
                foreach (string hubKey in hubKeys)
                {
                    token.ThrowIfCancellationRequested();
                    SupeyTripCluster cluster = null;
                    var hubClusters = byHub[hubKey];
                    hubClusters.Sort((a, b) => a.EarliestPickup.CompareTo(b.EarliestPickup));
                    foreach (var c in hubClusters)
                    {
                        if (!remaining.Contains(c)) continue;
                        cluster = c;
                        break;
                    }
                    if (cluster == null) continue;

                    var pick = await PickLightestFeasibleDriverAsync(cluster, plans, avgRiders, allowPuSlack,
                        token, preferEarlyShift: cluster.EarliestPickup < new TimeSpan(8, 0, 0))
                        .ConfigureAwait(false);
                    if (pick == null) continue;
                    pick.Groups.Add(cluster);
                    remaining.Remove(cluster);
                    anyAssigned = true;
                }
            }
            while (anyAssigned);
        }

        private static int TotalClusterRiders(List<SupeyTripCluster> clusters)
        {
            int n = 0;
            foreach (var c in clusters) n += c.RiderCount;
            return n;
        }

        private static int CompareShiftStart(SupeyDriverPlan a, SupeyDriverPlan b)
        {
            var sa = a.Driver.ParseShiftStart() ?? TimeSpan.MaxValue;
            var sb = b.Driver.ParseShiftStart() ?? TimeSpan.MaxValue;
            return sa.CompareTo(sb);
        }

        private Task<SupeyDriverPlan> PickLightestFeasibleDriverAsync(
            SupeyTripCluster cluster,
            List<SupeyDriverPlan> plans,
            double avgRiders,
            bool allowPuSlack,
            CancellationToken token,
            bool preferEarlyShift = false)
        {
            return PickLightestFeasibleDriverFromAsync(cluster, plans, avgRiders, allowPuSlack, preferEarlyShift, token);
        }

        private async Task<SupeyDriverPlan> PickLightestFeasibleDriverFromAsync(
            SupeyTripCluster cluster,
            List<SupeyDriverPlan> plans,
            double avgRiders,
            bool allowPuSlack,
            bool preferEarlyShift,
            CancellationToken token)
        {
            if (plans == null || plans.Count == 0) return null;

            var ordered = new List<SupeyDriverPlan>(plans);
            ordered.Sort((a, b) =>
            {
                if (preferEarlyShift)
                {
                    int shiftCmp = CompareShiftStart(a, b);
                    if (shiftCmp != 0) return shiftCmp;
                }
                int cmp = a.Groups.Count.CompareTo(b.Groups.Count);
                return cmp != 0 ? cmp : TotalRiders(a).CompareTo(TotalRiders(b));
            });

            SupeyDriverPlan best = null;
            double bestCost = double.MaxValue;
            double bestSlack = double.MaxValue;

            string clusterHub = cluster.FacilityMergeKey ?? "";
            foreach (var p in ordered)
            {
                token.ThrowIfCancellationRequested();
                var scored = await TryScoreDriverAsync(cluster, p, avgRiders, recordRejections: false,
                        assignmentMode: AssignmentCostMode.MaximizeCoverage, token)
                    .ConfigureAwait(false);
                if (scored.ok)
                {
                    double cost = scored.cost + MorningHubAffinityPenalty(p, clusterHub);
                    if (cost < bestCost) { bestCost = cost; best = p; bestSlack = 0; }
                    continue;
                }

                if (!allowPuSlack) continue;

                double maxSlack = CoverageMaxPuSlackMinutes;
                foreach (var t in cluster.Trips)
                    maxSlack = Math.Max(maxSlack, SupeyTripTimingPolicy.ExtraCoveragePuSlackMinutes(t));

                for (double slack = 1.0; slack <= maxSlack; slack += 1.0)
                {
                    scored = await TryScoreDriverAsync(cluster, p, avgRiders, recordRejections: false,
                            assignmentMode: AssignmentCostMode.MaximizeCoverage, token,
                            puLateGraceMinutes: slack).ConfigureAwait(false);
                    if (!scored.ok) continue;
                    double cost = scored.cost + MorningHubAffinityPenalty(p, clusterHub);
                    if (slack < bestSlack || (Math.Abs(slack - bestSlack) < 0.01 && cost < bestCost))
                    {
                        bestSlack = slack;
                        bestCost = cost;
                        best = p;
                    }
                    break;
                }
            }

            return best;
        }

        /// <summary>
        /// Nudge morning clinic assignment away from drivers already committed to a different
        /// drop-off hub so Falcon/646/Minot spreads instead of stacking Manley on whoever is lightest.
        /// </summary>
        private static double MorningHubAffinityPenalty(SupeyDriverPlan plan, string targetHubKey)
        {
            if (string.IsNullOrEmpty(targetHubKey) || plan.Groups.Count == 0) return 0;
            bool hasTarget = false;
            bool hasOther = false;
            foreach (var g in plan.Groups)
            {
                string hub = g.FacilityMergeKey ?? "";
                if (string.Equals(hub, targetHubKey, StringComparison.OrdinalIgnoreCase))
                    hasTarget = true;
                else if (!string.IsNullOrEmpty(hub))
                    hasOther = true;
            }
            if (hasOther && !hasTarget) return 1800.0;
            return 0;
        }

        private static double ParseLateMinutes(string note)
        {
            if (string.IsNullOrEmpty(note)) return 0;
            int idx = note.IndexOf("late by", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return 0;
            int start = idx + "late by".Length;
            int end = note.IndexOf(" min", start, StringComparison.OrdinalIgnoreCase);
            if (end < 0) end = note.Length;
            string num = note.Substring(start, end - start).Trim();
            double m;
            return double.TryParse(num, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out m) ? m : 0;
        }

        private async Task<SupeyDriverPlan> ScoreAndPickForCoverageAsync(
            SupeyTripCluster cluster,
            List<SupeyDriverPlan> plans,
            SupeyDriverPlan preferDriver,
            CancellationToken token)
        {
            cluster.Rejections.Clear();
            double avgRiders = AverageRiderLoad(plans);

            double extraPuSlack = 0;
            foreach (var t in cluster.Trips)
                extraPuSlack = Math.Max(extraPuSlack, SupeyTripTimingPolicy.ExtraCoveragePuSlackMinutes(t));

            if (preferDriver != null)
            {
                for (double slack = 0; slack <= CoverageMaxPuSlackMinutes + 4 + extraPuSlack; slack += 1.0)
                {
                    if ((await TryScoreDriverAsync(cluster, preferDriver, avgRiders, recordRejections: false,
                            assignmentMode: AssignmentCostMode.MaximizeCoverage, token,
                            puLateGraceMinutes: slack).ConfigureAwait(false)).ok)
                        return preferDriver;
                }
            }

            SupeyDriverPlan bestDriver = null;
            double bestCost = double.MaxValue;
            bool preferEarlyShift = cluster.EarliestPickup < new TimeSpan(8, 0, 0);
            var ordered = new List<SupeyDriverPlan>(plans);
            ordered.Sort((a, b) =>
            {
                if (preferEarlyShift)
                {
                    int shiftCmp = CompareShiftStart(a, b);
                    if (shiftCmp != 0) return shiftCmp;
                }
                int cmp = a.Groups.Count.CompareTo(b.Groups.Count);
                return cmp != 0 ? cmp : TotalRiders(a).CompareTo(TotalRiders(b));
            });

            foreach (var p in ordered)
            {
                token.ThrowIfCancellationRequested();
                var pickScore = await TryScoreDriverAsync(cluster, p, avgRiders, recordRejections: true,
                        assignmentMode: AssignmentCostMode.MaximizeCoverage, token)
                    .ConfigureAwait(false);
                if (!pickScore.ok) continue;
                if (pickScore.cost < bestCost) { bestCost = pickScore.cost; bestDriver = p; }
            }

            if (bestDriver != null)
                return bestDriver;

            if (cluster.Rejections.DoInfeasible.Count == 0
                && cluster.Rejections.TimeConflict.Count >= plans.Count - 1)
            {
                return await PickLightestFeasibleDriverAsync(cluster, plans, avgRiders, allowPuSlack: true, token,
                    preferEarlyShift: cluster.EarliestPickup < new TimeSpan(8, 0, 0)).ConfigureAwait(false);
            }

            return null;
        }

        private const int CoverageImproveMaxGroupSwaps = 40;
        private const int CoverageImproveMaxReserveGroups = 80;
        private const double ReserveRetryClusterWindowMinutes = 60.0;
        private const int ReserveRetryMaxRidersPerCluster = 4;

        /// <summary>
        /// Pass C: move whole groups from overloaded drivers to lighter ones, then re-cluster
        /// reserves and try to assign them without splitting household groups.
        /// </summary>
        private async Task ImproveCoverageAsync(
            SupeyScheduleResult result,
            List<SupeyDriverPlan> plans,
            Dictionary<MCDownloadedTrip, SupeyTripGeo> tripGeo,
            int capacityFloor,
            SupeyTemplateHints hints,
            IProgress<string> progress,
            CancellationToken token)
        {
            int swaps = await SwapGroupsForBalanceAsync(plans, token).ConfigureAwait(false);
            int fromReserves = await TryAssignReserveGroupsAsync(result, plans, tripGeo, capacityFloor, hints, token)
                .ConfigureAwait(false);
            int legPairs = await ReconcileLegPairsAsync(result, plans, tripGeo, capacityFloor, token)
                .ConfigureAwait(false);
            if (swaps > 0 || fromReserves > 0 || legPairs > 0)
                progress?.Report("Pass C: " + swaps + " group swap(s), " + fromReserves +
                    " from reserves, " + legPairs + " A/B leg pair(s).");
        }

        private async Task<int> SwapGroupsForBalanceAsync(List<SupeyDriverPlan> plans, CancellationToken token)
        {
            int moves = 0;
            while (moves < CoverageImproveMaxGroupSwaps)
            {
                token.ThrowIfCancellationRequested();
                SupeyDriverPlan donor = null;
                SupeyDriverPlan recipient = null;
                int maxGroups = -1;
                int minGroups = int.MaxValue;
                foreach (var p in plans)
                {
                    int n = p.Groups.Count;
                    if (n > maxGroups) { maxGroups = n; donor = p; }
                    if (n < minGroups) { minGroups = n; recipient = p; }
                }
                if (donor == null || recipient == null || donor == recipient) break;
                if (maxGroups <= minGroups + 1) break;

                double avgRiders = AverageRiderLoad(plans);
                bool moved = false;
                var candidates = new List<SupeyTripCluster>(donor.Groups);
                candidates.Sort((a, b) => a.EarliestPickup.CompareTo(b.EarliestPickup));
                foreach (var cluster in candidates)
                {
                    if (!(await TryScoreDriverAsync(cluster, recipient, avgRiders, recordRejections: false,
                            assignmentMode: AssignmentCostMode.MaximizeCoverage, token)
                        .ConfigureAwait(false)).ok)
                        continue;

                    donor.Groups.Remove(cluster);
                    recipient.Groups.Add(cluster);
                    await OrderDriverGroupsCorridorAsync(recipient, token).ConfigureAwait(false);
                    moves++;
                    moved = true;
                    break;
                }
                if (!moved) break;
            }
            return moves;
        }

        private async Task<int> TryAssignReserveGroupsAsync(
            SupeyScheduleResult result,
            List<SupeyDriverPlan> plans,
            Dictionary<MCDownloadedTrip, SupeyTripGeo> tripGeo,
            int capacityFloor,
            SupeyTemplateHints hints,
            CancellationToken token)
        {
            if (result.Reserves.Count == 0) return 0;

            var reserveTrips = new List<MCDownloadedTrip>(result.Reserves);
            result.Reserves.Clear();
            var hintsForCluster = UseTemplateHints ? hints : null;
            var clusters = await ClusterTripsAsync(reserveTrips, tripGeo, capacityFloor, token, hintsForCluster,
                ReserveRetryClusterWindowMinutes).ConfigureAwait(false);
            clusters = SupeyClusterRouting.MergeHouseholdClusters(clusters, capacityFloor);
            clusters = await ApplyMorningHubMergesAsync(clusters, capacityFloor, token).ConfigureAwait(false);
            clusters = SupeyClusterRouting.SplitClustersExceedingRiders(clusters, ReserveRetryMaxRidersPerCluster);

            int nextGroup = NextAvailableGroupNumber(plans);
            for (int i = 0; i < clusters.Count; i++)
            {
                clusters[i].GroupNumber = nextGroup + i;
                clusters[i].GroupColor = SupeyGroupPalette.For(clusters[i].GroupNumber);
            }

            clusters.Sort(CompareClustersForCoveragePriority);

            int assigned = 0;
            int processed = 0;
            foreach (var cluster in clusters)
            {
                token.ThrowIfCancellationRequested();
                if (processed >= CoverageImproveMaxReserveGroups) break;
                processed++;

                SyncClusterMetadataFromTrips(cluster);
                await SupeyClusterRouting.OptimizeClusterTourAsync(cluster, token).ConfigureAwait(false);
                await PopulateClusterPolylineAsync(cluster, token).ConfigureAwait(false);

                var pick = await ScoreAndPickForCoverageAsync(cluster, plans, preferDriver: null, token)
                    .ConfigureAwait(false);
                if (pick != null)
                {
                    pick.Groups.Add(cluster);
                    assigned++;
                    continue;
                }

                ReserveCluster(cluster, result);
            }

            return assigned;
        }

        /// <summary>
        /// When one leg of a round trip is on a driver and the partner leg is still in reserves,
        /// try to place the orphan on that same driver (timing permitting).
        /// </summary>
        private async Task<int> ReconcileLegPairsAsync(
            SupeyScheduleResult result,
            List<SupeyDriverPlan> plans,
            Dictionary<MCDownloadedTrip, SupeyTripGeo> tripGeo,
            int capacityFloor,
            CancellationToken token)
        {
            if (result.Reserves.Count == 0) return 0;

            var partnerBaseToDriver = new Dictionary<string, SupeyDriverPlan>(StringComparer.OrdinalIgnoreCase);
            var clientALegDriver = new Dictionary<string, SupeyDriverPlan>(StringComparer.OrdinalIgnoreCase);

            foreach (var plan in plans)
            {
                foreach (var cluster in plan.Groups)
                {
                    foreach (var t in cluster.Trips)
                    {
                        string tn = t.TripNumber ?? "";
                        if (string.IsNullOrEmpty(tn)) continue;
                        string pb = TripPartnerBase(tn);
                        if (!string.IsNullOrEmpty(pb))
                            partnerBaseToDriver[pb] = plan;
                        if (DetectLeg(tn) == 'A')
                        {
                            string cn = SupeyFrequentRiders.NormalizeClient(t.ClientFullName);
                            if (cn.Length >= 2)
                                clientALegDriver[cn] = plan;
                        }
                    }
                }
            }

            var orphans = new List<MCDownloadedTrip>(result.Reserves);
            result.Reserves.Clear();
            int placed = 0;

            foreach (var t in orphans)
            {
                token.ThrowIfCancellationRequested();
                if (!tripGeo.TryGetValue(t, out var g) || !g.Complete) { SupeyReserveBuckets.AddToReserves(result, t); continue; }
                if (!SupeyTripTimes.TryParsePU(t).HasValue) { SupeyReserveBuckets.AddToReserves(result, t); continue; }

                SupeyDriverPlan prefer = null;
                string pb = TripPartnerBase(t.TripNumber ?? "");
                if (!string.IsNullOrEmpty(pb) && partnerBaseToDriver.TryGetValue(pb, out var byBase))
                    prefer = byBase;
                else
                {
                    char leg = DetectLeg(t.TripNumber);
                    if (leg == 'B' || leg == 'C')
                    {
                        string cn = SupeyFrequentRiders.NormalizeClient(t.ClientFullName);
                        if (cn.Length >= 2 && clientALegDriver.TryGetValue(cn, out var byClient))
                            prefer = byClient;
                    }
                }

                var cluster = await ClusterTripsAsync(new List<MCDownloadedTrip> { t }, tripGeo, capacityFloor, token, null)
                    .ConfigureAwait(false);
                if (cluster.Count == 0) { SupeyReserveBuckets.AddToReserves(result, t); continue; }

                SyncClusterMetadataFromTrips(cluster[0]);
                await SupeyClusterRouting.OptimizeClusterTourAsync(cluster[0], token).ConfigureAwait(false);
                await PopulateClusterPolylineAsync(cluster[0], token).ConfigureAwait(false);

                var pick = await ScoreAndPickForCoverageAsync(cluster[0], plans, prefer, token)
                    .ConfigureAwait(false);
                if (pick != null)
                {
                    pick.Groups.Add(cluster[0]);
                    string pbOut = TripPartnerBase(t.TripNumber ?? "");
                    if (!string.IsNullOrEmpty(pbOut))
                        partnerBaseToDriver[pbOut] = pick;
                    placed++;
                }
                else
                    SupeyReserveBuckets.AddToReserves(result, t);
            }

            return placed;
        }

        private int CompareClustersForCoveragePriority(SupeyTripCluster a, SupeyTripCluster b)
        {
            bool fa = FrequentRiders != null && FrequentRiders.ClusterHasFrequent(a);
            bool fb = FrequentRiders != null && FrequentRiders.ClusterHasFrequent(b);
            if (fa != fb) return fb.CompareTo(fa);
            bool ma = IsMorningClinicCluster(a);
            bool mb = IsMorningClinicCluster(b);
            if (ma != mb) return mb.CompareTo(ma);
            int cmp = b.RiderCount.CompareTo(a.RiderCount);
            return cmp != 0 ? cmp : a.EarliestPickup.CompareTo(b.EarliestPickup);
        }

        private static int NextAvailableGroupNumber(List<SupeyDriverPlan> plans)
        {
            int next = 1;
            foreach (var p in plans)
            {
                foreach (var g in p.Groups)
                    if (g.GroupNumber >= next) next = g.GroupNumber + 1;
            }
            return next;
        }

        private async Task PolishAssignmentsAsync(List<SupeyDriverPlan> plans, CancellationToken token)
        {
            int moves = 0;
            const int maxMoves = 20;
            bool improved = true;
            while (improved && moves < maxMoves)
            {
                token.ThrowIfCancellationRequested();
                improved = false;
                double avgRiders = AverageRiderLoad(plans);
                foreach (var donor in plans)
                {
                    if (donor.Groups.Count == 0) continue;
                    var cluster = donor.Groups[donor.Groups.Count - 1];
                    SupeyDriverPlan bestRec = null;
                    double bestCost = double.MaxValue;
                    foreach (var rec in plans)
                    {
                        if (ReferenceEquals(rec, donor)) continue;
                        var polishScore = await TryScoreDriverAsync(cluster, rec, avgRiders, recordRejections: false,
                                assignmentMode: AssignmentCostMode.MinimizeExtension, token)
                            .ConfigureAwait(false);
                        if (!polishScore.ok) continue;
                        if (polishScore.cost < bestCost) { bestCost = polishScore.cost; bestRec = rec; }
                    }
                    if (bestRec == null) continue;
                    double before = TotalFleetMeters(plans);
                    donor.Groups.RemoveAt(donor.Groups.Count - 1);
                    bestRec.Groups.Add(cluster);
                    await OrderDriverGroupsCorridorAsync(bestRec, token).ConfigureAwait(false);
                    await SequenceDriverAsync(donor, token).ConfigureAwait(false);
                    await SequenceDriverAsync(bestRec, token).ConfigureAwait(false);
                    double after = TotalFleetMeters(plans);
                    if (after < before - 500)
                    {
                        moves++;
                        improved = true;
                        break;
                    }
                    bestRec.Groups.Remove(cluster);
                    donor.Groups.Add(cluster);
                    await SequenceDriverAsync(donor, token).ConfigureAwait(false);
                    await SequenceDriverAsync(bestRec, token).ConfigureAwait(false);
                }
            }
        }

        private static double TotalFleetMeters(List<SupeyDriverPlan> plans)
        {
            double m = 0;
            foreach (var p in plans) m += p.TotalMeters;
            return m;
        }

        private static GeoPoint PlanAnchorGeo(SupeyDriverPlan plan)
        {
            if (plan != null && plan.HomeGeo.HasValue)
                return plan.HomeGeo.Value;
            if (plan != null && plan.Groups.Count > 0)
                return FirstPickupGeo(plan.Groups[0]);
            return new GeoPoint(44.1004, -70.2148);
        }

        private static GeoPoint FirstPickupGeo(SupeyTripCluster c)
        {
            if (c == null || c.Trips.Count == 0)
                return c?.PickupCentroid ?? new GeoPoint(44.1004, -70.2148);
            int idx = FirstPickupIndex(c);
            if (c.PickupPoints != null && idx >= 0 && idx < c.PickupPoints.Count)
                return c.PickupPoints[idx];
            return c.PickupCentroid;
        }

        private enum AssignmentCostMode
        {
            MinimizeExtension,
            MaximizeCoverage,
        }

        /// <summary>
        /// Hard feasibility + soft cost for one (driver, cluster) pair — OSRM deadhead + in-group legs.
        /// </summary>
        private async Task<(bool ok, double cost)> TryScoreDriverAsync(
            SupeyTripCluster cluster,
            SupeyDriverPlan p,
            double avgRiders,
            bool recordRejections,
            AssignmentCostMode assignmentMode,
            CancellationToken token,
            double puLateGraceMinutes = 0,
            double doLateGraceMinutes = 0)
        {
            double cost = double.MaxValue;

            if (cluster.RiderCount > p.Driver.CapacityPassengers)
            {
                if (recordRejections) cluster.Rejections.Capacity.Add(p.Driver.Name);
                return (false, cost);
            }

            if (ScheduleRules != null && ScheduleRules.IsDriverBlockedForCluster(p.Driver.Name, cluster))
            {
                if (recordRejections) cluster.Rejections.PolicyAvoid.Add(p.Driver.Name);
                return (false, cost);
            }

            var shiftStart = p.Driver.ParseShiftStart();
            var shiftEnd = p.Driver.ParseShiftEnd();
            if (shiftStart.HasValue && cluster.EarliestPickup < shiftStart.Value)
            {
                if (recordRejections) cluster.Rejections.ShiftStart.Add(p.Driver.Name);
                return (false, cost);
            }

            if (!await EnsureClusterOsrmAsync(cluster, token).ConfigureAwait(false))
            {
                if (recordRejections) cluster.Rejections.OsrmUnavailable.Add(p.Driver.Name);
                return (false, cost);
            }

            var (currentLastDO, currentLastLoc) = await ProjectedLastEventAsync(p, token).ConfigureAwait(false);
            int firstPu = FirstPickupIndex(cluster);
            var deadhead = await GetDeadheadToClusterAsync(p, cluster, token).ConfigureAwait(false);
            if (!deadhead.FromOsrm)
            {
                if (recordRejections) cluster.Rejections.OsrmUnavailable.Add(p.Driver.Name);
                return (false, cost);
            }

            double dhSeconds = deadhead.Seconds;
            var arrivalAtFirstPU = currentLastDO.Add(TimeSpan.FromSeconds(dhSeconds));
            if (!SupeyClusterRouting.IsValidVisitOrder(cluster.PickupOrder, cluster.Trips.Count))
            {
                if (recordRejections) cluster.Rejections.OsrmUnavailable.Add(p.Driver.Name);
                return (false, cost);
            }
            if (!await SupeyClusterRouting.PickupOrderMeetsScheduledWindowsAsync(
                    cluster, new List<int>(cluster.PickupOrder), token, currentLastLoc, arrivalAtFirstPU)
                .ConfigureAwait(false))
            {
                if (recordRejections)
                {
                    if (p.Groups.Count > 0)
                        cluster.Rejections.TimeConflict.Add(p.Driver.Name);
                    else
                        cluster.Rejections.PuLate.Add(p.Driver.Name);
                }
                return (false, cost);
            }

            double doCap = DoLateMaxMinutes + doLateGraceMinutes;
            double clusterDoCap = SupeyTripTimingPolicy.DoLateCapMinutesForCluster(cluster);
            var (feasible, clusterEnd, lateTripIdx, lateMinutes) =
                ProjectClusterFeasibility(cluster, arrivalAtFirstPU, clusterDoCap);
            if (!feasible)
            {
                if (recordRejections)
                {
                    cluster.Rejections.DoInfeasible.Add(p.Driver.Name);
                    if (lateTripIdx >= 0 && lateTripIdx < cluster.Trips.Count
                        && string.IsNullOrEmpty(cluster.Rejections.LateRiderNote))
                    {
                        var t = cluster.Trips[lateTripIdx];
                        cluster.Rejections.LateRiderNote =
                            (t.ClientLastName ?? t.ClientFullName ?? t.TripNumber ?? "?")
                            + " late by " + ((int)Math.Round(lateMinutes)) + " min";
                    }
                }
                return (false, cost);
            }
            if (shiftEnd.HasValue && clusterEnd > shiftEnd.Value)
            {
                if (recordRejections) cluster.Rejections.ShiftEnd.Add(p.Driver.Name);
                return (false, cost);
            }

            if (!p.HomeGeo.HasValue)
            {
                if (recordRejections) cluster.Rejections.PolicyAvoid.Add(p.Driver.Name);
                return (false, cost);
            }

            double homeAffinitySeconds = 0;
            if (p.HomeGeo.HasValue && firstPu >= 0 && firstPu < cluster.PickupPoints.Count)
            {
                var homeLeg = await GetDriveLegAsync(p.HomeGeo.Value, cluster.PickupPoints[firstPu], token)
                    .ConfigureAwait(false);
                if (homeLeg.FromOsrm) homeAffinitySeconds = homeLeg.Seconds;
            }

            double extensionSeconds;
            if (p.Groups.Count == 0)
                extensionSeconds = (clusterEnd - cluster.EarliestPickup).TotalSeconds + dhSeconds;
            else
            {
                extensionSeconds = (clusterEnd - currentLastDO).TotalSeconds;
                if (extensionSeconds < 0) extensionSeconds = 0;
            }

            int currentRiders = TotalRiders(p);
            if (assignmentMode == AssignmentCostMode.MaximizeCoverage)
            {
                // Pass A: prefer idle/underloaded drivers; mild home affinity.
                cost = homeAffinitySeconds * 0.5;
                if (p.Groups.Count == 0) cost -= 3600.0;
                else if (p.Groups.Count <= 2 && cluster.RiderCount >= 3) cost -= 900.0;
                if (avgRiders > 0 && currentRiders < avgRiders * UnderloadedThresholdFraction)
                {
                    double under = avgRiders - currentRiders;
                    cost -= Math.Min(UnderloadedMaxCreditSeconds * 3, under * UnderloadedCreditPerRiderSeconds * 3);
                }
            }
            else
            {
                cost = HomeAffinityWeight * homeAffinitySeconds
                     + ActiveWindowWeight * extensionSeconds;
                if (avgRiders > 0 && currentRiders < avgRiders * UnderloadedThresholdFraction)
                {
                    double under = avgRiders - currentRiders;
                    cost -= Math.Min(UnderloadedMaxCreditSeconds, under * UnderloadedCreditPerRiderSeconds);
                }
            }

            if (ScheduleRules != null)
            {
                cost -= ScheduleRules.PreferredPairingBonusSeconds(cluster, p.Driver.Name);
                cost += ScheduleRules.LoadPreferencePenaltySeconds(cluster, p.Driver.Name);
            }

            cost -= await CorridorAssignmentBonusAsync(p, cluster, currentLastLoc, firstPu, token)
                .ConfigureAwait(false);

            if (FrequentRiders != null && FrequentRiders.ClusterHasFrequent(cluster))
                cost -= FrequentRiderCoverageBonusSeconds;

            if (ClusterMatchesPartnerDriverOnPlan(cluster, p))
                cost -= LegPairSameDriverBonusSeconds;

            if (UseTemplateHints && Hints != null && Hints.HasAnyTemplate)
            {
                foreach (var t in cluster.Trips)
                {
                    var preferred = Hints.PreferredDriverFor(t.TripNumber);
                    if (preferred != null &&
                        string.Equals(preferred, p.Driver.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        cost -= TemplateHintBonusSeconds;
                        break;
                    }
                }
                if (Hints.DriverTripOrder != null &&
                    Hints.DriverTripOrder.TryGetValue(p.Driver.Name ?? "", out var seq) &&
                    seq != null && seq.Count > 0)
                {
                    int matches = 0;
                    foreach (var t in cluster.Trips)
                    {
                        if (string.IsNullOrEmpty(t.TripNumber)) continue;
                        for (int si = 0; si < seq.Count; si++)
                        {
                            if (string.Equals(seq[si], t.TripNumber, StringComparison.OrdinalIgnoreCase))
                            {
                                matches++;
                                break;
                            }
                        }
                    }
                    if (matches > 0)
                        cost -= HistoricalPairBonusSeconds * matches;
                }
            }

            return (true, cost);
        }

        /// <summary>Modivcare trip number without the -A / -B / -C leg suffix.</summary>
        internal static string TripPartnerBase(string tripNumber)
        {
            if (string.IsNullOrEmpty(tripNumber)) return "";
            int len = tripNumber.Length;
            if (len >= 2 && tripNumber[len - 2] == '-')
            {
                char c = char.ToUpperInvariant(tripNumber[len - 1]);
                if (c == 'A' || c == 'B' || c == 'C')
                    return tripNumber.Substring(0, len - 2);
            }
            return tripNumber;
        }

        private bool ClusterMatchesPartnerDriverOnPlan(SupeyTripCluster cluster, SupeyDriverPlan plan)
        {
            if (cluster == null || plan == null) return false;
            foreach (var t in cluster.Trips)
            {
                string pb = TripPartnerBase(t.TripNumber ?? "");
                if (string.IsNullOrEmpty(pb)) continue;
                foreach (var g in plan.Groups)
                {
                    foreach (var assigned in g.Trips)
                    {
                        if (string.Equals(TripPartnerBase(assigned.TripNumber ?? ""), pb,
                                StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Mean rider count across all driver plans (= cluster sizes summed / driver count).
        /// Used by the load-balance credit so an idle driver gets a small score advantage on
        /// future clusters; in particular this prevents one driver from winning every cluster
        /// they're tied on while another sits at 0 riders all day.
        /// </summary>
        private static double AverageRiderLoad(List<SupeyDriverPlan> plans)
        {
            if (plans == null || plans.Count == 0) return 0;
            int total = 0;
            foreach (var p in plans) total += TotalRiders(p);
            return (double)total / plans.Count;
        }

        private static int TotalRiders(SupeyDriverPlan p)
        {
            if (p.Groups.Count == 0) return 0;
            int total = 0;
            foreach (var g in p.Groups) total += g.RiderCount;
            return total;
        }

        /// <summary>
        /// Walks the driver's currently-assigned clusters in chronological order and returns
        /// when (and where) they actually become free, propagating real lateness forward. This
        /// is what every feasibility check in <see cref="ScoreAndPick"/> uses for the candidate
        /// cluster's "after my last assignment, can I make this one?" question.
        /// </summary>
        /// <remarks>
        /// PRIOR BUG: this used to return the previous cluster's <c>HardestDropoff</c> (= the
        /// appointment time, not when the driver actually finished) — which made drivers look
        /// busier than they were and dumped huge numbers of trips into Reserves.
        /// <para/>
        /// SECOND BUG: the first attempted fix used <c>last.EarliestPickup + IntraClusterDrive</c>
        /// — i.e., assumed the driver hits every PU exactly on time. That's optimistic: when a
        /// driver was running 15 min late at PU 1, the algo still pretended they were on time
        /// for PU 2, then PU 3, etc. By the 5th cluster the prediction was an hour off, which
        /// is why post-sequencing we saw "may miss DO appt by 90 min" warnings — scoring had
        /// approved assignments that reality couldn't deliver.
        /// <para/>
        /// CURRENT: walk every previously-assigned cluster from shift-start outward, simulating
        /// dead-head + intra-cluster drive at each step and carrying any accumulated lateness
        /// into the next leg. The returned time is the driver's *actual* projected free moment
        /// after the entire chain so far. O(N) per call, O(N²) across a build, but N is small
        /// per driver (~10–25 clusters).
        /// </remarks>

        /// <summary>
        /// Computes the moment the cluster ends (driver becomes free at the last DO) AND
        /// whether every rider's individual drop-off deadline is met along the way. Models:
        /// <list type="bullet">
        ///   <item><b>A-leg early-pickup compression.</b> All-A-leg cluster can effectively
        ///         start up to 29 min before <see cref="SupeyTripCluster.EarliestPickup"/>.</item>
        ///   <item><b>Last PU before drops.</b> Clock follows drive + PU windows (wait only if
        ///         too early), not idle until each scheduled time.</item>
        ///   <item><b>Mid-tour drops.</b> Riders are dropped in deadline order; each rider
        ///         must arrive by their own appointment time, not by the cluster's earliest
        ///         deadline. This is what unlocks the "drop the 7:30 appointment on the way
        ///         to the 8:30 dialysis crowd" tours that the dispatcher uses constantly.</item>
        /// </list>
        /// Riders with no deadline (B/C return rides where Modivcare emits 00:00) are skipped
        /// in the per-rider check — the shift window still bounds when the day can run.
        /// </summary>
        private static (bool feasible, TimeSpan end, int latestRiderTripIndex, double latestRiderMinutesLate)
            ProjectClusterFeasibility(SupeyTripCluster c, TimeSpan arrivalAtFirstPU, double doLateMaxMinutes = DoLateMaxMinutes)
        {
            var startAtFirstPU = arrivalAtFirstPU > c.EffectiveEarliestPickup
                ? arrivalAtFirstPU : c.EffectiveEarliestPickup;

            int firstPuIdx = c.PickupOrder.Count > 0 ? c.PickupOrder[0] : 0;
            startAtFirstPU = SupeyDispatchDriveClock.AfterPickup(c, firstPuIdx, startAtFirstPU);

            var startAtLastPU = c.PickupOrder.Count > 0
                ? SupeyDispatchDriveClock.DepartureAfterLastPickup(c, c.PickupOrder, startAtFirstPU)
                : startAtFirstPU;

            var dropRun = SupeyDispatchDriveClock.ProjectDropRun(c, startAtLastPU);
            return (dropRun.feasible, dropRun.end, dropRun.worstLateTrip, dropRun.worstLateMinutes);
        }

        /// <summary>
        /// Convenience wrapper used by callers that only care about the cluster end time, not
        /// per-rider feasibility (sequencing, warning timing, projected-last-event). Same
        /// projection as <see cref="ProjectClusterFeasibility"/> minus the deadline check.
        /// </summary>
        private static TimeSpan ProjectClusterEnd(SupeyTripCluster c, TimeSpan arrivalAtFirstPU)
        {
            var (_, end, _, _) = ProjectClusterFeasibility(c, arrivalAtFirstPU);
            return end;
        }

        /// <summary>
        /// Returns the moment the driver picks up the LAST rider in the cluster, given when
        /// they arrive at the first PU. Same head-drive + wait-for-last-window logic as the
        /// full feasibility projection, broken out for callers (per-rider warning timing)
        /// that need to step through drop-offs themselves rather than trust the cached end.
        /// </summary>
        private static TimeSpan ComputeStartAtLastPU(SupeyTripCluster c, TimeSpan arrivalAtFirstPU)
        {
            var startAtFirstPU = arrivalAtFirstPU > c.EffectiveEarliestPickup
                ? arrivalAtFirstPU : c.EffectiveEarliestPickup;
            int firstPuIdx = c.PickupOrder.Count > 0 ? c.PickupOrder[0] : 0;
            startAtFirstPU = SupeyDispatchDriveClock.AfterPickup(c, firstPuIdx, startAtFirstPU);
            return c.PickupOrder.Count > 0
                ? SupeyDispatchDriveClock.DepartureAfterLastPickup(c, c.PickupOrder, startAtFirstPU)
                : startAtFirstPU;
        }

        // ----- Phase 6 -----

        private async Task SequenceDriverAsync(SupeyDriverPlan plan, CancellationToken token)
        {
            plan.DeadHeads.Clear();
            plan.TotalDriveSeconds = 0;
            plan.TotalMeters = 0;
            plan.FirstPickup = null;
            plan.LastDropoff = null;
            plan.ReleaseTimeOfDay = null;
            plan.Warnings.Clear();

            if (plan.Groups.Count == 0) return;

            bool hasHome = plan.HomeGeo.HasValue;

            if (hasHome)
            {
                await AddDeadHeadAsync(plan, plan.HomeGeo.Value, FirstPickupGeo(plan.Groups[0]),
                    "Home → Group " + plan.Groups[0].GroupNumber, token).ConfigureAwait(false);
            }
            else
            {
                plan.DeadHeads.Add(new SupeyDeadHeadSegment
                {
                    Label = "Start → Group " + plan.Groups[0].GroupNumber,
                    DurationSeconds = 0,
                    DistanceMeters = 0
                });
            }

            for (int i = 1; i < plan.Groups.Count; i++)
            {
                var prev = plan.Groups[i - 1];
                var curr = plan.Groups[i];
                await AddDeadHeadAsync(plan, LastDropoffPoint(prev),
                    FirstPickupGeo(curr),
                    "Group " + prev.GroupNumber + " → Group " + curr.GroupNumber, token).ConfigureAwait(false);
            }

            var lastGroup = plan.Groups[plan.Groups.Count - 1];
            if (hasHome)
            {
                await AddDeadHeadAsync(plan,
                    LastDropoffPoint(lastGroup),
                    plan.HomeGeo.Value,
                    "Group " + lastGroup.GroupNumber + " → Home", token).ConfigureAwait(false);
            }

            // Add intra-cluster totals.
            foreach (var g in plan.Groups)
            {
                plan.TotalDriveSeconds += g.IntraClusterDriveSeconds;
                plan.TotalMeters += g.IntraClusterMeters;
            }

            // Walk the day forward (mirroring EvaluateWarnings / ProjectedLastEvent) so the
            // displayed first/last/release times are the actual projected values, not the
            // scheduled-deadline placeholders. Without this, a compressed all-A-leg morning
            // load shows a release time half an hour later than reality, and consolidation
            // decisions made earlier in the build don't agree with the stats strip.
            var shiftStart = plan.Driver.ParseShiftStart() ?? TimeSpan.Zero;
            var current = shiftStart;
            for (int i = 0; i < plan.Groups.Count; i++)
            {
                double dhSec = i < plan.DeadHeads.Count ? plan.DeadHeads[i].DurationSeconds : 0;
                var arrival = current.Add(TimeSpan.FromSeconds(dhSec));
                current = ProjectClusterEnd(plan.Groups[i], arrival);
            }

            plan.FirstPickup = SupeyClusterTimeSplit.MinPickupTime(plan.Groups[0]);
            plan.LastDropoff = current; // actual end of the last cluster, not its appointment time
            if (hasHome && plan.DeadHeads.Count > 0)
            {
                var finalReturn = plan.DeadHeads[plan.DeadHeads.Count - 1];
                plan.ReleaseTimeOfDay = current.Add(TimeSpan.FromSeconds(finalReturn.DurationSeconds));
            }
            else
                plan.ReleaseTimeOfDay = current;
        }

        private async Task AddDeadHeadAsync(SupeyDriverPlan plan, GeoPoint from, GeoPoint to,
            string label, CancellationToken token)
        {
            var seg = new SupeyDeadHeadSegment { From = from, To = to, Label = label };
            var path = new List<GeoPoint> { from, to };
            var leg = await SupeyOsrmLegs.GetLegAsync(from, to, token).ConfigureAwait(false);
            if (leg.Ok)
            {
                seg.DistanceMeters = leg.Meters;
                seg.DurationSeconds = leg.Seconds;
                seg.IsStraightLineFallback = false;
                var route = await SupeyOsrmLegs.RouteAsync(path, token).ConfigureAwait(false);
                if (route.Ok && route.Polyline != null)
                    seg.Polyline.AddRange(route.Polyline);
                else
                {
                    seg.Polyline.Add(from);
                    seg.Polyline.Add(to);
                }
            }
            else
            {
                seg.IsStraightLineFallback = true;
                seg.DistanceMeters = 0;
                seg.DurationSeconds = 0;
                seg.Polyline.Add(from);
                seg.Polyline.Add(to);
            }
            plan.DeadHeads.Add(seg);
            if (leg.Ok)
            {
                plan.TotalDriveSeconds += seg.DurationSeconds;
                plan.TotalMeters += seg.DistanceMeters;
            }
        }

        // ----- Phase 7 -----

        /// <summary>
        /// Tries to release the earliest-finishing drivers earlier by moving their tail clusters
        /// onto a driver who's already going to be working that late. Only accepts a move when it
        /// reduces total fleet active-hours and respects every hard constraint.
        /// </summary>
        private async Task ConsolidateAsync(List<SupeyDriverPlan> plans, CancellationToken token)
        {
            // Naive single-pass hill-climb. Bigger changes happen automatically because the score
            // function in phase 4 already prefers consolidation; this is a polish step.
            bool improved = true;
            int safety = 32;
            while (improved && safety-- > 0)
            {
                token.ThrowIfCancellationRequested();
                improved = false;

                var donors = new List<SupeyDriverPlan>(plans);
                donors.Sort((a, b) =>
                {
                    var ar = a.ReleaseTimeOfDay ?? TimeSpan.MaxValue;
                    var br = b.ReleaseTimeOfDay ?? TimeSpan.MaxValue;
                    return ar.CompareTo(br);
                });

                foreach (var donor in donors)
                {
                    if (donor.Groups.Count == 0) continue;
                    var lastGroup = donor.Groups[donor.Groups.Count - 1];
                    SupeyDriverPlan bestRecipient = null;
                    foreach (var rec in plans)
                    {
                        if (ReferenceEquals(rec, donor)) continue;
                        if (rec.Groups.Count == 0) continue;
                        if (lastGroup.RiderCount > rec.Driver.CapacityPassengers) continue;
                        // Recipient must already be working at least as late as the moved cluster
                        // (use projected end, not appointment time).
                        var (recProjectedEnd, _) = await ProjectedLastEventAsync(rec, token)
                            .ConfigureAwait(false);
                        if (recProjectedEnd < lastGroup.EarliestPickup) continue;

                        bestRecipient = rec;
                        break;
                    }
                    if (bestRecipient == null) continue;

                    double avgRiders = AverageRiderLoad(plans);
                    if (!(await TryScoreDriverAsync(lastGroup, bestRecipient, avgRiders, recordRejections: false,
                            assignmentMode: AssignmentCostMode.MinimizeExtension, token)
                        .ConfigureAwait(false)).ok)
                        continue;

                    double beforeFleet = TotalFleetSeconds(plans);
                    donor.Groups.RemoveAt(donor.Groups.Count - 1);
                    bestRecipient.Groups.Add(lastGroup);
                    await OrderDriverGroupsCorridorAsync(bestRecipient, token).ConfigureAwait(false);
                    await SequenceDriverAsync(donor, token).ConfigureAwait(false);
                    await SequenceDriverAsync(bestRecipient, token).ConfigureAwait(false);
                    double afterFleet = TotalFleetSeconds(plans);

                    if (afterFleet < beforeFleet)
                    {
                        improved = true;
                        break; // Restart with updated state.
                    }
                    else
                    {
                        // Undo.
                        bestRecipient.Groups.Remove(lastGroup);
                        donor.Groups.Add(lastGroup);
                        await OrderDriverGroupsCorridorAsync(donor, token).ConfigureAwait(false);
                        await SequenceDriverAsync(donor, token).ConfigureAwait(false);
                        await SequenceDriverAsync(bestRecipient, token).ConfigureAwait(false);
                    }
                }
            }
        }

        private static double TotalFleetSeconds(List<SupeyDriverPlan> plans)
        {
            double t = 0;
            foreach (var p in plans) t += p.TotalDriveSeconds;
            return t;
        }

        // ----- Phase 8 -----

        private static void EvaluateWarnings(SupeyDriverPlan plan)
        {
            if (plan.Groups.Count == 0) return;

            // Walk the day forward in chronological order so reported times line up with the
            // identical projection ScoreAndPick / ProjectedLastEvent used to APPROVE the
            // assignment. Previous code used prev.HardestDropoff as the prev cluster's end
            // time, which mis-modeled A-leg early-pickup compression and accumulated drift
            // across multiple clusters — that's how a driver was approved at scoring time
            // and then flagged "late by 88 min" by warnings on the same schedule.
            var shiftStart = plan.Driver.ParseShiftStart() ?? TimeSpan.Zero;
            var current = shiftStart;

            for (int i = 0; i < plan.Groups.Count; i++)
            {
                var g = plan.Groups[i];
                // DeadHeads layout per SequenceDriverAsync: [0] = Home → Group 0,
                // [1] = Group 0 → Group 1, ..., [N-1] = Group N-2 → Group N-1, [N] = Group N-1 → Home.
                // So the dh BEFORE cluster i is DeadHeads[i].
                double dhSec = i < plan.DeadHeads.Count ? plan.DeadHeads[i].DurationSeconds : 0;
                var arrivalAtFirstPU = current.Add(TimeSpan.FromSeconds(dhSec));

                if (i == 0)
                {
                    double puCap = LegPuLateCapMinutes(g);
                    var scheduledFirstPu = SupeyClusterTimeSplit.MinPickupTime(g);
                    if (arrivalAtFirstPU > scheduledFirstPu.Add(TimeSpan.FromMinutes(puCap)))
                    {
                        int firstIdx = FirstPickupIndex(g);
                        string firstTn = firstIdx >= 0 && firstIdx < g.Trips.Count
                            ? g.Trips[firstIdx].TripNumber : "";
                        plan.Warnings.Add(new SupeyWarning(SupeyWarningKind.LateArrival,
                            firstTn ?? "", plan.Driver.Name,
                            "Driver may arrive at first PU around " +
                            SupeyTripTimes.FormatTimeOfDay(arrivalAtFirstPU) + " (scheduled " +
                            SupeyTripTimes.FormatTimeOfDay(scheduledFirstPu) + ")."));
                    }
                }

                if (g.IsStraightLineFallback)
                {
                    plan.Warnings.Add(new SupeyWarning(SupeyWarningKind.StraightLineFallback,
                        g.Trips.Count > 0 ? g.Trips[0].TripNumber : "", plan.Driver.Name,
                        "Group " + g.GroupNumber + " uses a straight-line route (OSRM unreachable)."));
                }

                // Per-rider feasibility — flags the SPECIFIC rider whose deadline is at risk
                // (or missed), not just "Group X late by N min". Lets the dispatcher zero in
                // on which appointment is the problem instead of guessing across 4 riders.
                var (feas, groupEnd, worstTripIdx, worstMinutes) =
                    ProjectClusterFeasibility(g, arrivalAtFirstPU);
                if (!feas && worstTripIdx >= 0 && worstTripIdx < g.Trips.Count && worstMinutes > 0)
                {
                    var worstTrip = g.Trips[worstTripIdx];
                    double capped = SupeyTripTimingPolicy.DoLateCapMinutes(worstTrip);
                    double displayMin = Math.Max(0, worstMinutes - capped);
                    if (displayMin > 0)
                    {
                        string detail = "Group " + g.GroupNumber + " — " +
                            (worstTrip.ClientFullName ?? worstTrip.TripNumber ?? "rider") +
                            " may miss DO appt by " + displayMin.ToString("0") + " min.";
                        plan.Warnings.Add(new SupeyWarning(SupeyWarningKind.LateArrival,
                            worstTrip.TripNumber ?? "", plan.Driver.Name, detail));
                    }
                }
                else if (feas)
                {
                    // Even when nobody's late, surface the tightest single rider so the
                    // dispatcher can see which appointment is closest to the wire.
                    double tightestMinutes = double.MaxValue;
                    int tightestIdx = -1;
                    var stepCurrent = ComputeStartAtLastPU(g, arrivalAtFirstPU);
                    for (int j = 0; j < g.DropoffOrder.Count; j++)
                    {
                        double legSec = j < g.DropoffLegSeconds.Count ? g.DropoffLegSeconds[j] : 0;
                        stepCurrent = stepCurrent.Add(TimeSpan.FromSeconds(legSec));
                        var trip = g.Trips[g.DropoffOrder[j]];
                        SupeyDispatchDriveClock.DropWindow(trip, out TimeSpan sched, out _, out TimeSpan latest);
                        if (sched <= TimeSpan.Zero) continue;
                        double slackMin = (latest - stepCurrent).TotalMinutes;
                        stepCurrent = SupeyDispatchDriveClock.AfterDropoff(trip, stepCurrent);
                        if (slackMin < tightestMinutes)
                        {
                            tightestMinutes = slackMin;
                            tightestIdx = g.DropoffOrder[j];
                        }
                    }
                    if (tightestIdx >= 0)
                    {
                        var t = g.Trips[tightestIdx];
                        if (tightestMinutes < 0)
                        {
                            double cap = SupeyTripTimingPolicy.DoLateCapMinutes(t);
                            double lateMin = -tightestMinutes - cap;
                            if (lateMin > 0)
                            {
                                plan.Warnings.Add(new SupeyWarning(SupeyWarningKind.LateArrival,
                                    t.TripNumber ?? "", plan.Driver.Name,
                                    "Group " + g.GroupNumber + " — " +
                                    (t.ClientFullName ?? t.TripNumber ?? "rider") +
                                    " may miss DO appt by " + lateMin.ToString("0") + " min."));
                            }
                        }
                        else if (tightestMinutes < TightArrivalSlackMinutes)
                        {
                            plan.Warnings.Add(new SupeyWarning(SupeyWarningKind.TightArrival,
                                t.TripNumber ?? "", plan.Driver.Name,
                                "Group " + g.GroupNumber + " — " +
                                (t.ClientFullName ?? t.TripNumber ?? "rider") +
                                " arrives with only " + tightestMinutes.ToString("0") + " min of slack."));
                        }
                    }
                }

                // Carry the actual finish forward so the next cluster's arrivalAtFirstPU
                // computation is anchored at reality, not at HardestDropoff.
                current = groupEnd;
            }
        }

        // ----- Leg-type rule helpers -----

        /// <summary>
        /// Reads the leg suffix (<c>-A</c> / <c>-B</c> / <c>-C</c>) off a Modivcare trip number.
        /// Matches the website's behavior (<c>check_scoreboard_trips.php</c> /
        /// <c>manage_daily_scores.php</c>) which defaults unrecognized suffixes to <c>'B'</c>.
        /// </summary>
        internal static char DetectLegPublic(string tripNumber) => DetectLeg(tripNumber);

        private static char DetectLeg(string tripNumber)
        {
            if (string.IsNullOrEmpty(tripNumber)) return 'B';
            int len = tripNumber.Length;
            if (len >= 2 && tripNumber[len - 2] == '-')
            {
                char c = char.ToUpperInvariant(tripNumber[len - 1]);
                if (c == 'A' || c == 'B' || c == 'C') return c;
            }
            return 'B';
        }

        /// <summary>
        /// Returns the pickup-lateness cap (in minutes) that applies to a whole cluster. The
        /// cluster picks the strictest cap of any trip in it, so a mixed-leg cluster gets the
        /// 14-min A-leg cap rather than the looser 29-min B/C cap.
        /// </summary>
        private static double LegPuLateCapMinutes(SupeyTripCluster cluster)
        {
            double cap = BcLegPuLateMaxMinutes;
            foreach (var t in cluster.Trips)
            {
                if (DetectLeg(t.TripNumber) == 'A')
                {
                    cap = ALegPuLateMaxMinutes;
                    break;
                }
            }
            return cap;
        }

        // ----- Geometry helpers -----

        private static GeoPoint Centroid(List<GeoPoint> pts)
        {
            if (pts == null || pts.Count == 0) return new GeoPoint(0, 0);
            double sLat = 0, sLng = 0;
            foreach (var p in pts) { sLat += p.Lat; sLng += p.Lng; }
            return new GeoPoint(sLat / pts.Count, sLng / pts.Count);
        }

        private static double HaversineMeters(GeoPoint a, GeoPoint b)
        {
            const double R = 6371000.0;
            double lat1 = a.Lat * Math.PI / 180.0;
            double lat2 = b.Lat * Math.PI / 180.0;
            double dLat = (b.Lat - a.Lat) * Math.PI / 180.0;
            double dLng = (b.Lng - a.Lng) * Math.PI / 180.0;
            double h = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                     + Math.Cos(lat1) * Math.Cos(lat2) * Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
            double c = 2 * Math.Atan2(Math.Sqrt(h), Math.Sqrt(1 - h));
            return R * c;
        }

        private static double HaversineMetersAlong(List<GeoPoint> path)
        {
            if (path == null || path.Count < 2) return 0;
            double total = 0;
            for (int i = 1; i < path.Count; i++) total += HaversineMeters(path[i - 1], path[i]);
            return total;
        }
    }
}
