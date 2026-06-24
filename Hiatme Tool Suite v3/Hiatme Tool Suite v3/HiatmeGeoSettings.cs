using System.Threading;
using System.Threading.Tasks;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Production dispatch uses the office AI panel only (Maine OSRM + shared geocode cache).
    /// No Nominatim or public OSRM fallback when <see cref="ServerOnly"/> is on (default).
    /// </summary>
    internal static class HiatmeGeoSettings
    {
        private static bool? _serverOnly;
        private static bool? _panelReachable;
        private static string _panelUrl;

        /// <summary>From <see cref="HiatmeAiSettings.UseServerGeo"/> (default true).</summary>
        public static bool ServerOnly
        {
            get
            {
                if (!_serverOnly.HasValue)
                    Refresh();
                return _serverOnly ?? true;
            }
        }

        /// <summary>Panel answered on last probe — server geocode/OSRM may be used.</summary>
        public static bool UseServer
        {
            get
            {
                if (!_panelReachable.HasValue)
                    Refresh();
                return _panelReachable == true;
            }
        }

        public static bool AllowOfflineFallback => !ServerOnly;

        public static string ActivePanelUrl => _panelUrl ?? "";

        public static string ServerRequiredMessage
        {
            get
            {
                var detail = HiatmeAiSettings.LastConnectionDetail;
                if (!string.IsNullOrWhiteSpace(detail))
                    return detail;
                return
                    "Office AI server is required for geocode and road miles.\r\n\r\n"
                    + "On the server PC: start Docker OSRM and the AI panel "
                    + "(scripts\\start-local-stack.ps1 in the AIagent repo).\r\n\r\n"
                    + "On this PC: desks on the same office Wi‑Fi/LAN auto-find the panel — no VPN.\r\n\r\n"
                    + "Panel: "
                    + (string.IsNullOrWhiteSpace(_panelUrl) ? "(not configured)" : _panelUrl);
            }
        }

        public static void Configure(HiatmeAiSettings settings)
        {
            if (settings == null)
            {
                _serverOnly = true;
                _panelReachable = false;
                _panelUrl = null;
                return;
            }

            _panelUrl = settings.BaseUrl?.Trim();
            _serverOnly = settings.UseServerGeo;
            _panelReachable = HiatmeAiSettings.ProbePanelPublic(settings.BaseUrl, settings.ApiToken);
        }

        public static void Refresh()
        {
            try
            {
                Configure(HiatmeAiSettings.Load());
            }
            catch
            {
                _serverOnly = true;
                _panelReachable = false;
                _panelUrl = null;
            }
        }

        public static void Invalidate()
        {
            _serverOnly = null;
            _panelReachable = null;
            _panelUrl = null;
        }

        /// <summary>
        /// Re-probe the panel before BUILD (Load() only probed once at startup).
        /// </summary>
        public static async Task<bool> RefreshConnectivityAsync(
            HiatmeAiSettings settings,
            CancellationToken token = default)
        {
            settings = settings ?? HiatmeAiSettings.Load();
            _panelUrl = settings.BaseUrl?.Trim();
            _serverOnly = settings.UseServerGeo;

            bool ok = await HiatmeAiSettings.RefreshPanelConnectionAsync(token).ConfigureAwait(false);
            settings = HiatmeAiSettings.Load();
            _panelUrl = settings.BaseUrl?.Trim();
            _panelReachable = ok;
            return ok;
        }
    }
}
