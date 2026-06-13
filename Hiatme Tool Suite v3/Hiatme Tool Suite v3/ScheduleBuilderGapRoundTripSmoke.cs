using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>Console smoke test — run via: Hiatme Tool Suite v3.exe --gap-roundtrip-test</summary>
    internal static class ScheduleBuilderGapRoundTripSmoke
    {
        public static int Run()
        {
            var trip1 = SampleTrip("1001");
            var trip2 = SampleTrip("1002");
            var lines = new List<ScheduleBuilderPreviewLine>
            {
                new ScheduleBuilderPreviewLine { Kind = ScheduleBuilderPreviewLine.LineKind.Trip, Trip = trip1 },
                new ScheduleBuilderPreviewLine { Kind = ScheduleBuilderPreviewLine.LineKind.Gap },
                new ScheduleBuilderPreviewLine { Kind = ScheduleBuilderPreviewLine.LineKind.Gap },
                new ScheduleBuilderPreviewLine { Kind = ScheduleBuilderPreviewLine.LineKind.Trip, Trip = trip2 },
            };

            var linesByTab = new Dictionary<string, List<ScheduleBuilderPreviewLine>>
            {
                ["DriverA"] = lines,
            };

            var opt = new ScheduleBuilderPreviewCsvExport.Options
            {
                IncludeGaps = true,
                IncludeGroupHeaders = false,
            };

            var tabs = ScheduleBuilderPreviewCsvExport.BuildWorkbookTabs(linesByTab, opt);
            if (tabs == null || tabs.Count != 1 || tabs[0].Rows.Count != 4)
            {
                Console.Error.WriteLine("FAIL build tabs: count={0} rows={1}",
                    tabs?.Count ?? 0, tabs?[0].Rows.Count ?? 0);
                return 1;
            }

            string xlsx = Path.Combine(Path.GetTempPath(), "HiatmeGapRoundTripTest.xlsx");
            ScheduleBuilderXlsxWriter.WriteWorkbookFromTabs(xlsx, tabs);

            string tempDir = Path.Combine(Path.GetTempPath(), "HiatmeGapRoundTripCsv_" + Guid.NewGuid().ToString("N"));
            var exported = ScheduleBuilderXlsxReader.ExportSheetsToCsvFolder(xlsx, tempDir);
            if (exported.Count == 0)
            {
                Console.Error.WriteLine("FAIL xlsx reader exported 0 sheets");
                return 2;
            }

            string csvPath = exported[0].CsvPath;
            var slots = SupeyTemplateCsvLoader.LoadSlotsFromFile(csvPath);
            int gapSlots = slots.Count(s => s?.Kind == SupeyTemplateSlot.SlotKind.Gap);
            if (gapSlots != 2)
            {
                Console.Error.WriteLine("FAIL loader gap slots={0} (expected 2). CSV:", gapSlots);
                foreach (var row in File.ReadAllLines(csvPath))
                    Console.Error.WriteLine("  " + row);
                return 3;
            }

            var loaded = ScheduleBuilderGroupInference.BuildDriverLines(
                csvPath, "DriverA", "Saturday", out string note);
            int gapLines = loaded.Count(l => l?.Kind == ScheduleBuilderPreviewLine.LineKind.Gap);
            if (gapLines != 2 || note != "route breaks in file")
            {
                Console.Error.WriteLine("FAIL BuildDriverLines gaps={0} note={1}", gapLines, note);
                return 4;
            }

            var loadResult = new ScheduleBuilderLoadResult();
            loadResult.DriverLines["DriverA"] = loaded;
            loadResult.DriverTrips["DriverA"] = loaded
                .Where(l => l?.Kind == ScheduleBuilderPreviewLine.LineKind.Trip && l.Trip != null)
                .Select(l => l.Trip)
                .ToList();

            var builder = FullScheduleBuilder.FromServiceDate(new DateTime(2026, 6, 13));
            builder.ApplyLoadedSchedule(loadResult);
            int afterApply = builder.PreviewDriverLines["DriverA"]
                .Count(l => l?.Kind == ScheduleBuilderPreviewLine.LineKind.Gap);
            if (afterApply != 2)
            {
                Console.Error.WriteLine("FAIL ApplyLoadedSchedule gaps={0} (expected 2)", afterApply);
                return 5;
            }

            Console.WriteLine("PASS gap round-trip (2 empty spacer rows through load)");
            try { Directory.Delete(tempDir, true); } catch { /* ignore */ }
            try { File.Delete(xlsx); } catch { /* ignore */ }
            return 0;
        }

        private static MCDownloadedTrip SampleTrip(string num) =>
            new MCDownloadedTrip
            {
                TripNumber = num,
                Date = "6/13/2026",
                ClientFullName = "Test Client",
                PUStreet = "1 Main St",
                PUCity = "Portland",
                PUTime = "8:00 AM",
                DOStreet = "2 Oak St",
                DOCITY = "Portland",
                DOTime = "9:00 AM",
            };
    }
}
