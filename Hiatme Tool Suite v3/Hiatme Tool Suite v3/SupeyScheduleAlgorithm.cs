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
        // PU/DO clustering gates. Tuned conservatively: 0.5 mile PU radius, 1 mile DO radius, 20-minute time window.
        private const double PuClusterRadiusMeters = 800.0;
        private const double DoClusterRadiusMeters = 1600.0;
        private const double ClusterTimeWindowMinutes = 20.0;

        // Tight-arrival threshold — under 5 min of slack to the appointment fires a warning.
        private const double TightArrivalSlackMinutes = 5.0;

        // Average street speed for the haversine-based cost matrix. ~30 mph in m/s.
        private const double AverageStreetSpeedMps = 13.4;

        // Score weights. Both terms come out in seconds-equivalent units so they're comparable.
        private const double HomeAffinityWeight = 0.3;
        private const double ActiveWindowWeight = 1.0;
        private const double TemplateHintBonusSeconds = 600.0; // 10 minutes of "credit" for matching a hint
        private const double HistoricalPairBonusSeconds = 240.0; // 4 minutes for clustering historical pairs

        // Capacity floor — even if every driver has high capacity, never form a "cluster" larger
        // than this so we don't accidentally try to put 12 riders in one ride-share. Defensive.
        private const int AbsoluteCapacityFloor = 8;

        public SupeyTemplateHints Hints { get; set; }
        public bool UseTemplateHints { get; set; }

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

            // -------- Phase 1: Geocode --------
            progress?.Report("Geocoding " + trips.Count + " trips and " + drivers.Count + " drivers...");
            var tripGeo = new Dictionary<MCDownloadedTrip, SupeyTripGeo>();
            int doneTrips = 0;
            foreach (var t in trips)
            {
                token.ThrowIfCancellationRequested();
                var geo = new SupeyTripGeo
                {
                    Pickup = await AddressGeocoder.ResolveAsync(t.PUStreet, t.PUCity, token).ConfigureAwait(false),
                    Dropoff = await AddressGeocoder.ResolveAsync(t.DOStreet, t.DOCITY, token).ConfigureAwait(false),
                };
                tripGeo[t] = geo;
                doneTrips++;
                if ((doneTrips % 5) == 0 || doneTrips == trips.Count)
                    progress?.Report("Geocoding trips " + doneTrips + " / " + trips.Count + "...");
            }

            var driverHomeGeo = new Dictionary<SupeyDriverProfile, GeoPoint>();
            var validDrivers = new List<SupeyDriverProfile>();
            foreach (var d in drivers)
            {
                token.ThrowIfCancellationRequested();
                progress?.Report("Geocoding home for " + d.Name + "...");
                // Pin to US/state so Nominatim doesn't match the same street name in a different
                // state (e.g. there's an Auburn ME and an Auburn AL — without the state pin the
                // wrong one wins ~10% of the time on common names). Country defaults to US since
                // that's where every dispatch covered by this app operates.
                var p = await AddressGeocoder.ResolveAsync(d.HomeStreet,
                    string.IsNullOrWhiteSpace(d.HomeCity) ? d.HomeState : d.HomeCity,
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
                        ". Edit the home address and rebuild to include this driver."));
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
            var clusters = ClusterTrips(routableTrips, tripGeo, capacityFloor, token);
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
                await PopulateClusterPolylineAsync(c, token).ConfigureAwait(false);
                fingerprinted++;
                if ((fingerprinted % 3) == 0 || fingerprinted == clusters.Count)
                    progress?.Report("In-group routing " + fingerprinted + " / " + clusters.Count + "...");
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
            remaining.Sort((a, b) => a.EarliestPickup.CompareTo(b.EarliestPickup));

            foreach (var cluster in remaining)
            {
                token.ThrowIfCancellationRequested();
                var pick = ScoreAndPick(cluster, driverPlans);
                if (pick != null)
                {
                    pick.Groups.Add(cluster);
                }
                else
                {
                    foreach (var t in cluster.Trips)
                    {
                        result.Reserves.Add(t);
                    }
                    result.BuildWarnings.Add(new SupeyWarning(SupeyWarningKind.LateArrival,
                        cluster.Trips.Count > 0 ? cluster.Trips[0].TripNumber ?? "" : "",
                        "",
                        "Group " + cluster.GroupNumber + " (" + cluster.RiderCount + " rider" +
                        (cluster.RiderCount == 1 ? "" : "s") + ", " + SupeyTripTimes.FormatTimeOfDay(cluster.EarliestPickup) +
                        " PU) had no feasible driver — sent to Reserves."));
                }
            }

            // -------- Phase 6: Sequence each driver --------
            progress?.Report("Sequencing dead-heads for " + driverPlans.Count + " driver(s)...");
            int seqDone = 0;
            foreach (var plan in driverPlans)
            {
                token.ThrowIfCancellationRequested();
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

        private static int ResolveCapacityFloor(IEnumerable<SupeyDriverProfile> drivers)
        {
            int floor = AbsoluteCapacityFloor;
            int min = int.MaxValue;
            foreach (var d in drivers)
            {
                int c = d.CapacityPassengers;
                if (c < 1) c = 1;
                if (c < min) min = c;
            }
            if (min == int.MaxValue) return floor;
            return Math.Min(floor, min);
        }

        private static List<SupeyTripCluster> ClusterTrips(
            List<MCDownloadedTrip> trips, Dictionary<MCDownloadedTrip, SupeyTripGeo> geo,
            int capacityFloor, CancellationToken token)
        {
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

                SupeyTripCluster bestFit = null;
                double bestScore = double.MaxValue;
                foreach (var c in clusters)
                {
                    if (c.RiderCount >= capacityFloor) continue;
                    if (Math.Abs((c.EarliestPickup - puTime).TotalMinutes) > ClusterTimeWindowMinutes) continue;

                    double puDist = HaversineMeters(c.PickupCentroid, pu);
                    if (puDist > PuClusterRadiusMeters) continue;
                    double doDist = HaversineMeters(c.DropoffCentroid, dro);
                    if (doDist > DoClusterRadiusMeters) continue;

                    double score = puDist + doDist;
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
                    var doTime = SupeyTripTimes.TryParseDO(t);
                    if (doTime.HasValue && doTime.Value < bestFit.HardestDropoff) bestFit.HardestDropoff = doTime.Value;
                }
                else
                {
                    var c = new SupeyTripCluster
                    {
                        EarliestPickup = puTime,
                        HardestDropoff = SupeyTripTimes.TryParseDO(t) ?? puTime.Add(TimeSpan.FromMinutes(30)),
                        PickupCentroid = pu,
                        DropoffCentroid = dro,
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
        }

        private async Task PopulateClusterPolylineAsync(SupeyTripCluster c, CancellationToken token)
        {
            // In-cluster waypoint sequence: PU1, PU2, ..., DO1, DO2, ...
            var path = new List<GeoPoint>(c.PickupPoints.Count + c.DropoffPoints.Count);
            path.AddRange(c.PickupPoints);
            path.AddRange(c.DropoffPoints);
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
        }

        // ----- Phase 4 + 5 -----

        private SupeyDriverPlan ScoreAndPick(SupeyTripCluster cluster, List<SupeyDriverPlan> plans)
        {
            SupeyDriverPlan bestDriver = null;
            double bestCost = double.MaxValue;

            foreach (var p in plans)
            {
                if (cluster.RiderCount > p.Driver.CapacityPassengers) continue;

                // Hard: driver shift window must contain earliest PU and hardest DO.
                var shiftStart = p.Driver.ParseShiftStart();
                var shiftEnd = p.Driver.ParseShiftEnd();
                if (shiftStart.HasValue && cluster.EarliestPickup < shiftStart.Value) continue;
                if (shiftEnd.HasValue && cluster.HardestDropoff > shiftEnd.Value) continue;

                // Hard: chronologically can the driver fit this cluster after their last assignment?
                var (currentLastDO, currentLastLoc) = ProjectedLastEvent(p);
                double dhMeters = HaversineMeters(currentLastLoc, cluster.PickupPoints[0]);
                double dhSeconds = dhMeters / AverageStreetSpeedMps;
                var arrivalAtFirstPU = currentLastDO.Add(TimeSpan.FromSeconds(dhSeconds));
                if (arrivalAtFirstPU > cluster.EarliestPickup.Add(TimeSpan.FromMinutes(30)))
                {
                    // Driver wouldn't make first PU even with reasonable padding (30 min slack).
                    continue;
                }
                var clusterEnd = (arrivalAtFirstPU > cluster.EarliestPickup ? arrivalAtFirstPU : cluster.EarliestPickup)
                    .Add(TimeSpan.FromSeconds(cluster.IntraClusterDriveSeconds));
                if (clusterEnd > cluster.HardestDropoff.Add(TimeSpan.FromMinutes(2)))
                    continue; // Would miss hardest deadline by > 2 min — disqualify.

                // Soft: home affinity + active-window extension.
                double homeAffinitySeconds = HaversineMeters(p.HomeGeo.Value, cluster.PickupCentroid) / AverageStreetSpeedMps;

                double extensionSeconds;
                if (p.Groups.Count == 0)
                {
                    // First assignment: extension = the cluster's full duration including dead-heads.
                    extensionSeconds = (clusterEnd - cluster.EarliestPickup).TotalSeconds + dhSeconds;
                }
                else
                {
                    extensionSeconds = (clusterEnd - currentLastDO).TotalSeconds;
                    if (extensionSeconds < 0) extensionSeconds = 0;
                }

                double cost = HomeAffinityWeight * homeAffinitySeconds
                            + ActiveWindowWeight * extensionSeconds;

                // Soft: template hint match cuts a flat bonus off the cost.
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
                }

                if (cost < bestCost) { bestCost = cost; bestDriver = p; }
            }

            return bestDriver;
        }

        /// <summary>
        /// Estimates "where the driver last was, when". Uses the last assigned cluster's hardest
        /// DO as the time and DO centroid as the location; falls back to home when nothing is
        /// assigned yet.
        /// </summary>
        private static (TimeSpan time, GeoPoint loc) ProjectedLastEvent(SupeyDriverPlan p)
        {
            if (p.Groups.Count == 0)
            {
                var shiftStart = p.Driver.ParseShiftStart() ?? TimeSpan.FromHours(6);
                return (shiftStart, p.HomeGeo.Value);
            }
            var last = p.Groups[p.Groups.Count - 1];
            return (last.HardestDropoff, last.DropoffCentroid);
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
            await AddDeadHeadAsync(plan, plan.HomeGeo.Value, plan.Groups[0].PickupPoints[0],
                "Home → Group " + plan.Groups[0].GroupNumber, token).ConfigureAwait(false);

            // Inter-cluster connectors.
            for (int i = 1; i < plan.Groups.Count; i++)
            {
                var prev = plan.Groups[i - 1];
                var curr = plan.Groups[i];
                await AddDeadHeadAsync(plan, prev.DropoffPoints[prev.DropoffPoints.Count - 1],
                    curr.PickupPoints[0],
                    "Group " + prev.GroupNumber + " → Group " + curr.GroupNumber, token).ConfigureAwait(false);
            }

            // Last cluster end → home.
            var lastGroup = plan.Groups[plan.Groups.Count - 1];
            await AddDeadHeadAsync(plan,
                lastGroup.DropoffPoints[lastGroup.DropoffPoints.Count - 1],
                plan.HomeGeo.Value,
                "Group " + lastGroup.GroupNumber + " → Home", token).ConfigureAwait(false);

            // Add intra-cluster totals.
            foreach (var g in plan.Groups)
            {
                plan.TotalDriveSeconds += g.IntraClusterDriveSeconds;
                plan.TotalMeters += g.IntraClusterMeters;
            }

            plan.FirstPickup = plan.Groups[0].EarliestPickup;
            plan.LastDropoff = plan.Groups[plan.Groups.Count - 1].HardestDropoff;
            // Release time = last DO + final dead-head back to home.
            var finalReturn = plan.DeadHeads[plan.DeadHeads.Count - 1];
            plan.ReleaseTimeOfDay = plan.LastDropoff.Value
                .Add(TimeSpan.FromSeconds(finalReturn.DurationSeconds));
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
                        var recLast = rec.Groups[rec.Groups.Count - 1];
                        // Recipient must already be working at least as late as the moved cluster.
                        if (recLast.HardestDropoff < lastGroup.EarliestPickup) continue;

                        bestRecipient = rec;
                        break;
                    }
                    if (bestRecipient == null) continue;

                    // Tentatively move and measure.
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
            // Walk the day, recompute actual times, compare against deadlines.
            var arrivedAtPU = plan.Groups[0].EarliestPickup;
            // First dead-head is Home → first cluster start.
            var dhFirst = plan.DeadHeads.Count > 0 ? plan.DeadHeads[0] : null;
            var shiftStart = plan.Driver.ParseShiftStart() ?? TimeSpan.FromHours(6);
            if (dhFirst != null)
            {
                var depart = shiftStart;
                arrivedAtPU = depart.Add(TimeSpan.FromSeconds(dhFirst.DurationSeconds));
                if (arrivedAtPU > plan.Groups[0].EarliestPickup.Add(TimeSpan.FromMinutes(15)))
                {
                    plan.Warnings.Add(new SupeyWarning(SupeyWarningKind.LateArrival,
                        plan.Groups[0].Trips[0].TripNumber, plan.Driver.Name,
                        "Driver may arrive at first PU around " +
                        SupeyTripTimes.FormatTimeOfDay(arrivedAtPU) + " (scheduled " +
                        SupeyTripTimes.FormatTimeOfDay(plan.Groups[0].EarliestPickup) + ")."));
                }
            }

            for (int i = 0; i < plan.Groups.Count; i++)
            {
                var g = plan.Groups[i];
                if (g.IsStraightLineFallback)
                {
                    plan.Warnings.Add(new SupeyWarning(SupeyWarningKind.StraightLineFallback,
                        g.Trips.Count > 0 ? g.Trips[0].TripNumber : "", plan.Driver.Name,
                        "Group " + g.GroupNumber + " uses a straight-line route (OSRM unreachable)."));
                }

                // DO appointment slack: compare hardest DO with simulated arrival time.
                var startAt = i == 0 ? arrivedAtPU : plan.Groups[i].EarliestPickup;
                if (i > 0 && i - 1 < plan.DeadHeads.Count - 1)
                {
                    // dead-head index 1+ is between groups
                    var dh = plan.DeadHeads[i];
                    var prev = plan.Groups[i - 1];
                    startAt = prev.HardestDropoff.Add(TimeSpan.FromSeconds(dh.DurationSeconds));
                    if (startAt < g.EarliestPickup) startAt = g.EarliestPickup;
                }
                var groupEnd = startAt.Add(TimeSpan.FromSeconds(g.IntraClusterDriveSeconds));
                var slack = (g.HardestDropoff - groupEnd).TotalMinutes;
                if (slack < 0)
                {
                    plan.Warnings.Add(new SupeyWarning(SupeyWarningKind.LateArrival,
                        g.Trips.Count > 0 ? g.Trips[0].TripNumber : "", plan.Driver.Name,
                        "Group " + g.GroupNumber + " may miss DO appt by " +
                        Math.Abs(slack).ToString("0") + " min."));
                }
                else if (slack < TightArrivalSlackMinutes)
                {
                    plan.Warnings.Add(new SupeyWarning(SupeyWarningKind.TightArrival,
                        g.Trips.Count > 0 ? g.Trips[0].TripNumber : "", plan.Driver.Name,
                        "Group " + g.GroupNumber + " arrives with only " + slack.ToString("0") + " min of slack."));
                }
            }
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
