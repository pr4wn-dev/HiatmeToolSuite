using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Geocode for Schedule Builder map pins. Uses disk cache + one server batch call when online;
    /// never uses aggressive per-address CancelAfter (that spammed TaskCanceledException in the debugger).
    /// </summary>
    internal static class ScheduleBuilderMapGeocode
    {
        /// <summary>
        /// Resolve PU/DO for every trip on a driver or Reserves tab. Prefer batch server geocode.
        /// </summary>
        public static async Task ResolveTripsForMapAsync(
            IEnumerable<MCDownloadedTrip> trips,
            IDictionary<string, GeoPoint> pickup,
            IDictionary<string, GeoPoint> dropoff,
            CancellationToken token = default)
        {
            if (trips == null || pickup == null || dropoff == null)
                return;

            var needLookup = new List<(MCDownloadedTrip trip, string key, bool isPickup)>();
            var serverAddresses = new List<(string street, string city, string state, string zip)>();
            var seenServer = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var trip in trips)
            {
                if (trip == null) continue;
                string key = (trip.TripNumber ?? "").Trim();
                if (key.Length == 0) continue;

                if (TryResolveCachedEndpoint(trip.PUStreet, trip.PUCity, out GeoPoint pu))
                    pickup[key] = pu;
                else
                {
                    needLookup.Add((trip, key, isPickup: true));
                    QueueServerAddress(serverAddresses, seenServer, trip.PUStreet, trip.PUCity);
                }

                if (TryResolveCachedEndpoint(trip.DOStreet, trip.DOCITY, out GeoPoint dof))
                    dropoff[key] = dof;
                else
                {
                    needLookup.Add((trip, key, isPickup: false));
                    QueueServerAddress(serverAddresses, seenServer, trip.DOStreet, trip.DOCITY);
                }
            }

            if (needLookup.Count == 0)
                return;

            HiatmeGeoSettings.Refresh();

            if (HiatmeGeoSettings.UseServer && serverAddresses.Count > 0)
            {
                try
                {
                    await AddressGeocoder.PrefetchViaServerAsync(serverAddresses, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch
                {
                    // Batch failed — fall through to per-address lookup below.
                }
            }
            else if (HiatmeGeoSettings.ServerOnly && !HiatmeGeoSettings.UseServer)
            {
                return;
            }

            foreach (var item in needLookup)
            {
                token.ThrowIfCancellationRequested();
                string street = item.isPickup ? item.trip.PUStreet : item.trip.DOStreet;
                string city = item.isPickup ? item.trip.PUCity : item.trip.DOCITY;
                var dict = item.isPickup ? pickup : dropoff;
                if (dict.ContainsKey(item.key))
                    continue;

                GeoPoint? pt = await ResolveEndpointAsync(street, city, token).ConfigureAwait(false);
                if (pt.HasValue)
                    dict[item.key] = pt.Value;
            }
        }

        public static async Task<GeoPoint?> ResolveEndpointAsync(
            string street, string city, CancellationToken token = default)
        {
            if (TryResolveCachedEndpoint(street, city, out GeoPoint cached))
                return cached;

            if (HiatmeGeoSettings.ServerOnly && !HiatmeGeoSettings.UseServer)
                return null;

            try
            {
                if (HiatmeGeoSettings.UseServer)
                {
                    var ai = HiatmeAiSettings.Load();
                    return await HiatmeGeoClient.ResolveAsync(
                        ai, street, city, "ME", null, "us", token).ConfigureAwait(false);
                }

                return await AddressGeocoder.ResolveTripEndpointAsync(street, city, token)
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

            try
            {
                if (HiatmeGeoSettings.UseServer)
                {
                    var ai = HiatmeAiSettings.Load();
                    var serverPt = await HiatmeGeoClient.ResolveAsync(
                        ai, street, city, state, zip, "us", token).ConfigureAwait(false);
                    if (serverPt.HasValue)
                        return serverPt;
                }

                return await AddressGeocoder.ResolveWithFallbacksAsync(
                    street, city, state, zip, "us", token).ConfigureAwait(false);
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

        private static bool TryResolveCachedEndpoint(string street, string city, out GeoPoint point)
        {
            if (SupeyKnownFacilities.TryResolve(street, city, out point))
                return true;

            if (AddressGeocoder.TryGetCachedPoint(street, city, "ME", null, "us", out var cached))
            {
                point = cached;
                return true;
            }

            point = default;
            return false;
        }

        private static void QueueServerAddress(
            List<(string street, string city, string state, string zip)> list,
            HashSet<string> seen,
            string street,
            string city)
        {
            string s = (street ?? "").Trim();
            string c = (city ?? "").Trim();
            if (s.Length == 0 && c.Length == 0)
                return;
            string key = s + "|" + c;
            if (!seen.Add(key))
                return;
            list.Add((s, c, "ME", ""));
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
