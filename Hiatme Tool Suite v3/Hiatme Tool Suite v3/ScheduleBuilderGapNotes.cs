using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>User-placed note rows at a fixed line index (gap lines — not moved by group reconcile).</summary>
    internal static class ScheduleBuilderGapNotes
    {
        public static bool HasNoteContent(ScheduleBuilderPreviewLine line)
        {
            if (line?.Kind != ScheduleBuilderPreviewLine.LineKind.Gap)
                return false;

            return !string.IsNullOrWhiteSpace(line.GapNoteText)
                || line.GapNoteRowColor.HasValue;
        }

        public static bool GapTagHasNoteBar(FsPreviewGapTag tag)
        {
            if (tag == null)
                return false;

            return !string.IsNullOrWhiteSpace(tag.NoteText)
                || tag.NoteRowColor.HasValue;
        }

        /// <summary>Colored note rows paint as a merged workbook bar (no per-cell grid lines).</summary>
        public static bool GapTagUsesMergeBar(FsPreviewGapTag tag)
            => tag?.NoteRowColor.HasValue == true;

        public static void InsertAt(
            IList<ScheduleBuilderPreviewLine> lines,
            int insertBeforeLineIndex,
            string noteText,
            Color? noteRowColor,
            bool centerText = false)
        {
            if (lines == null)
                return;

            noteText = noteText?.Trim() ?? "";
            if (string.IsNullOrEmpty(noteText) && !noteRowColor.HasValue)
                return;

            int insert = System.Math.Max(0, System.Math.Min(insertBeforeLineIndex, lines.Count));
            lines.Insert(insert, new ScheduleBuilderPreviewLine
            {
                Kind = ScheduleBuilderPreviewLine.LineKind.Gap,
                GapNoteText = noteText,
                GapNoteRowColor = noteRowColor,
                GapNoteCenterText = centerText,
            });
        }

        public static void ApplyAt(
            IList<ScheduleBuilderPreviewLine> lines,
            int lineIndex,
            string noteText,
            Color? noteRowColor,
            bool centerText = false)
        {
            if (lines == null || lineIndex < 0 || lineIndex >= lines.Count)
                return;

            var line = lines[lineIndex];
            if (line?.Kind != ScheduleBuilderPreviewLine.LineKind.Gap
                && line?.Kind != ScheduleBuilderPreviewLine.LineKind.GroupHeader)
            {
                return;
            }

            noteText = noteText?.Trim() ?? "";
            if (line.Kind == ScheduleBuilderPreviewLine.LineKind.Gap)
            {
                line.GapNoteText = noteText;
                line.GapNoteRowColor = noteRowColor;
                line.GapNoteCenterText = centerText;
                if (!HasNoteContent(line))
                    lines.RemoveAt(lineIndex);
                else
                    line.TrailingPad = false;
                return;
            }

            line.GroupNoteText = noteText;
            line.GroupNoteRowColor = noteRowColor;
            line.GroupNoteCenterText = centerText;
            if (string.IsNullOrWhiteSpace(line.GroupNoteText)
                && !line.GroupNoteRowColor.HasValue
                && !line.GroupColorOverride.HasValue)
            {
                lines.RemoveAt(lineIndex);
            }
        }

        public static bool TryReadNoteAt(
            IList<ScheduleBuilderPreviewLine> lines,
            int lineIndex,
            out string noteText,
            out Color? noteRowColor)
            => TryReadNoteAt(lines, lineIndex, out noteText, out noteRowColor, out _);

        public static bool TryReadNoteAt(
            IList<ScheduleBuilderPreviewLine> lines,
            int lineIndex,
            out string noteText,
            out Color? noteRowColor,
            out bool centerText)
        {
            noteText = "";
            noteRowColor = null;
            centerText = false;
            if (lines == null || lineIndex < 0 || lineIndex >= lines.Count)
                return false;

            var line = lines[lineIndex];
            if (line?.Kind == ScheduleBuilderPreviewLine.LineKind.Gap && HasNoteContent(line))
            {
                noteText = line.GapNoteText ?? "";
                noteRowColor = line.GapNoteRowColor;
                centerText = line.GapNoteCenterText;
                return true;
            }

            if (line?.Kind == ScheduleBuilderPreviewLine.LineKind.GroupHeader)
            {
                noteText = line.GroupNoteText ?? "";
                noteRowColor = line.GroupNoteRowColor;
                centerText = line.GroupNoteCenterText;
                return !string.IsNullOrWhiteSpace(noteText) || noteRowColor.HasValue;
            }

            return false;
        }

        public static bool IsEditableNoteRow(
            ListViewItem item,
            IList<ScheduleBuilderPreviewLine> lines,
            out int lineIndex)
        {
            lineIndex = FsPreviewLineRef.GetLineIndex(item?.Tag);
            if (lineIndex < 0)
                return false;

            if (item?.Tag is FsPreviewNoteTag noteTag)
                return noteTag.Group != null;

            if (item?.Tag is FsPreviewGapTag gapTag && GapTagHasNoteBar(gapTag))
                return true;

            return lines != null
                && lineIndex < lines.Count
                && HasNoteContent(lines[lineIndex]);
        }
    }
}
