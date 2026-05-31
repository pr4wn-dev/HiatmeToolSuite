using System;

namespace Hiatme_Tool_Suite_v3
{
    internal enum SupeyTemplateBuildMode
    {
        SupeyOnly,
        TemplateSeedOnly,
        TemplateThenSupey,
    }

    internal sealed class SupeyTemplateBuildMeta
    {
        public SupeyTemplateBuildMode Mode { get; set; } = SupeyTemplateBuildMode.SupeyOnly;
        public string Weekday { get; set; } = "";
        public bool FinishRemainingWasOn { get; set; }
        public int TemplateMatched { get; set; }
        public int TemplateUnmatchedRows { get; set; }
        public int OrphanTemplateDriverTabs { get; set; }
        public int TripsLockedByTemplate { get; set; }
        public int TripsAssignedBySolver { get; set; }
        public int TripsToReserversAfterTemplate { get; set; }
        public int WillCallCount { get; set; }
        public int ReserverCount { get; set; }
        public int RerouteCount { get; set; }

        public string FormatTemplatePassLine()
        {
            if (Mode == SupeyTemplateBuildMode.SupeyOnly)
                return "No weekday templates used for this build.";
            return TemplateMatched + " matched to roster"
                + (TemplateUnmatchedRows > 0 ? " · " + TemplateUnmatchedRows + " CSV row(s) had no live trip" : "")
                + (OrphanTemplateDriverTabs > 0 ? " · " + OrphanTemplateDriverTabs + " template tab(s) not on roster" : "");
        }

        public string FormatModeTag()
        {
            switch (Mode)
            {
                case SupeyTemplateBuildMode.TemplateSeedOnly:
                    return "Templates only";
                case SupeyTemplateBuildMode.TemplateThenSupey:
                    return "Templates+Supey";
                default:
                    return "Supey only";
            }
        }
    }
}
