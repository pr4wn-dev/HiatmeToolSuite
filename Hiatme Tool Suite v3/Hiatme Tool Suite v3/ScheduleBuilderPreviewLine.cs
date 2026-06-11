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
        public string SectionTitle { get; set; }
        /// <summary>User note on a route group header (merged cell in export).</summary>
        public string GroupNoteText { get; set; }
        public int GroupNumber { get; set; }
        public MCDownloadedTrip Trip { get; set; }
        /// <summary>Grp swatch on Reserves tab (reservers / reroute sections).</summary>
        public Color? ReserveBandColor { get; set; }
    }
}
