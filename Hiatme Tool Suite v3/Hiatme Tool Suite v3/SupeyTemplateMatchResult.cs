using System;
using System.Collections.Generic;

namespace Hiatme_Tool_Suite_v3
{
    internal sealed class SupeyTemplateMatchResult
    {
        public bool HadTemplates { get; set; }
        public string Weekday { get; set; } = "";
        public Dictionary<string, string> Locks { get; } =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Template file order per roster driver (gaps + trips).</summary>
        public Dictionary<string, List<SupeyTemplateSlot>> OrderedSlotsByRosterDriver { get; } =
            new Dictionary<string, List<SupeyTemplateSlot>>(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> MatchedLiveTripNumbers { get; } =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public int TemplateRowCount { get; set; }
        public int MatchedCount { get; set; }
        public int UnmatchedTemplateRowCount { get; set; }
        public List<string> OrphanTemplateDriverTabs { get; } = new List<string>();
        public List<SupeyWarning> Warnings { get; } = new List<SupeyWarning>();
    }
}
