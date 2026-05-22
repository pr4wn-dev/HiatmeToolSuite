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

        public static string ServerRequiredMessage =>
            "Office AI server is required for geocode and road miles.\r\n\r\n"
            + "On the server PC: start Docker OSRM and the AI panel "
            + "(scripts\\start-local-stack.ps1 in the AIagent repo).\r\n\r\n"
            + "On this PC: connect on the office network so Tool Suite can reach "
            + (string.IsNullOrWhiteSpace(_panelUrl) ? "the panel URL in hiatme_ai.defaults.json" : _panelUrl) + ".";

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
    }
}
