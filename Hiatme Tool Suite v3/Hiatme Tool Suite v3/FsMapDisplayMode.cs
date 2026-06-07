namespace Hiatme_Tool_Suite_v3
{
    /// <summary>How the Schedule Builder map filters trips for the active driver tab.</summary>
    internal enum FsMapDisplayMode
    {
        /// <summary>Every trip/group on the current driver tab.</summary>
        AllDriverTrips = 0,

        /// <summary>Only the group that contains the primary list selection.</summary>
        SelectedGroup = 1,

        /// <summary>Only the highlighted trip rows (supports multi-select).</summary>
        SelectedTrips = 2,
    }
}
