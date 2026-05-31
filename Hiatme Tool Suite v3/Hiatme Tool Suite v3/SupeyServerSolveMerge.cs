using System;

using System.Collections.Generic;

using System.Linq;

using System.Threading;

using System.Threading.Tasks;



namespace Hiatme_Tool_Suite_v3

{

    /// <summary>

    /// After server solve on unlocked trips only, merge template slot groups (not one trip per group).

    /// </summary>

    internal static class SupeyServerSolveMerge

    {

        public static Task ApplyTemplateScheduleAsync(

            SupeyScheduleResult result,

            SupeyTemplateMatchResult match,

            IDictionary<string, string> locks,

            IList<MCDownloadedTrip> allTrips,

            IList<SupeyDriverProfile> selectedDrivers,

            ISet<string> serverAssignedTripNumbers,

            CancellationToken cancellationToken = default)

        {

            if (result == null)

                return Task.CompletedTask;



            if (locks != null)

            {

                foreach (var kv in locks)

                {

                    if (!string.IsNullOrWhiteSpace(kv.Key))

                        result.Locks[kv.Key] = kv.Value ?? "";

                }

                SupeyPartnerLegHarmonizer.HarmonizeLocks(result.Locks);

            }



            if (match?.OrderedSlotsByRosterDriver != null

                && match.OrderedSlotsByRosterDriver.Count > 0)

            {

                return ApplyTemplateSlotGroupsAsync(

                    result, match, locks, allTrips, selectedDrivers,

                    serverAssignedTripNumbers, cancellationToken);

            }



            ApplyTemplateLocks(result, locks, allTrips, selectedDrivers);

            return Task.CompletedTask;

        }



        /// <summary>

        /// Legacy fallback: one cluster per locked trip when template slots are unavailable.

        /// </summary>

        public static void ApplyTemplateLocks(

            SupeyScheduleResult result,

            IDictionary<string, string> locks,

            IList<MCDownloadedTrip> allTrips,

            IList<SupeyDriverProfile> selectedDrivers)

        {

            if (result == null || locks == null || locks.Count == 0 || allTrips == null)

                return;



            var tripByNumber = HiatmeTripLookup.Build(allTrips);

            var assigned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var plan in result.DriverPlans)

            {

                if (plan?.Groups == null) continue;

                foreach (var g in plan.Groups)

                {

                    if (g?.Trips == null) continue;

                    foreach (var t in g.Trips)

                    {

                        if (!string.IsNullOrWhiteSpace(t?.TripNumber))

                            assigned.Add(t.TripNumber);

                    }

                }

            }



            foreach (var kv in locks)

            {

                string tn = (kv.Key ?? "").Trim();

                string driverName = (kv.Value ?? "").Trim();

                if (string.IsNullOrEmpty(tn) || string.IsNullOrEmpty(driverName))

                    continue;

                result.Locks[tn] = driverName;

                if (assigned.Contains(tn))

                    continue;

                if (!HiatmeTripLookup.TryResolve(tn, tripByNumber, out var trip))

                    continue;



                SupeyDriverPlan plan = FindOrCreatePlan(result, selectedDrivers, driverName);

                if (plan == null)

                    continue;



                int gn = plan.Groups.Count + 1;

                var cluster = new SupeyTripCluster

                {

                    GroupNumber = gn,

                    GroupColor = SupeyGroupPalette.For(gn),

                };

                cluster.Trips.Add(trip);

                plan.Groups.Add(cluster);

                assigned.Add(tn);

                RemoveFromReserves(result, tn);

            }

        }



        private static async Task ApplyTemplateSlotGroupsAsync(

            SupeyScheduleResult result,

            SupeyTemplateMatchResult match,

            IDictionary<string, string> locks,

            IList<MCDownloadedTrip> allTrips,

            IList<SupeyDriverProfile> selectedDrivers,

            ISet<string> serverAssignedTripNumbers,

            CancellationToken cancellationToken)

        {

            var serverAssigned = serverAssignedTripNumbers

                ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var algo = new SupeyScheduleAlgorithm();

            int templateTrips = 0;

            int templateGroups = 0;



            foreach (var kv in match.OrderedSlotsByRosterDriver)

            {

                cancellationToken.ThrowIfCancellationRequested();

                string driverName = kv.Key;

                var slots = kv.Value;

                if (string.IsNullOrWhiteSpace(driverName) || slots == null || slots.Count == 0)

                    continue;



                var templateGroupsForDriver = new List<SupeyTripCluster>();

                SupeyTripCluster current = null;



                void FlushCluster()

                {

                    if (current == null || current.Trips.Count == 0)

                    {

                        current = null;

                        return;

                    }

                    templateGroupsForDriver.Add(current);

                    current = null;

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

                    string tn = t.TripNumber ?? "";

                    if (serverAssigned.Contains(tn))

                        continue;

                    if (locks != null && locks.Count > 0 && !locks.ContainsKey(tn))

                        continue;



                    var pu = await AddressGeocoder.ResolveTripEndpointAsync(

                            t.PUStreet, t.PUCity, cancellationToken)

                        .ConfigureAwait(false);

                    var dof = await AddressGeocoder.ResolveTripEndpointAsync(

                            t.DOStreet, t.DOCITY, cancellationToken)

                        .ConfigureAwait(false);

                    if (!pu.HasValue || !dof.HasValue)

                        continue;



                    if (current == null)

                        current = new SupeyTripCluster();

                    int insertAt = 0;
                    var incomingPu = SupeyTripTimes.TryParsePU(t) ?? TimeSpan.MaxValue;
                    for (int i = 0; i < current.Trips.Count; i++)
                    {
                        var existingPu = SupeyTripTimes.TryParsePU(current.Trips[i]) ?? TimeSpan.MaxValue;
                        if (incomingPu < existingPu)
                        {
                            insertAt = i;
                            break;
                        }
                        insertAt = i + 1;
                    }
                    SupeyTripClusterGeo.InsertTripAt(current, insertAt, t, pu.Value, dof.Value);

                    templateTrips++;

                }



                FlushCluster();

                if (templateGroupsForDriver.Count == 0)

                    continue;



                SupeyDriverPlan plan = FindOrCreatePlan(result, selectedDrivers, driverName);

                if (plan == null)

                    continue;



                plan.TemplateDisplaySlots = new List<SupeyTemplateSlot>(slots);
                int templateSeedGroups = templateGroupsForDriver.Count;

                var serverGroups = plan.Groups.ToList();

                plan.Groups.Clear();



                int gn = 1;

                foreach (var g in templateGroupsForDriver)

                {

                    g.GroupNumber = gn++;

                    g.GroupColor = SupeyGroupPalette.For(g.GroupNumber);

                    algo.SyncClusterMetadataPublic(g);

                    plan.Groups.Add(g);

                    templateGroups++;

                    foreach (var t in g.Trips)

                    {

                        if (!string.IsNullOrWhiteSpace(t?.TripNumber))

                        {

                            RemoveFromReserves(result, t.TripNumber);

                            if (locks != null && locks.ContainsKey(t.TripNumber))

                                result.Locks[t.TripNumber] = locks[t.TripNumber];

                        }

                    }

                }



                serverGroups.Sort((a, b) =>
                {
                    int cmp = SupeyClusterTimeSplit.MinPickupTime(a)
                        .CompareTo(SupeyClusterTimeSplit.MinPickupTime(b));
                    return cmp != 0 ? cmp : a.GroupNumber.CompareTo(b.GroupNumber);
                });

                foreach (var g in serverGroups)

                {

                    g.GroupNumber = gn++;

                    g.GroupColor = SupeyGroupPalette.For(g.GroupNumber);

                    plan.Groups.Add(g);

                }

                plan.TemplateSeedGroupCount = 0;

            }



            if (templateGroups > 0)

            {

                result.BuildWarnings.Add(new SupeyWarning(

                    SupeyWarningKind.BuildDiagnostic,

                    "",

                    "Template",

                    "Template ride-share groups: " + templateGroups + " group(s), "

                    + templateTrips + " locked trip(s) (CSV slot order)."));

            }

            SupeyPartnerLegHarmonizer.HarmonizeSchedule(result);
            foreach (var plan in result.DriverPlans)
            {
                if (plan?.Groups != null && plan.Groups.Count > 0)
                {
                    SupeyClusterTimeSplit.NormalizeDayGroupOrder(plan);
                    SupeyScheduleDeskOrder.ApplyToPlan(plan);
                    plan.PreferChronologicalGroupPreview = true;
                }
            }

        }



        private static SupeyDriverPlan FindOrCreatePlan(

            SupeyScheduleResult result,

            IList<SupeyDriverProfile> selectedDrivers,

            string driverName)

        {

            foreach (var p in result.DriverPlans)

            {

                if (p?.Driver != null

                    && string.Equals(p.Driver.Name, driverName, StringComparison.OrdinalIgnoreCase))

                    return p;

            }



            var profile = FindDriver(selectedDrivers, driverName);

            if (profile == null)

                return null;



            var plan = new SupeyDriverPlan { Driver = profile };

            result.DriverPlans.Add(plan);

            return plan;

        }



        private static SupeyDriverProfile FindDriver(

            IList<SupeyDriverProfile> drivers, string name)

        {

            if (drivers == null || string.IsNullOrWhiteSpace(name))

                return null;

            return drivers.FirstOrDefault(d =>

                d != null && string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase));

        }



        private static void RemoveFromReserves(SupeyScheduleResult result, string tripNumber)

        {

            if (result == null || string.IsNullOrWhiteSpace(tripNumber))

                return;

            for (int i = result.Reserves.Count - 1; i >= 0; i--)

            {

                if (string.Equals(result.Reserves[i]?.TripNumber, tripNumber,

                        StringComparison.OrdinalIgnoreCase))

                    result.Reserves.RemoveAt(i);

            }

        }

    }

}


