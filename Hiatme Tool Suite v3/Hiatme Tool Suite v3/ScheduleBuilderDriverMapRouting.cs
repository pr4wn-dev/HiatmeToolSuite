using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>Driver home geocode + home↔first/last group legs for Schedule Builder map preview.</summary>
    internal static class ScheduleBuilderDriverMapRouting
    {
        internal enum GroupDayPosition
        {
            Middle,
            First,
            Last,
            Sole,
        }

        internal static GroupDayPosition ResolveDayPosition(int groupIndex, int groupCount)
        {
            if (groupCount <= 0) return GroupDayPosition.Middle;
            if (groupCount == 1) return GroupDayPosition.Sole;
            if (groupIndex <= 0) return GroupDayPosition.First;
            if (groupIndex >= groupCount - 1) return GroupDayPosition.Last;
            return GroupDayPosition.Middle;
        }

        internal static SupeyDriverProfile FindProfileByTabName(
            IReadOnlyList<SupeyDriverProfile> roster,
            string tabName)
        {
            return FindProfileForScheduleTab(roster, tabName);
        }

        internal static SupeyDriverProfile FindProfileForScheduleTab(
            IReadOnlyList<SupeyDriverProfile> roster,
            string tabName)
        {
            if (roster == null || string.IsNullOrWhiteSpace(tabName)) return null;
            string tab = tabName.Trim();
            string tabNorm = NormalizePersonKey(tab);

            SupeyDriverProfile fuzzy = null;
            SupeyDriverProfile abbrevMatch = null;
            int abbrevCount = 0;
            foreach (var d in roster)
            {
                if (d == null) continue;
                if (string.Equals((d.ScheduleTabKey ?? "").Trim(), tab, StringComparison.OrdinalIgnoreCase))
                    return d;
                if (string.Equals(NormalizePersonKey(d.Name), tabNorm, StringComparison.OrdinalIgnoreCase))
                    return d;
                if (string.Equals((d.Name ?? "").Trim(), tab, StringComparison.OrdinalIgnoreCase))
                    return d;
                if (fuzzy == null && NameMatchesScheduleTab(d.Name, tab))
                    fuzzy = d;
                if (AbbrevTabMatchesDriverName(tab, d.Name))
                {
                    abbrevMatch = d;
                    abbrevCount++;
                }
            }

            if (abbrevCount == 1)
                return abbrevMatch;
            return fuzzy;
        }

        private static string NormalizePersonKey(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            var parts = s.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            return string.Join(" ", parts);
        }

        /// <summary>Template tabs like "Aaron C" or "Jamie B" vs roster "Aaron N Cadwell".</summary>
        private static bool AbbrevTabMatchesDriverName(string tab, string driverName)
        {
            tab = NormalizePersonKey(tab);
            driverName = NormalizePersonKey(driverName);
            if (tab.Length == 0 || driverName.Length == 0) return false;

            var tabParts = tab.Split(' ');
            var drvParts = driverName.Split(' ');
            if (tabParts.Length < 2 || drvParts.Length < 2) return false;
            if (!tabParts[0].Equals(drvParts[0], StringComparison.OrdinalIgnoreCase))
                return false;

            string tabLast = tabParts[tabParts.Length - 1];
            string drvLast = drvParts[drvParts.Length - 1];
            if (tabLast.Length == 1)
                return drvLast.StartsWith(tabLast, StringComparison.OrdinalIgnoreCase);
            if (tabParts.Length == 2 && tabLast.Length <= 3)
                return drvLast.StartsWith(tabLast, StringComparison.OrdinalIgnoreCase)
                    || drvLast.Equals(tabLast, StringComparison.OrdinalIgnoreCase);
            return false;
        }

        private static bool NameMatchesScheduleTab(string driverName, string tab)
        {
            driverName = NormalizePersonKey(driverName);
            tab = NormalizePersonKey(tab);
            if (driverName.Length == 0 || tab.Length == 0) return false;
            if (driverName.StartsWith(tab, StringComparison.OrdinalIgnoreCase)
                && (driverName.Length == tab.Length || driverName[tab.Length] == ' '))
                return true;
            if (driverName.EndsWith(tab, StringComparison.OrdinalIgnoreCase)
                && driverName.Length > tab.Length
                && driverName[driverName.Length - tab.Length - 1] == ' ')
                return true;
            return false;
        }

        /// <summary>Cache-first home geocode (same resilience as trip map pins).</summary>
        internal static async Task<GeoPoint?> ResolveHomeGeoAsync(
            SupeyDriverProfile driver,
            CancellationToken token)
        {
            return await ScheduleBuilderMapGeocode.ResolveHomeAsync(driver, token).ConfigureAwait(false);
        }

        /// <summary>Home on first/last group OSRM waypoints (Schedule Builder map — not a separate deadhead line).</summary>
        internal static void ResolveHomeRouteBookends(
            int groupIndex,
            int groupCount,
            GeoPoint? homeGeo,
            out GeoPoint? routeStart,
            out GeoPoint? routeEnd)
        {
            routeStart = null;
            routeEnd = null;
            if (!homeGeo.HasValue || !IsValid(homeGeo.Value) || groupCount <= 0 || groupIndex < 0)
                return;
            if (groupIndex == 0)
                routeStart = homeGeo;
            if (groupCount == 1)
                routeEnd = homeGeo;
            else if (groupIndex == groupCount - 1)
                routeEnd = homeGeo;
        }

        internal static GeoPoint FirstPickupPoint(SupeyTripCluster group)
        {
            if (group?.PickupPoints != null && group.PickupPoints.Count > 0)
            {
                foreach (var p in group.PickupPoints)
                {
                    if (IsValid(p)) return p;
                }
            }

            var waypoints = ScheduleBuilderPreviewGroups.CollectDeskRouteWaypoints(group);
            if (waypoints.Count > 0) return waypoints[0];
            return group?.PickupCentroid ?? default;
        }

        internal static GeoPoint LastDropoffPoint(SupeyTripCluster group)
        {
            if (group?.DropoffPoints != null && group.DropoffPoints.Count > 0)
            {
                for (int i = group.DropoffPoints.Count - 1; i >= 0; i--)
                {
                    var p = group.DropoffPoints[i];
                    if (IsValid(p)) return p;
                }
            }

            var waypoints = ScheduleBuilderPreviewGroups.CollectDeskRouteWaypoints(group);
            if (waypoints.Count > 0) return waypoints[waypoints.Count - 1];
            return group?.DropoffCentroid ?? default;
        }

        internal static GeoPoint PickupForTripIndex(SupeyTripCluster group, int tripIndex)
        {
            if (group?.PickupPoints != null && tripIndex >= 0 && tripIndex < group.PickupPoints.Count
                && IsValid(group.PickupPoints[tripIndex]))
                return group.PickupPoints[tripIndex];
            return FirstPickupPoint(group);
        }

        internal static GeoPoint DropoffForTripIndex(SupeyTripCluster group, int tripIndex)
        {
            if (group?.DropoffPoints != null && tripIndex >= 0 && tripIndex < group.DropoffPoints.Count
                && IsValid(group.DropoffPoints[tripIndex]))
                return group.DropoffPoints[tripIndex];
            return LastDropoffPoint(group);
        }

        private static bool IsValid(GeoPoint p) => !(p.Lat == 0 && p.Lng == 0);
    }
}
