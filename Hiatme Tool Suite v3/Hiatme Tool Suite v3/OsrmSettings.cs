using System;
using System.Configuration;
using System.Globalization;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// OSRM endpoint configuration (local Docker by default; optional public fallback).
    /// </summary>
    internal static class OsrmSettings
    {
        private const string DefaultLocalUrl = "http://127.0.0.1:5000/route/v1/driving/";
        private const string DefaultPublicUrl = "https://router.project-osrm.org/route/v1/driving/";

        private static readonly object HealthLock = new object();
        private static DateTime _healthCheckedUtc = DateTime.MinValue;
        private static bool _localHealthy;
        private static readonly TimeSpan HealthCacheTtl = TimeSpan.FromSeconds(30);

        private static readonly HttpClient HealthHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

        public static string LocalBaseUrl { get; } = NormalizeRouteBaseUrl(
            ConfigurationManager.AppSettings["OsrmBaseUrl"] ?? DefaultLocalUrl);

        public static string PublicFallbackUrl { get; } = NormalizeRouteBaseUrl(
            ConfigurationManager.AppSettings["OsrmPublicFallbackUrl"] ?? DefaultPublicUrl);

        public static bool PreferLocal { get; } = ParseBool(
            ConfigurationManager.AppSettings["OsrmPreferLocal"], true);

        public static int MaxConcurrent { get; } = ParseInt(
            ConfigurationManager.AppSettings["OsrmMaxConcurrent"], 6, 1, 32);

        /// <summary>Base URI used for route requests after health / fallback resolution.</summary>
        public static string CurrentRouteBaseUri => ResolveRouteBaseUri();

        public static bool IsLocalHost(Uri uri)
        {
            if (uri == null || !uri.IsAbsoluteUri) return false;
            string host = uri.Host ?? "";
            return host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                || host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase)
                || host == "::1";
        }

        public static bool IsLocalEndpoint(string baseUrl)
        {
            if (string.IsNullOrWhiteSpace(baseUrl)) return false;
            Uri uri;
            return Uri.TryCreate(baseUrl, UriKind.Absolute, out uri) && IsLocalHost(uri);
        }

        /// <summary>Short label for the Supey toolbar.</summary>
        public static string EndpointDescription
        {
            get
            {
                if (IsLocalEndpoint(CurrentRouteBaseUri))
                    return "local (" + new Uri(CurrentRouteBaseUri).Host + ")";
                return "public demo";
            }
        }

        public static async Task<bool> TryHealthCheckAsync(CancellationToken token = default)
        {
            lock (HealthLock)
            {
                if (DateTime.UtcNow - _healthCheckedUtc < HealthCacheTtl)
                    return _localHealthy;
            }

            bool ok = await ProbeLocalAsync(token).ConfigureAwait(false);
            lock (HealthLock)
            {
                _healthCheckedUtc = DateTime.UtcNow;
                _localHealthy = ok;
            }
            return ok;
        }

        public static void InvalidateHealthCache()
        {
            lock (HealthLock) { _healthCheckedUtc = DateTime.MinValue; }
        }

        public static string LocalOfflineHint =>
            "Start local OSRM: run tools\\osrm\\scripts\\start-osrm.ps1 (see tools\\osrm\\README.md).";

        private static string ResolveRouteBaseUri()
        {
            if (!PreferLocal)
                return PublicFallbackUrl;

            lock (HealthLock)
            {
                if (DateTime.UtcNow - _healthCheckedUtc < HealthCacheTtl && _localHealthy)
                    return LocalBaseUrl;
            }

            // Synchronous probe on first route request if cache cold (Build path).
            try
            {
                bool ok = ProbeLocalAsync(CancellationToken.None).GetAwaiter().GetResult();
                lock (HealthLock)
                {
                    _healthCheckedUtc = DateTime.UtcNow;
                    _localHealthy = ok;
                }
                if (ok) return LocalBaseUrl;
            }
            catch { /* fall through */ }

            return PublicFallbackUrl;
        }

        private static async Task<bool> ProbeLocalAsync(CancellationToken token)
        {
            // Bangor-area coordinates inside Maine
            string probeUrl = LocalBaseUrl + "-68.77,44.80;-68.65,44.91?overview=false&alternatives=false&steps=false";
            try
            {
                using (var resp = await HealthHttp.GetAsync(probeUrl, token).ConfigureAwait(false))
                {
                    if (!resp.IsSuccessStatusCode) return false;
                    string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    return body != null && body.IndexOf("\"code\":\"Ok\"", StringComparison.Ordinal) >= 0;
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Builds a full OSRM route URL: base + lng,lat;... + query (no leading slash on coordinates).
        /// </summary>
        public static string BuildRouteRequestUrl(string coordinatePath, string queryOptions)
        {
            if (string.IsNullOrWhiteSpace(coordinatePath))
                throw new ArgumentException("OSRM coordinate path is empty.", nameof(coordinatePath));

            coordinatePath = coordinatePath.Trim().TrimStart('/');
            string q = (queryOptions ?? "").Trim().TrimStart('?');
            return CurrentRouteBaseUri + coordinatePath + "?" + q;
        }

        private static string NormalizeRouteBaseUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return DefaultLocalUrl;
            url = url.Trim();

            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri uri))
                return DefaultLocalUrl;

            string path = uri.AbsolutePath ?? "";
            string authority = uri.GetLeftPart(UriPartial.Authority);

            // Host-only (http://127.0.0.1:5000) — append standard OSRM route prefix.
            if (string.IsNullOrEmpty(path) || path == "/"
                || path.IndexOf("/route/v1/", StringComparison.OrdinalIgnoreCase) < 0)
                return authority + "/route/v1/driving/";

            if (!path.EndsWith("/", StringComparison.Ordinal))
                path += "/";

            return authority + path;
        }

        private static bool ParseBool(string value, bool defaultValue)
        {
            if (string.IsNullOrWhiteSpace(value)) return defaultValue;
            bool b;
            return bool.TryParse(value, out b) ? b : defaultValue;
        }

        private static int ParseInt(string value, int defaultValue, int min, int max)
        {
            if (string.IsNullOrWhiteSpace(value)) return defaultValue;
            int n;
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out n))
                return defaultValue;
            if (n < min) return min;
            if (n > max) return max;
            return n;
        }
    }
}
