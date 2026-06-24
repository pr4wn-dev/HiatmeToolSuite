using System;
using System.Threading;
using System.Threading.Tasks;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>BUILD requires office server OSRM (Maine graph on the AI panel host).</summary>
    internal static class ScheduleOsrmGate
    {
        private static readonly TimeSpan PreviewProbeCacheTtl = TimeSpan.FromSeconds(30);
        private static DateTime _previewProbedUtc = DateTime.MinValue;
        private static bool _previewRoutingOk;
        private static string _previewRoutingDetail = "";
        private static bool _previewGeoOk;
        private static string _previewGeoDetail = "";

        /// <summary>Last desk-preview routing probe (map + mileage HUD). Updated by <see cref="ProbePreviewServicesAsync"/>.</summary>
        public static bool PreviewRoutingOk => _previewRoutingOk;

        public static string PreviewRoutingDetail => _previewRoutingDetail ?? "";

        /// <summary>Office panel geocode (or local dev fallback). Updated by <see cref="ProbePreviewServicesAsync"/>.</summary>
        public static bool PreviewGeoOk => _previewGeoOk;

        public static string PreviewGeoDetail => _previewGeoDetail ?? "";

        public static void InvalidatePreviewRoutingProbe() => InvalidatePreviewServicesProbe();

        public static void InvalidatePreviewServicesProbe()
        {
            _previewProbedUtc = DateTime.MinValue;
        }

        /// <summary>
        /// Quick probe for Schedule Builder desk preview — no Docker bootstrap or per-group route attempts.
        /// </summary>
        public static async Task<(bool RoutingOk, string RoutingDetail)> ProbePreviewRoutingAsync(
            HiatmeAiSettings aiSettings,
            CancellationToken cancellationToken = default)
        {
            var probe = await ProbePreviewServicesAsync(aiSettings, cancellationToken).ConfigureAwait(false);
            return (probe.RoutingOk, probe.RoutingDetail);
        }

        public static async Task<(bool RoutingOk, string RoutingDetail, bool GeoOk, string GeoDetail)>
            ProbePreviewServicesAsync(
            HiatmeAiSettings aiSettings,
            CancellationToken cancellationToken = default)
        {
            if (DateTime.UtcNow - _previewProbedUtc < PreviewProbeCacheTtl)
                return (_previewRoutingOk, _previewRoutingDetail, _previewGeoOk, _previewGeoDetail);

            aiSettings = aiSettings ?? HiatmeAiSettings.Load();
            bool routingOk;
            string routingDetail;
            bool geoOk;
            string geoDetail;

            if (aiSettings.UseServerGeo)
            {
                await HiatmeGeoSettings.RefreshConnectivityAsync(aiSettings, cancellationToken).ConfigureAwait(false);
                aiSettings = HiatmeAiSettings.Load();

                if (HiatmeGeoSettings.ServerOnly && !HiatmeGeoSettings.UseServer)
                {
                    var offline = await TryDirectPreviewRoutingAsync(cancellationToken).ConfigureAwait(false);
                    if (offline.RoutingOk)
                    {
                        geoOk = offline.GeoOk;
                        geoDetail = offline.GeoDetail;
                        routingOk = true;
                        routingDetail = offline.RoutingDetail;
                    }
                    else
                    {
                        geoOk = false;
                        geoDetail = HiatmeGeoSettings.ServerRequiredMessage;
                        routingOk = false;
                        routingDetail = geoDetail;
                    }
                }
                else if (!HiatmeGeoSettings.UseServer)
                {
                    var offline = await TryDirectPreviewRoutingAsync(cancellationToken).ConfigureAwait(false);
                    if (offline.RoutingOk)
                    {
                        geoOk = offline.GeoOk;
                        geoDetail = offline.GeoDetail;
                        routingOk = true;
                        routingDetail = offline.RoutingDetail;
                    }
                    else
                    {
                        geoOk = false;
                        geoDetail = HiatmeGeoSettings.ServerRequiredMessage;
                        routingOk = false;
                        routingDetail = geoDetail;
                    }
                }
                else
                {
                    geoOk = true;
                    geoDetail = "Office AI panel OK (" + (aiSettings.BaseUrl ?? "") + ")";
                    var server = await HiatmeGeoClient.GetStatusAsync(aiSettings, cancellationToken)
                        .ConfigureAwait(false);
                    if (server != null && server.OsrmLocalOk)
                    {
                        routingOk = true;
                        routingDetail = "Server OSRM OK (" + (server.OsrmActiveEndpoint ?? "local") + ")";
                    }
                    else if (server != null)
                    {
                        try
                        {
                            await HiatmeAiClient.EnsureOsrmAsync(aiSettings, cancellationToken)
                                .ConfigureAwait(false);
                            server = await HiatmeGeoClient.GetStatusAsync(aiSettings, cancellationToken)
                                .ConfigureAwait(false);
                        }
                        catch { /* best effort */ }

                        if (server != null && server.OsrmLocalOk)
                        {
                            routingOk = true;
                            routingDetail = "Server OSRM OK (" + (server.OsrmActiveEndpoint ?? "local") + ")";
                        }
                        else
                        {
                            var fallback = await TryDirectPreviewRoutingAsync(cancellationToken).ConfigureAwait(false);
                            if (fallback.RoutingOk)
                            {
                                routingOk = true;
                                routingDetail = fallback.RoutingDetail;
                            }
                            else
                            {
                                routingOk = false;
                                routingDetail =
                                    "Panel reachable but Maine OSRM is not running on the server.\r\n\r\n"
                                    + "On the server PC: Docker Desktop on, then "
                                    + "tools\\osrm\\scripts\\start-osrm.ps1 and scripts\\restart-panel.ps1.";
                            }
                        }
                    }
                    else
                    {
                        var fallback = await TryDirectPreviewRoutingAsync(cancellationToken).ConfigureAwait(false);
                        if (fallback.RoutingOk)
                        {
                            geoOk = fallback.GeoOk;
                            geoDetail = fallback.GeoDetail;
                            routingOk = true;
                            routingDetail = fallback.RoutingDetail;
                        }
                        else
                        {
                            geoOk = false;
                            geoDetail = HiatmeGeoSettings.ServerRequiredMessage;
                            routingOk = false;
                            routingDetail = geoDetail;
                        }
                    }
                }
            }
            else
            {
                geoOk = true;
                geoDetail = "Local geocode allowed";
                if (await OsrmSettings.TryHealthCheckAsync(cancellationToken).ConfigureAwait(false))
                {
                    routingOk = true;
                    routingDetail = "Local OSRM OK";
                }
                else
                {
                    routingOk = false;
                    routingDetail = OsrmSettings.LocalOfflineHint;
                }
            }

            _previewGeoOk = geoOk;
            _previewGeoDetail = geoDetail ?? "";
            _previewRoutingOk = routingOk;
            _previewRoutingDetail = routingDetail ?? "";
            _previewProbedUtc = DateTime.UtcNow;
            return (routingOk, _previewRoutingDetail, geoOk, _previewGeoDetail);
        }

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

        private static readonly GeoPoint PreviewRouteProbeA = new GeoPoint(44.80, -68.77);
        private static readonly GeoPoint PreviewRouteProbeB = new GeoPoint(44.91, -68.65);

        private static async Task<(bool RoutingOk, string RoutingDetail, bool GeoOk, string GeoDetail)>
            TryDirectPreviewRoutingAsync(CancellationToken cancellationToken)
        {
            try
            {
                var route = await OsrmRouteResolver.RouteBestEffortAsync(
                    new[] { PreviewRouteProbeA, PreviewRouteProbeB }, cancellationToken).ConfigureAwait(false);
                if (route.Ok && !route.IsStraightLineFallback)
                {
                    return (true,
                        "Map routing OK (direct OSRM — office server not used for this preview).",
                        true,
                        "Geocode via cache / local lookup when server offline");
                }
            }
            catch { }

            return (false, "", false, "");
        }
    }
}
