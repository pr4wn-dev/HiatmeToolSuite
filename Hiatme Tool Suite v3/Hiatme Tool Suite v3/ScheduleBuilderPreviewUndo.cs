using System;
using System.Collections.Generic;
using System.Drawing;

namespace Hiatme_Tool_Suite_v3
{
    internal sealed class ScheduleBuilderUndoEntry
    {
        public string Label { get; set; }
        public Dictionary<string, List<ScheduleBuilderPreviewLine>> LinesByTab { get; set; }
        public MCDownloadedTrip CutTrip { get; set; }
        public Color? CutTripReserveBand { get; set; }
    }

    internal static class ScheduleBuilderPreviewUndo
    {
        internal const int MaxDepth = 30;

        internal static List<ScheduleBuilderPreviewLine> CloneLineList(IList<ScheduleBuilderPreviewLine> src)
        {
            var result = new List<ScheduleBuilderPreviewLine>(src?.Count ?? 0);
            if (src == null)
                return result;

            foreach (var line in src)
            {
                if (line == null)
                    continue;

                result.Add(new ScheduleBuilderPreviewLine
                {
                    Kind = line.Kind,
                    GapNoteText = line.GapNoteText,
                    GapNoteRowColor = line.GapNoteRowColor,
                    GapNoteCenterText = line.GapNoteCenterText,
                    GapNoteTextColor = line.GapNoteTextColor,
                    TrailingPad = line.TrailingPad,
                    SectionTitle = line.SectionTitle,
                    GroupNoteText = line.GroupNoteText,
                    GroupNumber = line.GroupNumber,
                    GroupColorOverride = line.GroupColorOverride,
                    GroupNoteRowColor = line.GroupNoteRowColor,
                    GroupNoteCenterText = line.GroupNoteCenterText,
                    GroupNoteTextColor = line.GroupNoteTextColor,
                    Trip = line.Trip,
                    ReroutedOnModivcare = line.ReroutedOnModivcare,
                    CancelledOnWellRyde = line.CancelledOnWellRyde,
                    ReserveBandColor = line.ReserveBandColor,
                });
            }

            return result;
        }

        internal static Dictionary<string, List<ScheduleBuilderPreviewLine>> CloneLinesByTab(
            IDictionary<string, List<ScheduleBuilderPreviewLine>> src)
        {
            var dict = new Dictionary<string, List<ScheduleBuilderPreviewLine>>(StringComparer.OrdinalIgnoreCase);
            if (src == null)
                return dict;

            foreach (var kv in src)
                dict[kv.Key] = CloneLineList(kv.Value);

            return dict;
        }

        internal static bool LinesByTabContainsGap(
            IDictionary<string, List<ScheduleBuilderPreviewLine>> linesByTab)
        {
            if (linesByTab == null)
                return false;

            foreach (var kv in linesByTab)
            {
                if (kv.Value == null)
                    continue;
                foreach (var line in kv.Value)
                {
                    if (line?.Kind == ScheduleBuilderPreviewLine.LineKind.Gap)
                        return true;
                }
            }

            return false;
        }

        internal static bool LinesByTabContainsGroupHeader(
            IDictionary<string, List<ScheduleBuilderPreviewLine>> linesByTab)
        {
            if (linesByTab == null)
                return false;

            foreach (var kv in linesByTab)
            {
                if (kv.Value == null)
                    continue;
                foreach (var line in kv.Value)
                {
                    if (line?.Kind == ScheduleBuilderPreviewLine.LineKind.GroupHeader)
                        return true;
                }
            }

            return false;
        }
    }

    internal sealed class ScheduleBuilderPreviewUndoStack
    {
        private readonly LinkedList<ScheduleBuilderUndoEntry> _undo = new LinkedList<ScheduleBuilderUndoEntry>();
        private readonly LinkedList<ScheduleBuilderUndoEntry> _redo = new LinkedList<ScheduleBuilderUndoEntry>();

        internal bool CanUndo => _undo.Count > 0;

        internal bool CanRedo => _redo.Count > 0;

        internal string NextUndoLabel => CanUndo ? _undo.First.Value.Label : null;

        internal string NextRedoLabel => CanRedo ? _redo.First.Value.Label : null;

        internal void PushBeforeEdit(ScheduleBuilderUndoEntry entry)
        {
            if (entry == null)
                return;

            _undo.AddFirst(entry);
            Trim(_undo);
            _redo.Clear();
        }

        internal void PushUndoCheckpoint(ScheduleBuilderUndoEntry entry)
        {
            if (entry == null)
                return;

            _undo.AddFirst(entry);
            Trim(_undo);
        }

        internal void PushRedo(ScheduleBuilderUndoEntry entry)
        {
            if (entry == null)
                return;

            _redo.AddFirst(entry);
            Trim(_redo);
        }

        internal ScheduleBuilderUndoEntry PopUndo()
        {
            if (!CanUndo)
                return null;

            var entry = _undo.First.Value;
            _undo.RemoveFirst();
            return entry;
        }

        internal ScheduleBuilderUndoEntry PopRedo()
        {
            if (!CanRedo)
                return null;

            var entry = _redo.First.Value;
            _redo.RemoveFirst();
            return entry;
        }

        internal void Clear()
        {
            _undo.Clear();
            _redo.Clear();
        }

        private static void Trim(LinkedList<ScheduleBuilderUndoEntry> stack)
        {
            while (stack.Count > ScheduleBuilderPreviewUndo.MaxDepth)
                stack.RemoveLast();
        }
    }
}
