using System;
using System.Text;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>Toolbar + settings state at BUILD time (for warnings / AI review paste).</summary>
    internal sealed class SupeyBuildOptionsSnapshot
    {
        public bool WeekdayTemplates { get; set; }
        public bool FinishRemaining { get; set; }
        public bool ServerSolveRequested { get; set; }
        public bool ServerSolveAttempted { get; set; }
        public bool BuiltOnServer { get; set; }
        public bool AllowLocalSolveFallback { get; set; }
        public bool ServerGeo { get; set; }
        public string WeekdayFolder { get; set; } = "";
        public string AssignPath { get; set; } = "";
        public string BuildEngine { get; set; } = "";
        public int RosterDriversChecked { get; set; }
        public int TemplateRowsMatched { get; set; }
        public int TemplateRowsUnmatched { get; set; }
        public int OrphanTemplateTabs { get; set; }

        public static SupeyBuildOptionsSnapshot Capture(
            DateTime serviceDate,
            bool weekdayTemplates,
            bool finishRemaining,
            HiatmeAiSettings ai,
            bool serverSolveAttempted,
            bool builtOnServer,
            string buildEngine,
            SupeyTemplateBuildMeta templateMeta,
            int rosterDriversChecked)
        {
            var snap = new SupeyBuildOptionsSnapshot
            {
                WeekdayTemplates = weekdayTemplates,
                FinishRemaining = finishRemaining,
                ServerSolveRequested = ai?.UseServerSolve ?? false,
                ServerSolveAttempted = serverSolveAttempted,
                BuiltOnServer = builtOnServer,
                AllowLocalSolveFallback = ai?.AllowLocalSolveFallback ?? false,
                ServerGeo = ai?.UseServerGeo ?? true,
                WeekdayFolder = serviceDate.DayOfWeek.ToString(),
                BuildEngine = buildEngine ?? "",
                RosterDriversChecked = rosterDriversChecked,
            };

            if (templateMeta != null)
            {
                snap.TemplateRowsMatched = templateMeta.TemplateMatched;
                snap.TemplateRowsUnmatched = templateMeta.TemplateUnmatchedRows;
                snap.OrphanTemplateTabs = templateMeta.OrphanTemplateDriverTabs;
                snap.AssignPath = DescribeAssignPath(templateMeta, serverSolveAttempted, builtOnServer, buildEngine);
            }
            else if (!weekdayTemplates)
            {
                snap.AssignPath = DescribeAssignPathNoTemplates(
                    snap.ServerSolveRequested, serverSolveAttempted, builtOnServer, buildEngine);
            }
            else
            {
                snap.AssignPath = "Weekday templates ON (no template folder or match pass)";
            }

            return snap;
        }

        public void AppendTo(StringBuilder sb)
        {
            if (sb == null) return;
            sb.AppendLine("## BUILD options (enabled for this build)");
            sb.AppendLine("- Weekday templates: " + OnOff(WeekdayTemplates));
            if (WeekdayTemplates)
            {
                sb.AppendLine("- Finish remaining: " + OnOff(FinishRemaining));
                sb.AppendLine("- Template folder: " + (string.IsNullOrEmpty(WeekdayFolder) ? "—" : WeekdayFolder));
                if (TemplateRowsMatched > 0 || TemplateRowsUnmatched > 0 || OrphanTemplateTabs > 0)
                {
                    sb.AppendLine("- Template match: " + TemplateRowsMatched + " row(s) locked"
                        + (TemplateRowsUnmatched > 0 ? " · " + TemplateRowsUnmatched + " CSV row(s) had no live trip" : "")
                        + (OrphanTemplateTabs > 0 ? " · " + OrphanTemplateTabs + " tab(s) not on roster" : ""));
                }
            }
            else
            {
                sb.AppendLine("- Finish remaining: (n/a — templates off)");
            }

            sb.AppendLine("- Assign path: " + (string.IsNullOrWhiteSpace(AssignPath) ? "—" : AssignPath));
            sb.AppendLine("- Server solve (setting): " + OnOff(ServerSolveRequested)
                + " · attempted: " + YesNo(ServerSolveAttempted)
                + " · used: " + YesNo(BuiltOnServer));
            sb.AppendLine("- Server geo / OSRM: " + OnOff(ServerGeo));
            sb.AppendLine("- Allow local solve if server fails: " + OnOff(AllowLocalSolveFallback));
            sb.AppendLine("- Roster drivers checked: " + RosterDriversChecked);
            if (!string.IsNullOrWhiteSpace(BuildEngine))
                sb.AppendLine("- Engine tag: " + BuildEngine.Trim());
            sb.AppendLine();
        }

        private static string DescribeAssignPath(
            SupeyTemplateBuildMeta meta,
            bool serverSolveAttempted,
            bool builtOnServer,
            string buildEngine)
        {
            switch (meta?.Mode ?? SupeyTemplateBuildMode.SupeyOnly)
            {
                case SupeyTemplateBuildMode.TemplateSeedOnly:
                    return "Template locks only — Supey/server assign did not run on leftovers";
                case SupeyTemplateBuildMode.TemplateThenSupey:
                    if (builtOnServer)
                        return "Template locks, then server solve on remainder";
                    if (serverSolveAttempted)
                        return "Template locks, then local C# (server solve failed or skipped)";
                    return "Template locks, then local C# on remainder";
                default:
                    return DescribeAssignPathNoTemplates(true, serverSolveAttempted, builtOnServer, buildEngine);
            }
        }

        private static string DescribeAssignPathNoTemplates(
            bool serverSolveRequested,
            bool serverSolveAttempted,
            bool builtOnServer,
            string buildEngine)
        {
            if (builtOnServer)
                return "Server solve (no template pass)";
            if (serverSolveAttempted)
                return "Local C# — server solve was attempted but did not apply schedule";
            if (serverSolveRequested)
                return "Local C# — server solve requested but not used";
            return "Local C# only (server solve off or unavailable)";
        }

        private static string OnOff(bool v) => v ? "ON" : "OFF";
        private static string YesNo(bool v) => v ? "yes" : "no";
    }
}
