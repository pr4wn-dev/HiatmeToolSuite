using System.Threading;
using System.Threading.Tasks;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// BUILD always has a routing path: server OSRM when the panel is up, else local Docker,
    /// else the built-in public OSRM endpoint — no extra software on the dispatcher PC.
    /// </summary>
    internal static class ScheduleOsrmGate
    {
        public static async Task<(bool Ok, string Detail)> CheckAsync(
            HiatmeAiSettings aiSettings,
            CancellationToken cancellationToken = default)
        {
            HiatmeGeoSettings.Refresh();
            aiSettings = aiSettings ?? HiatmeAiSettings.Load();

            if (HiatmeGeoSettings.UseServer)
            {
                var server = await HiatmeGeoClient.GetStatusAsync(aiSettings, cancellationToken)
                    .ConfigureAwait(false);
                if (server != null && server.OsrmLocalOk)
                    return (true, "Server OSRM (" + (server.OsrmActiveEndpoint ?? "local") + ")");
            }

            OsrmSettings.InvalidateHealthCache();
            if (await OsrmSettings.TryHealthCheckAsync(cancellationToken).ConfigureAwait(false))
                return (true, "Local OSRM on this PC");

            return (true, "Public OSRM (automatic fallback)");
        }
    }
}
