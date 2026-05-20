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
    /// 5. Greedy-assign clusters to lowest-cost feasible drivers in earliest-PU order.
    /// 5b. Pass C — swap groups for balance and retry reserve clusters as groups.
    /// 6. Sequence each driver's day, fetching real OSRM dead-head geometry.
    /// 7. Consolidation hill-climb — collapse late trips onto fewer drivers when it cuts fleet hours.
    /// 8. Reserves + warnings — anything unassigned plus per-driver feasibility re-checks.
    /// </summary>
    /// <remarks>
    /// Scoring uses haversine + average street speed for the (D × G) cost matrix so we don't burn
    /// OSRM throughput on probabilistic estimates; OSRM is reserved for in-cluster + dead-head
    /// geometry that actually gets rendered.
    /// <para/>
    /// The user-stated optimization target is "minimize total fleet active-hours and miles, then
    /// release drivers as early as possible". We implement that via a <c>λ × activeWindowExtension</c>
    /// term — assigning a late cluster to a driver already working late is cheap; assigning it to
    /// an early driver is expensive, so late trips naturally consolidate.
    /// </remarks>
    internal sealed class SupeyScheduleAlgorithm
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
        //   - B/C   : 0–29 min late.  30+ min late = LATE. (And early arrivals on B/C are
        //             "too early"; we don't enforce that here because the scheduler waits at
        //             the door — actual PU happens at the scheduled time, not on arrival.)
        // Dropoff lateness allowed (cluster end after the hardest deadline):
        //   - All legs: 0 min. At-deadline-or-after counts as LATE.
        private const double ALegPuLateMaxMinutes = 14.0;
        private const double BcLegPuLateMaxMinutes = 29.0;
        private const double DoLateMaxMinutes = 0.0;

        // Pass A: cap extra PU slack when every driver shows time-conflict (minutes beyond normal A/B cap).
        private const double CoverageMaxPuSlackMinutes = 8.0;
        private static readonly TimeSpan MorningHubWindowStart = new TimeSpan(6, 30, 0);
        private static readonly TimeSpan MorningHubWindowEnd = new TimeSpan(9, 30, 0);

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

        public SupeyRouteCache RouteCache { get; } = new SupeyRouteCache();

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
                        "Could not place driver home: " + (d.FormatHomeOneLine() ?? "(empty)") +
                        ". Check the spelling (especially the city) and rebuild to include this driver."));
                }
            }

            // Trips that didn't geocode go straight to Reserves with a warning.
            var routableTrips = new List<MCDownloadedTrip>(trips.Count);
            foreach (var t in trips)
            {
                if (!tripGeo[t].Complete)
                {
                    result.Reserves.Add(t);
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
                    result.Reserves.Add(t);
                    continue;
                }
                routableTrips.Add(t);
            }

            // -------- Phase 2: Cluster --------
            progress?.Report("Clustering " + routableTrips.Count + " trips...");
            int capacityFloor = ResolveCapacityFloor(validDrivers);
            var hintsForCluster = UseTemplateHints ? Hints : null;
            var clusters = ClusterTrips(routableTrips, tripGeo, capacityFloor, token, hintsForCluster);
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
                FingerprintCluster(c);
                SupeyClusterRouting.OptimizeClusterTour(c);
                await PopulateClusterPolylineAsync(c, token).ConfigureAwait(false);
                fingerprinted++;
                if ((fingerprinted % 3) == 0 || fingerprinted == clusters.Count)
                    progress?.Report("In-group routing " + fingerprinted + " / " + clusters.Count + "...");
            }

            int clusterCountBeforeMerge = clusters.Count;
            clusters = SupeyClusterRouting.MergeHouseholdClusters(clusters, capacityFloor);
            if (clusters.Count != clusterCountBeforeMerge)
            {
                progress?.Report("Merged " + (clusterCountBeforeMerge - clusters.Count) +
                    " household group(s); re-routing...");
                for (int i = 0; i < clusters.Count; i++)
                {
                    token.ThrowIfCancellationRequested();
                    clusters[i].RoutePolyline.Clear();
                    FingerprintCluster(clusters[i]);
                    SupeyClusterRouting.OptimizeClusterTour(clusters[i]);
                    await PopulateClusterPolylineAsync(clusters[i], token).ConfigureAwait(false);
                }
            }

            int clusterCountBeforeSplit = clusters.Count;
            clusters = SupeyClusterRouting.SplitInefficientClusters(clusters);
            if (clusters.Count != clusterCountBeforeSplit)
            {
                progress?.Report("Re-routing " + clusters.Count + " group(s) after mileage split...");
                for (int i = 0; i < clusters.Count; i++)
                {
                    token.ThrowIfCancellationRequested();
                    clusters[i].RoutePolyline.Clear();
                    FingerprintCluster(clusters[i]);
                    SupeyClusterRouting.OptimizeClusterTour(clusters[i]);
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
            remaining.Sort((a, b) =>
            {
                int cmp = b.RiderCount.CompareTo(a.RiderCount);
                return cmp != 0 ? cmp : a.EarliestPickup.CompareTo(b.EarliestPickup);
            });

            progress?.Report("Pass A: morning clinic hubs...");
            AssignMorningHubWaves(remaining, driverPlans, avgRidersForPassA);

            progress?.Report("Pass A: assigning groups for coverage...");
            foreach (var cluster in remaining)
            {
                token.ThrowIfCancellationRequested();
                await TryAssignClusterAsync(cluster, driverPlans, result, progress, token, splitDepth: 0)
                    .ConfigureAwait(false);
            }

            progress?.Report("Pass B: polishing assignments...");
            await PolishAssignmentsAsync(driverPlans, token).ConfigureAwait(false);

            progress?.Report("Pass C: improving coverage (group swaps + reserves)...");
            ImproveCoverage(result, driverPlans, tripGeo, capacityFloor, hintsForCluster, progress, token);

            // -------- Phase 6: Sequence each driver --------
            progress?.Report("Sequencing dead-heads for " + driverPlans.Count + " driver(s)...");
            int seqDone = 0;
            foreach (var plan in driverPlans)
            {
                token.ThrowIfCancellationRequested();
                ReorderDriverGroups(plan);
                plan.Groups.Sort((a, b) => a.EarliestPickup.CompareTo(b.EarliestPickup));
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

        private static List<SupeyTripCluster> ClusterTrips(
            List<MCDownloadedTrip> trips, Dictionary<MCDownloadedTrip, SupeyTripGeo> geo,
            int capacityFloor, CancellationToken token, SupeyTemplateHints hints,
            double? clusterTimeWindowMinutes = null)
        {
            double timeWindow = clusterTimeWindowMinutes ?? ClusterTimeWindowMinutes;
            var sorted = new List<MCDownloadedTrip>(trips);
            sorted.Sort((a, b) =>
            {
                var ta = SupeyTripTimes.TryParsePU(a) ?? TimeSpan.MaxValue;
                var tb = SupeyTripTimes.TryParsePU(b) ?? TimeSpan.MaxValue;
                return ta.CompareTo(tb);
            });

            var clusters = new List<SupeyTripCluster>();
            foreach (var t in sorted)
            {
                token.ThrowIfCancellationRequested();
                var pu = geo[t].Pickup.Value;
                var dro = geo[t].Dropoff.Value;
                var puTime = SupeyTripTimes.TryParsePU(t).Value;
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
                    double puDist = HaversineMeters(c.PickupCentroid, pu);
                    if (puDist > puRadius) continue;
                    double doDist = HaversineMeters(c.DropoffCentroid, dro);
                    if (doDist > doRadius) continue;

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

        private static int FirstPickupIndex(SupeyTripCluster c)
        {
            if (c.PickupOrder != null && c.PickupOrder.Count > 0) return c.PickupOrder[0];
            return 0;
        }

        private static GeoPoint LastDropoffPoint(SupeyTripCluster c)
        {
            if (c.DropoffOrder != null && c.DropoffOrder.Count > 0)
                return c.DropoffPoints[c.DropoffOrder[c.DropoffOrder.Count - 1]];
            return c.DropoffPoints[c.DropoffPoints.Count - 1];
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

            var route = await RouteCache.GetAsync(path, token).ConfigureAwait(false);
            if (route.Ok)
            {
                c.RoutePolyline.AddRange(route.Polyline);
                c.IntraClusterDriveSeconds = route.TotalSeconds;
                c.IntraClusterMeters = route.TotalMeters;
                c.IsStraightLineFallback = route.IsStraightLineFallback;
            }
            else
            {
                c.RoutePolyline.AddRange(path);
                c.IntraClusterDriveSeconds = HaversineMetersAlong(path) / AverageStreetSpeedMps;
                c.IntraClusterMeters = HaversineMetersAlong(path);
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
            else if (route.Ok && route.LegDurations != null && route.LegDurations.Count >= 2 * n - 1)
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
        }

        // ----- Phase 4 + 5 -----

        private const int MaxAssignmentSplitDepth = 2;
        private const double SplitOnRejectLateMinutes = 30.0;

        private async Task TryAssignClusterAsync(
            SupeyTripCluster cluster,
            List<SupeyDriverPlan> plans,
            SupeyScheduleResult result,
            IProgress<string> progress,
            CancellationToken token,
            int splitDepth)
        {
            var pick = ScoreAndPickForCoverage(cluster, plans);
            if (pick != null)
            {
                pick.Groups.Add(cluster);
                return;
            }

            if (splitDepth < MaxAssignmentSplitDepth && ShouldTrySplitCluster(cluster))
            {
                var parts = SupeyClusterRouting.SplitClusterForAssignment(cluster);
                if (parts.Count > 1)
                {
                    progress?.Report("Splitting group " + cluster.GroupNumber + " into " + parts.Count + " for coverage...");
                    foreach (var sub in parts)
                    {
                        token.ThrowIfCancellationRequested();
                        FingerprintCluster(sub);
                        SupeyClusterRouting.OptimizeClusterTour(sub);
                        await PopulateClusterPolylineAsync(sub, token).ConfigureAwait(false);
                        await TryAssignClusterAsync(sub, plans, result, progress, token, splitDepth + 1)
                            .ConfigureAwait(false);
                    }
                    return;
                }
            }

            ReserveCluster(cluster, result);
        }

        private static void ReserveCluster(
            SupeyTripCluster cluster,
            SupeyScheduleResult result,
            HashSet<string> suppressWarningTripNumbers = null)
        {
            foreach (var t in cluster.Trips)
                result.Reserves.Add(t);

            string breakdown = cluster.Rejections.FormatBreakdown();
            string baseMsg = "Group " + cluster.GroupNumber + " (" + cluster.RiderCount + " rider" +
                (cluster.RiderCount == 1 ? "" : "s") + ", " + SupeyTripTimes.FormatTimeOfDay(cluster.EarliestPickup) +
                " PU) had no feasible driver — sent to Reserves.";
            if (!string.IsNullOrEmpty(breakdown))
                baseMsg += " Rejected: " + breakdown + ".";

            foreach (var t in cluster.Trips)
            {
                string tripNum = t.TripNumber ?? "";
                if (suppressWarningTripNumbers != null &&
                    suppressWarningTripNumbers.Contains(tripNum))
                    continue;
                result.BuildWarnings.Add(new SupeyWarning(SupeyWarningKind.LateArrival,
                    tripNum, "", baseMsg));
            }
        }

        private static bool ShouldTrySplitCluster(SupeyTripCluster cluster)
        {
            if (cluster.RiderCount <= 1) return false;
            if (SupeyClusterRouting.ClusterSharesSinglePickupAddress(cluster)) return false;
            if (cluster.Rejections.DoInfeasible.Count > 0 && ParseLateMinutes(cluster.Rejections.LateRiderNote) >= SplitOnRejectLateMinutes)
                return true;
            if (cluster.Rejections.DoInfeasible.Count >= 3) return true;
            if (cluster.Rejections.TimeConflict.Count >= 3 && cluster.RiderCount >= 2) return true;
            if (cluster.Rejections.TimeConflict.Count >= 4 && cluster.RiderCount >= 2) return true;
            return false;
        }

        /// <summary>
        /// Spread morning A-leg clinic loads across drivers before the main greedy pass.
        /// One cluster per hub per sweep so Manley does not consume every driver before Falcon/Minot/646.
        /// </summary>
        private void AssignMorningHubWaves(
            List<SupeyTripCluster> remaining,
            List<SupeyDriverPlan> plans,
            double avgRiders)
        {
            var byHub = new Dictionary<string, List<SupeyTripCluster>>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in remaining)
            {
                if (c.EarliestPickup < MorningHubWindowStart || c.EarliestPickup >= MorningHubWindowEnd)
                    continue;
                if (c.Trips.Count == 0 || DetectLeg(c.Trips[0].TripNumber) != 'A')
                    continue;
                string hub = c.FacilityMergeKey ?? "";
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

            // Falcon / 646 / Minot before Manley-heavy hubs consume every driver slot.
            SeedCriticalMorningHubs(remaining, plans, avgRiders, byHub, hubKeys, hubsServed, driverLockedHub);

            // One cluster per clinic hub on a distinct driver before anyone stacks a second hub.
            SpreadMorningHubsDistinctDriverFirst(remaining, plans, avgRiders, byHub, hubKeys,
                allowPuSlack: false, hubsServed, driverLockedHub);
            SpreadMorningHubsDistinctDriverFirst(remaining, plans, avgRiders, byHub, hubKeys,
                allowPuSlack: true, hubsServed, driverLockedHub);
            SweepMorningHubsOncePerHub(remaining, plans, avgRiders, byHub, hubKeys, allowPuSlack: false);
            SweepMorningHubsOncePerHub(remaining, plans, avgRiders, byHub, hubKeys, allowPuSlack: true);
        }

        /// <summary>
        /// Assigns the earliest cluster for each hub that has not yet been served, preferring
        /// drivers not already locked to a different morning clinic hub.
        /// </summary>
        private void SpreadMorningHubsDistinctDriverFirst(
            List<SupeyTripCluster> remaining,
            List<SupeyDriverPlan> plans,
            double avgRiders,
            Dictionary<string, List<SupeyTripCluster>> byHub,
            List<string> hubKeys,
            bool allowPuSlack,
            HashSet<string> hubsServed,
            Dictionary<SupeyDriverPlan, string> driverLockedHub)
        {
            bool progress;
            do
            {
                progress = false;
                foreach (string hubKey in hubKeys)
                {
                    if (hubsServed.Contains(hubKey)) continue;
                    var cluster = EarliestRemainingClusterInHub(byHub[hubKey], remaining);
                    if (cluster == null) continue;

                    var pick = PickForDistinctMorningHub(cluster, plans, avgRiders, hubKey,
                        driverLockedHub, allowPuSlack);
                    if (pick == null) continue;

                    pick.Groups.Add(cluster);
                    remaining.Remove(cluster);
                    hubsServed.Add(hubKey);
                    if (!driverLockedHub.ContainsKey(pick))
                        driverLockedHub[pick] = hubKey;
                    progress = true;
                }
            }
            while (progress);
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

        private SupeyDriverPlan PickForDistinctMorningHub(
            SupeyTripCluster cluster,
            List<SupeyDriverPlan> plans,
            double avgRiders,
            string hubKey,
            Dictionary<SupeyDriverPlan, string> driverLockedHub,
            bool allowPuSlack)
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
            var pick = PickLightestFeasibleDriverFrom(cluster, open, avgRiders, allowPuSlack, preferEarlyShift);
            if (pick != null) return pick;
            return PickLightestFeasibleDriverFrom(cluster, sameHub, avgRiders, allowPuSlack, preferEarlyShift);
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
            if (hubKey.IndexOf("FALCON", StringComparison.OrdinalIgnoreCase) >= 0) return 0;
            if (hubKey.IndexOf("63 BROAD", StringComparison.OrdinalIgnoreCase) >= 0) return 1;
            if (hubKey.IndexOf("646 MAIN", StringComparison.OrdinalIgnoreCase) >= 0) return 2;
            if (hubKey.IndexOf("1512 MINOT", StringComparison.OrdinalIgnoreCase) >= 0 ||
                hubKey.IndexOf("589 MINOT", StringComparison.OrdinalIgnoreCase) >= 0)
                return 3;
            if (hubKey.IndexOf("MANLEY", StringComparison.OrdinalIgnoreCase) >= 0) return 8;
            return 4;
        }

        private static bool IsSeedPriorityMorningHub(string hubKey) =>
            MorningHubPriorityTier(hubKey) <= 3;

        /// <summary>
        /// Assigns the earliest cluster at each critical clinic hub while drivers are still open,
        /// so Falcon/646/Minot are not starved after Manley fills every timeline.
        /// </summary>
        private void SeedCriticalMorningHubs(
            List<SupeyTripCluster> remaining,
            List<SupeyDriverPlan> plans,
            double avgRiders,
            Dictionary<string, List<SupeyTripCluster>> byHub,
            List<string> hubKeys,
            HashSet<string> hubsServed,
            Dictionary<SupeyDriverPlan, string> driverLockedHub)
        {
            foreach (string hubKey in hubKeys)
            {
                if (!IsSeedPriorityMorningHub(hubKey)) continue;
                if (!byHub.TryGetValue(hubKey, out var hubClusters)) continue;
                var cluster = EarliestRemainingClusterInHub(hubClusters, remaining);
                if (cluster == null) continue;

                var pick = PickLightestFeasibleDriver(cluster, plans, avgRiders, allowPuSlack: false,
                    preferEarlyShift: true);
                if (pick == null)
                    pick = PickLightestFeasibleDriver(cluster, plans, avgRiders, allowPuSlack: true,
                        preferEarlyShift: true);
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

        private void SweepMorningHubsOncePerHub(
            List<SupeyTripCluster> remaining,
            List<SupeyDriverPlan> plans,
            double avgRiders,
            Dictionary<string, List<SupeyTripCluster>> byHub,
            List<string> hubKeys,
            bool allowPuSlack)
        {
            bool anyAssigned;
            do
            {
                anyAssigned = false;
                foreach (string hubKey in hubKeys)
                {
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

                    var pick = PickLightestFeasibleDriver(cluster, plans, avgRiders, allowPuSlack,
                        preferEarlyShift: cluster.EarliestPickup < new TimeSpan(8, 0, 0));
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

        private SupeyDriverPlan PickLightestFeasibleDriver(
            SupeyTripCluster cluster,
            List<SupeyDriverPlan> plans,
            double avgRiders,
            bool allowPuSlack,
            bool preferEarlyShift = false)
        {
            return PickLightestFeasibleDriverFrom(cluster, plans, avgRiders, allowPuSlack, preferEarlyShift);
        }

        private SupeyDriverPlan PickLightestFeasibleDriverFrom(
            SupeyTripCluster cluster,
            List<SupeyDriverPlan> plans,
            double avgRiders,
            bool allowPuSlack,
            bool preferEarlyShift)
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
                double cost;
                if (TryScoreDriver(cluster, p, avgRiders, recordRejections: false,
                        assignmentMode: AssignmentCostMode.MaximizeCoverage, out cost))
                {
                    cost += MorningHubAffinityPenalty(p, clusterHub);
                    if (cost < bestCost) { bestCost = cost; best = p; bestSlack = 0; }
                    continue;
                }

                if (!allowPuSlack) continue;

                for (double slack = 1.0; slack <= CoverageMaxPuSlackMinutes; slack += 1.0)
                {
                    if (!TryScoreDriver(cluster, p, avgRiders, recordRejections: false,
                            assignmentMode: AssignmentCostMode.MaximizeCoverage, out cost,
                            puLateGraceMinutes: slack))
                        continue;
                    cost += MorningHubAffinityPenalty(p, clusterHub);
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

        private SupeyDriverPlan ScoreAndPickForCoverage(SupeyTripCluster cluster, List<SupeyDriverPlan> plans)
        {
            SupeyDriverPlan bestDriver = null;
            double bestCost = double.MaxValue;
            cluster.Rejections.Clear();
            double avgRiders = AverageRiderLoad(plans);

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
                if (!TryScoreDriver(cluster, p, avgRiders, recordRejections: true,
                        assignmentMode: AssignmentCostMode.MaximizeCoverage, out double cost))
                    continue;
                if (cost < bestCost) { bestCost = cost; bestDriver = p; }
            }

            if (bestDriver != null)
                return bestDriver;

            // All busy on PU timing only: add the minimum extra slack (≤8 min), not a fixed 18 min block.
            if (cluster.Rejections.DoInfeasible.Count == 0
                && cluster.Rejections.TimeConflict.Count >= plans.Count - 1)
            {
                return PickLightestFeasibleDriver(cluster, plans, avgRiders, allowPuSlack: true,
                    preferEarlyShift: cluster.EarliestPickup < new TimeSpan(8, 0, 0));
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
        private void ImproveCoverage(
            SupeyScheduleResult result,
            List<SupeyDriverPlan> plans,
            Dictionary<MCDownloadedTrip, SupeyTripGeo> tripGeo,
            int capacityFloor,
            SupeyTemplateHints hints,
            IProgress<string> progress,
            CancellationToken token)
        {
            int swaps = SwapGroupsForBalance(plans, token);
            int fromReserves = TryAssignReserveGroups(result, plans, tripGeo, capacityFloor, hints, token);
            if (swaps > 0 || fromReserves > 0)
                progress?.Report("Pass C: " + swaps + " group swap(s), " + fromReserves +
                    " group(s) pulled from reserves.");
        }

        private int SwapGroupsForBalance(List<SupeyDriverPlan> plans, CancellationToken token)
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
                    if (!TryScoreDriver(cluster, recipient, avgRiders, recordRejections: false,
                            assignmentMode: AssignmentCostMode.MaximizeCoverage, out _))
                        continue;

                    donor.Groups.Remove(cluster);
                    recipient.Groups.Add(cluster);
                    recipient.Groups.Sort((a, b) => a.EarliestPickup.CompareTo(b.EarliestPickup));
                    moves++;
                    moved = true;
                    break;
                }
                if (!moved) break;
            }
            return moves;
        }

        private int TryAssignReserveGroups(
            SupeyScheduleResult result,
            List<SupeyDriverPlan> plans,
            Dictionary<MCDownloadedTrip, SupeyTripGeo> tripGeo,
            int capacityFloor,
            SupeyTemplateHints hints,
            CancellationToken token)
        {
            if (result.Reserves.Count == 0) return 0;

            var alreadyWarnedTrips = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var w in result.BuildWarnings)
            {
                if (w.Kind == SupeyWarningKind.LateArrival && !string.IsNullOrEmpty(w.TripNumber))
                    alreadyWarnedTrips.Add(w.TripNumber);
            }

            var reserveTrips = new List<MCDownloadedTrip>(result.Reserves);
            result.Reserves.Clear();
            var hintsForCluster = UseTemplateHints ? hints : null;
            var clusters = ClusterTrips(reserveTrips, tripGeo, capacityFloor, token, hintsForCluster,
                ReserveRetryClusterWindowMinutes);
            clusters = SupeyClusterRouting.MergeHouseholdClusters(clusters, capacityFloor);
            clusters = SupeyClusterRouting.SplitClustersExceedingRiders(clusters, ReserveRetryMaxRidersPerCluster);

            int nextGroup = NextAvailableGroupNumber(plans);
            for (int i = 0; i < clusters.Count; i++)
            {
                clusters[i].GroupNumber = nextGroup + i;
                clusters[i].GroupColor = SupeyGroupPalette.For(clusters[i].GroupNumber);
            }

            clusters.Sort((a, b) =>
            {
                int cmp = b.RiderCount.CompareTo(a.RiderCount);
                return cmp != 0 ? cmp : a.EarliestPickup.CompareTo(b.EarliestPickup);
            });

            int assigned = 0;
            int processed = 0;
            foreach (var cluster in clusters)
            {
                token.ThrowIfCancellationRequested();
                if (processed >= CoverageImproveMaxReserveGroups) break;
                processed++;

                FingerprintCluster(cluster);
                SupeyClusterRouting.OptimizeClusterTour(cluster);
                EstimateClusterDriveFromHaversine(cluster);

                var pick = ScoreAndPickForCoverage(cluster, plans);
                if (pick != null)
                {
                    pick.Groups.Add(cluster);
                    assigned++;
                    continue;
                }

                ReserveCluster(cluster, result, alreadyWarnedTrips);
            }

            return assigned;
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

        /// <summary>Haversine in-group metrics when Pass C skips OSRM (scoring only).</summary>
        private static void EstimateClusterDriveFromHaversine(SupeyTripCluster c)
        {
            int n = c.Trips.Count;
            if (n == 0) return;
            var path = new List<GeoPoint>(n * 2);
            if (c.PickupOrder.Count == 0)
                for (int i = 0; i < n; i++) c.PickupOrder.Add(i);
            foreach (int idx in c.PickupOrder)
                path.Add(c.PickupPoints[idx]);
            if (c.DropoffOrder.Count == 0)
                for (int i = 0; i < n; i++) c.DropoffOrder.Add(i);
            foreach (int idx in c.DropoffOrder)
                path.Add(c.DropoffPoints[idx]);

            c.RoutePolyline.Clear();
            c.RoutePolyline.AddRange(path);
            c.IntraClusterMeters = HaversineMetersAlong(path);
            c.IntraClusterDriveSeconds = c.IntraClusterMeters / AverageStreetSpeedMps;
            c.IsStraightLineFallback = true;

            c.DropoffLegSeconds.Clear();
            if (n <= 1)
            {
                c.TailDriveSeconds = c.IntraClusterDriveSeconds;
                c.DropoffLegSeconds.Add(c.IntraClusterDriveSeconds);
                return;
            }
            double perLeg = c.IntraClusterDriveSeconds / Math.Max(1, path.Count - 1);
            for (int i = 0; i < n; i++) c.DropoffLegSeconds.Add(perLeg);
            c.TailDriveSeconds = perLeg * n;
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
                        if (!TryScoreDriver(cluster, rec, avgRiders, recordRejections: false,
                                assignmentMode: AssignmentCostMode.MinimizeExtension, out double cost))
                            continue;
                        if (cost < bestCost) { bestCost = cost; bestRec = rec; }
                    }
                    if (bestRec == null) continue;
                    double before = TotalFleetMeters(plans);
                    donor.Groups.RemoveAt(donor.Groups.Count - 1);
                    bestRec.Groups.Add(cluster);
                    bestRec.Groups.Sort((a, b) => a.EarliestPickup.CompareTo(b.EarliestPickup));
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

        private static void ReorderDriverGroups(SupeyDriverPlan plan)
        {
            if (plan.Groups.Count <= 2) return;
            plan.Groups.Sort((a, b) => a.EarliestPickup.CompareTo(b.EarliestPickup));
            bool improved = true;
            int safety = plan.Groups.Count * 2;
            var shiftStart = plan.Driver.ParseShiftStart() ?? TimeSpan.Zero;
            while (improved && safety-- > 0)
            {
                improved = false;
                for (int i = 0; i < plan.Groups.Count - 1; i++)
                {
                    var a = plan.Groups[i];
                    var b = plan.Groups[i + 1];
                    double keep = HaversineMeters(LastDropoffPoint(a), FirstPickupGeo(b));
                    double swap = HaversineMeters(LastDropoffPoint(b), FirstPickupGeo(a));
                    if (swap + 200 >= keep) continue;
                    if (a.EarliestPickup > b.EarliestPickup) continue;
                    var trial = new List<SupeyTripCluster>(plan.Groups);
                    trial[i] = b;
                    trial[i + 1] = a;
                    if (!GroupsChronologicallyFeasible(plan, trial, shiftStart)) continue;
                    plan.Groups[i] = b;
                    plan.Groups[i + 1] = a;
                    improved = true;
                }
                plan.Groups.Sort((x, y) => x.EarliestPickup.CompareTo(y.EarliestPickup));
            }
        }

        private static bool GroupsChronologicallyFeasible(SupeyDriverPlan plan, List<SupeyTripCluster> order, TimeSpan shiftStart)
        {
            var current = shiftStart;
            var loc = plan.HomeGeo.Value;
            foreach (var c in order)
            {
                double dh = HaversineMeters(loc, FirstPickupGeo(c)) / AverageStreetSpeedMps;
                var arrival = current.Add(TimeSpan.FromSeconds(dh));
                double puCap = LegPuLateCapMinutes(c);
                if (arrival > c.EarliestPickup.Add(TimeSpan.FromMinutes(puCap))) return false;
                var (ok, end, _, _) = ProjectClusterFeasibility(c, arrival);
                if (!ok) return false;
                current = end;
                loc = LastDropoffPoint(c);
            }
            return true;
        }

        private static GeoPoint FirstPickupGeo(SupeyTripCluster c) =>
            c.PickupPoints[FirstPickupIndex(c)];

        private enum AssignmentCostMode
        {
            MinimizeExtension,
            MaximizeCoverage,
        }

        /// <summary>
        /// Hard feasibility + soft cost for one (driver, cluster) pair.
        /// </summary>
        private bool TryScoreDriver(
            SupeyTripCluster cluster,
            SupeyDriverPlan p,
            double avgRiders,
            bool recordRejections,
            AssignmentCostMode assignmentMode,
            out double cost,
            double puLateGraceMinutes = 0,
            double doLateGraceMinutes = 0)
        {
            cost = double.MaxValue;

            if (cluster.RiderCount > p.Driver.CapacityPassengers)
            {
                if (recordRejections) cluster.Rejections.Capacity.Add(p.Driver.Name);
                return false;
            }

            if (ScheduleRules != null && ScheduleRules.IsDriverBlockedForCluster(p.Driver.Name, cluster))
            {
                if (recordRejections) cluster.Rejections.PolicyAvoid.Add(p.Driver.Name);
                return false;
            }

            var shiftStart = p.Driver.ParseShiftStart();
            var shiftEnd = p.Driver.ParseShiftEnd();
            if (shiftStart.HasValue && cluster.EarliestPickup < shiftStart.Value)
            {
                if (recordRejections) cluster.Rejections.ShiftStart.Add(p.Driver.Name);
                return false;
            }

            var (currentLastDO, currentLastLoc) = ProjectedLastEvent(p);
            int firstPu = FirstPickupIndex(cluster);
            double dhMeters = HaversineMeters(currentLastLoc, cluster.PickupPoints[firstPu]);
            double dhSeconds = dhMeters / AverageStreetSpeedMps;
            var arrivalAtFirstPU = currentLastDO.Add(TimeSpan.FromSeconds(dhSeconds));
            double puLateCapMinutes = LegPuLateCapMinutes(cluster) + puLateGraceMinutes;
            if (arrivalAtFirstPU > cluster.EarliestPickup.Add(TimeSpan.FromMinutes(puLateCapMinutes)))
            {
                if (recordRejections)
                {
                    if (p.Groups.Count > 0)
                        cluster.Rejections.TimeConflict.Add(p.Driver.Name);
                    else
                        cluster.Rejections.PuLate.Add(p.Driver.Name);
                }
                return false;
            }

            double doCap = DoLateMaxMinutes + doLateGraceMinutes;
            var (feasible, clusterEnd, lateTripIdx, lateMinutes) =
                ProjectClusterFeasibility(cluster, arrivalAtFirstPU, doCap);
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
                return false;
            }
            if (shiftEnd.HasValue && clusterEnd > shiftEnd.Value)
            {
                if (recordRejections) cluster.Rejections.ShiftEnd.Add(p.Driver.Name);
                return false;
            }

            double homeAffinitySeconds = HaversineMeters(p.HomeGeo.Value, cluster.PickupCentroid) / AverageStreetSpeedMps;

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
                cost -= ScheduleRules.PreferredPairingBonusSeconds(cluster, p.Driver.Name);

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

            return true;
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
        private static (TimeSpan time, GeoPoint loc) ProjectedLastEvent(SupeyDriverPlan p)
        {
            // No shift configured ⇒ driver is available from midnight on. We don't fabricate
            // a 6 AM default; that quietly disqualifies drivers whose shift starts earlier.
            var shiftStart = p.Driver.ParseShiftStart() ?? TimeSpan.Zero;
            if (p.Groups.Count == 0)
                return (shiftStart, p.HomeGeo.Value);

            // Walk in chronological order so propagated lateness reflects the real day. The
            // Groups list is normally maintained sorted, but we sort defensively in case a
            // caller mutated it without re-sorting.
            var sorted = new List<SupeyTripCluster>(p.Groups);
            sorted.Sort((a, b) => a.EarliestPickup.CompareTo(b.EarliestPickup));

            var current = shiftStart;
            var loc = p.HomeGeo.Value;
            foreach (var c in sorted)
            {
                double dhSeconds = HaversineMeters(loc, c.PickupPoints[FirstPickupIndex(c)]) / AverageStreetSpeedMps;
                var arrival = current.Add(TimeSpan.FromSeconds(dhSeconds));
                // Cluster end uses the wait-at-the-door model (see ProjectClusterEnd) which
                // accounts for both A-leg early-pickup compression and the wait the driver
                // takes at the LAST PU when scheduled times are spread out.
                current = ProjectClusterEnd(c, arrival);
                // Use the last DO point (chronologically) as the driver's location; the cluster
                // was already fingerprinted into PU1..PUn → DO1..DOn order, so the final DO is
                // truly where they end up.
                loc = LastDropoffPoint(c);
            }
            return (current, loc);
        }

        /// <summary>
        /// Computes the moment the cluster ends (driver becomes free at the last DO) AND
        /// whether every rider's individual drop-off deadline is met along the way. Models:
        /// <list type="bullet">
        ///   <item><b>A-leg early-pickup compression.</b> All-A-leg cluster can effectively
        ///         start up to 29 min before <see cref="SupeyTripCluster.EarliestPickup"/>.</item>
        ///   <item><b>Wait at the last PU.</b> Spread-out PU times → the driver waits at the
        ///         last PU's door before starting the drop-off run.</item>
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
            // Wait-or-go-now at the first PU.
            var startAtFirstPU = arrivalAtFirstPU > c.EffectiveEarliestPickup
                ? arrivalAtFirstPU : c.EffectiveEarliestPickup;

            // Drive PU1 → ... → PUn. For singletons (or when fingerprinting hasn't computed
            // a tail) treat the whole intra drive as tail with head = 0; a singleton has no
            // inter-PU wait so the math reduces to "arrive + drive to drop".
            double headSeconds = c.IntraClusterDriveSeconds - c.TailDriveSeconds;
            if (headSeconds < 0) headSeconds = 0;
            var arrivalAtLastPU = startAtFirstPU.Add(TimeSpan.FromSeconds(headSeconds));

            // Wait-or-go-now at the last PU.
            var startAtLastPU = arrivalAtLastPU > c.EffectiveLatestPickup
                ? arrivalAtLastPU : c.EffectiveLatestPickup;

            // Walk through the drop sequence (deadline-ordered) accumulating the per-leg
            // drive time. Each rider's deadline is checked at the moment we drop them.
            var current = startAtLastPU;
            bool feasible = true;
            int worstTrip = -1;
            double worstMinutes = 0;
            for (int i = 0; i < c.DropoffOrder.Count; i++)
            {
                double legSec = i < c.DropoffLegSeconds.Count ? c.DropoffLegSeconds[i] : 0;
                current = current.Add(TimeSpan.FromSeconds(legSec));

                int tripIdx = c.DropoffOrder[i];
                var deadline = SupeyTripTimes.TryParseDO(c.Trips[tripIdx]);
                if (!deadline.HasValue) continue; // null = no specific deadline (B/C return)

                // Scoreboard rule: at-deadline-or-after = LATE. Strict inequality.
                if (current >= deadline.Value.Add(TimeSpan.FromMinutes(doLateMaxMinutes)))
                {
                    double overrunMinutes = (current - deadline.Value).TotalMinutes;
                    if (overrunMinutes > worstMinutes)
                    {
                        worstMinutes = overrunMinutes;
                        worstTrip = tripIdx;
                    }
                    feasible = false;
                }
            }

            return (feasible, current, worstTrip, worstMinutes);
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
            double headSeconds = c.IntraClusterDriveSeconds - c.TailDriveSeconds;
            if (headSeconds < 0) headSeconds = 0;
            var arrivalAtLastPU = startAtFirstPU.Add(TimeSpan.FromSeconds(headSeconds));
            return arrivalAtLastPU > c.EffectiveLatestPickup
                ? arrivalAtLastPU : c.EffectiveLatestPickup;
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
            if (!plan.HomeGeo.HasValue) return;

            // Home → first cluster start.
            await AddDeadHeadAsync(plan, plan.HomeGeo.Value, FirstPickupGeo(plan.Groups[0]),
                "Home → Group " + plan.Groups[0].GroupNumber, token).ConfigureAwait(false);

            // Inter-cluster connectors.
            for (int i = 1; i < plan.Groups.Count; i++)
            {
                var prev = plan.Groups[i - 1];
                var curr = plan.Groups[i];
                await AddDeadHeadAsync(plan, LastDropoffPoint(prev),
                    FirstPickupGeo(curr),
                    "Group " + prev.GroupNumber + " → Group " + curr.GroupNumber, token).ConfigureAwait(false);
            }

            // Last cluster end → home.
            var lastGroup = plan.Groups[plan.Groups.Count - 1];
            await AddDeadHeadAsync(plan,
                LastDropoffPoint(lastGroup),
                plan.HomeGeo.Value,
                "Group " + lastGroup.GroupNumber + " → Home", token).ConfigureAwait(false);

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

            plan.FirstPickup = plan.Groups[0].EarliestPickup;
            plan.LastDropoff = current; // actual end of the last cluster, not its appointment time
            var finalReturn = plan.DeadHeads[plan.DeadHeads.Count - 1];
            plan.ReleaseTimeOfDay = current.Add(TimeSpan.FromSeconds(finalReturn.DurationSeconds));
        }

        private async Task AddDeadHeadAsync(SupeyDriverPlan plan, GeoPoint from, GeoPoint to,
            string label, CancellationToken token)
        {
            var seg = new SupeyDeadHeadSegment { From = from, To = to, Label = label };
            var path = new List<GeoPoint> { from, to };
            var route = await RouteCache.GetAsync(path, token).ConfigureAwait(false);
            if (route.Ok)
            {
                seg.DistanceMeters = route.TotalMeters;
                seg.DurationSeconds = route.TotalSeconds;
                seg.IsStraightLineFallback = route.IsStraightLineFallback;
                seg.Polyline.AddRange(route.Polyline);
            }
            else
            {
                seg.IsStraightLineFallback = true;
                seg.DistanceMeters = HaversineMeters(from, to);
                seg.DurationSeconds = seg.DistanceMeters / AverageStreetSpeedMps;
                seg.Polyline.Add(from);
                seg.Polyline.Add(to);
            }
            plan.DeadHeads.Add(seg);
            plan.TotalDriveSeconds += seg.DurationSeconds;
            plan.TotalMeters += seg.DistanceMeters;
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
                        var (recProjectedEnd, _) = ProjectedLastEvent(rec);
                        if (recProjectedEnd < lastGroup.EarliestPickup) continue;

                        bestRecipient = rec;
                        break;
                    }
                    if (bestRecipient == null) continue;

                    double avgRiders = AverageRiderLoad(plans);
                    if (!TryScoreDriver(lastGroup, bestRecipient, avgRiders, recordRejections: false,
                            assignmentMode: AssignmentCostMode.MinimizeExtension, out _))
                        continue;

                    double beforeFleet = TotalFleetSeconds(plans);
                    donor.Groups.RemoveAt(donor.Groups.Count - 1);
                    bestRecipient.Groups.Add(lastGroup);
                    bestRecipient.Groups.Sort((a, b) => a.EarliestPickup.CompareTo(b.EarliestPickup));
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
                        donor.Groups.Sort((a, b) => a.EarliestPickup.CompareTo(b.EarliestPickup));
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
                    if (arrivalAtFirstPU > g.EarliestPickup.Add(TimeSpan.FromMinutes(puCap)))
                    {
                        plan.Warnings.Add(new SupeyWarning(SupeyWarningKind.LateArrival,
                            g.Trips.Count > 0 ? g.Trips[0].TripNumber : "", plan.Driver.Name,
                            "Driver may arrive at first PU around " +
                            SupeyTripTimes.FormatTimeOfDay(arrivalAtFirstPU) + " (scheduled " +
                            SupeyTripTimes.FormatTimeOfDay(g.EarliestPickup) + ")."));
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
                if (!feas && worstTripIdx >= 0 && worstTripIdx < g.Trips.Count)
                {
                    var worstTrip = g.Trips[worstTripIdx];
                    string detail = "Group " + g.GroupNumber + " — " +
                        (worstTrip.ClientFullName ?? worstTrip.TripNumber ?? "rider") +
                        " may miss DO appt by " + worstMinutes.ToString("0") + " min.";
                    plan.Warnings.Add(new SupeyWarning(SupeyWarningKind.LateArrival,
                        worstTrip.TripNumber ?? "", plan.Driver.Name, detail));
                }
                else
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
                        var deadline = SupeyTripTimes.TryParseDO(trip);
                        if (!deadline.HasValue) continue;
                        double slackMin = (deadline.Value - stepCurrent).TotalMinutes;
                        if (slackMin < tightestMinutes)
                        {
                            tightestMinutes = slackMin;
                            tightestIdx = g.DropoffOrder[j];
                        }
                    }
                    if (tightestIdx >= 0 && tightestMinutes < TightArrivalSlackMinutes)
                    {
                        var t = g.Trips[tightestIdx];
                        plan.Warnings.Add(new SupeyWarning(SupeyWarningKind.TightArrival,
                            t.TripNumber ?? "", plan.Driver.Name,
                            "Group " + g.GroupNumber + " — " +
                            (t.ClientFullName ?? t.TripNumber ?? "rider") +
                            " arrives with only " + tightestMinutes.ToString("0") + " min of slack."));
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
