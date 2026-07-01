namespace Hiatme_Tool_Suite_v3
{
    internal enum TripScoutListRowKind
    {
        Trip,
        ChangeDetail,
        WillCallDetail,
    }

    /// <summary>Tag for <see cref="System.Windows.Forms.ListViewItem"/> rows in Trip Scout.</summary>
    internal sealed class TripScoutListRow
    {
        public TripScoutListRowKind Kind { get; private set; }
        public WRDownloadedTrip Trip { get; private set; }
        public HiatmeAiClient.TripScoutChangeRow Change { get; private set; }
        public HiatmeAiClient.WellRydeBellWillCall WillCall { get; private set; }
        public string TripNo { get; private set; }
        public bool HasChanges { get; private set; }
        public bool IsExpanded { get; private set; }

        public static TripScoutListRow ForTrip(
            WRDownloadedTrip trip,
            bool hasChanges,
            bool isExpanded)
        {
            return new TripScoutListRow
            {
                Kind = TripScoutListRowKind.Trip,
                Trip = trip,
                TripNo = trip?.TripNumber?.Trim() ?? "",
                HasChanges = hasChanges,
                IsExpanded = isExpanded,
            };
        }

        public static TripScoutListRow ForChange(
            HiatmeAiClient.TripScoutChangeRow change,
            string tripNo)
        {
            return new TripScoutListRow
            {
                Kind = TripScoutListRowKind.ChangeDetail,
                Change = change,
                TripNo = tripNo ?? "",
            };
        }

        public static TripScoutListRow ForWillCall(
            HiatmeAiClient.WellRydeBellWillCall willCall,
            string tripNo)
        {
            return new TripScoutListRow
            {
                Kind = TripScoutListRowKind.WillCallDetail,
                WillCall = willCall,
                TripNo = tripNo ?? "",
            };
        }

        public static WRDownloadedTrip TryGetTrip(object tag)
        {
            if (tag is TripScoutListRow row && row.Kind == TripScoutListRowKind.Trip)
                return row.Trip;
            return tag as WRDownloadedTrip;
        }
    }
}
