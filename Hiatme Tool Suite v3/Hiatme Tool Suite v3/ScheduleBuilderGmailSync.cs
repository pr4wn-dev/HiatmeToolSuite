using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Keeps office Gmail in hiatme_config/gmail_default.json aligned with the AI panel
    /// (same pattern as driver emails and out-of-area).
    /// </summary>
    internal static class ScheduleBuilderGmailSync
    {
        public sealed class SyncResult
        {
            public bool ServerUsed { get; set; }
            public bool ServerUnreachable { get; set; }
            public bool LocalSaved { get; set; }
            public bool ServerPushed { get; set; }
            public bool PulledFromServer { get; set; }
        }

        public static bool TrySaveLocal(string address, string appPassword)
        {
            address = (address ?? "").Trim();
            appPassword = appPassword ?? "";
            if (string.IsNullOrEmpty(address) || string.IsNullOrEmpty(appPassword))
                return false;

            try
            {
                string path = ScheduleBuilderGmailDefaults.ConfigPath;
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                var root = new JObject
                {
                    ["address"] = address,
                    ["appPassword"] = appPassword,
                };
                File.WriteAllText(path, root.ToString(Newtonsoft.Json.Formatting.Indented));
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static async Task<SyncResult> SyncWithServerAsync(
            HiatmeAiSettings settings,
            CancellationToken cancellationToken = default)
        {
            var result = new SyncResult();
            settings = settings ?? HiatmeAiSettings.Load();
            if (settings == null || string.IsNullOrWhiteSpace(settings.BaseUrl))
                return result;
            if (!HiatmeGeoSettings.UseServer)
                return result;

            result.ServerUsed = true;
            bool localConfigured = ScheduleBuilderGmailDefaults.TryGet(out string localAddress, out string localPass);

            try
            {
                var server = await HiatmeAiClient.GetGmailDefaultsAsync(settings, cancellationToken)
                    .ConfigureAwait(false);
                if (server == null)
                {
                    result.ServerUnreachable = true;
                    if (localConfigured)
                        result.ServerPushed = await PushToServerAsync(settings, localAddress, localPass, cancellationToken)
                            .ConfigureAwait(false);
                    return result;
                }

                bool serverConfigured = !string.IsNullOrWhiteSpace(server.Address)
                    && !string.IsNullOrWhiteSpace(server.AppPassword);

                if (serverConfigured && !localConfigured)
                {
                    result.LocalSaved = TrySaveLocal(server.Address, server.AppPassword);
                    result.PulledFromServer = result.LocalSaved;
                }
                else if (localConfigured)
                {
                    result.ServerPushed = await PushToServerAsync(
                        settings, localAddress, localPass, cancellationToken).ConfigureAwait(false);
                }
                else if (serverConfigured)
                {
                    result.LocalSaved = TrySaveLocal(server.Address, server.AppPassword);
                    result.PulledFromServer = result.LocalSaved;
                }
            }
            catch
            {
                result.ServerUnreachable = true;
                if (localConfigured)
                {
                    try
                    {
                        result.ServerPushed = await PushToServerAsync(
                            settings, localAddress, localPass, cancellationToken).ConfigureAwait(false);
                    }
                    catch
                    {
                        result.ServerPushed = false;
                    }
                }
            }

            return result;
        }

        public static async Task<bool> PushToServerAsync(
            HiatmeAiSettings settings,
            string address,
            string appPassword,
            CancellationToken cancellationToken = default)
        {
            address = (address ?? "").Trim();
            appPassword = appPassword ?? "";
            if (string.IsNullOrEmpty(address) || string.IsNullOrEmpty(appPassword))
                return false;

            settings = settings ?? HiatmeAiSettings.Load();
            if (settings == null || string.IsNullOrWhiteSpace(settings.BaseUrl))
                return false;
            if (!HiatmeGeoSettings.UseServer)
                return false;

            TrySaveLocal(address, appPassword);

            try
            {
                return await HiatmeAiClient.PutGmailDefaultsAsync(
                    settings, address, appPassword, Environment.UserName, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch
            {
                return false;
            }
        }

        public static void TryPushToServerFireAndForget(
            HiatmeAiSettings settings,
            string address,
            string appPassword)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await PushToServerAsync(settings, address, appPassword, CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch
                {
                    // best-effort
                }
            });
        }
    }
}
