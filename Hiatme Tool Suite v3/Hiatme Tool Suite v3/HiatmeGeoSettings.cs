namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// When the AI panel is reachable, geocode/OSRM use it. Otherwise Tool Suite uses its own
    /// Nominatim + local/public OSRM — no VPN or extra apps required.
    /// </summary>
    internal static class HiatmeGeoSettings
    {
        private static bool? _useServer;
        private static string _panelUrl;

        public static bool UseServer
        {
            get
            {
                if (!_useServer.HasValue)
                    Refresh();
                return _useServer ?? false;
            }
        }

        public static string ActivePanelUrl => _panelUrl ?? "";

        public static void Configure(HiatmeAiSettings settings)
        {
            if (settings == null)
            {
                _useServer = false;
                _panelUrl = null;
                return;
            }

            _panelUrl = settings.BaseUrl;
            _useServer = settings.UseServerGeo
                && HiatmeAiSettings.ProbePanelPublic(settings.BaseUrl, settings.ApiToken);
        }

        public static void Refresh()
        {
            try
            {
                Configure(HiatmeAiSettings.Load());
            }
            catch
            {
                _useServer = false;
                _panelUrl = null;
            }
        }

        public static void Invalidate()
        {
            _useServer = null;
            _panelUrl = null;
        }
    }
}
