using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Best-effort geocode for Schedule Builder map pins only. Never blocks BUILD/SAVE;
    /// uses known facilities + disk cache first, then optional short network tries.
    /// </summary>
    internal static class ScheduleBuilderMapGeocode
    {
        private static readonly TimeSpan NetworkTryTimeout = TimeSpan.FromSeconds(2);

        public static async Task<GeoPoint?> ResolveEndpointAsync(
            string street, string city, CancellationToken token = default)
        {
            GeoPoint known;
            if (SupeyKnownFacilities.TryResolve(street, city, out known))
                return known;

            if (AddressGeocoder.TryGetCachedPoint(street, city, "ME", null, "us", out var cached))
                return cached;

            if (HiatmeGeoSettings.ServerOnly && !HiatmeGeoSettings.UseServer)
                return null;

            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                timeout.CancelAfter(NetworkTryTimeout);
                var linked = timeout.Token;
                try
                {
                    if (HiatmeGeoSettings.UseServer)
                    {
                        var ai = HiatmeAiSettings.Load();
                        var serverPt = await HiatmeGeoClient.ResolveAsync(
                            ai, street, city, "ME", null, "us", linked).ConfigureAwait(false);
                        if (serverPt.HasValue)
                            return serverPt;
                    }

                    // Panel unreachable → AddressGeocoder uses Nominatim (no office server).
                    if (!HiatmeGeoSettings.UseServer)
                        return await AddressGeocoder.ResolveTripEndpointAsync(street, city, linked)
                            .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return null;
                }
                catch
                {
                    return null;
                }
            }

            return null;
        }

        /// <summary>Driver home — disk cache with state variants, then server / fallbacks.</summary>
        public static async Task<GeoPoint?> ResolveHomeAsync(
            SupeyDriverProfile driver,
            CancellationToken token = default)
        {
            if (driver == null) return null;
            string street = (driver.HomeStreet ?? "").Trim();
            string city = (driver.HomeCity ?? "").Trim();
            if (street.Length == 0 && city.Length == 0) return null;

            string zip = (driver.HomeZip ?? "").Trim();
            string state = (driver.HomeState ?? "").Trim();
            if (state.Length == 0) state = "ME";

            foreach (var st in HomeStateCacheVariants(state))
            {
                if (AddressGeocoder.TryGetCachedPoint(street, city, st, zip, "us", out var cached))
                    return cached;
            }

            HiatmeGeoSettings.Refresh();

            if (HiatmeGeoSettings.ServerOnly && !HiatmeGeoSettings.UseServer)
                return null;

            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(8));
                try
                {
                    if (HiatmeGeoSettings.UseServer)
                    {
                        var ai = HiatmeAiSettings.Load();
                        var serverPt = await HiatmeGeoClient.ResolveAsync(
                            ai, street, city, state, zip, "us", timeout.Token).ConfigureAwait(false);
                        if (serverPt.HasValue)
                            return serverPt;
                    }

                    return await AddressGeocoder.ResolveWithFallbacksAsync(
                        street, city, state, zip, "us", timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return null;
                }
                catch
                {
                    return null;
                }
            }
        }

        private static IEnumerable<string> HomeStateCacheVariants(string state)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string[] candidates =
            {
                state,
                "ME",
                "Maine",
                "maine",
                "",
            };
            foreach (var c in candidates)
            {
                if (seen.Add(c ?? ""))
                    yield return c ?? "";
            }
        }
    }
}
