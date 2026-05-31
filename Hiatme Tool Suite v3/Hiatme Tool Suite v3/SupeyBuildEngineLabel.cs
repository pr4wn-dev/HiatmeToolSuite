using System;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>Human-readable BUILD solver line for warnings log and clipboard exports.</summary>
    internal static class SupeyBuildEngineLabel
    {
        public const string WarningPrefix = "BUILD solver:";

        /// <summary>e.g. "BUILD solver: Server (greedy)" or "BUILD solver: Local C# (desktop)".</summary>
        public static string Describe(string buildEngine)
        {
            if (string.IsNullOrWhiteSpace(buildEngine))
                return WarningPrefix + " (unknown — run BUILD again, then copy warnings)";

            string e = buildEngine.Trim();
            if (e.StartsWith("server", StringComparison.OrdinalIgnoreCase))
            {
                string detail = e.Length > 6 ? e.Substring(6).Trim() : "";
                if (string.IsNullOrEmpty(detail)) detail = "solve";
                return WarningPrefix + " Server (" + detail + ")";
            }

            if (e.IndexOf("fallback", StringComparison.OrdinalIgnoreCase) >= 0
                || e.IndexOf("server failed", StringComparison.OrdinalIgnoreCase) >= 0)
                return WarningPrefix + " Local C# (server failed — used desktop builder)";

            if (e.IndexOf("local", StringComparison.OrdinalIgnoreCase) >= 0)
                return WarningPrefix + " Local C# (desktop)";

            return WarningPrefix + " " + e;
        }

        /// <summary>First Build warning row(s) — Warnings list and review paste always show solver.</summary>
        public static void SyncBuildWarning(
            SupeyScheduleResult result,
            string buildEngine,
            string serverSolveError = null)
        {
            if (result?.BuildWarnings == null) return;
            for (int i = result.BuildWarnings.Count - 1; i >= 0; i--)
            {
                var w = result.BuildWarnings[i];
                if (w?.Kind != SupeyWarningKind.BuildDiagnostic) continue;
                var d = w.Detail ?? "";
                if (d.StartsWith(WarningPrefix, StringComparison.OrdinalIgnoreCase)
                    || d.StartsWith("Server solve failed:", StringComparison.OrdinalIgnoreCase))
                {
                    result.BuildWarnings.RemoveAt(i);
                }
            }
            int insertAt = 0;
            result.BuildWarnings.Insert(insertAt++, new SupeyWarning(
                SupeyWarningKind.BuildDiagnostic, "", "Build", Describe(buildEngine)));
            if (!string.IsNullOrWhiteSpace(serverSolveError))
            {
                result.BuildWarnings.Insert(insertAt, new SupeyWarning(
                    SupeyWarningKind.BuildDiagnostic,
                    "",
                    "Build",
                    "Server solve failed: " + serverSolveError.Trim()));
            }
        }
    }
}
