using System;
using System.Text;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>Paste-friendly BUILD transcript for Cursor / support.</summary>
    internal static class SupeyBuildLogExport
    {
        public static string Build(
            DateTime serviceDate,
            SupeyBuildSessionLog sessionLog,
            HiatmeAiSettings settings,
            int tripsLoaded,
            string lastStatus,
            string stopReason,
            HiatmeBuildStats stats,
            string serverLogPathHint = null)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Supey BUILD log — paste into Cursor");
            sb.AppendLine("Exported: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("Service date: " + serviceDate.ToString("yyyy-MM-dd"));
            if (settings != null)
            {
                sb.AppendLine("Panel URL: " + (settings.BaseUrl ?? ""));
                sb.AppendLine("Client id: " + settings.ResolvedClientId());
                sb.AppendLine("UseServerSolve: " + settings.UseServerSolve);
                sb.AppendLine("UseServerGeo: " + HiatmeGeoSettings.UseServer);
            }
            sb.AppendLine("Trips loaded: " + tripsLoaded);
            if (!string.IsNullOrWhiteSpace(lastStatus))
                sb.AppendLine("Last status: " + lastStatus.Trim());
            if (!string.IsNullOrWhiteSpace(stopReason))
                sb.AppendLine("Stop reason: " + stopReason.Trim());
            if (stats != null)
            {
                sb.AppendLine("--- build_stats ---");
                sb.AppendLine("trips_total=" + stats.TripsTotal);
                sb.AppendLine("trips_assigned=" + stats.TripsAssigned);
                sb.AppendLine("cluster_count=" + stats.ClusterCount);
                sb.AppendLine("geocoded_new=" + stats.GeocodedNew);
                sb.AppendLine("no_geo_count=" + stats.NoGeoCount);
                if (stats.SolveElapsedMs > 0)
                    sb.AppendLine("solve_elapsed_ms=" + stats.SolveElapsedMs);
                if (stats.BuildElapsedMs > 0)
                    sb.AppendLine("build_elapsed_ms=" + stats.BuildElapsedMs);
                if (stats.TripsClustered > 0)
                    sb.AppendLine("trips_clustered=" + stats.TripsClustered);
                if (stats.TripsLockedSkippedCluster > 0)
                    sb.AppendLine("trips_locked_skipped_cluster=" + stats.TripsLockedSkippedCluster);
                if (stats.OsrmRouteHttp > 0 || stats.OsrmRouteCacheHits > 0 || stats.OsrmTableCalls > 0)
                {
                    sb.AppendLine("osrm_route_http=" + stats.OsrmRouteHttp);
                    sb.AppendLine("osrm_route_cache_hits=" + stats.OsrmRouteCacheHits);
                    sb.AppendLine("osrm_table_calls=" + stats.OsrmTableCalls);
                    sb.AppendLine("osrm_pair_ram_hits=" + stats.OsrmPairRamHits);
                }
            }
            if (!string.IsNullOrWhiteSpace(serverLogPathHint))
                sb.AppendLine("Server log file (office PC): " + serverLogPathHint);
            sb.AppendLine();
            sb.AppendLine("--- transcript ---");
            if (sessionLog != null)
                sb.AppendLine(sessionLog.ToText());
            return sb.ToString();
        }
    }
}
