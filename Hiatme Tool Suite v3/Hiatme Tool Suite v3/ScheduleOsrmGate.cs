using System.Threading;
using System.Threading.Tasks;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>BUILD needs road routing on the AI server (default) or local OSRM only if server geo is off.</summary>
    internal static class ScheduleOsrmGate
    {
        public static async Task<(bool Ok, string Detail)> CheckAsync(
            HiatmeAiSettings aiSettings,
            CancellationToken cancellationToken = default)
        {
            HiatmeGeoSettings.Refresh();
            OsrmSettings.InvalidateHealthCache();

            if (HiatmeGeoSettings.UseServer && aiSettings != null)
            {
                var server = await HiatmeGeoClient.GetStatusAsync(aiSettings, cancellationToken)
                    .ConfigureAwait(false);
                if (server != null && server.OsrmLocalOk)
                    return (true, "Server OSRM OK (" + (server.OsrmActiveEndpoint ?? "local") + ")");

                string baseUrl = (aiSettings.BaseUrl ?? "").Trim();
                return (false,
                    "Road routing runs on the AI server, not this PC.\r\n\r\n"
                    + "On the server (" + (string.IsNullOrEmpty(baseUrl) ? "panel host" : baseUrl) + "):\r\n"
                    + "  • Docker running\r\n"
                    + "  • OSRM started (tools\\osrm\\scripts\\start-osrm.ps1 on the server)\r\n\r\n"
                    + "Supey only talks to the server for geocode + miles.");
            }

            if (await OsrmSettings.TryHealthCheckAsync(cancellationToken).ConfigureAwait(false))
                return (true, "Local OSRM OK (server geo disabled).");

            return (false,
                "Road routing (OSRM) is not available on this PC.\r\n\r\n"
                + OsrmSettings.LocalOfflineHint);
        }
    }
}
