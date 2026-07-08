using System.Drawing;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>One row in the Schedule Builder preview list — trip, gap, or reserves section header.</summary>
    internal sealed class ScheduleBuilderPreviewLine
    {
        public enum LineKind
        {
            Gap,
            Trip,
            SectionHeader,
            GroupHeader,
        }

        public LineKind Kind { get; set; }
        public string GapNoteText { get; set; }
        /// <summary>Optional color bar for a user-placed note gap row.</summary>
        public Color? GapNoteRowColor { get; set; }
        /// <summary>Center gap note text across the merged row.</summary>
        public bool GapNoteCenterText { get; set; }
        public string SectionTitle { get; set; }
        /// <summary>User note on a route group header (merged cell in export).</summary>
        public string GroupNoteText { get; set; }
        public int GroupNumber { get; set; }
        /// <summary>Custom route group color; null uses the default palette for this group number.</summary>
        public Color? GroupColorOverride { get; set; }
        /// <summary>Color for the note header row only — does not tint trips in the group.</summary>
        public Color? GroupNoteRowColor { get; set; }
        /// <summary>Center group note text across the merged row.</summary>
        public bool GroupNoteCenterText { get; set; }
        public MCDownloadedTrip Trip { get; set; }
        /// <summary>Trip was submitted for reroute on Modivcare; shown with a red row in the list.</summary>
        public bool ReroutedOnModivcare { get; set; }
        /// <summary>WellRyde trip list shows Cancelled/Suspended for this trip.</summary>
        public bool CancelledOnWellRyde { get; set; }
        /// <summary>Grp swatch on Reserves tab (reservers / reroute sections).</summary>
        public Color? ReserveBandColor { get; set; }
        /// <summary>Blank spacer row pinned to the bottom of a driver tab (not exported).</summary>
        public bool TrailingPad { get; set; }
    }
}

