using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>Reads driver template CSVs in file order, preserving gap rows.</summary>
    internal static class SupeyTemplateCsvLoader
    {
        private static readonly Regex CsvParser = new Regex(",(?=(?:[^\"]*\"[^\"]*\")*(?![^\"]*\"))");

        public static List<SupeyTemplateSlot> LoadSlotsFromFile(string filePath)
        {
            var slots = new List<SupeyTemplateSlot>();
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                return slots;

            string[] lines;
            try { lines = File.ReadAllLines(filePath); }
            catch { return slots; }

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    slots.Add(new SupeyTemplateSlot { Kind = SupeyTemplateSlot.SlotKind.Gap, NoteText = "" });
                    continue;
                }

                var rowValues = CsvParser.Split(line);
                if (rowValues == null || rowValues.Length == 0)
                    continue;

                for (int i = 0; i < rowValues.Length && i < ScheduleBuilderPreviewCsvExport.WorkbookExportColumnCount; i++)
                    rowValues[i] = (rowValues[i] ?? "").Replace("\"", "").Trim();

                if (ScheduleBuilderGapMeta.RowHasGapMarker(rowValues))
                {
                    slots.Add(new SupeyTemplateSlot
                    {
                        Kind = SupeyTemplateSlot.SlotKind.Gap,
                        NoteText = "",
                    });
                    continue;
                }

                if (rowValues.Length < 14)
                {
                    if (TripTemplateCsvValidator.IsTemplateGapRow(rowValues))
                    {
                        slots.Add(new SupeyTemplateSlot
                        {
                            Kind = SupeyTemplateSlot.SlotKind.Gap,
                            NoteText = TripTemplateCsvValidator.ExtractInstructionText(rowValues),
                            NoteCenterText = ParseNoteCenterFromRow(rowValues),
                        });
                    }
                    continue;
                }

                if (TripTemplateCsvValidator.IsLikelyHeaderRow(rowValues))
                    continue;

                if (ScheduleBuilderGroupHeaderMeta.TryDecodeRow(
                        rowValues, out int groupNumber, out string headerNote, out bool headerCenter))
                {
                    slots.Add(new SupeyTemplateSlot
                    {
                        Kind = SupeyTemplateSlot.SlotKind.GroupHeader,
                        GroupNumber = groupNumber,
                        NoteText = headerNote ?? "",
                        NoteCenterText = headerCenter,
                    });
                    continue;
                }

                if (TripTemplateCsvValidator.IsTemplateGapRow(rowValues))
                {
                    slots.Add(new SupeyTemplateSlot
                    {
                        Kind = SupeyTemplateSlot.SlotKind.Gap,
                        NoteText = TripTemplateCsvValidator.ExtractInstructionText(rowValues),
                        NoteCenterText = ParseNoteCenterFromRow(rowValues),
                    });
                    continue;
                }

                if (TripTemplateCsvValidator.IsPlaceholderTripNumber(rowValues[0]))
                    continue;

                slots.Add(new SupeyTemplateSlot
                {
                    Kind = SupeyTemplateSlot.SlotKind.Trip,
                    TemplateTrip = TemplateTripRowParser.FromRow(rowValues),
                    ReroutedOnModivcare = ScheduleBuilderRerouteMeta.RowIsRerouted(rowValues),
                });
            }

            return slots;
        }

        private static bool ParseNoteCenterFromRow(string[] rowValues)
        {
            if (rowValues == null
                || rowValues.Length <= ScheduleBuilderPreviewCsvExport.WorkbookMetaColumnIndex)
            {
                return false;
            }

            string colO = rowValues[ScheduleBuilderPreviewCsvExport.WorkbookMetaColumnIndex] ?? "";
            return ScheduleBuilderNoteRowMeta.TryParseGapNoteMeta(colO, out bool center) && center;
        }
    }
}
