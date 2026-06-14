using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    internal sealed class SuggestPreviewRowTag
    {
        public bool IsGroupBar { get; set; }
        public bool IsGap { get; set; }
        public string GroupBarText { get; set; } = "";
        public Color BarColor { get; set; }
        public Color BarFore { get; set; }
    }

    /// <summary>Populates a read-only ListView strip for the driver-suggest dialog.</summary>
    internal static class ScheduleBuilderSuggestPreviewBinder
    {
        private static readonly string[] Columns = { "Grp", "Trip #", "Client", "PU", "PU City", "DO", "DO City" };

        public static string BuildCaption(
            ScheduleBuilderDriverSuggestion suggestion,
            int focusGroupIdx,
            int groupCount)
        {
            if (suggestion == null)
                return "";

            string placement = suggestion.Kind == ScheduleBuilderSuggestPlacementKind.MergeIntoGroup
                ? "group " + suggestion.TargetGroupNumber + " (merged)"
                : suggestion.Kind == ScheduleBuilderSuggestPlacementKind.NewGroupAtStart
                    ? "new first group"
                    : suggestion.Kind == ScheduleBuilderSuggestPlacementKind.NewGroupAtEnd
                        ? "new last group"
                        : "new group " + suggestion.TargetGroupNumber;

            return suggestion.DriverDisplayName + " — " + placement
                + (suggestion.Kind == ScheduleBuilderSuggestPlacementKind.MergeIntoGroup
                    ? " · desk order"
                    : " · pickup-time order")
                + " · faded = earlier/later on route";
        }

        public static void ConfigureListView(ListView lv)
        {
            if (lv == null)
                return;

            lv.Columns.Clear();
            int[] widths = { 34, 72, 130, 52, 82, 52, 82 };
            for (int i = 0; i < Columns.Length; i++)
                lv.Columns.Add(Columns[i], widths[i]);
        }

        public static void Populate(
            ListView lv,
            MCDownloadedTrip insertedTrip,
            ScheduleBuilderDriverSuggestion suggestion,
            IReadOnlyDictionary<string, List<ScheduleBuilderPreviewLine>> linesByTab,
            bool showGroupColors,
            out string caption)
        {
            caption = "";
            if (lv == null)
                return;

            lv.BeginUpdate();
            lv.Items.Clear();

            if (insertedTrip == null || suggestion == null || linesByTab == null)
            {
                lv.EndUpdate();
                return;
            }

            var placed = ScheduleBuilderDriverSuggest.BuildPlacedTargetLines(insertedTrip, suggestion, linesByTab);
            if (placed.Count == 0)
            {
                lv.EndUpdate();
                return;
            }

            var groups = ScheduleBuilderPreviewGroups.BuildFromPreviewLines(placed);
            if (groups.Count == 0)
            {
                lv.EndUpdate();
                return;
            }

            int focusGroupIdx = FindGroupIndexForTrip(groups, insertedTrip);
            if (focusGroupIdx < 0)
                focusGroupIdx = FindGroupIndexByNumber(groups, suggestion.TargetGroupNumber);
            if (focusGroupIdx < 0)
            {
                lv.EndUpdate();
                return;
            }

            var chrono = BuildChronologicalGroupOrder(groups);
            int focusChrono = chrono.FindIndex(x => x.GroupIndex == focusGroupIdx);
            if (focusChrono < 0)
                focusChrono = 0;

            int firstChrono = Math.Max(0, focusChrono - 1);
            int lastChrono = Math.Min(chrono.Count - 1, focusChrono + 1);
            caption = BuildCaption(suggestion, focusGroupIdx, groups.Count);

            for (int ci = firstChrono; ci <= lastChrono; ci++)
            {
                if (ci > firstChrono)
                    AddGapRow(lv);

                var entry = chrono[ci];
                var g = groups[entry.GroupIndex];
                bool isContext = entry.GroupIndex != focusGroupIdx;
                string suffix = ci < focusChrono
                    ? " · earlier on route"
                    : ci > focusChrono ? " · later on route" : "";

                string note = FindGroupNoteOnLines(placed, g?.GroupNumber ?? 0);
                AddGroupRow(lv, g, note, suffix, isContext, showGroupColors);

                IEnumerable<MCDownloadedTrip> trips = suggestion.Kind == ScheduleBuilderSuggestPlacementKind.MergeIntoGroup
                    ? TripsInDeskOrder(placed, g)
                    : SortTripsByPickup(g);
                foreach (var trip in trips)
                {
                    if (trip == null)
                        continue;
                    bool inserted = TripEquals(trip, insertedTrip);
                    AddTripRow(lv, g, trip, inserted, isContext, showGroupColors);
                }
            }

            lv.EndUpdate();
        }

        private sealed class ChronoGroup
        {
            public int GroupIndex { get; set; }
            public TimeSpan SortKey { get; set; }
        }

        private static List<ChronoGroup> BuildChronologicalGroupOrder(IList<SupeyTripCluster> groups)
        {
            var list = new List<ChronoGroup>();
            for (int i = 0; i < groups.Count; i++)
            {
                var g = groups[i];
                TimeSpan key = g?.EarliestPickup ?? TimeSpan.MaxValue;
                if (key == TimeSpan.MaxValue && g?.Trips != null)
                {
                    foreach (var t in g.Trips)
                    {
                        var pu = SupeyTripTimes.TryParsePU(t);
                        if (pu.HasValue && pu.Value < key)
                            key = pu.Value;
                    }
                }
                if (key == TimeSpan.MaxValue)
                    key = TimeSpan.FromHours(12).Add(TimeSpan.FromMinutes(i));

                list.Add(new ChronoGroup { GroupIndex = i, SortKey = key });
            }

            return list
                .OrderBy(x => x.SortKey)
                .ThenBy(x => x.GroupIndex)
                .ToList();
        }

        private static IEnumerable<MCDownloadedTrip> SortTripsByPickup(SupeyTripCluster g)
        {
            if (g?.Trips == null)
                yield break;

            var sorted = g.Trips
                .Where(t => t != null)
                .OrderBy(t => SupeyTripTimes.TryParsePU(t) ?? TimeSpan.MaxValue)
                .ThenBy(t => (t.TripNumber ?? "").Trim(), StringComparer.OrdinalIgnoreCase);
            foreach (var t in sorted)
                yield return t;
        }

        /// <summary>Line-list order for merged groups (insert position), not PU sort.</summary>
        private static IEnumerable<MCDownloadedTrip> TripsInDeskOrder(
            IList<ScheduleBuilderPreviewLine> placed,
            SupeyTripCluster g)
        {
            if (placed == null || g?.Trips == null || g.Trips.Count == 0)
                yield break;

            var want = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in g.Trips)
            {
                if (t == null)
                    continue;
                string tn = (t.TripNumber ?? "").Trim();
                if (tn.Length > 0)
                    want.Add(tn);
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in placed)
            {
                if (line?.Kind != ScheduleBuilderPreviewLine.LineKind.Trip || line.Trip == null)
                    continue;
                string tn = (line.Trip.TripNumber ?? "").Trim();
                if (tn.Length == 0 || !want.Contains(tn) || !seen.Add(tn))
                    continue;
                yield return line.Trip;
            }
        }

        private static void AddGroupRow(
            ListView lv,
            SupeyTripCluster g,
            string note,
            string routeSuffix,
            bool isContext,
            bool showGroupColors)
        {
            string text = "Group " + (g?.GroupNumber ?? 0);
            string n = (note ?? "").Trim();
            if (n.Length > 0)
                text += " · " + n;
            if (!string.IsNullOrEmpty(routeSuffix))
                text += routeSuffix;

            Color bar = showGroupColors && g != null
                ? g.DisplayColor
                : Color.FromArgb(55, 55, 58);
            if (isContext)
                bar = Blend(bar, Color.FromArgb(28, 28, 28), 0.35f);

            var lvi = new ListViewItem("");
            lvi.UseItemStyleForSubItems = false;
            for (int i = 1; i < Columns.Length; i++)
                lvi.SubItems.Add("");
            lvi.Tag = new SuggestPreviewRowTag
            {
                IsGroupBar = true,
                GroupBarText = text,
                BarColor = bar,
                BarFore = ScheduleBuilderPreviewStyle.ContrastText(bar),
            };
            lv.Items.Add(lvi);
        }

        private static void AddGapRow(ListView lv)
        {
            var lvi = new ListViewItem("");
            lvi.UseItemStyleForSubItems = false;
            for (int i = 1; i < Columns.Length; i++)
                lvi.SubItems.Add("");
            lvi.Tag = new SuggestPreviewRowTag { IsGap = true };
            lv.Items.Add(lvi);
        }

        private static void AddTripRow(
            ListView lv,
            SupeyTripCluster g,
            MCDownloadedTrip trip,
            bool isInserted,
            bool isContext,
            bool showGroupColors)
        {
            var lvi = new ListViewItem(g != null ? g.GroupNumber.ToString() : "");
            lvi.UseItemStyleForSubItems = false;
            lvi.SubItems.Add(trip.TripNumber ?? "");
            lvi.SubItems.Add(trip.ClientFullName ?? "");
            lvi.SubItems.Add(FormatTime(trip.PUTime));
            lvi.SubItems.Add(trip.PUCity ?? "");
            lvi.SubItems.Add(FormatTime(trip.DOTime));
            lvi.SubItems.Add(trip.DOCITY ?? "");

            Color bg = SupeyTheme.ListBody;
            if (showGroupColors && g != null)
                bg = Blend(g.DisplayColor, SupeyTheme.ListBody, 0.35f);
            if (isInserted)
                bg = Color.FromArgb(52, 68, 42);
            else if (isContext)
                bg = Blend(bg, Color.FromArgb(24, 24, 24), 0.4f);

            Color fg = isInserted ? Color.FromArgb(220, 235, 200) : SupeyTheme.ListText;
            ApplyRowColor(lvi, bg, fg);
            if (isInserted)
                lvi.Font = new Font(lv.Font, FontStyle.Bold);
            lv.Items.Add(lvi);
        }

        private static void ApplyRowColor(ListViewItem lvi, Color bg, Color fg)
        {
            lvi.BackColor = bg;
            lvi.ForeColor = fg;
            for (int i = 0; i < lvi.SubItems.Count; i++)
            {
                lvi.SubItems[i].BackColor = bg;
                lvi.SubItems[i].ForeColor = fg;
            }
        }

        private static string FindGroupNoteOnLines(IList<ScheduleBuilderPreviewLine> lines, int groupNumber)
        {
            if (lines == null || groupNumber <= 0)
                return "";
            foreach (var line in lines)
            {
                if (line?.Kind == ScheduleBuilderPreviewLine.LineKind.GroupHeader
                    && line.GroupNumber == groupNumber)
                    return (line.GroupNoteText ?? "").Trim();
            }
            return "";
        }

        private static int FindGroupIndexForTrip(IList<SupeyTripCluster> groups, MCDownloadedTrip trip)
        {
            if (groups == null || trip == null)
                return -1;
            for (int i = 0; i < groups.Count; i++)
            {
                if (TripInGroup(trip, groups[i]))
                    return i;
            }
            return -1;
        }

        private static int FindGroupIndexByNumber(IList<SupeyTripCluster> groups, int groupNumber)
        {
            if (groups == null || groupNumber <= 0)
                return -1;
            for (int i = 0; i < groups.Count; i++)
            {
                if (groups[i] != null && groups[i].GroupNumber == groupNumber)
                    return i;
            }
            return -1;
        }

        private static bool TripInGroup(MCDownloadedTrip trip, SupeyTripCluster group)
        {
            if (trip == null || group?.Trips == null)
                return false;
            string tn = (trip.TripNumber ?? "").Trim();
            foreach (var t in group.Trips)
            {
                if (t == null)
                    continue;
                if (ReferenceEquals(t, trip))
                    return true;
                if (tn.Length > 0 && string.Equals((t.TripNumber ?? "").Trim(), tn, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static bool TripEquals(MCDownloadedTrip a, MCDownloadedTrip b)
        {
            if (a == null || b == null)
                return false;
            if (ReferenceEquals(a, b))
                return true;
            string ta = (a.TripNumber ?? "").Trim();
            string tb = (b.TripNumber ?? "").Trim();
            return ta.Length > 0 && string.Equals(ta, tb, StringComparison.OrdinalIgnoreCase);
        }

        private static string FormatTime(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return "";
            var t = SupeyTripTimes.TryParse(raw.Trim());
            return t.HasValue ? SupeyTripTimes.FormatTimeOfDay(t) : raw.Trim();
        }

        private static Color Blend(Color a, Color b, float amount)
        {
            amount = Math.Max(0f, Math.Min(1f, amount));
            int r = (int)(a.R + (b.R - a.R) * amount);
            int g = (int)(a.G + (b.G - a.G) * amount);
            int bl = (int)(a.B + (b.B - a.B) * amount);
            return Color.FromArgb(r, g, bl);
        }
    }
}
