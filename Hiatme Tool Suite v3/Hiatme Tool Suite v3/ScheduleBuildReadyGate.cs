using System;
using System.Threading;
using System.Threading.Tasks;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Pre-BUILD gate when server solve is required: panel reachability, OSRM, solve smoke.
    /// </summary>
    internal static class ScheduleBuildReadyGate
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

            if (aiSettings != null && !aiSettings.UseServerSolve)
                return await ScheduleOsrmGate.CheckAsync(aiSettings, cancellationToken)
                    .ConfigureAwait(false);

            string panelUrl = HiatmeGeoSettings.ActivePanelUrl ?? aiSettings.BaseUrl ?? "(not set)";
            if (string.IsNullOrWhiteSpace(aiSettings?.BaseUrl))
            {
                return (false,
                    "Office AI panel URL is not configured.\r\n\r\n"
                    + "Set HiatmeAiBaseUrl in App.config or hiatme_ai.json.");
            }

            if (!await HiatmeGeoSettings.RefreshConnectivityAsync(aiSettings, cancellationToken)
                .ConfigureAwait(false))
            {
                return (false,
                    "Office AI panel is not reachable at " + panelUrl + ".\r\n\r\n"
                    + "On the server PC: scripts\\restart-panel.ps1 (AIagent repo).\r\n"
                    + "Desks: confirm HiatmeAiBaseUrl points at the office LAN IP and port.");
            }

            await HiatmeAiClient.EnsureOsrmAsync(aiSettings, cancellationToken).ConfigureAwait(false);

            var ready = await HiatmeAiClient.BuildReadyAsync(aiSettings, cancellationToken)
                .ConfigureAwait(false);
            if (ready != null && ready.Ok)
            {
                string ep = string.IsNullOrWhiteSpace(ready.OsrmActiveEndpoint)
                    ? "local"
                    : ready.OsrmActiveEndpoint;
                string solver = string.IsNullOrWhiteSpace(ready.Solver) ? "greedy" : ready.Solver;
                return (true, "Server ready — OSRM OK (" + ep + "), solve smoke OK (" + solver + ").");
            }

            if (ready == null)
            {
                var (osrmOk, osrmDetail) = await ScheduleOsrmGate.CheckAsync(aiSettings, cancellationToken)
                    .ConfigureAwait(false);
                if (!osrmOk)
                    return (false, osrmDetail);
                return (false,
                    "Panel responded but GET /api/hiatme/ready failed.\r\n\r\n"
                    + "Restart the office panel (scripts\\restart-panel.ps1) after pulling AIagent.\r\n"
                    + "Panel: " + panelUrl);
            }

            string issues = ready.Issues != null && ready.Issues.Count > 0
                ? string.Join("\r\n• ", ready.Issues)
                : "Office server is not ready for BUILD.";
            return (false,
                "Office server is not ready for BUILD:\r\n\r\n• " + issues + "\r\n\r\n"
                + "Panel: " + panelUrl + "\r\n"
                + "On the server PC: Docker + tools\\osrm\\scripts\\start-osrm.ps1, then restart-panel.ps1.");
        }
    }
}
