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
                if (item.Tag is FsPreviewGapTag)
                {
                    lines.Add(new ScheduleBuilderPreviewLine
                    {
                        Kind = ScheduleBuilderPreviewLine.LineKind.Gap,
                        GapNoteText = "",
                    });
                    continue;
                }

                if (item.Tag is FsPreviewNoteTag)
                {
                    if (lines.Count > 0
                        && lines[lines.Count - 1].Kind == ScheduleBuilderPreviewLine.LineKind.Trip)
                    {
                        lines.Add(new ScheduleBuilderPreviewLine
                        {
                            Kind = ScheduleBuilderPreviewLine.LineKind.Gap,
                            GapNoteText = "",
                        });
                    }
                    continue;
                }

                if (item.Tag is FsPreviewSectionHeaderTag section)
                {
                    lines.Add(new ScheduleBuilderPreviewLine
                    {
                        Kind = ScheduleBuilderPreviewLine.LineKind.SectionHeader,
                        SectionTitle = section.Title,
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

        internal static int CountTripAndGapLines(ListView lv)
        {
            int n = 0;
            if (lv == null) return 0;
            foreach (ListViewItem item in lv.Items)
            {
                if (item.Tag is FsPreviewGapTag || item.Tag is FsPreviewTripTag)
                    n++;
            }
            return n;
        }

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
                if (item.Tag is FsPreviewNoteTag || item.Tag is FsPreviewSectionHeaderTag)
                    continue;
                if (i == itemIndex)
                {
                    isGap = item.Tag is FsPreviewGapTag;
                    tripTag = item.Tag as FsPreviewTripTag;
                    return line;
                }
                if (item.Tag is FsPreviewGapTag || item.Tag is FsPreviewTripTag)
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
                insertBeforeLine = CountTripAndGapLines(lv);
                return true;
            }

            if (hit.Item.Tag is FsPreviewNoteTag)
            {
                var noteBounds = hit.Item.GetBounds(ItemBoundsPortion.Entire);
                int noteH = Math.Max(noteBounds.Height, 1);
                int noteRelY = clientPt.Y - noteBounds.Top;
                insertBeforeLine = ListViewIndexToLineIndex(lv, hit.Item.Index + 1, out targetTrip, out _);
                if (insertBeforeLine < 0)
                    insertBeforeLine = CountTripAndGapLines(lv);
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

        private static bool TripEquals(MCDownloadedTrip a, MCDownloadedTrip b)
        {
            if (a == null || b == null) return false;
            if (ReferenceEquals(a, b)) return true;
            return string.Equals(a.TripNumber, b.TripNumber, StringComparison.OrdinalIgnoreCase);
        }
    }
}
