namespace Hiatme_Tool_Suite_v3
{
    /// <summary>One row in the Schedule Builder preview list — trip or template gap spacer.</summary>
    internal sealed class ScheduleBuilderPreviewLine
    {
        public enum LineKind
        {
            Gap,
            Trip,
        }

        public LineKind Kind { get; set; }
        public string GapNoteText { get; set; }
        public MCDownloadedTrip Trip { get; set; }
    }
}
