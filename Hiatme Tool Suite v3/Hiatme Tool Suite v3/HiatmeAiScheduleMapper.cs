using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>Turns AI schedule JSON into a <see cref="SupeyScheduleResult"/> for preview/save.</summary>
    internal static class HiatmeAiScheduleMapper
    {
        public static SupeyScheduleResult ToSupeyScheduleResult(
            HiatmeAiScheduleBody schedule,
            DateTime serviceDate,
            IList<SupeyDriverProfile> selectedDrivers,
            IList<MCDownloadedTrip> allTrips,
            string aiMessage)
        {
            if (schedule == null) throw new ArgumentNullException(nameof(schedule));
            if (selectedDrivers == null) throw new ArgumentNullException(nameof(selectedDrivers));
            if (allTrips == null) throw new ArgumentNullException(nameof(allTrips));

            var tripByNumber = HiatmeTripLookup.Build(allTrips);
            var result = new SupeyScheduleResult { ServiceDate = serviceDate };
            var assignedTripNumbers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int requested = 0;
            int resolved = 0;

            if (!string.IsNullOrWhiteSpace(aiMessage))
            {
                result.BuildWarnings.Add(new SupeyWarning(
                    SupeyWarningKind.MissingGeo,
                    "",
                    "",
                    "AI: " + aiMessage));
            }

            var assignedDrivers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (schedule.Drivers != null)
            {
                foreach (var block in schedule.Drivers)
                {
                    if (block == null || string.IsNullOrWhiteSpace(block.DriverName)) continue;
                    var profile = FindDriverProfile(selectedDrivers, block.DriverName);
                    if (profile == null) continue;

                    assignedDrivers.Add(profile.Name);
                    var plan = new SupeyDriverPlan { Driver = profile };

                    if (block.Groups != null && block.Groups.Count > 0)
                    {
                        int gn = 1;
                        foreach (var groupNums in block.Groups)
                        {
                            var cluster = NewCluster(gn++);
                            AddTripNumbersToCluster(cluster, groupNums, tripByNumber, assignedTripNumbers,
                                ref requested, ref resolved);
                            if (cluster.Trips.Count > 0)
                                plan.Groups.Add(cluster);
                        }
                    }
                    else
                    {
                        var cluster = NewCluster(1);
                        AddTripNumbersToCluster(cluster, block.TripNumbers, tripByNumber, assignedTripNumbers,
                            ref requested, ref resolved);
                        if (cluster.Trips.Count > 0)
                            plan.Groups.Add(cluster);
                    }

                    result.DriverPlans.Add(plan);
                }
            }

            foreach (var d in selectedDrivers)
            {
                if (d == null || assignedDrivers.Contains(d.Name)) continue;
                result.DriverPlans.Add(new SupeyDriverPlan { Driver = d });
            }

            if (schedule.Reserves != null)
            {
                foreach (var tn in schedule.Reserves)
                {
                    requested++;
                    if (!HiatmeTripLookup.TryResolve(tn, tripByNumber, out var trip)) continue;
                    if (!assignedTripNumbers.Add(trip.TripNumber)) continue;
                    resolved++;
                    result.Reserves.Add(trip);
                }
            }

            int scheduled = CountAssignedTrips(result);
            if (requested > 0 && resolved < requested)
            {
                result.BuildWarnings.Add(new SupeyWarning(
                    SupeyWarningKind.MissingGeo,
                    "",
                    "",
                    "AI schedule: matched " + resolved + " of " + requested
                    + " trip references to loaded Modivcare rows. Rebuild if the list looks empty."));
            }
            if (scheduled == 0 && allTrips.Count > 0)
            {
                result.BuildWarnings.Add(new SupeyWarning(
                    SupeyWarningKind.MissingGeo,
                    "",
                    "",
                    "AI returned a schedule but no trips could be placed on drivers. Check driver names match the roster and trip numbers match LOAD TRIPS."));
            }

            return result;
        }

        public static int CountAssignedTrips(SupeyScheduleResult result)
        {
            if (result == null) return 0;
            int n = result.Reserves?.Count ?? 0;
            if (result.DriverPlans != null)
            {
                foreach (var p in result.DriverPlans)
                {
                    if (p?.Groups == null) continue;
                    foreach (var g in p.Groups)
                        n += g?.Trips?.Count ?? 0;
                }
            }
            return n;
        }

        private static SupeyTripCluster NewCluster(int groupNumber)
        {
            return new SupeyTripCluster
            {
                GroupNumber = groupNumber,
                GroupColor = SupeyGroupPalette.For(groupNumber),
            };
        }

        private static void AddTripNumbersToCluster(
            SupeyTripCluster cluster,
            IEnumerable<string> tripNumbers,
            IReadOnlyDictionary<string, MCDownloadedTrip> tripByNumber,
            HashSet<string> assignedTripNumbers,
            ref int requested,
            ref int resolved)
        {
            if (tripNumbers == null) return;
            var tripList = new List<MCDownloadedTrip>();
            foreach (var tn in tripNumbers)
            {
                if (string.IsNullOrWhiteSpace(tn)) continue;
                requested++;
                if (!HiatmeTripLookup.TryResolve(tn, tripByNumber, out var trip)) continue;
                if (!assignedTripNumbers.Add(trip.TripNumber)) continue;
                resolved++;
                tripList.Add(trip);
            }
            tripList.Sort(CompareTripsByPickup);
            foreach (var t in tripList)
                cluster.Trips.Add(t);
        }

        private static int CompareTripsByPickup(MCDownloadedTrip a, MCDownloadedTrip b)
        {
            if (TryParsePu(a, out var ta) && TryParsePu(b, out var tb))
                return ta.CompareTo(tb);
            return string.Compare(a?.PUTime, b?.PUTime, StringComparison.OrdinalIgnoreCase);
        }

        private static SupeyDriverProfile FindDriverProfile(
            IList<SupeyDriverProfile> selectedDrivers,
            string driverName)
        {
            if (selectedDrivers == null || string.IsNullOrWhiteSpace(driverName))
                return null;
            var want = driverName.Trim();
            foreach (var d in selectedDrivers)
            {
                if (d != null && string.Equals(d.Name, want, StringComparison.OrdinalIgnoreCase))
                    return d;
            }
            string norm(string s) =>
                string.Join(" ", (s ?? "").Trim().Split((char[])null, StringSplitOptions.RemoveEmptyEntries))
                    .ToLowerInvariant();
            var wantNorm = norm(want);
            foreach (var d in selectedDrivers)
            {
                if (d != null && norm(d.Name) == wantNorm)
                    return d;
            }
            return null;
        }

        private static bool TryParsePu(MCDownloadedTrip t, out TimeSpan ts)
        {
            ts = default;
            var s = t?.PUTime;
            if (string.IsNullOrWhiteSpace(s)) return false;
            return TimeSpan.TryParse(s.Trim(), CultureInfo.InvariantCulture, out ts)
                || TimeSpan.TryParse(s.Trim(), CultureInfo.CurrentCulture, out ts);
        }
    }
}

