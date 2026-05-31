using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>Bring Maine OSRM online before a BUILD (office panel API or local dev Docker).</summary>
    internal static class OsrmBootstrap
    {
        private static readonly TimeSpan ProbeInterval = TimeSpan.FromSeconds(2);
        private const int DefaultWaitSeconds = 90;

        public static async Task EnsureForBuildAsync(
            HiatmeAiSettings aiSettings,
            IProgress<string> progress,
            CancellationToken token)
        {
            HiatmeGeoSettings.Invalidate();
            aiSettings = aiSettings ?? HiatmeAiSettings.Load();
            HiatmeAiSettings.InvalidateSessionCache();
            aiSettings = HiatmeAiSettings.Load();

            // Production default: all road miles via panel POST /api/hiatme/route (shared cache + OSRM on panel host).
            if (aiSettings.UseServerGeo)
            {
                progress?.Report("Connecting to office AI panel…");
                if (!await HiatmeGeoSettings.RefreshConnectivityAsync(aiSettings, token).ConfigureAwait(false))
                {
                    throw new InvalidOperationException(
                        "Office AI panel is not reachable at "
                        + (aiSettings.BaseUrl ?? "(no URL)") + ".\r\n\r\n"
                        + HiatmeGeoSettings.ServerRequiredMessage);
                }

                progress?.Report("Ensuring OSRM on office server…");
                bool ok = await HiatmeAiClient.EnsureOsrmAsync(aiSettings, token).ConfigureAwait(false);
                if (!ok)
                {
                    throw new InvalidOperationException(
                        "Office OSRM did not come online. On the server PC: Docker running, "
                        + "tools\\osrm\\scripts\\start-osrm.ps1, scripts\\restart-panel.ps1.");
                }
                progress?.Report("Office OSRM ready (via panel).");
                return;
            }

            // Dev only (UseServerGeo=false): direct local Docker on this PC.
            progress?.Report("Checking local OSRM…");
            if (await OsrmSettings.TryHealthCheckAsync(token).ConfigureAwait(false))
            {
                progress?.Report("Local OSRM ready.");
                return;
            }

            progress?.Report("Starting local OSRM (Docker)…");
            TryLaunchLocalStartScript();
            OsrmSettings.InvalidateHealthCache();

            var deadline = DateTime.UtcNow.AddSeconds(DefaultWaitSeconds);
            while (DateTime.UtcNow < deadline)
            {
                token.ThrowIfCancellationRequested();
                await Task.Delay(ProbeInterval, token).ConfigureAwait(false);
                if (await OsrmSettings.TryHealthCheckAsync(token).ConfigureAwait(false))
                {
                    progress?.Report("Local OSRM ready.");
                    return;
                }
            }

            throw new InvalidOperationException(
                "Local OSRM did not start within " + DefaultWaitSeconds + "s. "
                + OsrmSettings.LocalOfflineHint);
        }

        private static void TryLaunchLocalStartScript()
        {
            string script = ResolveStartScriptPath();
            if (string.IsNullOrEmpty(script) || !File.Exists(script))
                return;

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = "-ExecutionPolicy Bypass -NoProfile -File \"" + script + "\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(script) ?? ""
                };
                Process.Start(psi);
            }
            catch
            {
                // Docker may already be starting; polling handles the rest.
            }
        }

        private static string ResolveStartScriptPath()
        {
            string env = Environment.GetEnvironmentVariable("HIATME_OSRM_START_SCRIPT");
            if (!string.IsNullOrWhiteSpace(env) && File.Exists(env))
                return env;

            string[] roots =
            {
                @"F:\Projects\AIagent",
                @"C:\Users\megap\AIagent",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AIagent"),
            };

            foreach (var root in roots)
            {
                if (string.IsNullOrWhiteSpace(root)) continue;
                string candidate = Path.Combine(root, "tools", "osrm", "scripts", "start-osrm.ps1");
                if (File.Exists(candidate))
                    return candidate;
            }

            return null;
        }
    }
}
