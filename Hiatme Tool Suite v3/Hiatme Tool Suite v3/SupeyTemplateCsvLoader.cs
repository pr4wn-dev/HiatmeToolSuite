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

                for (int i = 0; i < rowValues.Length && i < 14; i++)
                    rowValues[i] = (rowValues[i] ?? "").Replace("\"", "").Trim();

                if (rowValues.Length < 14)
                {
                    if (TripTemplateCsvValidator.IsTemplateGapRow(rowValues))
                    {
                        slots.Add(new SupeyTemplateSlot
                        {
                            Kind = SupeyTemplateSlot.SlotKind.Gap,
                            NoteText = TripTemplateCsvValidator.ExtractInstructionText(rowValues),
                        });
                    }
                    continue;
                }

                if (TripTemplateCsvValidator.IsLikelyHeaderRow(rowValues))
                    continue;

                if (TripTemplateCsvValidator.IsTemplateGapRow(rowValues))
                {
                    slots.Add(new SupeyTemplateSlot
                    {
                        Kind = SupeyTemplateSlot.SlotKind.Gap,
                        NoteText = TripTemplateCsvValidator.ExtractInstructionText(rowValues),
                    });
                    continue;
                }

                if (TripTemplateCsvValidator.IsPlaceholderTripNumber(rowValues[0]))
                    continue;

                slots.Add(new SupeyTemplateSlot
                {
                    Kind = SupeyTemplateSlot.SlotKind.Trip,
                    TemplateTrip = TemplateTripRowParser.FromRow(rowValues),
                });
            }

            return slots;
        }
    }
}
