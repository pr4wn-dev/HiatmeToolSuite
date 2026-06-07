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

        public List<MCDownloadedTrip> AllTrips { get; } = new List<MCDownloadedTrip>();

        public string SourceDescription { get; set; } = "";

        public DateTime? ServiceDate { get; set; }

        public string ServiceDateSource { get; set; } = "";
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

            foreach (var path in Directory.GetFiles(folderPath, "*.csv", SearchOption.TopDirectoryOnly)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                string tab = Path.GetFileNameWithoutExtension(path) ?? "";
                if (ShouldSkipDriverFile(tab))
                    continue;

                if (tab.Equals("Reserves", StringComparison.OrdinalIgnoreCase))
                {
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
                var lines = ScheduleBuilderGroupInference.BuildDriverLines(
                    pair.Path, pair.Tab, weekdayName, out string note);
                AddDriver(result, pair.Tab, lines, note);
            }

            result.SourceDescription = folderPath;
            FinalizeAllTrips(result);
            return result;
        }

        public static async Task<ScheduleBuilderLoadResult> LoadFromWorkbookAsync(string workbookPath)
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
                    if (tab.Equals("Reserves", StringComparison.OrdinalIgnoreCase))
                    {
                        result.ReserveFileTrips.AddRange(LoadTripsFromTripCsv(pair.CsvPath));
                        continue;
                    }

                    if (ShouldSkipDriverFile(tab))
                        continue;

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
                    var lines = ScheduleBuilderGroupInference.BuildDriverLines(
                        pair.CsvPath, pair.Tab, weekdayName, out string note);
                    AddDriver(result, pair.Tab, lines, note);
                }

                result.SourceDescription = workbookPath;
                FinalizeAllTrips(result);
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
            out List<MCDownloadedTrip> willCalls)
        {
            ScheduleBuilderBannedClients.ReloadCache();
            if (SupeyOutOfArea.CachedAreas == null || SupeyOutOfArea.CachedAreas.Count == 0)
                SupeyOutOfArea.SetCachedAreas(SupeyOutOfArea.LoadLocalFallback());

            reservers = new List<MCDownloadedTrip>();
            reroutes = new List<MCDownloadedTrip>();
            banned = new List<MCDownloadedTrip>();
            willCalls = new List<MCDownloadedTrip>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var trip in load?.ReserveFileTrips ?? Enumerable.Empty<MCDownloadedTrip>())
            {
                if (trip == null) continue;
                string tn = (trip.TripNumber ?? "").Trim();
                if (tn.Length > 0 && !seen.Add(tn))
                    continue;

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

        private static string MakeSafeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return string.IsNullOrWhiteSpace(name) ? "Sheet" : name.Trim();
        }
    }
}
