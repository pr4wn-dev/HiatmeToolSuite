using System;

using System.Collections.Generic;

using System.Linq;

using System.Text.RegularExpressions;



namespace Hiatme_Tool_Suite_v3

{

    /// <summary>Keep A/B legs of the same trip on the template-locked driver (not greedy post-solve placement).</summary>

    internal static class SupeyPartnerLegHarmonizer

    {

        private static readonly Regex LegSuffix =

            new Regex(@"^(\d+-\d+)-[AB]$", RegexOptions.IgnoreCase | RegexOptions.Compiled);



        /// <summary>Align template locks so B leg uses the same driver as A leg when split.</summary>

        public static int HarmonizeLocks(IDictionary<string, string> locks)

        {

            if (locks == null || locks.Count == 0) return 0;

            var byBase = IndexLegsByBase(locks, (tn, drv) => drv);

            int n = 0;

            foreach (var kv in byBase)

            {

                if (!kv.Value.TryGetValue("A", out var drvA)

                    || !kv.Value.TryGetValue("B", out var drvB))

                    continue;

                if (string.Equals(drvA, drvB, StringComparison.OrdinalIgnoreCase))

                    continue;

                string tnB = FindLockKey(locks, kv.Key, "B");

                if (string.IsNullOrEmpty(tnB)) continue;

                locks[tnB] = drvA;

                n++;

            }

            return n;

        }



        /// <summary>

        /// Move A and B legs onto the driver from template locks (A-leg lock wins).

        /// Does not pair using whoever the server greedy solver placed on the A leg.

        /// </summary>

        public static int HarmonizeSchedule(SupeyScheduleResult result)

        {

            if (result?.DriverPlans == null) return 0;

            if (result.Locks != null && result.Locks.Count > 0)

                HarmonizeLocks(result.Locks);



            int moved = 0;

            bool progress = true;

            while (progress)

            {

                progress = false;

                var located = IndexAssignedLegs(result);

                foreach (var kv in located)

                {

                    string targetDriver = ResolvePairDriver(result, kv.Key, kv.Value);

                    if (string.IsNullOrEmpty(targetDriver))

                        continue;

                    if (MoveAllPartnerLegsToDriver(result, kv.Value, targetDriver, ref moved))

                        progress = true;

                    SyncLocksForBase(result, kv.Key, targetDriver);

                }

            }

            moved += RemoveStrayDuplicateLegs(result);

            if (moved > 0)

            {

                var algo = new SupeyScheduleAlgorithm();

                foreach (var plan in result.DriverPlans)

                {

                    if (plan?.Groups == null) continue;

                    foreach (var g in plan.Groups)

                    {

                        if (g != null && g.Trips.Count > 0)

                            algo.SyncClusterMetadataPublic(g);

                    }

                }

                result.BuildWarnings.Add(new SupeyWarning(

                    SupeyWarningKind.BuildDiagnostic,

                    "",

                    "Build",

                    "Moved " + moved + " partner leg(s) onto template A-leg driver (split Friday tabs)."));

            }

            return moved;

        }

        /// <summary>Drop duplicate rows when a prior harmonize left the same leg on two drivers.</summary>
        private static int RemoveStrayDuplicateLegs(SupeyScheduleResult result)
        {
            if (result?.DriverPlans == null) return 0;
            var hits = new Dictionary<string, List<SupeyTripLocation>>(StringComparer.OrdinalIgnoreCase);
            foreach (var plan in result.DriverPlans)
            {
                if (plan?.Groups == null) continue;
                string drv = plan.Driver?.Name?.Trim() ?? "";
                foreach (var g in plan.Groups)
                {
                    if (g?.Trips == null) continue;
                    for (int i = 0; i < g.Trips.Count; i++)
                    {
                        string tn = g.Trips[i]?.TripNumber ?? "";
                        if (string.IsNullOrWhiteSpace(tn)) continue;
                        if (!hits.TryGetValue(tn, out var list))
                        {
                            list = new List<SupeyTripLocation>();
                            hits[tn] = list;
                        }
                        list.Add(new SupeyTripLocation
                        {
                            Trip = g.Trips[i],
                            Plan = plan,
                            Cluster = g,
                            TripIndex = i,
                            DriverName = drv,
                        });
                    }
                }
            }

            var toDrop = new List<SupeyTripLocation>();
            foreach (var kv in hits)
            {
                if (kv.Value.Count < 2) continue;
                string keepDriver = null;
                if (result.Locks != null && result.Locks.TryGetValue(kv.Key, out var locked)
                    && !string.IsNullOrWhiteSpace(locked))
                    keepDriver = locked.Trim();

                SupeyTripLocation keeper = null;
                if (!string.IsNullOrEmpty(keepDriver))
                {
                    foreach (var loc in kv.Value)
                    {
                        if (string.Equals(loc.DriverName, keepDriver, StringComparison.OrdinalIgnoreCase))
                        {
                            keeper = loc;
                            break;
                        }
                    }
                }
                if (keeper == null)
                    keeper = kv.Value[0];

                foreach (var loc in kv.Value)
                {
                    if (ReferenceEquals(loc, keeper))
                        continue;
                    toDrop.Add(loc);
                }
            }

            int removed = 0;
            foreach (var loc in toDrop.OrderByDescending(l => l.TripIndex))
            {
                if (loc.TripIndex < 0 || loc.TripIndex >= loc.Cluster.Trips.Count)
                    continue;
                string tn = loc.Cluster.Trips[loc.TripIndex]?.TripNumber ?? "";
                if (string.IsNullOrEmpty(tn)) continue;
                RemoveTripAt(loc.Cluster, loc.TripIndex);
                if (loc.Cluster.Trips.Count == 0 && loc.Plan.Groups != null)
                    loc.Plan.Groups.Remove(loc.Cluster);
                removed++;
            }
            return removed;
        }

        private static bool RelocateLegIfNeeded(

            SupeyScheduleResult result,

            SupeyTripLocation leg,

            SupeyTripLocation anchor,

            string targetDriver)

        {

            if (leg == null || string.IsNullOrEmpty(targetDriver))

                return false;

            if (string.Equals(leg.DriverName, targetDriver, StringComparison.OrdinalIgnoreCase))

                return false;

            if (!RelocateTrip(result, leg, anchor, targetDriver))

                return false;



            string tn = leg.Trip?.TripNumber ?? "";

            if (!string.IsNullOrEmpty(tn) && result.Locks != null)

                result.Locks[tn] = targetDriver;

            return true;

        }



        private static bool MoveAllPartnerLegsToDriver(
            SupeyScheduleResult result,
            Dictionary<string, List<SupeyTripLocation>> legs,
            string targetDriver,
            ref int moved)
        {
            SupeyTripLocation anchor = null;
            foreach (var list in legs.Values)
            {
                foreach (var loc in list)
                {
                    if (string.Equals(loc.DriverName, targetDriver, StringComparison.OrdinalIgnoreCase))
                    {
                        anchor = loc;
                        break;
                    }
                }
                if (anchor != null) break;
            }
            if (anchor == null)
            {
                foreach (var list in legs.Values)
                {
                    if (list.Count > 0) { anchor = list[0]; break; }
                }
            }

            foreach (var list in legs.Values)
            {
                foreach (var loc in list)
                {
                    if (string.Equals(loc.DriverName, targetDriver, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (RelocateLegIfNeeded(result, loc, anchor, targetDriver))
                    {
                        moved++;
                        return true;
                    }
                }
            }
            return false;
        }

        private static string ResolvePairDriver(
            SupeyScheduleResult result,
            string baseId,
            Dictionary<string, List<SupeyTripLocation>> legs)
        {
            string fromLocks = ResolvePairDriverFromLocks(result, baseId);
            if (!string.IsNullOrEmpty(fromLocks))
                return fromLocks;
            if (legs != null && legs.TryGetValue("A", out var listA) && listA.Count > 0
                && !string.IsNullOrWhiteSpace(listA[0]?.DriverName))
                return listA[0].DriverName.Trim();
            if (legs != null && legs.TryGetValue("B", out var listB) && listB.Count > 0
                && !string.IsNullOrWhiteSpace(listB[0]?.DriverName))
                return listB[0].DriverName.Trim();
            return null;
        }

        private static void SyncLocksForBase(SupeyScheduleResult result, string baseId, string driver)
        {
            if (result?.Locks == null || string.IsNullOrEmpty(baseId) || string.IsNullOrEmpty(driver))
                return;
            string keyA = FindLockKey(result.Locks, baseId, "A");
            string keyB = FindLockKey(result.Locks, baseId, "B");
            if (!string.IsNullOrEmpty(keyA))
                result.Locks[keyA] = driver;
            if (!string.IsNullOrEmpty(keyB))
                result.Locks[keyB] = driver;
        }

        private static string ResolvePairDriverFromLocks(SupeyScheduleResult result, string baseId)

        {

            if (result?.Locks == null || result.Locks.Count == 0 || string.IsNullOrEmpty(baseId))

                return null;



            string keyA = FindLockKey(result.Locks, baseId, "A");

            if (!string.IsNullOrEmpty(keyA)

                && result.Locks.TryGetValue(keyA, out var drvA)

                && !string.IsNullOrWhiteSpace(drvA))

                return drvA.Trim();



            string keyB = FindLockKey(result.Locks, baseId, "B");

            if (!string.IsNullOrEmpty(keyB)

                && result.Locks.TryGetValue(keyB, out var drvB)

                && !string.IsNullOrWhiteSpace(drvB))

                return drvB.Trim();



            return null;

        }



        private sealed class SupeyTripLocation

        {

            public MCDownloadedTrip Trip;

            public SupeyDriverPlan Plan;

            public SupeyTripCluster Cluster;

            public int TripIndex;

            public string DriverName;

        }



        private static Dictionary<string, Dictionary<string, List<SupeyTripLocation>>> IndexAssignedLegs(

            SupeyScheduleResult result)

        {

            var byBase = new Dictionary<string, Dictionary<string, List<SupeyTripLocation>>>(

                StringComparer.OrdinalIgnoreCase);

            foreach (var plan in result.DriverPlans)

            {

                if (plan?.Groups == null) continue;

                string drv = plan.Driver?.Name?.Trim() ?? "";

                foreach (var g in plan.Groups)

                {

                    if (g?.Trips == null) continue;

                    for (int i = 0; i < g.Trips.Count; i++)

                    {

                        var t = g.Trips[i];

                        string tn = t?.TripNumber ?? "";

                        string leg = LegLetter(tn);

                        if (string.IsNullOrEmpty(leg)) continue;

                        string baseId = BaseTripId(tn);

                        if (string.IsNullOrEmpty(baseId)) continue;

                        if (!byBase.TryGetValue(baseId, out var legs))

                        {

                            legs = new Dictionary<string, List<SupeyTripLocation>>(StringComparer.OrdinalIgnoreCase);

                            byBase[baseId] = legs;

                        }

                        if (!legs.TryGetValue(leg, out var list))

                        {

                            list = new List<SupeyTripLocation>();

                            legs[leg] = list;

                        }

                        list.Add(new SupeyTripLocation

                        {

                            Trip = t,

                            Plan = plan,

                            Cluster = g,

                            TripIndex = i,

                            DriverName = drv,

                        });

                    }

                }

            }

            return byBase;

        }



        private static bool RelocateTrip(

            SupeyScheduleResult result,

            SupeyTripLocation from,

            SupeyTripLocation anchor,

            string targetDriver)

        {

            if (from?.Trip == null || from.Cluster == null || from.Plan == null)

                return false;

            if (from.TripIndex < 0 || from.TripIndex >= from.Cluster.Trips.Count)

                return false;



            GeoPoint pu = from.TripIndex < from.Cluster.PickupPoints.Count

                ? from.Cluster.PickupPoints[from.TripIndex]

                : default;

            GeoPoint dof = from.TripIndex < from.Cluster.DropoffPoints.Count

                ? from.Cluster.DropoffPoints[from.TripIndex]

                : default;



            RemoveTripAt(from.Cluster, from.TripIndex);

            if (from.Cluster.Trips.Count == 0 && from.Plan.Groups != null)

                from.Plan.Groups.Remove(from.Cluster);



            SupeyDriverPlan targetPlan = null;

            foreach (var p in result.DriverPlans)

            {

                if (p != null && string.Equals(p.Driver?.Name, targetDriver, StringComparison.OrdinalIgnoreCase))

                {

                    targetPlan = p;

                    break;

                }

            }

            if (targetPlan == null) return false;



            string baseId = BaseTripId(from.Trip.TripNumber);

            string movingLeg = LegLetter(from.Trip.TripNumber);

            string partnerLeg = movingLeg == "B" ? "A" : movingLeg == "A" ? "B" : "";

            SupeyTripCluster targetCluster = null;

            if (!string.IsNullOrEmpty(baseId) && !string.IsNullOrEmpty(partnerLeg))

            {

                var partnerCluster = FindClusterWithLegLetter(targetPlan, baseId, partnerLeg);

                if (partnerCluster != null

                    && !SupeyClusterTimeSplit.WouldExceedPickupGap(partnerCluster, from.Trip))

                    targetCluster = partnerCluster;

            }

            if (targetCluster == null)

                targetCluster = FindClusterWithLeg(targetPlan, from.Trip.TripNumber);

            if (targetCluster == null && anchor != null

                && string.Equals(anchor.DriverName, targetDriver, StringComparison.OrdinalIgnoreCase)

                && anchor.Cluster != null

                && targetPlan.Groups.Contains(anchor.Cluster)

                && anchor.Cluster.Trips.Count > 0)

                targetCluster = anchor.Cluster;



            if (targetCluster == null)

            {

                int gn = targetPlan.Groups.Count + 1;

                targetCluster = new SupeyTripCluster

                {

                    GroupNumber = gn,

                    GroupColor = SupeyGroupPalette.For(gn),

                };

                targetPlan.Groups.Add(targetCluster);

            }



            int insertAt = FindPuInsertIndex(targetCluster, from.Trip);
            SupeyTripClusterGeo.InsertTripAt(targetCluster, insertAt, from.Trip, pu, dof);

            targetCluster.PickupOrder.Clear();

            targetCluster.DropoffOrder.Clear();

            targetCluster.RoadTourOptimized = false;

            return true;

        }



        private static SupeyTripCluster FindClusterWithLeg(SupeyDriverPlan plan, string tripNumber)

        {

            if (plan?.Groups == null || string.IsNullOrWhiteSpace(tripNumber))

                return null;

            string baseId = BaseTripId(tripNumber);

            foreach (var g in plan.Groups)

            {

                if (g?.Trips == null) continue;

                foreach (var t in g.Trips)

                {

                    if (t != null && string.Equals(BaseTripId(t.TripNumber), baseId, StringComparison.OrdinalIgnoreCase))

                        return g;

                }

            }

            return null;

        }

        private static SupeyTripCluster FindClusterWithLegLetter(
            SupeyDriverPlan plan, string baseId, string leg)
        {
            if (plan?.Groups == null || string.IsNullOrEmpty(baseId) || string.IsNullOrEmpty(leg))
                return null;
            foreach (var g in plan.Groups)
            {
                if (g?.Trips == null) continue;
                foreach (var t in g.Trips)
                {
                    if (t != null
                        && string.Equals(BaseTripId(t.TripNumber), baseId, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(LegLetter(t.TripNumber), leg, StringComparison.OrdinalIgnoreCase))
                        return g;
                }
            }
            return null;
        }

        private static int FindPuInsertIndex(SupeyTripCluster g, MCDownloadedTrip trip)
        {
            var incoming = SupeyTripTimes.TryParsePU(trip) ?? TimeSpan.MaxValue;
            for (int i = 0; i < g.Trips.Count; i++)
            {
                var pu = SupeyTripTimes.TryParsePU(g.Trips[i]) ?? TimeSpan.MaxValue;
                if (incoming < pu) return i;
            }
            return g.Trips.Count;
        }

        private static void RemoveTripAt(SupeyTripCluster g, int index)

        {

            g.Trips.RemoveAt(index);

            if (index < g.PickupPoints.Count) g.PickupPoints.RemoveAt(index);

            if (index < g.DropoffPoints.Count) g.DropoffPoints.RemoveAt(index);

            g.PickupOrder.Clear();

            g.DropoffOrder.Clear();

            g.RoutePolyline.Clear();

            g.RoadTourOptimized = false;

        }



        private static Dictionary<string, Dictionary<string, T>> IndexLegsByBase<T>(

            IDictionary<string, string> source,

            Func<string, string, T> selector)

        {

            var byBase = new Dictionary<string, Dictionary<string, T>>(StringComparer.OrdinalIgnoreCase);

            foreach (var kv in source)

            {

                string tn = (kv.Key ?? "").Trim();

                string leg = LegLetter(tn);

                if (string.IsNullOrEmpty(leg)) continue;

                string baseId = BaseTripId(tn);

                if (string.IsNullOrEmpty(baseId)) continue;

                if (!byBase.TryGetValue(baseId, out var legs))

                {

                    legs = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);

                    byBase[baseId] = legs;

                }

                legs[leg] = selector(tn, kv.Value);

            }

            return byBase;

        }



        private static string FindLockKey(IDictionary<string, string> locks, string baseId, string leg)

        {

            foreach (var kv in locks)

            {

                if (string.Equals(BaseTripId(kv.Key), baseId, StringComparison.OrdinalIgnoreCase)

                    && string.Equals(LegLetter(kv.Key), leg, StringComparison.OrdinalIgnoreCase))

                    return kv.Key;

            }

            return baseId + "-" + leg;

        }



        private static string BaseTripId(string tripNumber)

        {

            var m = LegSuffix.Match((tripNumber ?? "").Trim());

            if (m.Success) return m.Groups[1].Value;

            string s = (tripNumber ?? "").Trim();

            if (s.Length < 3) return "";

            char last = char.ToUpperInvariant(s[s.Length - 1]);

            if (last != 'A' && last != 'B') return "";

            if (s[s.Length - 2] != '-') return "";

            return s.Substring(0, s.Length - 2);

        }



        private static string LegLetter(string tripNumber)

        {

            string s = (tripNumber ?? "").Trim();

            if (s.Length < 2) return "";

            char c = char.ToUpperInvariant(s[s.Length - 1]);

            return c == 'A' || c == 'B' ? c.ToString() : "";

        }

    }

}


