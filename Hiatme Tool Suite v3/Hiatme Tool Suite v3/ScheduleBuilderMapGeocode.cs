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
        internal sealed class GeocodeRunDiagnostics
        {
            public DateTime GeneratedAtLocal { get; set; } = DateTime.Now;
            public int TripsSeen { get; set; }
            public int EndpointLookupsNeeded { get; set; }
            public int PickupResolved { get; set; }
            public int DropoffResolved { get; set; }
            public int PickupUnresolved { get; set; }
            public int DropoffUnresolved { get; set; }
            public int ResolvedFromCache { get; set; }
            public int ResolvedFromServer { get; set; }
            public int ResolvedFromFallback { get; set; }
            public int ServerBatchQueued { get; set; }
            public int ServerBatchSubmitted { get; set; }
            public long CacheHitsDelta { get; set; }
            public long CacheMissesDelta { get; set; }
            public List<string> UnresolvedEndpointSamples { get; } = new List<string>();

            public int EndpointsResolved => PickupResolved + DropoffResolved;
            public int EndpointsUnresolved => PickupUnresolved + DropoffUnresolved;

            public string ToStatusLine()
            {
                return "geo: " + EndpointsResolved + " resolved, " + EndpointsUnresolved + " unresolved"
                    + " (cache +" + CacheHitsDelta + "/" + CacheMissesDelta
                    + ", server " + ResolvedFromServer
                    + ", fallback " + ResolvedFromFallback + ")";
            }

            public string ToDetailText(int maxSamples = 6)
            {
                var text = "Geocode diagnostics\r\n"
                    + "Generated: " + GeneratedAtLocal.ToString("g") + "\r\n"
                    + "Trips scanned: " + TripsSeen + "\r\n"
                    + "Endpoints needed lookup: " + EndpointLookupsNeeded + "\r\n"
                    + "Resolved endpoints: " + EndpointsResolved + "\r\n"
                    + "Unresolved endpoints: " + EndpointsUnresolved + "\r\n"
                    + "Resolved from cache: " + ResolvedFromCache + "\r\n"
                    + "Resolved from AI server: " + ResolvedFromServer + "\r\n"
                    + "Resolved from local fallback: " + ResolvedFromFallback + "\r\n"
                    + "Server batch queued/submitted: " + ServerBatchQueued + "/" + ServerBatchSubmitted + "\r\n"
                    + "AddressGeocoder cache hit/miss delta: +" + CacheHitsDelta + "/" + CacheMissesDelta;

                if (UnresolvedEndpointSamples.Count > 0)
                {
                    text += "\r\n\r\nUnresolved examples:";
                    for (int i = 0; i < Math.Min(maxSamples, UnresolvedEndpointSamples.Count); i++)
                        text += "\r\n- " + UnresolvedEndpointSamples[i];
                }
                return text;
            }
        }

        private enum EndpointResolveSource
        {
            None,
            Cache,
            Server,
            LocalFallback,
        }

        private static GeocodeRunDiagnostics _lastDiagnostics = new GeocodeRunDiagnostics();
        internal static GeocodeRunDiagnostics LastDiagnostics => _lastDiagnostics;
        internal static string BuildDiagnosticsClipboardText(int maxSamples = 20) =>
            _lastDiagnostics?.ToDetailText(maxSamples) ?? "No geocode diagnostics available.";

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

            long startHits = AddressGeocoder.CacheHits;
            long startMisses = AddressGeocoder.CacheMisses;
            var diag = new GeocodeRunDiagnostics();
            var needLookup = new List<(MCDownloadedTrip trip, string key, bool isPickup)>();
            var serverAddresses = new List<(string street, string city, string state, string zip)>();
            var seenServer = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var trip in trips)
            {
                if (trip == null) continue;
                diag.TripsSeen++;
                string key = (trip.TripNumber ?? "").Trim();
                if (key.Length == 0) continue;

                if (TryResolveCachedEndpoint(trip.PUStreet, trip.PUCity, out GeoPoint pu))
                {
                    pickup[key] = pu;
                    diag.PickupResolved++;
                    diag.ResolvedFromCache++;
                }
                else
                {
                    needLookup.Add((trip, key, isPickup: true));
                    QueueServerAddress(serverAddresses, seenServer, trip.PUStreet, trip.PUCity);
                }

                if (TryResolveCachedEndpoint(trip.DOStreet, trip.DOCITY, out GeoPoint dof))
                {
                    dropoff[key] = dof;
                    diag.DropoffResolved++;
                    diag.ResolvedFromCache++;
                }
                else
                {
                    needLookup.Add((trip, key, isPickup: false));
                    QueueServerAddress(serverAddresses, seenServer, trip.DOStreet, trip.DOCITY);
                }
            }
            diag.EndpointLookupsNeeded = needLookup.Count;
            diag.ServerBatchQueued = serverAddresses.Count;

            if (needLookup.Count == 0)
            {
                diag.CacheHitsDelta = AddressGeocoder.CacheHits - startHits;
                diag.CacheMissesDelta = AddressGeocoder.CacheMisses - startMisses;
                _lastDiagnostics = diag;
                return;
            }

            HiatmeGeoSettings.Refresh();

            if (HiatmeGeoSettings.UseServer && serverAddresses.Count > 0)
            {
                try
                {
                    diag.ServerBatchSubmitted = await AddressGeocoder.PrefetchViaServerAsync(serverAddresses, token)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    diag.CacheHitsDelta = AddressGeocoder.CacheHits - startHits;
                    diag.CacheMissesDelta = AddressGeocoder.CacheMisses - startMisses;
                    _lastDiagnostics = diag;
                    return;
                }
                catch
                {
                    // Batch failed — fall through to per-address lookup below.
                }
            }
            else if (HiatmeGeoSettings.ServerOnly && !HiatmeGeoSettings.UseServer)
            {
                diag.CacheHitsDelta = AddressGeocoder.CacheHits - startHits;
                diag.CacheMissesDelta = AddressGeocoder.CacheMisses - startMisses;
                _lastDiagnostics = diag;
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

                var (pt, source) = await ResolveEndpointDetailedAsync(street, city, token).ConfigureAwait(false);
                if (pt.HasValue)
                {
                    dict[item.key] = pt.Value;
                    if (item.isPickup) diag.PickupResolved++;
                    else diag.DropoffResolved++;

                    switch (source)
                    {
                        case EndpointResolveSource.Cache: diag.ResolvedFromCache++; break;
                        case EndpointResolveSource.Server: diag.ResolvedFromServer++; break;
                        case EndpointResolveSource.LocalFallback: diag.ResolvedFromFallback++; break;
                    }
                }
                else
                {
                    if (item.isPickup) diag.PickupUnresolved++;
                    else diag.DropoffUnresolved++;
                    if (diag.UnresolvedEndpointSamples.Count < 20)
                    {
                        string label = item.isPickup ? "PU" : "DO";
                        diag.UnresolvedEndpointSamples.Add(label + " " + item.key + " · "
                            + ((street ?? "").Trim()) + ", " + ((city ?? "").Trim()));
                    }
                }
            }

            diag.CacheHitsDelta = AddressGeocoder.CacheHits - startHits;
            diag.CacheMissesDelta = AddressGeocoder.CacheMisses - startMisses;
            diag.GeneratedAtLocal = DateTime.Now;
            _lastDiagnostics = diag;
        }

        public static async Task<GeoPoint?> ResolveEndpointAsync(
            string street, string city, CancellationToken token = default)
        {
            var (pt, _) = await ResolveEndpointDetailedAsync(street, city, token).ConfigureAwait(false);
            return pt;
        }

        private static async Task<(GeoPoint? point, EndpointResolveSource source)> ResolveEndpointDetailedAsync(
            string street, string city, CancellationToken token = default)
        {
            if (TryResolveCachedEndpoint(street, city, out GeoPoint cached))
                return (cached, EndpointResolveSource.Cache);

            if (HiatmeGeoSettings.ServerOnly && !HiatmeGeoSettings.UseServer)
                return (null, EndpointResolveSource.None);

            try
            {
                if (HiatmeGeoSettings.UseServer)
                {
                    var ai = HiatmeAiSettings.Load();
                    var serverPt = await HiatmeGeoClient.ResolveAsync(
                        ai, street, city, "ME", null, "us", token).ConfigureAwait(false);
                    if (serverPt.HasValue)
                        return (serverPt, EndpointResolveSource.Server);

                    // Panel had no match (or transient issue) — use local fallback path when allowed.
                    if (!HiatmeGeoSettings.ServerOnly)
                    {
                        var localPt = await AddressGeocoder.ResolveTripEndpointAsync(street, city, token)
                            .ConfigureAwait(false);
                        if (localPt.HasValue)
                            return (localPt, EndpointResolveSource.LocalFallback);
                    }
                    return (null, EndpointResolveSource.None);
                }

                var localOnly = await AddressGeocoder.ResolveTripEndpointAsync(street, city, token)
                    .ConfigureAwait(false);
                return localOnly.HasValue
                    ? (localOnly, EndpointResolveSource.LocalFallback)
                    : (null, EndpointResolveSource.None);
            }
            catch (OperationCanceledException)
            {
                return (null, EndpointResolveSource.None);
            }
            catch
            {
                return (null, EndpointResolveSource.None);
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

            foreach (var st in CachedStateVariants())
            {
                if (AddressGeocoder.TryGetCachedPoint(street, city, st, null, "us", out var cached))
                {
                    point = cached;
                    return true;
                }
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
            s = CollapseSpaces(s);
            c = CollapseSpaces(c);
            if (s.Length == 0 && c.Length == 0)
                return;
            string key = s + "|" + c;
            if (!seen.Add(key))
                return;
            list.Add((s, c, "ME", ""));
        }

        private static IEnumerable<string> CachedStateVariants()
        {
            yield return "ME";
            yield return "Maine";
            yield return "";
        }

        private static string CollapseSpaces(string s)
        {
            if (string.IsNullOrWhiteSpace(s))
                return "";
            while (s.Contains("  "))
                s = s.Replace("  ", " ");
            return s.Trim();
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
