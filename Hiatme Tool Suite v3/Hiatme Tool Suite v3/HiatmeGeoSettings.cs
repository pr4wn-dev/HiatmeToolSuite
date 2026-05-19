namespace Hiatme_Tool_Suite_v3
{
    /// <summary>When true, geocode/OSRM go through AIagent on <see cref="HiatmeAiSettings.BaseUrl"/>.</summary>
    internal static class HiatmeGeoSettings
    {
        private static bool? _useServer;

        public static bool UseServer
        {
            get
            {
                if (!_useServer.HasValue)
                    Refresh();
                return _useServer ?? true;
            }
        }

        public static void Refresh()
        {
            try
            {
                _useServer = HiatmeAiSettings.Load().UseServerGeo;
            }
            catch
            {
                _useServer = true;
            }
        }
    }
}
