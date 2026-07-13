namespace Hiatme_Tool_Suite_v3
{
    /// <summary>One row from a driver template CSV — real trip or intentional blank spacer.</summary>
    internal sealed class SupeyTemplateSlot
    {
        public enum SlotKind
        {
            Gap,
            Trip,
            GroupHeader,
        }

        public SlotKind Kind { get; set; }
        /// <summary>Route group number on exported group header rows (column N metadata).</summary>
        public int GroupNumber { get; set; }
        /// <summary>Dispatcher note on a gap/instruction row (e.g. "6:50 PICK UP", "PICK UP TOGETHER").</summary>
        public string NoteText { get; set; }
        /// <summary>When true, note text is centered across the row in preview and export.</summary>
        public bool NoteCenterText { get; set; }
        /// <summary>Optional font color for note text.</summary>
        public System.Drawing.Color? NoteTextColor { get; set; }
        /// <summary>Optional note-row fill color (not whole-group color).</summary>
        public System.Drawing.Color? NoteRowColor { get; set; }
        /// <summary>Template row from CSV (trip slots only).</summary>
        public MCDownloadedTrip TemplateTrip { get; set; }
        /// <summary>Live Modivcare trip when template row matched (trip slots only).</summary>
        public MCDownloadedTrip MatchedLiveTrip { get; set; }
        /// <summary>Trip was rerouted on Modivcare (column O metadata on saved rows).</summary>
        public bool ReroutedOnModivcare { get; set; }

        public bool IsMatched => MatchedLiveTrip != null;
    }
}
