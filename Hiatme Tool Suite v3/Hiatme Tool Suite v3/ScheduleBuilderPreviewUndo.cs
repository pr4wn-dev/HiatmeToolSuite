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
                    SectionTitle = line.SectionTitle,
                    GroupNoteText = line.GroupNoteText,
                    GroupNumber = line.GroupNumber,
                    Trip = line.Trip,
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
    }

    internal sealed class ScheduleBuilderPreviewUndoStack
    {
        private readonly LinkedList<ScheduleBuilderUndoEntry> _undo = new LinkedList<ScheduleBuilderUndoEntry>();

        internal bool CanUndo => _undo.Count > 0;

        internal string NextUndoLabel => CanUndo ? _undo.First.Value.Label : null;

        internal void Push(ScheduleBuilderUndoEntry entry)
        {
            if (entry == null)
                return;

            _undo.AddFirst(entry);
            while (_undo.Count > ScheduleBuilderPreviewUndo.MaxDepth)
                _undo.RemoveLast();
        }

        internal ScheduleBuilderUndoEntry Pop()
        {
            if (!CanUndo)
                return null;

            var entry = _undo.First.Value;
            _undo.RemoveFirst();
            return entry;
        }

        internal void Clear()
        {
            _undo.Clear();
        }
    }
}
