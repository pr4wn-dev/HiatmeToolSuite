using System;
using System.Collections.Generic;
using System.Threading;

namespace Hiatme_Tool_Suite_v3
{
    public partial class Form1
    {
        private sealed class FsTabMapCacheEntry
        {
            public int LinesRevision;
            public int DisplayKey;
            public Dictionary<string, GeoPoint> Pickup;
            public Dictionary<string, GeoPoint> Dropoff;
            public SupeyDriverPlan Plan;
            public string StatusMessage;
        }

        private readonly Dictionary<string, FsTabMapCacheEntry> _fsTabMapCache =
            new Dictionary<string, FsTabMapCacheEntry>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, int> _fsTabLinesRevision =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        private int _fsTabLinesRevisionSeq;

        private void ClearFsTabMapCache()
        {
            _fsTabMapCache.Clear();
        }

        private void ResetFsTabLinesRevisions()
        {
            _fsTabLinesRevision.Clear();
            _fsTabLinesRevisionSeq = 0;
        }

        private void BumpFsTabLinesRevision(string tabName)
        {
            tabName = (tabName ?? "").Trim();
            if (tabName.Length == 0)
                return;

            _fsTabLinesRevision[tabName] = Interlocked.Increment(ref _fsTabLinesRevisionSeq);
            _fsTabMapCache.Remove(tabName);
        }

        private int GetFsTabLinesRevision(string tabName)
        {
            tabName = (tabName ?? "").Trim();
            if (tabName.Length == 0)
                return 0;

            return _fsTabLinesRevision.TryGetValue(tabName, out int revision) ? revision : 0;
        }

        private void SetFsLinesByTabEntry(string tabName, List<ScheduleBuilderPreviewLine> lines)
        {
            tabName = (tabName ?? "").Trim();
            if (tabName.Length == 0)
                return;

            _fsLinesByTab[tabName] = lines ?? new List<ScheduleBuilderPreviewLine>();
            BumpFsTabLinesRevision(tabName);
        }

        private void ReplaceFsLinesByTabFrom(
            IDictionary<string, List<ScheduleBuilderPreviewLine>> source)
        {
            _fsLinesByTab.Clear();
            if (source == null)
                return;

            foreach (var kv in source)
            {
                if (string.IsNullOrWhiteSpace(kv.Key))
                    continue;
                _fsLinesByTab[kv.Key] = kv.Value ?? new List<ScheduleBuilderPreviewLine>();
                BumpFsTabLinesRevision(kv.Key);
            }
        }

        private int ComputeFsTabMapDisplayKey(bool isReservesTab)
        {
            int key = 0;
            if (isReservesTab) key |= 1;
            if (FsShowGroupColorsEnabled) key |= 2;
            if (_fsMapDisplayMode == FsMapDisplayMode.AllDriverTrips) key |= 4;
            return key;
        }

        private bool TryRestoreFsTabMapFromCache(string tabName, int gen, bool isReservesTab)
        {
            if (_fsMap == null || string.IsNullOrWhiteSpace(tabName))
                return false;

            if (!_fsTabMapCache.TryGetValue(tabName, out var entry) || entry?.Plan == null)
                return false;

            int linesRevision = GetFsTabLinesRevision(tabName);
            int displayKey = ComputeFsTabMapDisplayKey(isReservesTab);
            if (entry.LinesRevision != linesRevision || entry.DisplayKey != displayKey)
                return false;

            if (!ScheduleOsrmGate.PreviewRoutingOk)
                return false;

            if (IsFsMapRefreshStale(gen, tabName))
                return false;

            QueueFsMapRefreshApply(
                gen,
                tabName,
                isReservesTab,
                FsShowGroupColorsEnabled,
                new FsMapRefreshPayload
                {
                    Pickup = entry.Pickup,
                    Dropoff = entry.Dropoff,
                    Groups = entry.Plan?.Groups != null
                        ? new List<SupeyTripCluster>(entry.Plan.Groups)
                        : null,
                    Plan = entry.Plan,
                    StatusMessage = entry.StatusMessage,
                    HasOsrmRoutes = true,
                    RoutingOk = true,
                },
                fromCache: true,
                finalizeSelectionSync: true);
            return true;
        }

        private void SaveFsTabMapCache(
            string tabName,
            bool isReservesTab,
            SupeyDriverPlan plan,
            Dictionary<string, GeoPoint> pickup,
            Dictionary<string, GeoPoint> dropoff,
            string statusMessage)
        {
            if (string.IsNullOrWhiteSpace(tabName) || plan == null)
                return;

            var cachedPlan = CloneFsDriverPlanForMapCache(plan);
            _fsTabMapCache[tabName] = new FsTabMapCacheEntry
            {
                LinesRevision = GetFsTabLinesRevision(tabName),
                DisplayKey = ComputeFsTabMapDisplayKey(isReservesTab),
                Pickup = CloneGeoDict(pickup),
                Dropoff = CloneGeoDict(dropoff),
                Plan = cachedPlan,
                StatusMessage = statusMessage ?? "",
            };
        }

        private static SupeyDriverPlan CloneFsDriverPlanForMapCache(SupeyDriverPlan source)
        {
            if (source == null)
                return null;

            var clone = new SupeyDriverPlan
            {
                Driver = source.Driver,
                HomeGeo = source.HomeGeo,
            };

            foreach (var group in source.Groups)
            {
                if (group != null)
                    clone.Groups.Add(CloneFsTripClusterForMapCache(group));
            }

            return clone;
        }

        private static SupeyTripCluster CloneFsTripClusterForMapCache(SupeyTripCluster source)
        {
            var clone = new SupeyTripCluster
            {
                GroupNumber = source.GroupNumber,
                GroupColor = source.GroupColor,
                EarliestPickup = source.EarliestPickup,
                LatestPickup = source.LatestPickup,
                IntraClusterMeters = source.IntraClusterMeters,
                IntraClusterDriveSeconds = source.IntraClusterDriveSeconds,
                IsStraightLineFallback = source.IsStraightLineFallback,
            };

            foreach (var trip in source.Trips)
                clone.Trips.Add(trip);

            clone.PickupPoints.AddRange(source.PickupPoints);
            clone.DropoffPoints.AddRange(source.DropoffPoints);
            clone.RoutePolyline.AddRange(source.RoutePolyline);

            foreach (var leg in source.TripLegPolylines)
            {
                if (leg == null)
                    continue;
                var legClone = new SupeyTripLegPolyline
                {
                    TripNumber = leg.TripNumber,
                    IsStraightLineFallback = leg.IsStraightLineFallback,
                };
                legClone.Points.AddRange(leg.Points);
                clone.TripLegPolylines.Add(legClone);
            }

            return clone;
        }

        private static Dictionary<string, GeoPoint> CloneGeoDict(Dictionary<string, GeoPoint> source)
        {
            if (source == null || source.Count == 0)
                return new Dictionary<string, GeoPoint>(StringComparer.OrdinalIgnoreCase);

            var copy = new Dictionary<string, GeoPoint>(source.Count, StringComparer.OrdinalIgnoreCase);
            foreach (var kv in source)
                copy[kv.Key] = kv.Value;
            return copy;
        }
    }
}
