using Microsoft.Office.Interop.Excel;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.UI;
using System.Windows.Forms;
namespace Hiatme_Tool_Suite_v3
{
    /// <summary>Thrown when schedule building fails. Use StepName, FilePath, DriverName, TripNumber, RowIndex, ColumnOrField to locate the problem.</summary>
    public class ScheduleBuilderException : Exception
    {
        public string StepName { get; }
        public string FilePath { get; }
        public string DriverName { get; }
        public string TripNumber { get; }
        public int RowIndex { get; }
        /// <summary>Column letter, field name, or other pinpoint (e.g. "Column L (trip date)").</summary>
        public string ColumnOrField { get; }

        public ScheduleBuilderException(string stepName, string filePath, string driverName, string tripNumber, int rowIndex, Exception inner, string columnOrField = null)
            : base(BuildMessage(stepName, filePath, driverName, tripNumber, rowIndex, columnOrField, inner), inner)
        {
            StepName = stepName ?? "";
            FilePath = filePath ?? "";
            DriverName = driverName ?? "";
            TripNumber = tripNumber ?? "";
            RowIndex = rowIndex;
            ColumnOrField = columnOrField ?? "";
        }

        private static string BuildMessage(string step, string path, string driver, string trip, int row, string columnOrField, Exception inner)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Schedule builder could not finish.");
            sb.AppendLine();
            if (!string.IsNullOrEmpty(step))
                sb.AppendLine("Step: " + step);
            if (!string.IsNullOrEmpty(path))
                sb.AppendLine("File: " + path);
            if (!string.IsNullOrEmpty(driver))
                sb.AppendLine("Excel tab / driver template: " + driver);
            if (row > 0)
                sb.AppendLine("Line in template CSV (1-based; includes header if present): " + row);
            if (!string.IsNullOrEmpty(trip))
                sb.AppendLine("Trip #: " + trip);
            if (!string.IsNullOrEmpty(columnOrField) && columnOrField != "—")
                sb.AppendLine("Column / field: " + columnOrField);
            sb.AppendLine();
            sb.AppendLine(inner?.Message ?? "Unknown error.");
            return sb.ToString();
        }
    }

    internal class FullScheduleBuilder
    {
        public delegate void UpdateLoadingScreenHandler(string text);
        public delegate void ShowLoadingScreenHandler();
        public delegate void HideLoadingScreenHandler();

        public event UpdateLoadingScreenHandler UpdateLoadingScreen;
        public event ShowLoadingScreenHandler ShowLoadingScreen;
        public event HideLoadingScreenHandler HideLoadingScreen;

        public MCTripDownloader MCTripListDLer;
        public List<MCDownloadedTrip> MCTripList { get; set; }
        public List<MCDownloadedTrip> TripsFound { get; set; }
        public string NameOfDay { get; set; }
        public string Day { get; set; }
        public string NameOfMonth { get; set; }
        public string Month { get; set; }
        public string Year { get; set; }

        /// <summary>When set, SAVE/export uses these Excel A–N widths (from the trips ListView).</summary>
        internal double[] WorkbookColumnWidths { get; set; }

        /// <summary>When set, SAVE uses this path instead of prompting (e.g. loaded .xlsx).</summary>
        internal string PreferredExportPath { get; set; }

        public DateTime ServiceDate
        {
            get
            {
                if (int.TryParse(Day, out int day)
                    && int.TryParse(Month, out int month)
                    && int.TryParse(Year, out int year)
                    && month >= 1 && month <= 12
                    && day >= 1 && day <= DateTime.DaysInMonth(year, month))
                {
                    return new DateTime(year, month, day);
                }

                return DateTime.Today;
            }
        }

        /// <summary>Sync export filename/folder fields from the Schedule Builder date picker.</summary>
        public void ApplyServiceDate(DateTime serviceDate)
        {
            var d = serviceDate.Date;
            NameOfDay = d.DayOfWeek.ToString();
            Day = d.Day.ToString();
            NameOfMonth = d.ToString("MMMM");
            Month = d.Month.ToString();
            Year = d.Year.ToString();
        }

        public IDictionary<string, List<MCDownloadedTrip>> driverTripList;

        private readonly Dictionary<string, List<SupeyTemplateSlot>> _driverTemplateSlots =
            new Dictionary<string, List<SupeyTemplateSlot>>(StringComparer.OrdinalIgnoreCase);

        private readonly List<string> _driverTemplateSlotOrder = new List<string>();

        /// <summary>Full preview/export tab order including Reserves when present.</summary>
        public List<string> TabOrder { get; private set; } = new List<string>();

        /// <summary>How to write Template Temps CSV rows (gaps, group headers, reserve sections).</summary>
        public ScheduleBuilderPreviewCsvExport.Options PreviewCsvExportOptions { get; set; }

        /// <summary>When true, keep every blank template row instead of collapsing runs of gaps to one.</summary>
        public bool PreserveMultiRowGaps { get; set; }

        private IReadOnlyDictionary<string, List<ScheduleBuilderPreviewLine>> _previewLinesByTab;

        /// <summary>Rewrite working CSVs from preview lines (used before SAVE and after BUILD).</summary>
        public void ExportPreviewCsvs(IReadOnlyDictionary<string, List<ScheduleBuilderPreviewLine>> linesByTab)
        {
            if (linesByTab == null)
                return;

            _previewLinesByTab = linesByTab;
            var opt = PreviewCsvExportOptions ?? ScheduleBuilderPreviewCsvExport.Options.TripsOnly;
            string tempDir = TemplateBuilder.GetTemplateTempDirectory();
            foreach (var key in ScheduleBuilderTabOrder.OrderedKeys(linesByTab, TabOrder))
            {
                if (string.IsNullOrWhiteSpace(key))
                    continue;

                if (!linesByTab.TryGetValue(key, out var lines))
                    lines = new List<ScheduleBuilderPreviewLine>();

                string path = Path.Combine(tempDir, key + ".csv");
                bool reserves = key.Equals("Reserves", StringComparison.OrdinalIgnoreCase);
                try
                {
                    ScheduleBuilderPreviewCsvExport.WriteTabCsv(
                        path,
                        lines ?? new List<ScheduleBuilderPreviewLine>(),
                        opt,
                        reserves);
                }
                catch (Exception ex)
                {
                    throw new ScheduleBuilderException(
                        "ExportPreviewCsvs",
                        path,
                        key,
                        null,
                        0,
                        new IOException("Could not write CSV for tab \"" + key + "\".\n\n" + ex.Message, ex),
                        "Working folder: " + tempDir);
                }
            }
        }

        /// <summary>Apply tab order from the Schedule Builder UI before export/save.</summary>
        public void SetTabOrder(IReadOnlyList<string> tabOrder)
        {
            TabOrder = tabOrder != null && tabOrder.Count > 0
                ? new List<string>(tabOrder)
                : new List<string>();
        }

        private void WriteAllPreviewCsvsFromBuilder()
        {
            var linesByTab = new Dictionary<string, List<ScheduleBuilderPreviewLine>>(StringComparer.OrdinalIgnoreCase);
            if (PreviewDriverLines != null)
            {
                foreach (var kv in PreviewDriverLines)
                    linesByTab[kv.Key] = kv.Value ?? new List<ScheduleBuilderPreviewLine>();
            }

            linesByTab["Reserves"] = ScheduleBuilderReserveBuckets.BuildReservePreviewLines(
                PreviewReserves,
                PreviewReservesReroute,
                PreviewReservesBanned,
                PreviewReservesWillCalls,
                WillCallsInDownloadCount);

            ExportPreviewCsvs(linesByTab);
        }

        private IReadOnlyList<ScheduleBuilderPreviewCsvExport.WorkbookTab> BuildWorkbookTabsFromPreview()
        {
            if (_previewLinesByTab == null || _previewLinesByTab.Count == 0)
                return null;

            var opt = PreviewCsvExportOptions ?? ScheduleBuilderPreviewCsvExport.Options.TripsOnly;
            return ScheduleBuilderPreviewCsvExport.BuildWorkbookTabs(_previewLinesByTab, opt, TabOrder);
        }

        /// <summary>Per-driver preview rows in template order (gaps + matched trips).</summary>
        public IReadOnlyDictionary<string, List<ScheduleBuilderPreviewLine>> PreviewDriverLines { get; private set; } =
            new Dictionary<string, List<ScheduleBuilderPreviewLine>>();

        /// <summary>Per-driver matched trips after <see cref="BuildPreviewAsync"/> (trips only, no gaps).</summary>
        public IReadOnlyDictionary<string, List<MCDownloadedTrip>> PreviewDriverTrips
        {
            get
            {
                if (driverTripList == null)
                    return new Dictionary<string, List<MCDownloadedTrip>>();
                return new Dictionary<string, List<MCDownloadedTrip>>(driverTripList);
            }
        }

        /// <summary>Downloaded trips that did not match any template row (excludes no-go reroutes).</summary>
        public List<MCDownloadedTrip> PreviewReserves { get; private set; } = new List<MCDownloadedTrip>();

        /// <summary>Unassigned no-go trips — reroute to Modivcare. Trips on a driver template stay on that driver (exceptions).</summary>
        public List<MCDownloadedTrip> PreviewReservesReroute { get; private set; } = new List<MCDownloadedTrip>();

        /// <summary>Banned clients (name + age) — not placed on driver tabs.</summary>
        public List<MCDownloadedTrip> PreviewReservesBanned { get; private set; } = new List<MCDownloadedTrip>();

        /// <summary>00:00 PU will calls — top of Reserves; not kept on driver tabs after BUILD.</summary>
        public List<MCDownloadedTrip> PreviewReservesWillCalls { get; private set; } = new List<MCDownloadedTrip>();

        /// <summary>When set, Reserves tab bind uses saved sheet order instead of PU-time sort.</summary>
        public IList<SupeyTemplateSlot> LoadedReserveSlots { get; private set; }

        /// <summary>True after loading a saved schedule — keep reserve trip order unless user runs BUILD again.</summary>
        public bool PreserveReserveTripOrder { get; private set; }

        internal void ClearLoadedReserveLayout()
        {
            LoadedReserveSlots = null;
            PreserveReserveTripOrder = false;
        }

        /// <summary>Downloaded will-call trips (00:00 PU and/or WILL CALL in comments).</summary>
        public int WillCallsInDownloadCount { get; private set; }

        public int WillCallsPuMidnightInDownloadCount { get; private set; }

        public int WillCallsCommentInDownloadCount { get; private set; }

        private readonly List<MCDownloadedTrip> _bannedPulledFromDrivers = new List<MCDownloadedTrip>();

        private readonly List<MCDownloadedTrip> _willCallPulledFromDrivers = new List<MCDownloadedTrip>();

        public FullScheduleBuilder(string dayname, string daynumber, string monthname, string monthnumber, string year)
        {
            NameOfDay = dayname;
            Day = daynumber;
            NameOfMonth = monthname;
            Month = monthnumber;
            Year = year;
        }

        public static FullScheduleBuilder FromServiceDate(DateTime serviceDate)
        {
            var d = serviceDate.Date;
            return new FullScheduleBuilder(
                d.DayOfWeek.ToString(),
                d.Day.ToString(),
                d.ToString("MMMM"),
                d.Month.ToString(),
                d.Year.ToString());
        }
        private async Task AsyncUpdateLoadingScreen(string txt)
        {
            UpdateLoadingScreen?.Invoke(txt);
            await Task.Yield();
        }

        private void NotifyHideLoadingScreen()
        {
            HideLoadingScreen?.Invoke();
        }

        private void ClearLastExport()
        {
            LastExportPath = null;
            LastExportWasCsv = false;
        }
        public async Task DownloadMCTrips(DateTime mcdate, MCLoginHandler mcLoginHandler)
        {
            try
            {
                MCTripListDLer = new MCTripDownloader();
                MCTripList = new List<MCDownloadedTrip>();
                await AsyncUpdateLoadingScreen("Checking connections");
                await AsyncUpdateLoadingScreen("Downloading trips…");
                MCTripList = await MCTripListDLer.DownloadTripRecords(mcdate, mcLoginHandler);
                if (MCTripList != null)
                {
                    foreach (MCDownloadedTrip mcrtr in MCTripList)
                    {
                        Console.WriteLine(mcrtr.Date + ": " + mcrtr.ClientFullName + " " + mcrtr.TripNumber);
                    }
                }
                Console.WriteLine("finished gathering trips!");
            }
            catch (Exception ex)
            {
                throw new ScheduleBuilderException(
                    "DownloadMCTrips",
                    null,
                    null,
                    null,
                    0,
                    new InvalidOperationException(
                        "Could not download trips from Modivcare for the selected date.\n\n" +
                        "Check that you are signed in, the service date is correct, and your network connection is stable.\n\n" +
                        "Original error: " + ex.Message,
                        ex),
                    "—");
            }
        }
        /// <summary>Download, match templates, and fill preview data — no Excel export.</summary>
        public async Task BuildPreviewAsync(DateTime modcdate, MCLoginHandler modcLoginHandler)
        {
            try
            {
                await DownloadMCTrips(modcdate, modcLoginHandler).ConfigureAwait(false);

                if (MCTripList == null || !MCTripList.Any())
                {
                    throw new ScheduleBuilderException("DownloadMCTrips", null, null, null, 0,
                        new InvalidOperationException("No trips were downloaded. Check your Modivcare connection and date."));
                }

                await AsyncUpdateLoadingScreen("Loading rules").ConfigureAwait(false);
                await RefreshNoGoAreasAsync().ConfigureAwait(false);
                ScheduleBuilderBannedClients.ReloadCache();

                await AsyncUpdateLoadingScreen("Loading template files").ConfigureAwait(false);
                LoadTemplateFiles();
                EnsureMatchedTripsOrThrow();
            }
            catch (ScheduleBuilderException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new ScheduleBuilderException("BuildPreview", null, null, null, 0, ex);
            }
        }

        public async Task BuildFullSchedule(DateTime modcdate, MCLoginHandler modcLoginHandler)
        {
            await BuildPreviewAsync(modcdate, modcLoginHandler).ConfigureAwait(false);
            await CreateWorkbookAsync(promptForLocation: true).ConfigureAwait(false);
        }

        /// <summary>Populate preview from a saved CSV package or workbook (no Modivcare download).</summary>
        public void ApplyLoadedSchedule(ScheduleBuilderLoadResult load)
        {
            if (load == null)
                throw new ArgumentNullException(nameof(load));

            EnsureScheduleBuilderRulesLoaded();

            var driverLines = new Dictionary<string, List<ScheduleBuilderPreviewLine>>(
                StringComparer.OrdinalIgnoreCase);
            foreach (var kv in load.DriverLines)
                driverLines[kv.Key] = kv.Value ?? new List<ScheduleBuilderPreviewLine>();
            PreviewDriverLines = driverLines;

            driverTripList = new Dictionary<string, List<MCDownloadedTrip>>(
                StringComparer.OrdinalIgnoreCase);
            foreach (var kv in load.DriverTrips)
                driverTripList[kv.Key] = kv.Value ?? new List<MCDownloadedTrip>();

            TripsFound = load.AllTrips != null
                ? new List<MCDownloadedTrip>(load.AllTrips)
                : new List<MCDownloadedTrip>();
            MCTripList = TripsFound;

            ScheduleBuilderScheduleLoad.ApplyReserveBuckets(
                load,
                out var reservers,
                out var reroutes,
                out var banned,
                out var willCalls);

            ScheduleBuilderReserveBuckets.RebucketBannedTripsIntoReroutes(
                reservers, reroutes, willCalls, banned);

            PreviewReserves = reservers;
            PreviewReservesReroute = reroutes;
            PreviewReservesWillCalls = willCalls;
            PreviewReservesBanned = new List<MCDownloadedTrip>();

            if (MCTripList != null)
            {
                ScheduleBuilderReserveBuckets.CountWillCallsInDownload(
                    MCTripList,
                    out int wcTotal,
                    out int wcPu,
                    out int wcCmt);
                WillCallsInDownloadCount = wcTotal;
                WillCallsPuMidnightInDownloadCount = wcPu;
                WillCallsCommentInDownloadCount = wcCmt;
            }
            else
                WillCallsInDownloadCount = WillCallsPuMidnightInDownloadCount = WillCallsCommentInDownloadCount = 0;

            RemoveWillCallsFromDriverPreview();
            RemoveBannedTripsFromDriverPreview();

            if (load.ReserveSlots != null && load.ReserveSlots.Count > 0)
                LoadedReserveSlots = new List<SupeyTemplateSlot>(load.ReserveSlots);
            else
                LoadedReserveSlots = null;
            PreserveReserveTripOrder = true;

            TabOrder = load.TabOrder != null && load.TabOrder.Count > 0
                ? new List<string>(load.TabOrder)
                : ScheduleBuilderTabOrder.DefaultBuildTabOrder(load.DriverLines.Keys);
        }

        /// <summary>Banned-client trips belong in Reserves → Reroutes, not on driver tabs.</summary>
        internal void RemoveBannedTripsFromDriverPreview()
        {
            EnsureScheduleBuilderRulesLoaded();

            var dict = PreviewDriverLines as Dictionary<string, List<ScheduleBuilderPreviewLine>>;
            if (dict == null) return;

            var reroutes = PreviewReservesReroute ?? new List<MCDownloadedTrip>();
            ScheduleBuilderReserveBuckets.PullBannedTripsFromDriverLines(dict, reroutes);

            var reservers = PreviewReserves ?? new List<MCDownloadedTrip>();
            var willCalls = PreviewReservesWillCalls ?? new List<MCDownloadedTrip>();
            var legacyBanned = PreviewReservesBanned ?? new List<MCDownloadedTrip>();
            ScheduleBuilderReserveBuckets.RebucketBannedTripsIntoReroutes(
                reservers, reroutes, willCalls, legacyBanned);

            PreviewReserves = reservers;
            PreviewReservesReroute = reroutes;
            PreviewReservesWillCalls = willCalls;
            PreviewReservesBanned = new List<MCDownloadedTrip>();

            if (driverTripList != null)
            {
                foreach (var kv in dict)
                {
                    var trips = new List<MCDownloadedTrip>();
                    foreach (var line in kv.Value ?? Enumerable.Empty<ScheduleBuilderPreviewLine>())
                    {
                        if (line?.Kind == ScheduleBuilderPreviewLine.LineKind.Trip && line.Trip != null)
                            trips.Add(line.Trip);
                    }
                    driverTripList[kv.Key] = trips;
                }
            }
        }

        /// <summary>After Modivcare reroute — bucket under Reserves → Reroutes and pull off driver tabs.</summary>
        internal bool MoveTripToPreviewReservesReroute(MCDownloadedTrip trip)
        {
            if (trip == null)
                return false;

            MCDownloadedTrip best = FindTripInPreviewByNumber(trip.TripNumber);
            if (best != null)
            {
                if (!ReferenceEquals(best, trip))
                    best.MergeMissingScheduleFieldsFrom(trip);
                trip = best;
            }

            var reservers = PreviewReserves ?? new List<MCDownloadedTrip>();
            var reroutes = PreviewReservesReroute ?? new List<MCDownloadedTrip>();
            var willCalls = PreviewReservesWillCalls ?? new List<MCDownloadedTrip>();

            bool changed = ScheduleBuilderReserveBuckets.MoveTripToReroutesBucket(
                trip, reservers, reroutes, willCalls);

            var dict = PreviewDriverLines as Dictionary<string, List<ScheduleBuilderPreviewLine>>;
            if (dict != null)
            {
                int pulled = ScheduleBuilderReserveBuckets.PullTripFromDriverLines(dict, trip);
                if (pulled > 0)
                {
                    changed = true;
                    if (driverTripList != null)
                    {
                        foreach (var kv in dict)
                        {
                            var trips = new List<MCDownloadedTrip>();
                            foreach (var line in kv.Value ?? Enumerable.Empty<ScheduleBuilderPreviewLine>())
                            {
                                if (line?.Kind == ScheduleBuilderPreviewLine.LineKind.Trip && line.Trip != null)
                                    trips.Add(line.Trip);
                            }
                            driverTripList[kv.Key] = trips;
                        }
                    }
                }
            }

            PreviewReserves = reservers;
            PreviewReservesReroute = reroutes;
            PreviewReservesWillCalls = willCalls;

            return changed;
        }

        internal bool TripExistsInPreview(string tripNumber)
        {
            string key = ScheduleBuilderReroutedTrips.TripNumberKey(tripNumber);
            if (key.Length == 0)
                return false;

            bool Match(MCDownloadedTrip t) =>
                ScheduleBuilderReroutedTrips.TripNumberKeysMatch(t?.TripNumber, key);

            if (MCTripList?.Any(Match) == true)
                return true;
            if (TripsFound?.Any(Match) == true)
                return true;

            var reserveLists = new[] { PreviewReserves, PreviewReservesReroute, PreviewReservesWillCalls };
            foreach (var list in reserveLists)
            {
                if (list?.Any(Match) == true)
                    return true;
            }

            if (PreviewDriverLines == null)
                return false;

            foreach (var kv in PreviewDriverLines)
            {
                foreach (var line in kv.Value ?? Enumerable.Empty<ScheduleBuilderPreviewLine>())
                {
                    if (line?.Kind == ScheduleBuilderPreviewLine.LineKind.Trip && Match(line.Trip))
                        return true;
                }
            }

            return false;
        }

        /// <summary>Shared registry ghost — trip rerouted on Modivcare but no longer in download.</summary>
        internal bool TryAddSharedReroutedGhost(MCDownloadedTrip ghost)
        {
            if (ghost == null || string.IsNullOrWhiteSpace(ghost.TripNumber))
                return false;

            MCDownloadedTrip richer = FindTripInPreviewByNumber(ghost.TripNumber);
            if (richer != null)
                ghost.MergeMissingScheduleFieldsFrom(richer);

            if (TripExistsInPreview(ghost.TripNumber))
                return false;

            var reroutes = PreviewReservesReroute ?? new List<MCDownloadedTrip>();
            reroutes.Add(ghost);
            PreviewReservesReroute = reroutes;
            if (TripsFound != null)
                TripsFound.Add(ghost);
            return true;
        }

        /// <summary>Move rerouted trips off driver tabs into Reserves → Reroutes (load / registry reconcile).</summary>
        internal int ReconcileReroutedTripsForPreview(IEnumerable<string> reroutedTripNumbers)
        {
            if (reroutedTripNumbers == null)
                return 0;

            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var raw in reroutedTripNumbers)
                ScheduleBuilderReroutedTrips.AddTripNumberKey(keys, raw);
            if (keys.Count == 0)
                return 0;

            int moved = 0;
            foreach (string key in keys)
            {
                var trip = FindTripInPreviewByNumber(key);
                if (trip == null)
                    continue;
                if (MoveTripToPreviewReservesReroute(trip))
                    moved++;
            }

            return moved;
        }

        internal MCDownloadedTrip FindTripInPreviewByNumber(string tripNumber)
        {
            string key = ScheduleBuilderReroutedTrips.TripNumberKey(tripNumber);
            if (key.Length == 0)
                return null;

            bool Match(MCDownloadedTrip t) =>
                ScheduleBuilderReroutedTrips.TripNumberKeysMatch(t?.TripNumber, key);

            MCDownloadedTrip best = null;

            void Consider(MCDownloadedTrip candidate)
            {
                if (candidate == null || !Match(candidate))
                    return;
                if (best == null || TripHasRicherScheduleFields(candidate, best))
                    best = candidate;
            }

            if (PreviewDriverLines != null)
            {
                foreach (var kv in PreviewDriverLines)
                {
                    foreach (var line in kv.Value ?? Enumerable.Empty<ScheduleBuilderPreviewLine>())
                    {
                        if (line?.Kind == ScheduleBuilderPreviewLine.LineKind.Trip)
                            Consider(line.Trip);
                    }
                }
            }

            foreach (var list in new[] { MCTripList, TripsFound, PreviewReserves, PreviewReservesWillCalls })
            {
                if (list == null)
                    continue;
                foreach (var t in list)
                    Consider(t);
            }

            foreach (var t in PreviewReservesReroute ?? Enumerable.Empty<MCDownloadedTrip>())
                Consider(t);

            return best;
        }

        private static bool TripHasRicherScheduleFields(MCDownloadedTrip candidate, MCDownloadedTrip current)
        {
            if (candidate == null)
                return false;
            if (current == null)
                return true;

            return ScheduleFieldScore(candidate) > ScheduleFieldScore(current);
        }

        private static int ScheduleFieldScore(MCDownloadedTrip trip)
        {
            if (trip == null)
                return 0;

            int score = 0;
            if (!string.IsNullOrWhiteSpace(trip.PUTelephone)) score += 4;
            if (!string.IsNullOrWhiteSpace(trip.DOTelephone)) score += 4;
            if (!string.IsNullOrWhiteSpace(trip.PUStreet)) score++;
            if (!string.IsNullOrWhiteSpace(trip.DOStreet)) score++;
            if (!string.IsNullOrWhiteSpace(trip.PUTime)) score++;
            if (!string.IsNullOrWhiteSpace(trip.DOTime)) score++;
            if (!string.IsNullOrWhiteSpace(trip.ClientFullName)) score++;
            return score;
        }

        /// <summary>Re-bucket Reserves lists after banned / no-go rules change (unban, remove town).</summary>
        internal void RebucketPreviewReserves()
        {
            var reservers = PreviewReserves ?? new List<MCDownloadedTrip>();
            var reroutes = PreviewReservesReroute ?? new List<MCDownloadedTrip>();
            var willCalls = PreviewReservesWillCalls ?? new List<MCDownloadedTrip>();
            ScheduleBuilderReserveBuckets.ReclassifyReserveBuckets(reservers, reroutes, willCalls);
            PreviewReserves = reservers;
            PreviewReservesReroute = reroutes;
            PreviewReservesWillCalls = willCalls;
            PreviewReservesBanned = new List<MCDownloadedTrip>();
        }

        private void EnsureMatchedTripsOrThrow()
        {
            if (TripsFound != null && TripsFound.Any())
                return;

            var dayDir = Path.Combine(AppContext.BaseDirectory, NameOfDay);
            throw new ScheduleBuilderException(
                "BuildTempCsvFiles",
                dayDir,
                NameOfDay,
                null,
                0,
                new InvalidOperationException(
                    "No Modivcare trips matched your template rows for " + NameOfDay + ".\n\n" +
                    "Common causes:\n" +
                    "• Wrong weekday folder (templates must be under the same day name as the service date, e.g. Friday).\n" +
                    "• Client name, PU/DO street or city, or PU/DO times differ between template CSV and today's download.\n" +
                    "• Template CSV was built from a different route pattern than today's trips.\n\n" +
                    "Calendar month/year in the template date column is ignored.\n" +
                    "Template folder used:\n" + dayDir),
                "Match uses: client name, PU street & city, DO street & city, PU time, DO time (not trip #).");
        }

        private static async Task RefreshNoGoAreasAsync()
        {
            try
            {
                var ai = HiatmeAiSettings.Load();
                if (HiatmeGeoSettings.UseServer)
                {
                    await SupeyOutOfArea.TrySyncLocalFileToServerAsync(ai, CancellationToken.None)
                        .ConfigureAwait(false);
                    var areas = await HiatmeAiClient.GetOutOfAreaAsync(ai, CancellationToken.None)
                        .ConfigureAwait(false);
                    SupeyOutOfArea.SetCachedAreas(areas);
                    SupeyOutOfArea.TrySaveLocalFallback(areas);
                }
                else
                    SupeyOutOfArea.SetCachedAreas(SupeyOutOfArea.LoadLocalFallback());
            }
            catch
            {
                SupeyOutOfArea.SetCachedAreas(SupeyOutOfArea.LoadLocalFallback());
            }
        }

        private static void EnsureScheduleBuilderRulesLoaded()
        {
            ScheduleBuilderBannedClients.ReloadCache();
            if (SupeyOutOfArea.CachedAreas == null || SupeyOutOfArea.CachedAreas.Count == 0)
                SupeyOutOfArea.SetCachedAreas(SupeyOutOfArea.LoadLocalFallback());
        }

        private void SplitReserveBuckets()
        {
            EnsureScheduleBuilderRulesLoaded();

            var reroute = new List<MCDownloadedTrip>();
            var willCalls = new List<MCDownloadedTrip>();
            var reserves = new List<MCDownloadedTrip>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void AddToBucket(MCDownloadedTrip trip)
            {
                if (trip == null) return;
                string tn = (trip.TripNumber ?? "").Trim();
                if (tn.Length > 0)
                {
                    if (!seen.Add(tn)) return;
                }
                switch (ScheduleBuilderReserveBuckets.Classify(trip))
                {
                    case ScheduleBuilderReserveBuckets.ReserveBucket.Banned:
                    case ScheduleBuilderReserveBuckets.ReserveBucket.Reroute:
                        reroute.Add(trip);
                        break;
                    case ScheduleBuilderReserveBuckets.ReserveBucket.WillCall:
                        willCalls.Add(trip);
                        break;
                    default:
                        reserves.Add(trip);
                        break;
                }
            }

            foreach (var t in _bannedPulledFromDrivers)
                AddToBucket(t);
            foreach (var t in _willCallPulledFromDrivers)
                AddToBucket(t);

            if (MCTripList != null)
            {
                foreach (var dled in MCTripList)
                {
                    if (dled == null) continue;
                    string tn = (dled.TripNumber ?? "").Trim();
                    if (tn.Length > 0 && seen.Contains(tn)) continue;

                    // On a driver tab: no-go may stay; banned + 00:00 PU will-calls always go to Reserves.
                    if (TripStillOnDriverAssignment(dled))
                    {
                        if (ScheduleBuilderBannedClients.IsBanned(dled)
                            || ScheduleBuilderReserveBuckets.Classify(dled)
                                == ScheduleBuilderReserveBuckets.ReserveBucket.WillCall)
                        {
                            RemoveTripFromFound(dled);
                            AddToBucket(dled);
                        }
                        continue;
                    }

                    AddToBucket(dled);
                }
            }

            FinalizeWillCallReserves(willCalls, reserves, seen);

            if (MCTripList != null)
            {
                ScheduleBuilderReserveBuckets.CountWillCallsInDownload(
                    MCTripList,
                    out int wcTotal,
                    out int wcPu,
                    out int wcCmt);
                WillCallsInDownloadCount = wcTotal;
                WillCallsPuMidnightInDownloadCount = wcPu;
                WillCallsCommentInDownloadCount = wcCmt;
            }
            else
                WillCallsInDownloadCount = WillCallsPuMidnightInDownloadCount = WillCallsCommentInDownloadCount = 0;

            PreviewReservesReroute = reroute;
            PreviewReservesBanned = new List<MCDownloadedTrip>();
            PreviewReservesWillCalls = willCalls;
            PreviewReserves = reserves;
        }

        /// <summary>Sync reserve bucket lists after manual cut/insert on the Reserves preview tab.</summary>
        internal void ApplyPreviewReserveLines(IList<ScheduleBuilderPreviewLine> lines)
        {
            var reservers = new List<MCDownloadedTrip>();
            var reroutes = new List<MCDownloadedTrip>();
            var willCalls = new List<MCDownloadedTrip>();
            var legacyBanned = new List<MCDownloadedTrip>();

            if (lines != null)
            {
                foreach (var line in lines)
                {
                    if (line?.Kind != ScheduleBuilderPreviewLine.LineKind.Trip || line.Trip == null)
                        continue;
                    Color? band = line.ReserveBandColor;
                    if (band == ScheduleBuilderReserveBuckets.BannedBand
                        || band == ScheduleBuilderReserveBuckets.RerouteBand)
                        reroutes.Add(line.Trip);
                    else if (band == ScheduleBuilderReserveBuckets.WillCallBand)
                        willCalls.Add(line.Trip);
                    else
                        reservers.Add(line.Trip);
                }
            }

            ScheduleBuilderReserveBuckets.RebucketBannedTripsIntoReroutes(
                reservers, reroutes, willCalls, legacyBanned);

            PreviewReserves = reservers;
            PreviewReservesReroute = reroutes;
            PreviewReservesWillCalls = willCalls;
            PreviewReservesBanned = new List<MCDownloadedTrip>();
        }

        /// <summary>
        /// Every downloaded 00:00-PU trip (not banned / no-go) lands in Will calls and is off driver tabs.
        /// </summary>
        private void FinalizeWillCallReserves(
            List<MCDownloadedTrip> willCalls,
            List<MCDownloadedTrip> reserves,
            HashSet<string> seen)
        {
            if (MCTripList == null) return;

            foreach (var t in MCTripList)
            {
                if (t == null || !ScheduleBuilderReserveBuckets.IsWillCallTrip(t)) continue;
                if (ScheduleBuilderReserveBuckets.Classify(t) != ScheduleBuilderReserveBuckets.ReserveBucket.WillCall)
                    continue;

                RemoveTripFromFound(t);

                string tn = (t.TripNumber ?? "").Trim();
                reserves.RemoveAll(x =>
                    x != null && (string.IsNullOrEmpty(tn)
                        || string.Equals(x.TripNumber, tn, StringComparison.OrdinalIgnoreCase)));

                if (tn.Length > 0)
                {
                    if (seen.Contains(tn))
                    {
                        for (int i = 0; i < willCalls.Count; i++)
                        {
                            if (willCalls[i] != null
                                && string.Equals(willCalls[i].TripNumber, tn, StringComparison.OrdinalIgnoreCase))
                            {
                                willCalls[i] = t;
                                break;
                            }
                        }
                        continue;
                    }
                    seen.Add(tn);
                }

                willCalls.Add(t);
            }
        }

        /// <summary>00:00-PU trips must not remain on driver tabs after BUILD (Reserves → Will calls).</summary>
        private void RemoveWillCallsFromDriverPreview()
        {
            var dict = PreviewDriverLines as Dictionary<string, List<ScheduleBuilderPreviewLine>>;
            if (dict == null) return;
            foreach (var kv in dict.ToList())
            {
                var kept = new List<ScheduleBuilderPreviewLine>();
                foreach (var line in kv.Value ?? Enumerable.Empty<ScheduleBuilderPreviewLine>())
                {
                    if (line?.Kind == ScheduleBuilderPreviewLine.LineKind.Trip
                        && line.Trip != null
                        && ScheduleBuilderReserveBuckets.IsWillCallPickup(line.Trip))
                        continue;
                    kept.Add(line);
                }
                dict[kv.Key] = kept;
            }
        }

        private bool TripStillOnDriverAssignment(MCDownloadedTrip trip)
        {
            if (trip == null || TripsFound == null) return false;
            string tn = (trip.TripNumber ?? "").Trim();
            if (tn.Length == 0) return false;
            foreach (var t in TripsFound)
            {
                if (t != null && string.Equals(t.TripNumber, tn, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private void RemoveRuleBlockedTripsFromDriverAssignments(
            IDictionary<string, List<ScheduleBuilderPreviewLine>> previewByDriver,
            IDictionary<string, List<MCDownloadedTrip>> driverTrips)
        {
            EnsureScheduleBuilderRulesLoaded();
            _bannedPulledFromDrivers.Clear();
            _willCallPulledFromDrivers.Clear();
            if (previewByDriver == null || TripsFound == null) return;

            foreach (var kv in previewByDriver.ToList())
            {
                var keptLines = new List<ScheduleBuilderPreviewLine>();
                var keptTrips = new List<MCDownloadedTrip>();
                foreach (var line in kv.Value ?? Enumerable.Empty<ScheduleBuilderPreviewLine>())
                {
                    if (line?.Kind == ScheduleBuilderPreviewLine.LineKind.Trip && line.Trip != null)
                    {
                        if (ScheduleBuilderBannedClients.IsBanned(line.Trip))
                        {
                            _bannedPulledFromDrivers.Add(line.Trip);
                            RemoveTripFromFound(line.Trip);
                            continue;
                        }
                        if (ScheduleBuilderReserveBuckets.Classify(line.Trip)
                            == ScheduleBuilderReserveBuckets.ReserveBucket.WillCall)
                        {
                            _willCallPulledFromDrivers.Add(line.Trip);
                            RemoveTripFromFound(line.Trip);
                            continue;
                        }
                    }
                    keptLines.Add(line);
                    if (line?.Kind == ScheduleBuilderPreviewLine.LineKind.Trip && line.Trip != null)
                        keptTrips.Add(line.Trip);
                }
                previewByDriver[kv.Key] = keptLines;
                if (driverTrips != null)
                    driverTrips[kv.Key] = keptTrips;
            }
        }

        private void RemoveTripFromFound(MCDownloadedTrip trip)
        {
            if (trip == null || TripsFound == null) return;
            string tn = (trip.TripNumber ?? "").Trim();
            for (int i = TripsFound.Count - 1; i >= 0; i--)
            {
                var t = TripsFound[i];
                if (t == null) continue;
                if (ReferenceEquals(t, trip)
                    || (!string.IsNullOrEmpty(tn)
                        && string.Equals(t.TripNumber, tn, StringComparison.OrdinalIgnoreCase)))
                {
                    TripsFound.RemoveAt(i);
                    break;
                }
            }
        }
        private const string CsvColumnLegend =
            "Each trip row must have 14 values in order: A Trip#, B Date, C Client name, D PU street, E PU city, F PU phone, G PU time, H DO street, I DO city, J DO phone, K DO time, L Age, M Miles, N Comments.";

        private void LoadTemplateFiles()
        {
            var dayDir = Path.Combine(AppContext.BaseDirectory, NameOfDay);
            try
            {
                if (!Directory.Exists(dayDir))
                {
                    throw new ScheduleBuilderException(
                        "LoadTemplateFiles",
                        dayDir,
                        NameOfDay,
                        null,
                        0,
                        new DirectoryNotFoundException(
                            "No template folder was found for " + NameOfDay + ".\n\n" +
                            "Expected folder:\n" + dayDir + "\n\n" +
                            "On the Templates tab, add templates for this weekday (one CSV per driver tab), then run the schedule builder again."),
                        "—");
                }

                _driverTemplateSlots.Clear();
                _driverTemplateSlotOrder.Clear();
                driverTripList = null;
                var filePaths = Directory.GetFiles(dayDir, "*.csv");
                if (filePaths.Length == 0)
                {
                    throw new ScheduleBuilderException(
                        "LoadTemplateFiles",
                        dayDir,
                        NameOfDay,
                        null,
                        0,
                        new InvalidOperationException(
                            "The folder for " + NameOfDay + " exists but contains no .csv template files.\n\n" +
                            "Add driver templates on the Templates tab, or confirm you chose the correct weekday on the schedule builder."),
                        "—");
                }

                foreach (string s in filePaths)
                {
                    try
                    {
                        AddTemplateToList(s);
                    }
                    catch (ScheduleBuilderException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        throw new ScheduleBuilderException(
                            "LoadTemplateFiles",
                            s,
                            Path.GetFileNameWithoutExtension(s),
                            null,
                            0,
                            ex,
                            "While reading this template CSV");
                    }
                }

                if (_driverTemplateSlots.Count == 0)
                {
                    throw new ScheduleBuilderException(
                        "LoadTemplateFiles",
                        dayDir,
                        NameOfDay,
                        null,
                        0,
                        new InvalidOperationException(
                            "No driver template CSVs were loaded (only special files such as Reserves/Schedule/LGTC may have been present).\n\n" +
                            "Check that each driver has a .csv file in:\n" + dayDir),
                        "—");
                }

                CleanTempFolder();
                BuildTempCsvFiles();
            }
            catch (ScheduleBuilderException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new ScheduleBuilderException("LoadTemplateFiles", dayDir, NameOfDay, null, 0, ex, "—");
            }
        }
        private void BuildTempCsvFiles()
        {
            ClearLoadedReserveLayout();

            TripsFound = new List<MCDownloadedTrip>();
            if (_driverTemplateSlots == null || _driverTemplateSlots.Count == 0)
            {
                throw new ScheduleBuilderException("BuildTempCsvFiles", null, null, null, 0, new InvalidOperationException("No templates for chosen day. Create templates on the Templates tab first."), "—");
            }

            driverTripList = new Dictionary<string, List<MCDownloadedTrip>>(StringComparer.OrdinalIgnoreCase);
            var previewByDriver = new Dictionary<string, List<ScheduleBuilderPreviewLine>>(StringComparer.OrdinalIgnoreCase);
            var matchedLiveTripNumbers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                foreach (string driverName in _driverTemplateSlotOrder)
                {
                    if (!_driverTemplateSlots.TryGetValue(driverName, out var slots))
                        continue;

                    if (!PreserveMultiRowGaps)
                        slots = ScheduleBuilderTemplateSlots.CollapseConsecutiveGaps(slots);

                    List<ScheduleBuilderPreviewLine> previewLines;
                    try
                    {
                        previewLines = ScheduleBuilderTemplateSlots.BuildPreviewLines(
                            slots, MCTripList, matchedLiveTripNumbers, collapseGaps: !PreserveMultiRowGaps);
                    }
                    catch (ScheduleBuilderException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        throw new ScheduleBuilderException(
                            "BuildTempCsvFiles.MatchTemplateTrip",
                            null,
                            driverName,
                            null,
                            0,
                            ex,
                            "Matching template rows for driver " + driverName);
                    }

                    previewByDriver[driverName] = previewLines;

                    var confirmedtrips = new List<MCDownloadedTrip>();
                    foreach (var line in previewLines)
                    {
                        if (line?.Kind != ScheduleBuilderPreviewLine.LineKind.Trip || line.Trip == null)
                            continue;
                        confirmedtrips.Add(line.Trip);
                        TripsFound.Add(line.Trip);
                    }

                    driverTripList[driverName] = confirmedtrips;
                }

                RemoveRuleBlockedTripsFromDriverAssignments(previewByDriver, driverTripList);
                PreviewDriverLines = previewByDriver;
                TabOrder = ScheduleBuilderTabOrder.DefaultBuildTabOrder(_driverTemplateSlotOrder);
                SplitReserveBuckets();
                RemoveWillCallsFromDriverPreview();
                WriteAllPreviewCsvsFromBuilder();
            }
            catch (ScheduleBuilderException) { throw; }
            catch (Exception ex)
            {
                throw new ScheduleBuilderException("BuildTempCsvFiles", null, null, null, 0, ex);
            }
        }
        private void CleanTempFolder()
        {
            var dir = TemplateBuilder.GetTemplateTempDirectory();
            if (Directory.Exists(dir))
            {
                foreach (var filePath in Directory.GetFiles(dir))
                    File.Delete(filePath);
            }
            else
            {
                Directory.CreateDirectory(dir);
            }
        }
        public void SaveTripListToCSVFile(List<MCDownloadedTrip> triplist, string filename)
        {
            string fullPath = Path.Combine(TemplateBuilder.GetTemplateTempDirectory(), filename + ".csv");
            try
            {
                var csv = new StringBuilder();
                foreach (MCDownloadedTrip trip in triplist)
                {
                    var newLine = string.Format("\"{0}\",\"{1}\",\"{2}\",\"{3}\",\"{4}\",\"{5}\",\"{6}\",\"{7}\",\"{8}\",\"{9}\",\"{10}\",\"{11}\",\"{12}\",\"{13}\"", trip.TripNumber ?? "", trip.Date ?? "", trip.ClientFullName ?? "", trip.PUStreet ?? "", trip.PUCity ?? "", trip.PUTelephone ?? "", trip.PUTime ?? "", trip.DOStreet ?? "", trip.DOCITY ?? "", trip.DOTelephone ?? "", trip.DOTime ?? "", trip.Age ?? "", trip.Miles ?? "", trip.Comments ?? "");
                    csv.AppendLine(newLine);
                }
                File.WriteAllText(fullPath, csv.ToString());
            }
            catch (Exception ex)
            {
                throw new ScheduleBuilderException(
                    "SaveTripListToCSVFile",
                    fullPath,
                    filename,
                    null,
                    0,
                    new IOException("Could not write the CSV for this driver tab.\n\n" + ex.Message, ex),
                    "Output file: " + Path.GetFileName(fullPath));
            }
        }
        private void AddTemplateToList(string filename)
        {
            if (!filename.Contains("Schedule") && !filename.Contains("Reserves") && !filename.Contains("LGTC"))
            {

                if (File.Exists(filename))
                {
                    string actualfilename = Path.GetFileNameWithoutExtension(filename);
                    if (_driverTemplateSlots.ContainsKey(actualfilename))
                        return;
                    GetTripListFromCSVFile(filename, false);
                    var slots = SupeyTemplateCsvLoader.LoadSlotsFromFile(filename);
                    if (slots == null || slots.Count == 0)
                        return;
                    _driverTemplateSlots.Add(actualfilename, slots);
                    _driverTemplateSlotOrder.Add(actualfilename);
                }
                else
                {
                    //Console.WriteLine("No file found for: " + drivername);
                }
            }
        }
        private List<MCDownloadedTrip> GetTripListFromCSVFile(string filePath, bool checkdates)
        {
            var templatetriplist = new List<MCDownloadedTrip>();
            string[] rows;
            try
            {
                rows = File.ReadAllLines(filePath);
            }
            catch (Exception ex)
            {
                throw new ScheduleBuilderException(
                    "GetTripListFromCSVFile",
                    filePath,
                    Path.GetFileNameWithoutExtension(filePath),
                    null,
                    0,
                    new IOException("Could not read the template file.\n\n" + ex.Message, ex),
                    "Open the CSV outside the app to verify it is not locked or corrupt.");
            }

            if (rows == null || rows.Length == 0)
                return templatetriplist;

            bool anyNonBlankLine = false;
            foreach (var line in rows)
            {
                if (!string.IsNullOrWhiteSpace(line))
                {
                    anyNonBlankLine = true;
                    break;
                }
            }

            if (!anyNonBlankLine)
                return templatetriplist;

            IDictionary<string, string[]> keyValuePairs = new Dictionary<string, string[]>();
            Regex CSVParser = new Regex(",(?=(?:[^\"]*\"[^\"]*\")*(?![^\"]*\"))");
            int rowcounter = 0;

            for (int row = 0; row < rows.Length; row++)
            {
                int rowIndexOneBased = row + 1;
                string tripNumberForError = null;
                try
                {
                    if (string.IsNullOrWhiteSpace(rows[row]))
                        continue;

                    string[] rowValues = CSVParser.Split(rows[row]);
                    string firstCell = rowValues.Length > 0 ? rowValues[0].Replace("\"", string.Empty).Trim() : string.Empty;

                    if (rowValues.Length < 14)
                    {
                        // Blank Excel tabs export as CSV with one empty field or a single short line — not a real trip row.
                        if (string.IsNullOrEmpty(firstCell) && IsAllCellsWhitespace(rowValues))
                            continue;

                        string tab = Path.GetFileNameWithoutExtension(filePath) ?? "template";
                        throw new ScheduleBuilderException(
                            "GetTripListFromCSVFile",
                            filePath,
                            tab,
                            rowValues.Length > 0 ? rowValues[0].Replace("\"", string.Empty) : null,
                            rowIndexOneBased,
                            new InvalidOperationException(
                                "This line has " + rowValues.Length + " comma-separated value(s) but the schedule format needs 14.\n\n" +
                                CsvColumnLegend + "\n\n" +
                                "Fix line " + rowIndexOneBased + " in " + tab + ".csv (often a comma inside a field without quotes, or a line that is not a full trip)."),
                            "Found " + rowValues.Length + " column(s); need 14 (A through N).");
                    }

                    string[] rowForValidate = rowValues.Length == 14 ? rowValues : rowValues.Take(14).ToArray();
                    if (TripTemplateCsvValidator.IsLikelyHeaderRow(rowForValidate))
                        continue;

                    if (TripTemplateCsvValidator.IsTemplateGapRow(rowForValidate))
                        continue;

                    if (TripTemplateCsvValidator.IsPlaceholderTripNumber(rowValues[0]))
                        continue;

                    tripNumberForError = rowValues[0].Replace("\"", string.Empty);
                    string tabName = Path.GetFileNameWithoutExtension(filePath) ?? "template";
                    var cellIssues = TripTemplateCsvValidator.ValidateTripRow(rowForValidate);
                    if (cellIssues != null && cellIssues.Count > 0)
                    {
                        var firstIssue = cellIssues[0];
                        var detail = new StringBuilder();
                        foreach (var issue in cellIssues)
                            detail.AppendLine(issue.FormatForUser(tabName, rowIndexOneBased));
                        throw new ScheduleBuilderException(
                            "GetTripListFromCSVFile",
                            filePath,
                            tabName,
                            tripNumberForError,
                            rowIndexOneBased,
                            new InvalidOperationException(detail.ToString().TrimEnd()),
                            firstIssue.ColumnLetter + " — " + firstIssue.FieldLabel);
                    }

                    MCDownloadedTrip mCTrip = new MCDownloadedTrip();
                    mCTrip.TripNumber = rowValues[0].Replace("\"", string.Empty);
                    mCTrip.Date = rowValues[1].Replace("\"", string.Empty);
                    mCTrip.ClientFullName = rowValues[2].Replace("\"", string.Empty);
                    mCTrip.PUStreet = rowValues[3].Replace("\"", string.Empty);
                    mCTrip.PUCity = rowValues[4].Replace("\"", string.Empty);
                    mCTrip.PUTelephone = rowValues[5].Replace("\"", string.Empty);
                    mCTrip.PUTime = TripTemplateCsvValidator.NormalizeTimeField(rowValues[6]);
                    mCTrip.DOStreet = rowValues[7].Replace("\"", string.Empty);
                    mCTrip.DOCITY = rowValues[8].Replace("\"", string.Empty);
                    mCTrip.DOTelephone = rowValues[9].Replace("\"", string.Empty);
                    mCTrip.DOTime = TripTemplateCsvValidator.NormalizeTimeField(rowValues[10]);
                    mCTrip.Age = rowValues[11].Replace("\"", string.Empty);
                    mCTrip.Miles = rowValues[12].Replace("\"", string.Empty);
                    mCTrip.Comments = rowValues[13].Replace("\"", string.Empty);

                    templatetriplist.Add(mCTrip);
                    keyValuePairs.Add("row" + rowcounter.ToString(), rowValues);
                    rowcounter++;
                }
                catch (ScheduleBuilderException) { throw; }
                catch (Exception ex)
                {
                    throw new ScheduleBuilderException(
                        "GetTripListFromCSVFile",
                        filePath,
                        Path.GetFileNameWithoutExtension(filePath),
                        tripNumberForError,
                        rowIndexOneBased,
                        ex,
                        "Parsing trip columns A–N from this line");
                }
            }

            return templatetriplist;
        }

        private static bool IsAllCellsWhitespace(string[] rowValues)
        {
            if (rowValues == null || rowValues.Length == 0)
                return true;
            foreach (var c in rowValues)
            {
                if (!string.IsNullOrWhiteSpace((c ?? "").Replace("\"", string.Empty)))
                    return false;
            }

            return true;
        }



        /// <summary>True when Excel COM automation can run on this PC (Office installed, bitness matches).</summary>
        internal static bool IsExcelAvailable()
        {
            try
            {
                return Type.GetTypeFromProgID("Excel.Application") != null;
            }
            catch
            {
                return false;
            }
        }

        public Task CreateWorkbookAsync() => CreateWorkbookAsync(promptForLocation: true);

        /// <param name="promptForLocation">False for SAVE SCHEDULE — uses service date and skips the file dialog.</param>
        public async Task CreateWorkbookAsync(bool promptForLocation)
        {
            await CreateWorkbookAsync(promptForLocation, openAfterSave: promptForLocation).ConfigureAwait(false);
        }

        internal async Task CreateWorkbookAsync(bool promptForLocation, bool openAfterSave)
        {
            ClearLastExport();

            var tempDir = TemplateBuilder.GetTemplateTempDirectory();
            if (!Directory.Exists(tempDir))
            {
                NotifyHideLoadingScreen();
                throw new ScheduleBuilderException(
                    "CreateWorkbook",
                    tempDir,
                    null,
                    null,
                    0,
                    new DirectoryNotFoundException(
                        "The working folder for schedule CSV files does not exist:\n" + tempDir + "\n\n" +
                        "Run the schedule builder again from the start."),
                    "—");
            }

            var fileList = Directory.EnumerateFiles(tempDir)
                .OrderBy(
                    f => Path.GetFileNameWithoutExtension(f) ?? f,
                    Comparer<string>.Create((a, b) =>
                        TabOrder != null && TabOrder.Count > 0
                            ? ScheduleBuilderTabOrder.CompareByTabOrder(TabOrder, a, b)
                            : ScheduleBuilderPreviewCsvExport.CompareWorkbookTabNames(a, b)))
                .ToList();
            if (fileList.Count == 0)
            {
                NotifyHideLoadingScreen();
                throw new ScheduleBuilderException(
                    "CreateWorkbook",
                    tempDir,
                    null,
                    null,
                    0,
                    new InvalidOperationException(
                        "There are no CSV files in the working folder to put into the new workbook.\n\n" +
                        "Try building the schedule again; if this keeps happening, check that template matching produced files in:\n" + tempDir),
                    "—");
            }

            ScheduleExportPaths.GetDefaultWorkbookSaveLocation(
                NameOfMonth, Day, Year, out string yearFolder, out string fileName, out string defaultFullPath);

            string path;
            if (promptForLocation)
            {
                await AsyncUpdateLoadingScreen("Choose a location to save schedule");
                using (var saveDlg = new SaveFileDialog())
                {
                    saveDlg.InitialDirectory = yearFolder;
                    saveDlg.Filter = "Excel files (*.xlsx)|*.xlsx";
                    saveDlg.FilterIndex = 0;
                    saveDlg.RestoreDirectory = false;
                    saveDlg.Title = "Export Excel File To";
                    saveDlg.FileName = fileName;
                    if (saveDlg.ShowDialog() != DialogResult.OK)
                    {
                        await AsyncUpdateLoadingScreen("Cancelling process..");
                        NotifyHideLoadingScreen();
                        return;
                    }
                    path = saveDlg.FileName;
                }
            }
            else
            {
                await AsyncUpdateLoadingScreen("Saving schedule…");
                path = !string.IsNullOrWhiteSpace(PreferredExportPath)
                    ? PreferredExportPath
                    : defaultFullPath;
            }

            await AsyncUpdateLoadingScreen("Building workbook");

            var workbookTabs = BuildWorkbookTabsFromPreview();

            // Prefer our xlsx writer (preserves gap markers, group colors, column N metadata).
            // Excel COM CSV import drops leading empty columns and breaks blank-row round-trip.
            if (workbookTabs != null && workbookTabs.Count > 0)
            {
                await RunWorkbookWriteAsync(path,
                    () => ScheduleBuilderXlsxWriter.WriteWorkbookFromTabs(path, workbookTabs, WorkbookColumnWidths))
                    .ConfigureAwait(false);
                await FinishWorkbookExportAsync(path, openAfterSave).ConfigureAwait(false);
                return;
            }

            if (!IsExcelAvailable())
            {
                await RunWorkbookWriteAsync(path,
                    () => ScheduleBuilderXlsxWriter.WriteWorkbookFromCsvFiles(path, fileList))
                    .ConfigureAwait(false);
                await FinishWorkbookExportAsync(path, openAfterSave).ConfigureAwait(false);
                return;
            }

            object misValue = System.Reflection.Missing.Value;
            Microsoft.Office.Interop.Excel.Application xlApp = null;
            Microsoft.Office.Interop.Excel.Workbook newWorkbook = null;
            string currentFile = null;

            try
            {
                await AsyncUpdateLoadingScreen("Starting Excel");
                xlApp = new Microsoft.Office.Interop.Excel.Application { Visible = false };
                newWorkbook = xlApp.Workbooks.Add();

                var counter = 1;
                foreach (var file in fileList)
                {
                    string tabName = Path.GetFileNameWithoutExtension(file) ?? file;
                    currentFile = file;
                    Microsoft.Office.Interop.Excel.Workbook csvWorkbook = null;
                    Microsoft.Office.Interop.Excel.Worksheet worksheetCSV = null;
                    Microsoft.Office.Interop.Excel.Worksheet targetWorksheet = null;
                    try
                    {
                        csvWorkbook = xlApp.Workbooks.Open(file);
                        worksheetCSV = (Microsoft.Office.Interop.Excel.Worksheet)csvWorkbook.Worksheets[1];
                        targetWorksheet = (Microsoft.Office.Interop.Excel.Worksheet)newWorkbook.Worksheets[counter];
                        worksheetCSV.Copy(targetWorksheet);
                        counter++;
                    }
                    catch (Exception ex)
                    {
                        throw new ScheduleBuilderException(
                            "CreateWorkbook.ImportCsv",
                            file,
                            tabName,
                            null,
                            counter,
                            new InvalidOperationException(
                                "Excel could not import this driver CSV into the new workbook.\n\n" +
                                "Close the file if it is open elsewhere, check the CSV is not corrupt, then try again.\n\n" +
                                "Detail: " + ex.Message,
                                ex),
                            "Workbook sheet index " + counter + " (each template CSV becomes one sheet tab named like the CSV file)");
                    }
                    finally
                    {
                        if (worksheetCSV != null)
                        {
                            try
                            {
                                Marshal.ReleaseComObject(worksheetCSV);
                            }
                            catch
                            {
                                /* ignore */
                            }
                        }

                        if (csvWorkbook != null)
                        {
                            try
                            {
                                Marshal.ReleaseComObject(csvWorkbook);
                            }
                            catch
                            {
                                /* ignore */
                            }
                        }

                        if (targetWorksheet != null)
                        {
                            try
                            {
                                Marshal.ReleaseComObject(targetWorksheet);
                            }
                            catch
                            {
                                /* ignore */
                            }
                        }
                    }
                }

                currentFile = "(deleting empty sheets)";
                for (int i = xlApp.ActiveWorkbook.Worksheets.Count; i > 0; i--)
                {
                    Worksheet wkSheet = (Worksheet)xlApp.ActiveWorkbook.Worksheets[i];
                    if (wkSheet.Name == "Sheet1")
                    {
                        wkSheet.Delete();
                    }
                    Marshal.ReleaseComObject(wkSheet);
                }
                xlApp.DisplayAlerts = false;

                if (workbookTabs != null && workbookTabs.Count > 0)
                {
                    ScheduleBuilderExcelWorkbookColors.ApplyTabColors(newWorkbook, workbookTabs);
                    ScheduleBuilderExcelWorkbookColors.ApplyColumnWidthsFromTabs(newWorkbook, workbookTabs, WorkbookColumnWidths);
                    ScheduleBuilderExcelWorkbookColors.ApplyTripGridTimeColumnAlignment(newWorkbook);
                }
                else
                    ScheduleBuilderExcelWorkbookColors.AutoFitAllWorksheets(newWorkbook);

                currentFile = "(saving workbook)";
                newWorkbook.SaveAs(path, Microsoft.Office.Interop.Excel.XlFileFormat.xlWorkbookDefault, Type.Missing, Type.Missing, false, false, XlSaveAsAccessMode.xlNoChange, XlSaveConflictResolution.xlLocalSessionChanges, Type.Missing, Type.Missing);
                newWorkbook.Close(true, misValue, misValue);

                xlApp.Quit();
                Marshal.ReleaseComObject(newWorkbook);
                Marshal.ReleaseComObject(xlApp);

                await FinishWorkbookExportAsync(path, openAfterSave).ConfigureAwait(false);
            }
            catch (ScheduleBuilderException)
            {
                NotifyHideLoadingScreen();
                try { newWorkbook?.Close(false); xlApp?.Quit(); } catch { }
                throw;
            }
            catch (System.Runtime.InteropServices.COMException comEx)
                when (comEx.HResult == unchecked((int)0x80040154))
            {
                try { newWorkbook?.Close(false); xlApp?.Quit(); } catch { }
                if (workbookTabs != null && workbookTabs.Count > 0)
                {
                    await RunWorkbookWriteAsync(path,
                        () => ScheduleBuilderXlsxWriter.WriteWorkbookFromTabs(path, workbookTabs, WorkbookColumnWidths))
                        .ConfigureAwait(false);
                }
                else
                {
                    await RunWorkbookWriteAsync(path,
                        () => ScheduleBuilderXlsxWriter.WriteWorkbookFromCsvFiles(path, fileList))
                        .ConfigureAwait(false);
                }
                await FinishWorkbookExportAsync(path, openAfterSave).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                NotifyHideLoadingScreen();
                try
                {
                    newWorkbook?.Close(false);
                    xlApp?.Quit();
                }
                catch
                {
                    /* ignore */
                }

                string tabHint = null;
                try
                {
                    if (!string.IsNullOrEmpty(currentFile) && File.Exists(currentFile))
                        tabHint = Path.GetFileNameWithoutExtension(currentFile);
                }
                catch
                {
                    /* ignore */
                }

                Exception inner = ex;
                if (ex is System.Runtime.InteropServices.COMException comEx &&
                    comEx.HResult == unchecked((int)0x80040154))
                {
                    inner = new InvalidOperationException(
                        "Excel could not be started on this PC. The schedule was not saved.",
                        ex);
                }

                throw new ScheduleBuilderException(
                    "CreateWorkbook",
                    currentFile ?? "(unknown step)",
                    tabHint,
                    null,
                    0,
                    inner,
                    "Building or saving the final .xlsx from the CSV files in Template Temps");
            }
        }

        private static async Task RunWorkbookWriteAsync(string path, System.Action write)
        {
            // Catch inside the worker so IOException is never user-unhandled on the thread pool
            // (Visual Studio breaks on that before await observes the faulted task).
            Exception fault = await Task.Run(() =>
            {
                try
                {
                    write();
                    return (Exception)null;
                }
                catch (Exception ex)
                {
                    return ex;
                }
            }).ConfigureAwait(false);

            if (fault == null)
                return;

            fault = ScheduleBuilderXlsxWriter.UnwrapException(fault);
            if (!ScheduleBuilderXlsxWriter.IsFileLockError(fault))
                throw fault;

            throw new ScheduleBuilderException(
                "CreateWorkbook",
                path,
                null,
                null,
                0,
                ScheduleBuilderXlsxWriter.CreateFileInUseException(path, fault),
                "—");
        }

        private async Task FinishWorkbookExportAsync(string path, bool openAfterSave = true)
        {
            if (openAfterSave)
            {
                try
                {
                    System.Diagnostics.Process.Start(path);
                }
                catch
                {
                    /* ignore */
                }
            }

            LastExportPath = path;
            LastExportWasCsv = false;
            await AsyncUpdateLoadingScreen("Finalizing process..");
            NotifyHideLoadingScreen();
        }

        /// <summary>Set after a successful export (workbook path or CSV folder).</summary>
        internal string LastExportPath { get; private set; }

        internal bool LastExportWasCsv { get; private set; }



















    }
}
