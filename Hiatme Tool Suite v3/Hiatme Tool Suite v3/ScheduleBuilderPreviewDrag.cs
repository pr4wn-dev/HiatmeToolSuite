using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>Parses and mutates Schedule Builder preview lines for trip drag-and-drop.</summary>
    internal static class ScheduleBuilderPreviewDrag
    {
        internal static List<ScheduleBuilderPreviewLine> ParseLinesFromListView(ListView lv)
        {
            var lines = new List<ScheduleBuilderPreviewLine>();
            if (lv == null) return lines;

            foreach (ListViewItem item in lv.Items)
            {
                if (item.Tag is FsPreviewGapTag gapTag)
                {
                    lines.Add(new ScheduleBuilderPreviewLine
                    {
                        Kind = ScheduleBuilderPreviewLine.LineKind.Gap,
                        GapNoteText = gapTag.NoteText ?? "",
                        GapNoteRowColor = gapTag.NoteRowColor,
                        TrailingPad = gapTag.TrailingPad,
                    });
                    continue;
                }

                if (item.Tag is FsPreviewNoteTag noteTag)
                {
                    lines.Add(new ScheduleBuilderPreviewLine
                    {
                        Kind = ScheduleBuilderPreviewLine.LineKind.GroupHeader,
                        GroupNumber = noteTag.Group?.GroupNumber ?? 0,
                        GroupNoteText = noteTag.NoteText ?? "",
                        GroupNoteRowColor = noteTag.NoteRowColor,
                    });
                    continue;
                }

                if (item.Tag is FsPreviewSectionHeaderTag section)
                {
                    lines.Add(new ScheduleBuilderPreviewLine
                    {
                        Kind = ScheduleBuilderPreviewLine.LineKind.SectionHeader,
                        SectionTitle = section.Title,
                        ReserveBandColor = section.SectionColor,
                    });
                    continue;
                }

                if (item.Tag is FsPreviewTripTag row && row.Trip != null)
                {
                    lines.Add(new ScheduleBuilderPreviewLine
                    {
                        Kind = ScheduleBuilderPreviewLine.LineKind.Trip,
                        Trip = row.Trip,
                    });
                }
            }

            return lines;
        }

        internal static int CountPreviewLines(ListView lv)
        {
            int n = 0;
            if (lv == null) return 0;
            foreach (ListViewItem item in lv.Items)
            {
                if (item.Tag is FsPreviewNoteTag)
                    continue;
                if (item.Tag is FsPreviewGapTag
                    || item.Tag is FsPreviewTripTag
                    || item.Tag is FsPreviewSectionHeaderTag)
                    n++;
            }
            return n;
        }

        /// <summary>Backward-compatible name — counts gap, trip, and section rows (not group notes).</summary>
        internal static int CountTripAndGapLines(ListView lv) => CountPreviewLines(lv);

        internal static int ListViewIndexToLineIndex(
            ListView lv,
            int itemIndex,
            out FsPreviewTripTag tripTag,
            out bool isGap)
        {
            tripTag = null;
            isGap = false;
            if (lv == null) return -1;
            int line = 0;
            for (int i = 0; i < lv.Items.Count; i++)
            {
                var item = lv.Items[i];
                if (item.Tag is FsPreviewNoteTag)
                    continue;
                if (i == itemIndex)
                {
                    if (item.Tag is FsPreviewSectionHeaderTag
                        || item.Tag is FsPreviewNoteTag)
                        return -1;
                    isGap = item.Tag is FsPreviewGapTag;
                    tripTag = item.Tag as FsPreviewTripTag;
                    return line;
                }
                if (item.Tag is FsPreviewGapTag
                    || item.Tag is FsPreviewTripTag
                    || item.Tag is FsPreviewSectionHeaderTag)
                    line++;
            }
            return line;
        }

        internal static bool TryGetDropTarget(
            ListView lv,
            Point clientPt,
            FsPreviewTripTag dragging,
            out int insertBeforeLine,
            out bool mergeOntoTarget,
            out FsPreviewTripTag targetTrip)
        {
            return TryGetDropTarget(
                lv,
                clientPt,
                dragging,
                dragActive: false,
                dragSourceItemIndex: -1,
                dragRowHeight: 0,
                dragInsertItemIndex: -1,
                dragMerge: false,
                out insertBeforeLine,
                out mergeOntoTarget,
                out targetTrip);
        }

        internal static bool TryGetDropTarget(
            ListView lv,
            Point clientPt,
            FsPreviewTripTag dragging,
            bool dragActive,
            int dragSourceItemIndex,
            int dragRowHeight,
            int dragInsertItemIndex,
            bool dragMerge,
            out int insertBeforeLine,
            out bool mergeOntoTarget,
            out FsPreviewTripTag targetTrip)
        {
            insertBeforeLine = 0;
            mergeOntoTarget = false;
            targetTrip = null;
            if (lv == null) return false;

            var hit = lv.HitTest(clientPt);
            if (hit.Item == null)
            {
                insertBeforeLine = CountPreviewLines(lv);
                return true;
            }

            if (hit.Item.Tag is FsPreviewNoteTag)
            {
                var noteBounds = hit.Item.GetBounds(ItemBoundsPortion.Entire);
                int noteH = Math.Max(noteBounds.Height, 1);
                int noteRelY = clientPt.Y - noteBounds.Top;
                insertBeforeLine = ListViewIndexToLineIndex(lv, hit.Item.Index + 1, out targetTrip, out _);
                if (insertBeforeLine < 0)
                    insertBeforeLine = CountPreviewLines(lv);
                if (noteRelY < noteH / 2)
                {
                    mergeOntoTarget = false;
                    targetTrip = null;
                    return true;
                }

                mergeOntoTarget = true;
                return true;
            }

            if (hit.Item.Tag is FsPreviewSectionHeaderTag)
                return false;

            int lineIdx = ListViewIndexToLineIndex(lv, hit.Item.Index, out targetTrip, out bool isGap);
            if (lineIdx < 0) return false;

            if (isGap)
            {
                insertBeforeLine = lineIdx;
                return true;
            }

            if (targetTrip == null) return false;
            if (dragging != null && ReferenceEquals(dragging.Trip, targetTrip.Trip))
                return false;

            var bounds = hit.Item.GetBounds(ItemBoundsPortion.Entire);
            int bump = dragActive && !dragMerge
                ? GetVisualBumpPixels(lv, hit.Item.Index, dragSourceItemIndex, dragInsertItemIndex, dragRowHeight)
                : 0;
            int visualTop = bounds.Top + bump;
            int h = Math.Max(bounds.Height, 1);
            int relY = clientPt.Y - visualTop;

            if (relY < 0)
            {
                insertBeforeLine = lineIdx;
                mergeOntoTarget = false;
                return true;
            }

            if (relY >= h)
            {
                insertBeforeLine = lineIdx + 1;
                mergeOntoTarget = false;
                return true;
            }

            int insertEdge = Math.Max(4, (h * 45) / 100);
            int mergeEdge = h - insertEdge;
            if (relY < insertEdge)
            {
                insertBeforeLine = lineIdx;
                mergeOntoTarget = false;
                return true;
            }

            if (relY >= mergeEdge)
            {
                insertBeforeLine = lineIdx + 1;
                mergeOntoTarget = false;
                return true;
            }

            mergeOntoTarget = true;
            insertBeforeLine = lineIdx;
            return true;
        }

        internal static int GetVisualBumpPixels(
            ListView lv,
            int itemIndex,
            int dragSourceItemIndex,
            int dragInsertItemIndex,
            int dragRowHeight)
        {
            if (lv == null || dragInsertItemIndex < 0 || dragRowHeight <= 0)
                return 0;
            if (itemIndex < 0 || itemIndex == dragSourceItemIndex)
                return 0;

            int bumpFrom = dragInsertItemIndex;
            if (bumpFrom < lv.Items.Count && lv.Items[bumpFrom].Tag is FsPreviewNoteTag)
            {
                bumpFrom++;
                while (bumpFrom < lv.Items.Count && lv.Items[bumpFrom].Tag is FsPreviewNoteTag)
                    bumpFrom++;
            }

            if (itemIndex >= bumpFrom)
                return dragRowHeight;
            return 0;
        }

        internal static bool TryRemoveTrip(IList<ScheduleBuilderPreviewLine> lines, MCDownloadedTrip trip)
        {
            if (lines == null || trip == null) return false;
            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i]?.Kind == ScheduleBuilderPreviewLine.LineKind.Trip
                    && ReferenceEquals(lines[i].Trip, trip))
                {
                    lines.RemoveAt(i);
                    return true;
                }
            }

            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i]?.Kind == ScheduleBuilderPreviewLine.LineKind.Trip
                    && TripEquals(lines[i].Trip, trip))
                {
                    lines.RemoveAt(i);
                    return true;
                }
            }
            return false;
        }

        /// <summary>Insert a trip line (e.g. after Cut) without removing it from the list first.</summary>
        internal static void InsertTripLine(
            IList<ScheduleBuilderPreviewLine> lines,
            MCDownloadedTrip trip,
            int insertBeforeLineIndex,
            Color? reserveBandColor = null,
            bool reroutedOnModivcare = false)
        {
            if (lines == null || trip == null) return;
            if (FindTripLine(lines, trip) >= 0) return;

            int insert = Math.Max(0, Math.Min(insertBeforeLineIndex, lines.Count));
            var line = new ScheduleBuilderPreviewLine
            {
                Kind = ScheduleBuilderPreviewLine.LineKind.Trip,
                Trip = trip,
                ReroutedOnModivcare = reroutedOnModivcare,
            };
            if (reserveBandColor.HasValue)
                line.ReserveBandColor = reserveBandColor.Value;
            lines.Insert(insert, line);
        }

        /// <summary>Insert a blank gap/spacer row (Excel-style insert row).</summary>
        internal static void InsertGapLine(IList<ScheduleBuilderPreviewLine> lines, int insertBeforeLineIndex)
        {
            if (lines == null) return;

            int insert = Math.Max(0, Math.Min(insertBeforeLineIndex, lines.Count));
            lines.Insert(insert, new ScheduleBuilderPreviewLine
            {
                Kind = ScheduleBuilderPreviewLine.LineKind.Gap,
                GapNoteText = "",
            });
        }

        /// <summary>
        /// Map a context-menu ListView row to an index in the preview <paramref name="lines"/> list.
        /// ListView trip/gap counts omit group headers and hidden gaps — use tag line indices instead.
        /// </summary>
        internal static bool TryResolveInsertLineIndex(
            IList<ScheduleBuilderPreviewLine> lines,
            ListViewItem item,
            bool below,
            out int insertIndex)
        {
            insertIndex = 0;
            if (lines == null)
                return false;

            if (item == null)
            {
                insertIndex = lines.Count;
                return true;
            }

            if (item.Tag is FsPreviewNoteTag noteTag)
            {
                if (noteTag.PreviewLineIndex < 0)
                    return false;
                if (below)
                {
                    insertIndex = FindInsertIndexBelowNoteBar(lines, noteTag.PreviewLineIndex);
                    return true;
                }

                insertIndex = noteTag.PreviewLineIndex;
                return true;
            }

            int lineIdx = FsPreviewLineRef.GetLineIndex(item.Tag);
            if (lineIdx < 0)
                return false;

            // Gap row: above = before this spacer, below = after it (Excel insert-row on a blank line).
            if (item.Tag is FsPreviewGapTag)
            {
                insertIndex = below ? lineIdx + 1 : lineIdx;
                insertIndex = Math.Max(0, Math.Min(insertIndex, lines.Count));
                return true;
            }

            insertIndex = below ? lineIdx + 1 : lineIdx;
            insertIndex = Math.Max(0, Math.Min(insertIndex, lines.Count));
            return true;
        }

        private static int FindInsertIndexBelowNoteBar(IList<ScheduleBuilderPreviewLine> lines, int anchorLineIdx)
        {
            if (anchorLineIdx >= 0
                && anchorLineIdx < lines.Count
                && lines[anchorLineIdx]?.Kind == ScheduleBuilderPreviewLine.LineKind.GroupHeader)
            {
                for (int i = anchorLineIdx + 1; i < lines.Count; i++)
                {
                    var kind = lines[i]?.Kind;
                    if (kind == ScheduleBuilderPreviewLine.LineKind.Trip
                        || kind == ScheduleBuilderPreviewLine.LineKind.Gap)
                        return i;
                }

                return lines.Count;
            }

            return Math.Max(0, Math.Min(anchorLineIdx, lines.Count));
        }

        internal static Color? ResolveReserveBandForInsert(IList<ScheduleBuilderPreviewLine> lines, int insertIndex)
        {
            if (lines == null || lines.Count == 0)
                return ScheduleBuilderReserveBuckets.ReserversBand;

            int start = Math.Max(0, Math.Min(insertIndex, lines.Count));
            for (int i = start - 1; i >= 0; i--)
            {
                var line = lines[i];
                if (line == null) continue;
                if (line.Kind == ScheduleBuilderPreviewLine.LineKind.Trip && line.ReserveBandColor.HasValue)
                    return line.ReserveBandColor;
                if (line.Kind == ScheduleBuilderPreviewLine.LineKind.SectionHeader)
                    return ReserveBandFromSectionTitle(line.SectionTitle);
            }

            for (int i = start; i < lines.Count; i++)
            {
                var line = lines[i];
                if (line == null) continue;
                if (line.Kind == ScheduleBuilderPreviewLine.LineKind.SectionHeader)
                    return ReserveBandFromSectionTitle(line.SectionTitle);
                if (line.Kind == ScheduleBuilderPreviewLine.LineKind.Trip && line.ReserveBandColor.HasValue)
                    return line.ReserveBandColor;
            }

            return ScheduleBuilderReserveBuckets.ReserversBand;
        }

        private static Color? ReserveBandFromSectionTitle(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
                return null;
            if (title.StartsWith("Will calls", StringComparison.OrdinalIgnoreCase))
                return ScheduleBuilderReserveBuckets.WillCallBand;
            if (title.StartsWith("Reservers", StringComparison.OrdinalIgnoreCase))
                return ScheduleBuilderReserveBuckets.ReserversBand;
            if (title.StartsWith("Reroutes", StringComparison.OrdinalIgnoreCase))
                return ScheduleBuilderReserveBuckets.RerouteBand;
            if (title.StartsWith("Cancels", StringComparison.OrdinalIgnoreCase))
                return ScheduleBuilderReserveBuckets.CancelBand;
            if (title.StartsWith("Banned", StringComparison.OrdinalIgnoreCase))
                return ScheduleBuilderReserveBuckets.RerouteBand;
            return null;
        }

        internal static void ApplyTripMove(
            IList<ScheduleBuilderPreviewLine> lines,
            MCDownloadedTrip dragged,
            MCDownloadedTrip dropOnTargetTrip,
            int insertBeforeLineIndex,
            bool mergeOntoTarget)
        {
            if (lines == null || dragged == null) return;

            int from = -1;
            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i]?.Kind == ScheduleBuilderPreviewLine.LineKind.Trip
                    && TripEquals(lines[i].Trip, dragged))
                {
                    from = i;
                    break;
                }
            }
            if (from < 0) return;

            var moving = lines[from];
            lines.RemoveAt(from);

            if (mergeOntoTarget && dropOnTargetTrip != null)
            {
                int targetLine = FindTripLine(lines, dropOnTargetTrip);
                if (targetLine < 0)
                {
                    lines.Insert(Math.Min(insertBeforeLineIndex, lines.Count), moving);
                    return;
                }
                lines.Insert(targetLine + 1, moving);
                return;
            }

            int insert = insertBeforeLineIndex;
            if (from < insert) insert--;
            insert = Math.Max(0, Math.Min(insert, lines.Count));
            lines.Insert(insert, moving);
        }

        internal static int FindTripLineIndex(IList<ScheduleBuilderPreviewLine> lines, MCDownloadedTrip trip) =>
            FindTripLine(lines, trip);

        private static int FindTripLine(IList<ScheduleBuilderPreviewLine> lines, MCDownloadedTrip trip)
        {
            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i]?.Kind == ScheduleBuilderPreviewLine.LineKind.Trip
                    && TripEquals(lines[i].Trip, trip))
                    return i;
            }
            return -1;
        }

        internal static string NormalizeTripNumberKey(string tripNumber)
        {
            string tn = WellRydeFilterDataParser.FormatTripIdForScheduleMatch(
                ModivcareDelimitedTripParser.CanonicalizeTripNumber(tripNumber));
            if (tn.Length >= 2 && tn.StartsWith("1-", StringComparison.OrdinalIgnoreCase))
                tn = tn.Substring(2).Trim();
            return tn;
        }

        internal static bool HasLegSuffix(string tripNumber)
        {
            if (string.IsNullOrWhiteSpace(tripNumber))
                return false;
            string t = tripNumber.Trim();
            int len = t.Length;
            if (len < 2 || t[len - 2] != '-')
                return false;
            char c = char.ToUpperInvariant(t[len - 1]);
            return c == 'A' || c == 'B' || c == 'C';
        }

        internal static char ParseLegChar(string legText)
        {
            if (string.IsNullOrWhiteSpace(legText))
                return '\0';
            char c = char.ToUpperInvariant(legText.Trim()[0]);
            return c == 'A' || c == 'B' || c == 'C' ? c : '\0';
        }

        /// <summary>Same Modivcare trip id, different A/B/C leg (not the same leg twice).</summary>
        internal static bool IsPartnerLeg(string tripNumberA, string tripNumberB)
        {
            if (string.IsNullOrWhiteSpace(tripNumberA) || string.IsNullOrWhiteSpace(tripNumberB))
                return false;
            if (TripLegKeysMatch(tripNumberA, tripNumberB))
                return false;

            string baseA = SupeyScheduleAlgorithm.TripPartnerBase(
                NormalizeTripNumberKey(tripNumberA));
            string baseB = SupeyScheduleAlgorithm.TripPartnerBase(
                NormalizeTripNumberKey(tripNumberB));
            return baseA.Length > 0
                && string.Equals(baseA, baseB, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Append -A/-B/-C when the trip number has no leg suffix yet.</summary>
        internal static string ApplyLegSuffix(string tripNumber, char leg)
        {
            if (leg != 'A' && leg != 'B' && leg != 'C')
                return (tripNumber ?? "").Trim();
            string tn = (tripNumber ?? "").Trim();
            if (tn.Length == 0 || HasLegSuffix(tn))
                return tn;
            return tn + "-" + char.ToUpperInvariant(leg);
        }

        /// <summary>Identity for matching — includes A/B/C leg so partner legs never collide.</summary>
        internal static string TripLegKey(string tripNumber)
        {
            string canonical = ModivcareDelimitedTripParser.CanonicalizeTripNumber(tripNumber);
            string tn = NormalizeTripNumberKey(canonical);
            if (tn.Length == 0)
                return "";

            if (!HasLegSuffix(canonical))
                return tn;

            string baseId = SupeyScheduleAlgorithm.TripPartnerBase(tn);
            if (string.IsNullOrWhiteSpace(baseId))
                baseId = tn;

            char leg = SupeyScheduleAlgorithm.DetectLegPublic(canonical);
            return baseId + "-" + char.ToUpperInvariant(leg);
        }

        internal static bool TripLegKeysMatch(string a, string b)
        {
            string ka = TripLegKey(a);
            string kb = TripLegKey(b);
            return ka.Length > 0
                && string.Equals(ka, kb, StringComparison.OrdinalIgnoreCase);
        }

        internal static bool TripEquals(MCDownloadedTrip a, MCDownloadedTrip b)
        {
            if (a == null || b == null) return false;
            if (ReferenceEquals(a, b)) return true;
            return TripLegKeysMatch(a.TripNumber, b.TripNumber);
        }

        /// <summary>Reorder trip lines inside one gap-delimited group (1-based group number).</summary>
        internal static bool ApplyTripOrderToGroup(
            IList<ScheduleBuilderPreviewLine> lines,
            int groupNumber,
            IReadOnlyList<int> tripOrderIndices)
        {
            if (lines == null || groupNumber <= 0 || tripOrderIndices == null || tripOrderIndices.Count == 0)
                return false;

            if (!TryFindGroupTripLineIndices(lines, groupNumber, out var tripLineIndices))
                return false;
            if (tripLineIndices.Count != tripOrderIndices.Count)
                return false;

            var tripLines = new List<ScheduleBuilderPreviewLine>(tripLineIndices.Count);
            foreach (int idx in tripLineIndices)
                tripLines.Add(lines[idx]);

            for (int pos = 0; pos < tripOrderIndices.Count; pos++)
            {
                int from = tripOrderIndices[pos];
                if (from < 0 || from >= tripLines.Count)
                    return false;
                lines[tripLineIndices[pos]] = tripLines[from];
            }

            return true;
        }

        private static bool TryFindGroupTripLineIndices(
            IList<ScheduleBuilderPreviewLine> lines,
            int groupNumber,
            out List<int> tripLineIndices)
        {
            tripLineIndices = new List<int>();
            if (lines == null || groupNumber <= 0) return false;

            int groupCount = 0;
            int segStart = -1;
            for (int i = 0; i <= lines.Count; i++)
            {
                bool boundary = i == lines.Count
                    || lines[i].Kind == ScheduleBuilderPreviewLine.LineKind.Gap
                    || lines[i].Kind == ScheduleBuilderPreviewLine.LineKind.SectionHeader
                    || lines[i].Kind == ScheduleBuilderPreviewLine.LineKind.GroupHeader;

                if (boundary && segStart >= 0)
                {
                    groupCount++;
                    if (groupCount == groupNumber)
                    {
                        for (int j = segStart; j < i; j++)
                        {
                            if (lines[j]?.Kind == ScheduleBuilderPreviewLine.LineKind.SectionHeader
                                || lines[j]?.Kind == ScheduleBuilderPreviewLine.LineKind.GroupHeader)
                                continue;
                            if (lines[j]?.Kind == ScheduleBuilderPreviewLine.LineKind.Trip)
                                tripLineIndices.Add(j);
                        }
                        return tripLineIndices.Count > 0;
                    }
                    segStart = -1;
                }

                if (i < lines.Count && lines[i].Kind == ScheduleBuilderPreviewLine.LineKind.Trip)
                {
                    if (segStart < 0)
                        segStart = i;
                }
            }

            return false;
        }
    }
}
