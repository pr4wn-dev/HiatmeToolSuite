using System.Threading;
using System.Threading.Tasks;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>BUILD requires office server OSRM (Maine graph on the AI panel host).</summary>
    internal static class ScheduleOsrmGate
    {
        public static async Task<(bool Ok, string Detail)> CheckAsync(
            HiatmeAiSettings aiSettings,
            CancellationToken cancellationToken = default)
        {
            HiatmeGeoSettings.Refresh();
            aiSettings = aiSettings ?? HiatmeAiSettings.Load();

            if (HiatmeGeoSettings.ServerOnly && !HiatmeGeoSettings.UseServer)
                return (false, HiatmeGeoSettings.ServerRequiredMessage);

            if (!HiatmeGeoSettings.UseServer)
                return (true, "Offline geocode/OSRM allowed (UseServerGeo=false in config).");

            var server = await HiatmeGeoClient.GetStatusAsync(aiSettings, cancellationToken)
                .ConfigureAwait(false);
            if (server != null && server.OsrmLocalOk)
                return (true, "Server OSRM OK (" + (server.OsrmActiveEndpoint ?? "local") + ")");

            return (false,
                "Maine OSRM on the office AI server is not available.\r\n\r\n"
                + "On the server PC: Docker running, then tools\\osrm\\scripts\\start-osrm.ps1 "
                + "and scripts\\restart-panel.ps1 (AIagent repo).\r\n\r\n"
                + "Panel: " + (HiatmeGeoSettings.ActivePanelUrl ?? aiSettings.BaseUrl ?? "(not set)"));
        }
    }
}
