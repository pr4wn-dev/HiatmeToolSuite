using System;
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
    }
}
