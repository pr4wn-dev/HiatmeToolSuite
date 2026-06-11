using System.Drawing;

namespace Hiatme_Tool_Suite_v3
{
    internal sealed class FsPreviewGapTag { }

    internal sealed class FsPreviewSectionHeaderTag
    {
        public string Title { get; }
        public Color SectionColor { get; }

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

        public FsPreviewTripTag(SupeyTripCluster g, MCDownloadedTrip t)
        {
            Group = g;
            Trip = t;
        }
    }
}
