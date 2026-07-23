using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Hiatme_Tool_Suite_v3
{
    internal sealed class ScheduleBuilderLoadResult
    {
        public Dictionary<string, List<ScheduleBuilderPreviewLine>> DriverLines { get; } =
            new Dictionary<string, List<ScheduleBuilderPreviewLine>>(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, List<MCDownloadedTrip>> DriverTrips { get; } =
            new Dictionary<string, List<MCDownloadedTrip>>(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, string> DriverGroupingNotes { get; } =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public List<MCDownloadedTrip> ReserveFileTrips { get; } = new List<MCDownloadedTrip>();

        /// <summary>Reserves sheet rows in file order (section headers + trips) for bucket restore on load.</summary>
        public List<SupeyTemplateSlot> ReserveSlots { get; } = new List<SupeyTemplateSlot>();

        public List<MCDownloadedTrip> AllTrips { get; } = new List<MCDownloadedTrip>();

        /// <summary>Trip numbers marked rerouted on Modivcare (__FSRR) anywhere in the loaded package.</summary>
        public HashSet<string> ReroutedTripNumbers { get; } =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public string SourceDescription { get; set; } = "";

        public DateTime? ServiceDate { get; set; }

        public string ServiceDateSource { get; set; } = "";

        /// <summary>Tab order from the loaded workbook or CSV folder (includes Reserves when present).</summary>
        public List<string> TabOrder { get; } = new List<string>();

        /// <summary>Excel A–N column widths from a loaded .xlsx (null for CSV loads).</summary>
        public double[] WorkbookColumnWidths { get; set; }
    }

    /// <summary>Load a previously saved schedule (CSV folder or Excel workbook) into preview/map shape.</summary>
    internal static class ScheduleBuilderScheduleLoad
    {
        private static readonly string[] SkipCsvNames = { "Schedule", "LGTC" };

        public static ScheduleBuilderLoadResult LoadFromFolder(string folderPath, string pickedFilePath = null)
        {
            var result = new ScheduleBuilderLoadResult();
            if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
                return result;

            var driverFiles = new List<(string Tab, string Path)>();

            foreach (var path in Directory.GetFiles(folderPath, "*.csv", SearchOption.TopDirectoryOnly))
            {
                string tab = Path.GetFileNameWithoutExtension(path) ?? "";
                if (ShouldSkipDriverFile(tab))
                    continue;

                result.TabOrder.Add(tab);

                if (tab.Equals("Reserves", StringComparison.OrdinalIgnoreCase))
                {
                    var slots = SupeyTemplateCsvLoader.LoadSlotsFromFile(path);
                    result.ReserveSlots.AddRange(slots);
                    CollectReroutedFromCsv(result, path);
                    result.ReserveFileTrips.AddRange(LoadTripsFromTripCsv(path));
                    continue;
                }

                driverFiles.Add((tab, path));
            }

            var tripsForDate = CollectTripsForDateResolution(result, driverFiles);
            string pathForDate = string.IsNullOrWhiteSpace(pickedFilePath) ? folderPath : pickedFilePath;
            if (ScheduleBuilderLoadDateResolver.TryResolve(
                    pathForDate, tripsForDate, out DateTime resolved, out string source))
            {
                result.ServiceDate = resolved.Date;
                result.ServiceDateSource = source;
            }

            string weekdayName = ResolveWeekdayName(result);

            foreach (var pair in driverFiles)
            {
                CollectReroutedFromCsv(result, pair.Path);
                var lines = ScheduleBuilderGroupInference.BuildDriverLines(
                    pair.Path, pair.Tab, weekdayName, out string note, loadedSchedule: true);
                AddDriver(result, pair.Tab, lines, note);
            }

            result.SourceDescription = folderPath;
            FinalizeAllTrips(result);
            StabilizeLoadedTabOrder(result);
            return result;
        }

        /// <summary>
        /// Synchronous workbook load. Prefer this from WinForms UI code — the async
        /// wrapper only yields for awaiters and will deadlock if blocked with GetResult
        /// on the UI synchronization context.
        /// </summary>
        public static ScheduleBuilderLoadResult LoadFromWorkbook(string workbookPath)
        {
            var result = new ScheduleBuilderLoadResult();
            if (string.IsNullOrWhiteSpace(workbookPath) || !File.Exists(workbookPath))
                return result;

            if (workbookPath.EndsWith(".xls", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "Old Excel .xls files are not supported for load.\n\n"
                    + "Open the file in Excel and Save As .xlsx, or export/load the driver CSV folder instead.");

            string tempDir = Path.Combine(
                Path.GetTempPath(),
                "HiatmeScheduleLoad_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            var driverSheets = new List<(string Tab, string CsvPath)>();

            try
            {
                foreach (var pair in ScheduleBuilderXlsxReader.ExportSheetsToCsvFolder(workbookPath, tempDir))
                {
                    string tab = pair.Tab;
                    if (ShouldSkipDriverFile(tab))
                        continue;

                    result.TabOrder.Add(tab);

                    if (tab.Equals("Reserves", StringComparison.OrdinalIgnoreCase))
                    {
                        var slots = SupeyTemplateCsvLoader.LoadSlotsFromFile(pair.CsvPath);
                        result.ReserveSlots.AddRange(slots);
                        CollectReroutedFromCsv(result, pair.CsvPath);
                        result.ReserveFileTrips.AddRange(LoadTripsFromTripCsv(pair.CsvPath));
                        continue;
                    }

                    driverSheets.Add(pair);
                }

                if (driverSheets.Count == 0 && result.ReserveFileTrips.Count == 0)
                    throw new InvalidOperationException(
                        "No driver sheets were found in that workbook.\n\n"
                        + "Expected one sheet per driver (same names as templates), plus Reserves if any.");

                var tripsForDate = CollectTripsForDateResolution(result, driverSheets);
                if (ScheduleBuilderLoadDateResolver.TryResolve(
                        workbookPath, tripsForDate, out DateTime resolved, out string source))
                {
                    result.ServiceDate = resolved.Date;
                    result.ServiceDateSource = source;
                }

                string weekdayName = ResolveWeekdayName(result);

                foreach (var pair in driverSheets)
                {
                    CollectReroutedFromCsv(result, pair.CsvPath);
                    var lines = ScheduleBuilderGroupInference.BuildDriverLines(
                        pair.CsvPath, pair.Tab, weekdayName, out string note, loadedSchedule: true);
                    AddDriver(result, pair.Tab, lines, note);
                }

                result.SourceDescription = workbookPath;
                result.WorkbookColumnWidths = ScheduleBuilderXlsxReader.ReadTripGridColumnWidths(workbookPath);
                FinalizeAllTrips(result);
                // Workbook sheet order is the saved tab order — keep as-is.
            }
            finally
            {
                try
                {
                    if (Directory.Exists(tempDir))
                        Directory.Delete(tempDir, true);
                }
                catch { /* ignore */ }
            }

            return result;
        }

        public static async Task<ScheduleBuilderLoadResult> LoadFromWorkbookAsync(string workbookPath)
        {
            var result = LoadFromWorkbook(workbookPath);
            await Task.Yield();
            return result;
        }

        public static List<MCDownloadedTrip> LoadTripsFromTripCsv(string filePath)
        {
            var trips = new List<MCDownloadedTrip>();
            var slots = SupeyTemplateCsvLoader.LoadSlotsFromFile(filePath);
            foreach (var slot in slots)
            {
                if (slot?.Kind == SupeyTemplateSlot.SlotKind.Trip && slot.TemplateTrip != null)
                    trips.Add(slot.TemplateTrip);
            }
            return trips;
        }

        public static void ApplyReserveBuckets(
            ScheduleBuilderLoadResult load,
            out List<MCDownloadedTrip> reservers,
            out List<MCDownloadedTrip> reroutes,
            out List<MCDownloadedTrip> banned,
            out List<MCDownloadedTrip> willCalls,
            out List<MCDownloadedTrip> cancels)
        {
            ScheduleBuilderBannedClients.ReloadCache();
            if (SupeyOutOfArea.CachedAreas == null || SupeyOutOfArea.CachedAreas.Count == 0)
                SupeyOutOfArea.SetCachedAreas(SupeyOutOfArea.LoadLocalFallback());

            reservers = new List<MCDownloadedTrip>();
            reroutes = new List<MCDownloadedTrip>();
            banned = new List<MCDownloadedTrip>();
            willCalls = new List<MCDownloadedTrip>();
            cancels = new List<MCDownloadedTrip>();

            if (load?.ReserveSlots != null && load.ReserveSlots.Count > 0)
            {
                ApplyReserveBucketsFromSlots(load, load.ReserveSlots, ref reservers, ref reroutes, ref willCalls, ref cancels);
                return;
            }

            ApplyReserveBucketsFromTrips(load, ref reservers, ref reroutes, ref banned, ref willCalls);
        }

        private static void ApplyReserveBucketsFromSlots(
            ScheduleBuilderLoadResult load,
            IList<SupeyTemplateSlot> slots,
            ref List<MCDownloadedTrip> reservers,
            ref List<MCDownloadedTrip> reroutes,
            ref List<MCDownloadedTrip> willCalls,
            ref List<MCDownloadedTrip> cancels)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            ScheduleBuilderReserveBuckets.ReserveBucket? currentSection = null;

            foreach (var slot in slots)
            {
                if (slot == null)
                    continue;

                if (slot.Kind == SupeyTemplateSlot.SlotKind.Gap)
                {
                    if (ScheduleBuilderReserveBuckets.TryParseSectionBucket(slot.NoteText, out var section))
                        currentSection = section;
                    continue;
                }

                if (slot.Kind != SupeyTemplateSlot.SlotKind.Trip || slot.TemplateTrip == null)
                    continue;

                var trip = slot.TemplateTrip;
                string tn = (trip.TripNumber ?? "").Trim();
                string key = ScheduleBuilderReroutedTrips.TripNumberKey(tn);
                if (key.Length > 0 && !seen.Add(key))
                    continue;

                if (slot.ReroutedOnModivcare
                    || ScheduleBuilderReroutedTrips.TripNumberKeySetContains(load.ReroutedTripNumbers, tn))
                {
                    reroutes.Add(trip);
                    continue;
                }

                if (currentSection.HasValue)
                {
                    switch (currentSection.Value)
                    {
                        case ScheduleBuilderReserveBuckets.ReserveBucket.Reroute:
                            reroutes.Add(trip);
                            break;
                        case ScheduleBuilderReserveBuckets.ReserveBucket.Cancel:
                            cancels.Add(trip);
                            break;
                        case ScheduleBuilderReserveBuckets.ReserveBucket.WillCall:
                            willCalls.Add(trip);
                            break;
                        default:
                            reservers.Add(trip);
                            break;
                    }

                    continue;
                }

                AddTripByClassify(load, trip, ref reservers, ref reroutes, ref willCalls);
            }
        }

        private static void ApplyReserveBucketsFromTrips(
            ScheduleBuilderLoadResult load,
            ref List<MCDownloadedTrip> reservers,
            ref List<MCDownloadedTrip> reroutes,
            ref List<MCDownloadedTrip> banned,
            ref List<MCDownloadedTrip> willCalls)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var trip in load?.ReserveFileTrips ?? Enumerable.Empty<MCDownloadedTrip>())
            {
                if (trip == null) continue;
                string tn = (trip.TripNumber ?? "").Trim();
                string key = ScheduleBuilderReroutedTrips.TripNumberKey(tn);
                if (key.Length > 0 && !seen.Add(key))
                    continue;

                AddTripByClassify(load, trip, ref reservers, ref reroutes, ref willCalls);
            }
        }

        private static void AddTripByClassify(
            ScheduleBuilderLoadResult load,
            MCDownloadedTrip trip,
            ref List<MCDownloadedTrip> reservers,
            ref List<MCDownloadedTrip> reroutes,
            ref List<MCDownloadedTrip> willCalls)
        {
            string tn = (trip.TripNumber ?? "").Trim();

            if (ScheduleBuilderReroutedTrips.TripNumberKeySetContains(load?.ReroutedTripNumbers, tn))
            {
                reroutes.Add(trip);
                return;
            }

            switch (ScheduleBuilderReserveBuckets.Classify(trip))
            {
                case ScheduleBuilderReserveBuckets.ReserveBucket.Banned:
                case ScheduleBuilderReserveBuckets.ReserveBucket.Reroute:
                    reroutes.Add(trip);
                    break;
                case ScheduleBuilderReserveBuckets.ReserveBucket.WillCall:
                    willCalls.Add(trip);
                    break;
                default:
                    reservers.Add(trip);
                    break;
            }
        }

        private static void AddDriver(
            ScheduleBuilderLoadResult result,
            string tab,
            List<ScheduleBuilderPreviewLine> lines,
            string groupingNote)
        {
            if (result == null || string.IsNullOrWhiteSpace(tab))
                return;

            lines = lines ?? new List<ScheduleBuilderPreviewLine>();
            result.DriverLines[tab] = lines;
            result.DriverGroupingNotes[tab] = groupingNote ?? "";
            CollectReroutedFromLines(result, lines);

            var trips = new List<MCDownloadedTrip>();
            foreach (var line in lines)
            {
                if (line?.Kind == ScheduleBuilderPreviewLine.LineKind.Trip && line.Trip != null)
                    trips.Add(line.Trip);
            }
            result.DriverTrips[tab] = trips;
        }

        private static List<MCDownloadedTrip> CollectTripsForDateResolution(
            ScheduleBuilderLoadResult result,
            IList<(string Tab, string Path)> driverFiles)
        {
            var trips = new List<MCDownloadedTrip>();
            if (result?.ReserveFileTrips != null)
                trips.AddRange(result.ReserveFileTrips);
            if (driverFiles == null)
                return trips;
            foreach (var pair in driverFiles)
                trips.AddRange(LoadTripsFromTripCsv(pair.Path));
            return trips;
        }

        private static string ResolveWeekdayName(ScheduleBuilderLoadResult result)
        {
            if (result?.ServiceDate != null)
                return result.ServiceDate.Value.DayOfWeek.ToString();
            return DateTime.Today.DayOfWeek.ToString();
        }

        private static void FinalizeAllTrips(ScheduleBuilderLoadResult result)
        {
            if (result == null) return;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in result.DriverTrips)
            {
                foreach (var t in kv.Value ?? Enumerable.Empty<MCDownloadedTrip>())
                {
                    if (t == null) continue;
                    string tn = (t.TripNumber ?? "").Trim();
                    if (tn.Length > 0 && !seen.Add(tn))
                        continue;
                    result.AllTrips.Add(t);
                }
            }
            foreach (var t in result.ReserveFileTrips)
            {
                if (t == null) continue;
                string tn = (t.TripNumber ?? "").Trim();
                if (tn.Length > 0 && !seen.Add(tn))
                    continue;
                result.AllTrips.Add(t);
            }
        }

        /// <summary>
        /// CSV folder loads have no sheet order — keep GetFiles sequence, but pin Reserves first when present.
        /// </summary>
        private static void StabilizeLoadedTabOrder(ScheduleBuilderLoadResult result)
        {
            if (result?.TabOrder == null || result.TabOrder.Count == 0)
                return;

            var stabilized = ScheduleBuilderTabOrder.DefaultBuildTabOrder(result.TabOrder);
            result.TabOrder.Clear();
            result.TabOrder.AddRange(stabilized);
        }

        private static bool ShouldSkipDriverFile(string tabName)
        {
            if (string.IsNullOrWhiteSpace(tabName))
                return true;
            foreach (var skip in SkipCsvNames)
            {
                if (tabName.IndexOf(skip, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        private static void CollectReroutedFromCsv(ScheduleBuilderLoadResult result, string csvPath)
        {
            if (result == null || string.IsNullOrWhiteSpace(csvPath))
                return;
            CollectReroutedFromSlots(result, SupeyTemplateCsvLoader.LoadSlotsFromFile(csvPath));
        }

        private static void CollectReroutedFromSlots(
            ScheduleBuilderLoadResult result,
            IList<SupeyTemplateSlot> slots)
        {
            if (result == null || slots == null)
                return;
            foreach (var slot in slots)
            {
                if (slot?.Kind != SupeyTemplateSlot.SlotKind.Trip || slot.TemplateTrip == null)
                    continue;
                if (!slot.ReroutedOnModivcare)
                    continue;
                ScheduleBuilderReroutedTrips.AddTripNumberKey(
                    result.ReroutedTripNumbers, slot.TemplateTrip.TripNumber);
            }
        }

        private static void CollectReroutedFromLines(
            ScheduleBuilderLoadResult result,
            IList<ScheduleBuilderPreviewLine> lines)
        {
            if (result == null || lines == null)
                return;
            foreach (var line in lines)
            {
                if (line?.Kind != ScheduleBuilderPreviewLine.LineKind.Trip || line.Trip == null)
                    continue;
                if (!line.ReroutedOnModivcare)
                    continue;
                ScheduleBuilderReroutedTrips.AddTripNumberKey(
                    result.ReroutedTripNumbers, line.Trip.TripNumber);
            }
        }

        private static string MakeSafeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return string.IsNullOrWhiteSpace(name) ? "Sheet" : name.Trim();
        }
    }
}
