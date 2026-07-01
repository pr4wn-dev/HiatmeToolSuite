using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>Group header notes on driver preview tabs — stored as lines and exported as merged workbook cells.</summary>
    internal static class ScheduleBuilderGroupNotes
    {
        public static void ApplyNote(
            IList<ScheduleBuilderPreviewLine> lines,
            IList<SupeyTripCluster> groups,
            SupeyTripCluster group,
            string noteText,
            Color? noteRowColor)
        {
            if (lines == null || group == null)
                return;

            noteText = noteText?.Trim() ?? "";
            int groupNumber = group.GroupNumber;
            int firstTripIdx = FindFirstTripLineIndex(lines, groups, groupNumber);
            if (firstTripIdx < 0)
                return;

            int headerIdx = firstTripIdx - 1;
            bool hasHeader = headerIdx >= 0
                && lines[headerIdx].Kind == ScheduleBuilderPreviewLine.LineKind.GroupHeader
                && lines[headerIdx].GroupNumber == groupNumber;

            if (!hasHeader
                && string.IsNullOrEmpty(noteText)
                && !noteRowColor.HasValue)
            {
                return;
            }

            if (hasHeader)
            {
                lines[headerIdx].GroupNoteText = noteText;
                lines[headerIdx].GroupNoteRowColor = noteRowColor;
                if (ShouldRemoveEmptyHeader(lines[headerIdx]))
                    lines.RemoveAt(headerIdx);
                return;
            }

            lines.Insert(firstTripIdx, new ScheduleBuilderPreviewLine
            {
                Kind = ScheduleBuilderPreviewLine.LineKind.GroupHeader,
                GroupNumber = groupNumber,
                GroupNoteText = noteText,
                GroupNoteRowColor = noteRowColor,
            });
        }

        public static string GetNote(
            IList<ScheduleBuilderPreviewLine> lines,
            int groupNumber)
        {
            if (lines == null)
                return "";

            foreach (var line in lines)
            {
                if (line?.Kind != ScheduleBuilderPreviewLine.LineKind.GroupHeader)
                    continue;
                if (line.GroupNumber == groupNumber)
                    return line.GroupNoteText ?? "";
            }

            return "";
        }

        public static Color? GetNoteRowColor(
            IList<ScheduleBuilderPreviewLine> lines,
            int groupNumber)
        {
            if (lines == null)
                return null;

            foreach (var line in lines)
            {
                if (line?.Kind != ScheduleBuilderPreviewLine.LineKind.GroupHeader)
                    continue;
                if (line.GroupNumber == groupNumber)
                    return line.GroupNoteRowColor;
            }

            return null;
        }

        public static bool HasStoredNoteContent(
            IList<ScheduleBuilderPreviewLine> lines,
            int groupNumber)
        {
            if (lines == null || groupNumber <= 0)
                return false;

            foreach (var line in lines)
            {
                if (line?.Kind != ScheduleBuilderPreviewLine.LineKind.GroupHeader)
                    continue;
                if (line.GroupNumber != groupNumber)
                    continue;

                return !string.IsNullOrWhiteSpace(line.GroupNoteText)
                    || line.GroupNoteRowColor.HasValue;
            }

            return false;
        }

        internal static bool ShouldShowNoteRow(
            ScheduleBuilderPreviewLine line,
            bool showGroupColors)
        {
            if (line?.Kind != ScheduleBuilderPreviewLine.LineKind.GroupHeader)
                return false;

            return showGroupColors
                || !string.IsNullOrWhiteSpace(line.GroupNoteText)
                || line.GroupNoteRowColor.HasValue;
        }

        internal static Color? ResolveNoteRowDisplayColor(
            Color? noteRowColor,
            SupeyTripCluster group,
            bool showGroupColors)
        {
            if (noteRowColor.HasValue)
                return noteRowColor;
            if (showGroupColors && group != null)
                return group.DisplayColor;
            return null;
        }

        public static bool TryRemoveNoteRow(
            IList<ScheduleBuilderPreviewLine> lines,
            int previewLineIndex)
        {
            if (lines == null || previewLineIndex < 0 || previewLineIndex >= lines.Count)
                return false;

            var line = lines[previewLineIndex];
            if (line?.Kind != ScheduleBuilderPreviewLine.LineKind.GroupHeader)
                return false;

            line.GroupNoteText = "";
            line.GroupNoteRowColor = null;
            if (ShouldRemoveEmptyHeader(line))
                lines.RemoveAt(previewLineIndex);
            return true;
        }

        internal static bool IsDeletableNoteRow(
            ListViewItem item,
            IList<ScheduleBuilderPreviewLine> lines)
        {
            if (!(item?.Tag is FsPreviewNoteTag noteTag) || noteTag.PreviewLineIndex < 0)
                return false;
            if (lines == null || noteTag.PreviewLineIndex >= lines.Count)
                return false;

            return lines[noteTag.PreviewLineIndex]?.Kind
                == ScheduleBuilderPreviewLine.LineKind.GroupHeader;
        }

        private static bool ShouldRemoveEmptyHeader(ScheduleBuilderPreviewLine header)
        {
            if (header == null)
                return true;

            return string.IsNullOrWhiteSpace(header.GroupNoteText)
                && !header.GroupNoteRowColor.HasValue
                && !header.GroupColorOverride.HasValue;
        }

        private static int FindFirstTripLineIndex(
            IList<ScheduleBuilderPreviewLine> lines,
            IList<SupeyTripCluster> groups,
            int groupNumber)
        {
            if (lines == null || groups == null)
                return -1;

            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                if (line?.Kind != ScheduleBuilderPreviewLine.LineKind.Trip || line.Trip == null)
                    continue;

                var g = ScheduleBuilderPreviewGroups.FindGroupForTrip(groups, line.Trip);
                if (g != null && g.GroupNumber == groupNumber)
                    return i;
            }

            return -1;
        }
    }
}
