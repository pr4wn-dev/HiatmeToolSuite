namespace Hiatme_Tool_Suite_v3
{
    internal sealed class FsPreviewGapTag { }

    internal sealed class FsPreviewSectionHeaderTag
    {
        public string Title { get; }
        public FsPreviewSectionHeaderTag(string title) => Title = title ?? "";
    }

    internal sealed class FsPreviewNoteTag
    {
        public SupeyTripCluster Group { get; }
        public FsPreviewNoteTag(SupeyTripCluster g) => Group = g;
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
