using System.Drawing;

namespace Hiatme_Tool_Suite_v3
{
    internal static class FsPreviewLineRef
    {
        internal static int GetLineIndex(object tag)
        {
            switch (tag)
            {
                case FsPreviewTripTag tripTag:
                    return tripTag.PreviewLineIndex;
                case FsPreviewGapTag gapTag:
                    return gapTag.PreviewLineIndex;
                case FsPreviewNoteTag noteTag:
                    return noteTag.PreviewLineIndex;
                case FsPreviewSectionHeaderTag sectionTag:
                    return sectionTag.PreviewLineIndex;
                default:
                    return -1;
            }
        }
    }

    internal sealed class FsPreviewGapTag
    {
        /// <summary>Index in <see cref="ScheduleBuilderPreviewLine"/> list for this row.</summary>
        public int PreviewLineIndex { get; set; } = -1;
    }

    internal sealed class FsPreviewSectionHeaderTag
    {
        public string Title { get; }
        public Color SectionColor { get; }
        public int PreviewLineIndex { get; set; } = -1;

        public FsPreviewSectionHeaderTag(string title, Color sectionColor)
        {
            Title = title ?? "";
            SectionColor = sectionColor;
        }
    }

    internal sealed class FsPreviewNoteTag
    {
        public SupeyTripCluster Group { get; }
        public string NoteText { get; set; }
        /// <summary>GroupHeader line index, or first trip line when the bar is auto-injected.</summary>
        public int PreviewLineIndex { get; set; } = -1;

        public FsPreviewNoteTag(SupeyTripCluster g, string noteText = "")
        {
            Group = g;
            NoteText = noteText ?? "";
        }
    }

    internal sealed class FsPreviewTripTag
    {
        public SupeyTripCluster Group { get; }
        public MCDownloadedTrip Trip { get; }
        public int PreviewLineIndex { get; set; } = -1;
        public bool ReroutedOnModivcare { get; set; }

        public FsPreviewTripTag(SupeyTripCluster g, MCDownloadedTrip t)
        {
            Group = g;
            Trip = t;
        }
    }
}
