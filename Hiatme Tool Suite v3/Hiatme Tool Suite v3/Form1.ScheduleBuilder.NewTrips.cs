using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    public partial class Form1
    {
        private Panel _fsNewTripsBar;
        private Label _fsNewTripsBarLine1;
        private Label _fsNewTripsBarLine2;
        private Label _fsNewTripsPrevBtn;
        private Label _fsNewTripsNextBtn;
        private ToolTip _fsNewTripsBarToolTip;
        private List<MCDownloadedTrip> _fsNewTripsNavList;
        private int _fsNewTripsNavIndex = -1;
        private SupeyToolbarIconButton _fsSyncNewTripsBtn;
        private bool _fsSyncNewTripsRunning;
        private ToolTip _fsSyncNewTripsTip;

        internal void WireFsSyncNewTripsButton(Panel host)
        {
            _fsSyncNewTripsBtn = new SupeyToolbarIconButton
            {
                Size = new Size(26, 26),
                Margin = new Padding(0, 0, 4, 0),
                Enabled = false,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
            };
            _fsSyncNewTripsBtn.SetIconFactory(ScheduleBuilderModivcareNewTripsIcon.Create);
            _fsSyncNewTripsBtn.Click += async (s, e) => await FsSyncNewTripsBtn_ClickAsync();

            _fsSyncNewTripsTip = SupeyToolTip.Create(autoPopDelay: 12000, initialDelay: 400);
            _fsSyncNewTripsTip.SetToolTip(_fsSyncNewTripsBtn,
                "Check Modivcare for new trips and add any missing ones to Reserves "
                + "for the selected service date.");

            host.Controls.Add(_fsSyncNewTripsBtn);
            _fsSyncNewTripsBtn.BringToFront();
            host.Resize += (s, e) => PositionFsDriverTabStripActionButtons();
            PositionFsDriverTabStripActionButtons();
        }

        private async Task FsSyncNewTripsBtn_ClickAsync()
        {
            if (_fsSyncNewTripsRunning || fsbuilder == null || !_fsHasPreview)
                return;

            DateTime serviceDate = fsbdatepicker?.Value.Date ?? DateTime.Today;
            _fsSyncNewTripsRunning = true;
            SetFsPreviewExportButtonsEnabled(_fsHasPreview);

            try
            {
                var result = await FsSyncNewModivcareTripsAsync(serviceDate).ConfigureAwait(true);
                SetScheduleBuilderStatus("Modivcare new-trip check." + result.StatusNote);

                if (_fsHasPreview && !string.IsNullOrWhiteSpace(_fsActiveDriverTab))
                    ShowFsTripsForTab(_fsActiveDriverTab, preserveScroll: true);

                if (SupeyModivcareNewTripsResultForm.Show(this, result)
                    && result.HasAddedTrips
                    && _fsLinesByTab != null
                    && _fsLinesByTab.ContainsKey("Reserves"))
                {
                    SelectFsDriverTab("Reserves");
                }
            }
            finally
            {
                _fsSyncNewTripsRunning = false;
                SetFsPreviewExportButtonsEnabled(_fsHasPreview);
            }
        }

        /// <summary>
        /// After a schedule load, pull the current Modivcare trip list for the service date and add any
        /// trips not on the schedule anywhere to the Reserves tab. Reuses the live Modivcare session
        /// (only re-logs in when the session is dead). Fails soft — the load continues without the check.
        /// </summary>
        private async Task<FsModivcareNewTripsSyncResult> FsSyncNewModivcareTripsAsync(DateTime serviceDate)
        {
            if (fsbuilder == null || _fsLinesByTab == null || _fsLinesByTab.Count == 0)
                return FsModivcareNewTripsSyncResult.EmptySchedule(serviceDate);

            void ProbeStatus(string text)
            {
                SetScheduleBuilderStatus(text);
                UpdateTabLoadingOverlayMessage(tabPage6, text);
            }

            ProbeStatus("Checking Modivcare for new trips…");

            bool mcReady;
            try
            {
                mcReady = await EnsureModivcareSessionAsync().ConfigureAwait(true);
            }
            catch
            {
                mcReady = false;
            }
            if (!mcReady)
            {
                return FsModivcareNewTripsSyncResult.Skipped(
                    serviceDate,
                    FsModivcareNewTripsSyncFailure.ModivcareUnavailable,
                    " New-trip check skipped — Modivcare not available.");
            }

            List<MCDownloadedTrip> downloaded;
            try
            {
                ProbeStatus("Downloading Modivcare trip list…");
                downloaded = await new MCTripDownloader()
                    .DownloadTripRecords(serviceDate, mcLoginHandler)
                    .ConfigureAwait(true);
            }
            catch
            {
                return FsModivcareNewTripsSyncResult.Skipped(
                    serviceDate,
                    FsModivcareNewTripsSyncFailure.DownloadFailed,
                    " New-trip check skipped — Modivcare download failed.");
            }

            if (downloaded == null || downloaded.Count == 0)
            {
                return FsModivcareNewTripsSyncResult.Skipped(
                    serviceDate,
                    FsModivcareNewTripsSyncFailure.NoModivcareTrips,
                    " New-trip check — Modivcare returned no trips for the date.");
            }

            ProbeStatus("Comparing Modivcare trips against the schedule…");

            var scheduleTrips = FsCollectAllTripsOnSchedule();
            var knownTripNumbers = FsCollectKnownScheduleTripNumbers(scheduleTrips);
            var knownDetailKeys = FsCollectKnownScheduleDetailKeys(scheduleTrips);
            var reroutedTripNumbers = FsCollectKnownReroutedTripNumbers(serviceDate);

            var debugLog = new StringBuilder();
            debugLog.AppendLine(DateTime.Now.ToString("s") + "  service " + serviceDate.ToString("yyyy-MM-dd"));
            debugLog.AppendLine("  schedule trips=" + scheduleTrips.Count
                + "  trip# keys=" + knownTripNumbers.Count
                + "  detail keys=" + knownDetailKeys.Count
                + "  reroute keys=" + reroutedTripNumbers.Count);

            var newTrips = new List<MCDownloadedTrip>();
            int skippedOnSchedule = 0;
            int skippedRerouted = 0;
            foreach (var trip in downloaded)
            {
                if (trip == null)
                    continue;

                trip.TripNumber = ScheduleBuilderModivcareTripMatch.NormalizeTripNumber(trip.TripNumber);
                string legKey = ScheduleBuilderReroutedTrips.TripNumberKey(trip.TripNumber);

                string skipReason = FsClassifyDownloadedTripSkipReason(
                    trip,
                    scheduleTrips,
                    knownTripNumbers,
                    knownDetailKeys,
                    reroutedTripNumbers);

                debugLog.AppendLine("  MC " + (trip.TripNumber ?? "")
                    + (legKey.Length > 0 ? " key=" + legKey : "")
                    + (string.IsNullOrWhiteSpace(skipReason) ? "  -> NEW" : "  -> skip " + skipReason));

                if (skipReason == "on-schedule")
                {
                    skippedOnSchedule++;
                    continue;
                }

                if (skipReason == "rerouted")
                {
                    skippedRerouted++;
                    continue;
                }

                newTrips.Add(trip);
            }

            FsWriteNewTripsDebugLog(debugLog.ToString());

            string skipNote = BuildNewTripsSkipNote(skippedOnSchedule, skippedRerouted);
            int modivcareCount = downloaded.Count;

            if (newTrips.Count == 0)
            {
                FsHideNewTripsBar();
                return FsModivcareNewTripsSyncResult.Completed(
                    serviceDate,
                    " No new Modivcare trips" + skipNote + ".",
                    modivcareCount,
                    skippedOnSchedule,
                    skippedRerouted,
                    Array.Empty<FsModivcareNewTripsAddedEntry>());
            }

            ProbeStatus("Adding " + newTrips.Count + " new trip"
                + (newTrips.Count == 1 ? "" : "s") + " to Reserves…");

            if (!_fsLinesByTab.TryGetValue("Reserves", out var reserveLines) || reserveLines == null)
                reserveLines = new List<ScheduleBuilderPreviewLine>();

            var actuallyAdded = new List<MCDownloadedTrip>();
            var addedEntries = new List<FsModivcareNewTripsAddedEntry>();
            foreach (var trip in newTrips)
            {
                if (FsReserveLinesContainTrip(reserveLines, trip))
                    continue;
                if (ScheduleBuilderReroutedTrips.IsInReservesRerouteBucket(
                        fsbuilder?.PreviewReservesReroute, trip))
                    continue;

                var bucket = ScheduleBuilderReserveBuckets.InsertNewDownloadTripIntoReserveLines(
                    reserveLines, trip);
                FsAddNewTripToReserveBucket(trip, bucket);
                if (fsbuilder.MCTripList != null && !fsbuilder.MCTripList.Contains(trip))
                    fsbuilder.MCTripList.Add(trip);
                actuallyAdded.Add(trip);
                addedEntries.Add(new FsModivcareNewTripsAddedEntry
                {
                    Trip = trip,
                    Bucket = bucket,
                });
            }

            if (actuallyAdded.Count == 0)
            {
                FsHideNewTripsBar();
                return FsModivcareNewTripsSyncResult.Completed(
                    serviceDate,
                    " No new Modivcare trips" + skipNote + ".",
                    modivcareCount,
                    skippedOnSchedule,
                    skippedRerouted,
                    Array.Empty<FsModivcareNewTripsAddedEntry>());
            }

            FsCommitReservePreviewLinesDirect(reserveLines);
            FsReapplyReroutedHighlights();
            FsReapplyWellRydeCancelledHighlights();
            FsShowNewTripsBar(actuallyAdded);

            return FsModivcareNewTripsSyncResult.Completed(
                serviceDate,
                " New trips — " + actuallyAdded.Count + " added to Reserves from Modivcare"
                    + skipNote + ".",
                modivcareCount,
                skippedOnSchedule,
                skippedRerouted,
                addedEntries);
        }

        private static string BuildNewTripsSkipNote(int skippedOnSchedule, int skippedRerouted)
        {
            if (skippedOnSchedule <= 0 && skippedRerouted <= 0)
                return "";

            var parts = new List<string>();
            if (skippedOnSchedule > 0)
                parts.Add(skippedOnSchedule + " already on schedule");
            if (skippedRerouted > 0)
                parts.Add(skippedRerouted + " already rerouted");
            return " (" + string.Join(", ", parts) + " skipped)";
        }

        /// <summary>Empty = not skipped; otherwise short reason for debug log.</summary>
        private string FsClassifyDownloadedTripSkipReason(
            MCDownloadedTrip downloaded,
            IList<MCDownloadedTrip> scheduleTrips,
            ISet<string> knownTripNumbers,
            ISet<string> knownDetailKeys,
            ISet<string> reroutedTripNumbers)
        {
            if (downloaded == null)
                return "empty";

            string legKey = ScheduleBuilderReroutedTrips.TripNumberKey(downloaded.TripNumber);
            if (legKey.Length > 0 && knownTripNumbers.Contains(legKey))
                return "on-schedule";

            if (fsbuilder != null && fsbuilder.TripExistsInPreview(downloaded.TripNumber))
                return "on-schedule";

            if (ScheduleBuilderReroutedTrips.IsInReservesRerouteBucket(
                    fsbuilder?.PreviewReservesReroute, downloaded))
                return "rerouted";

            string detailKey = ScheduleBuilderModivcareTripMatch.DetailKey(downloaded);
            if (detailKey.Length > 0 && knownDetailKeys.Contains(detailKey))
                return "on-schedule";

            foreach (var existing in scheduleTrips)
            {
                if (ScheduleBuilderPreviewDrag.TripEquals(existing, downloaded))
                    return "on-schedule";
                if (ScheduleBuilderModivcareTripMatch.TripDetailsMatch(existing, downloaded))
                    return "on-schedule";
            }

            if (legKey.Length > 0 && reroutedTripNumbers.Contains(legKey))
                return "rerouted";

            if (ScheduleBuilderReroutedTrips.TripNumberKeySetContains(reroutedTripNumbers, downloaded.TripNumber))
                return "rerouted";

            if (_fsReroutedTripKeys != null
                && ScheduleBuilderReroutedTrips.TripNumberKeySetContains(_fsReroutedTripKeys, downloaded.TripNumber))
                return "rerouted";

            return "";
        }

        private static void FsWriteNewTripsDebugLog(string text)
        {
            try
            {
                string dir = Path.Combine(AppContext.BaseDirectory ?? "", "hiatme_config");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "new_trips_check.log"), text + Environment.NewLine);
            }
            catch
            {
                // diagnostic only
            }
        }

        /// <summary>Every trip object currently on the schedule (lines + buckets + loaded trip lists).</summary>
        private List<MCDownloadedTrip> FsCollectAllTripsOnSchedule()
        {
            var trips = new List<MCDownloadedTrip>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void Add(MCDownloadedTrip trip)
            {
                if (trip == null)
                    return;
                string key = ScheduleBuilderReroutedTrips.TripNumberKey(trip.TripNumber);
                if (key.Length == 0 || !seen.Add(key))
                    return;
                trips.Add(trip);
            }

            foreach (var kv in _fsLinesByTab)
            {
                var lines = kv.Value;
                if (lines == null)
                    continue;
                foreach (var line in lines)
                {
                    if (line?.Kind == ScheduleBuilderPreviewLine.LineKind.Trip)
                        Add(line.Trip);
                }
            }

            var buckets = new[]
            {
                fsbuilder?.PreviewReserves,
                fsbuilder?.PreviewReservesReroute,
                fsbuilder?.PreviewReservesWillCalls,
                fsbuilder?.PreviewReservesCancel,
                fsbuilder?.PreviewReservesBanned,
            };
            foreach (var bucket in buckets)
            {
                if (bucket == null)
                    continue;
                foreach (var trip in bucket)
                    Add(trip);
            }

            foreach (var trip in fsbuilder?.MCTripList ?? Enumerable.Empty<MCDownloadedTrip>())
                Add(trip);
            foreach (var trip in fsbuilder?.TripsFound ?? Enumerable.Empty<MCDownloadedTrip>())
                Add(trip);

            return trips;
        }

        private static HashSet<string> FsCollectKnownScheduleTripNumbers(IList<MCDownloadedTrip> scheduleTrips)
        {
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (scheduleTrips == null)
                return keys;
            foreach (var trip in scheduleTrips)
            {
                string key = ScheduleBuilderReroutedTrips.TripNumberKey(trip?.TripNumber);
                if (key.Length > 0)
                    keys.Add(key);
            }
            return keys;
        }

        private static HashSet<string> FsCollectKnownScheduleDetailKeys(IList<MCDownloadedTrip> scheduleTrips)
        {
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (scheduleTrips == null)
                return keys;
            foreach (var trip in scheduleTrips)
            {
                string key = ScheduleBuilderModivcareTripMatch.DetailKey(trip);
                if (key.Length > 0)
                    keys.Add(key);
            }
            return keys;
        }

        /// <summary>Normalized trip numbers we already treat as rerouted (do not re-add from MC download).</summary>
        private HashSet<string> FsCollectKnownReroutedTripNumbers(DateTime serviceDate)
        {
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void Track(string tripNumber)
            {
                ScheduleBuilderReroutedTrips.AddTripNumberKey(keys, tripNumber);
            }

            if (_fsReroutedTripKeys != null)
            {
                foreach (string raw in _fsReroutedTripKeys)
                    Track(raw);
            }

            foreach (var record in ScheduleBuilderReroutedTripsRegistry.LoadLocal(serviceDate))
            {
                if (record == null)
                    continue;
                Track(record.TripNumber);
                char leg = record.ParseRecordLegChar();
                if (leg != '\0')
                    Track(ScheduleBuilderPreviewDrag.ApplyLegSuffix(record.TripNumber, leg));
            }

            foreach (var trip in fsbuilder?.PreviewReservesReroute ?? Enumerable.Empty<MCDownloadedTrip>())
                Track(trip?.TripNumber);

            if (_fsLinesByTab != null
                && _fsLinesByTab.TryGetValue("Reserves", out var reserveLines)
                && reserveLines != null)
            {
                bool inReroutes = false;
                foreach (var line in reserveLines)
                {
                    if (line?.Kind == ScheduleBuilderPreviewLine.LineKind.SectionHeader)
                    {
                        inReroutes = ScheduleBuilderReserveBuckets.TryParseSectionBucket(
                            line.SectionTitle, out var bucket)
                            && bucket == ScheduleBuilderReserveBuckets.ReserveBucket.Reroute;
                        continue;
                    }

                    if (line?.Kind != ScheduleBuilderPreviewLine.LineKind.Trip || line.Trip == null)
                        continue;

                    if (line.ReroutedOnModivcare || inReroutes)
                        Track(line.Trip.TripNumber);
                }
            }

            return keys;
        }

        private static bool FsReserveLinesContainTrip(
            IList<ScheduleBuilderPreviewLine> reserveLines,
            MCDownloadedTrip trip)
        {
            if (reserveLines == null || trip == null)
                return false;

            foreach (var line in reserveLines)
            {
                if (line?.Kind != ScheduleBuilderPreviewLine.LineKind.Trip || line.Trip == null)
                    continue;
                if (ScheduleBuilderPreviewDrag.TripEquals(line.Trip, trip))
                    return true;
            }

            return false;
        }

        /// <summary>Add a new download trip to the matching builder bucket list (no rebuild, no re-parse).</summary>
        private void FsAddNewTripToReserveBucket(
            MCDownloadedTrip trip,
            ScheduleBuilderReserveBuckets.ReserveBucket bucket)
        {
            if (fsbuilder == null || trip == null)
                return;

            List<MCDownloadedTrip> target;
            switch (bucket)
            {
                case ScheduleBuilderReserveBuckets.ReserveBucket.WillCall:
                    target = fsbuilder.PreviewReservesWillCalls;
                    break;
                case ScheduleBuilderReserveBuckets.ReserveBucket.Reroute:
                case ScheduleBuilderReserveBuckets.ReserveBucket.Banned:
                    target = fsbuilder.PreviewReservesReroute;
                    break;
                case ScheduleBuilderReserveBuckets.ReserveBucket.Cancel:
                    target = fsbuilder.PreviewReservesCancel;
                    break;
                default:
                    target = fsbuilder.PreviewReserves;
                    break;
            }

            if (target == null)
                return;

            foreach (var existing in target)
            {
                if (ScheduleBuilderPreviewDrag.TripEquals(existing, trip))
                    return;
            }

            target.Add(trip);
        }

        private void BuildFsNewTripsBar(Panel host)
        {
            _fsNewTripsBar = new Panel
            {
                Dock = DockStyle.Top,
                Height = 0,
                Visible = false,
                BackColor = SupeyTheme.SurfaceElevated,
                Padding = new Padding(0),
                Cursor = Cursors.Hand,
            };

            var accent = new Panel
            {
                Dock = DockStyle.Left,
                Width = 3,
                BackColor = ScheduleBuilderReserveBuckets.ReserversBand,
            };

            var closeLabel = new Label
            {
                Dock = DockStyle.Right,
                Width = 30,
                Text = "✕",
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 10f),
                ForeColor = SupeyTheme.TextSecondary,
                BackColor = SupeyTheme.SurfaceElevated,
                Cursor = Cursors.Hand,
            };
            closeLabel.Click += (s, e) => FsHideNewTripsBar();

            _fsNewTripsBarToolTip = SupeyToolTip.Create();

            Label MakeNavArrow(string glyph, string tip)
            {
                var arrow = new Label
                {
                    Dock = DockStyle.Right,
                    Width = 28,
                    Text = glyph,
                    TextAlign = ContentAlignment.MiddleCenter,
                    Font = new Font("Segoe UI", 12f, FontStyle.Bold),
                    ForeColor = SupeyTheme.TextSecondary,
                    BackColor = SupeyTheme.SurfaceElevated,
                    Cursor = Cursors.Hand,
                };
                arrow.MouseEnter += (s, e) => arrow.ForeColor = SupeyTheme.TextPrimary;
                arrow.MouseLeave += (s, e) => arrow.ForeColor = SupeyTheme.TextSecondary;
                _fsNewTripsBarToolTip.SetToolTip(arrow, tip);
                return arrow;
            }

            _fsNewTripsPrevBtn = MakeNavArrow("‹", "Previous new trip");
            _fsNewTripsPrevBtn.Click += (s, e) => FsStepThroughNewTrips(-1);

            _fsNewTripsNextBtn = MakeNavArrow("›", "Next new trip");
            _fsNewTripsNextBtn.Click += (s, e) => FsStepThroughNewTrips(1);

            var textHost = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = SupeyTheme.SurfaceElevated,
                Padding = new Padding(10, 6, 10, 6),
                Cursor = Cursors.Hand,
            };

            _fsNewTripsBarLine1 = new Label
            {
                Dock = DockStyle.Top,
                Height = 20,
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI Semibold", 9.75f),
                ForeColor = SupeyTheme.TextPrimary,
                BackColor = SupeyTheme.SurfaceElevated,
                Cursor = Cursors.Hand,
            };

            _fsNewTripsBarLine2 = new Label
            {
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 9f),
                ForeColor = SupeyTheme.TextSecondary,
                BackColor = SupeyTheme.SurfaceElevated,
                Cursor = Cursors.Hand,
            };

            void GoToReserves(object s, EventArgs e)
            {
                if (_fsHasPreview && _fsLinesByTab.ContainsKey("Reserves"))
                    SelectFsDriverTab("Reserves");
            }
            _fsNewTripsBar.Click += GoToReserves;
            textHost.Click += GoToReserves;
            _fsNewTripsBarLine1.Click += GoToReserves;
            _fsNewTripsBarLine2.Click += GoToReserves;

            textHost.Controls.Add(_fsNewTripsBarLine2);
            textHost.Controls.Add(_fsNewTripsBarLine1);

            _fsNewTripsBar.Controls.Add(textHost);
            _fsNewTripsBar.Controls.Add(_fsNewTripsPrevBtn);
            _fsNewTripsBar.Controls.Add(_fsNewTripsNextBtn);
            _fsNewTripsBar.Controls.Add(closeLabel);
            _fsNewTripsBar.Controls.Add(accent);

            var bottomRule = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 1,
                BackColor = SupeyTheme.Divider,
            };
            _fsNewTripsBar.Controls.Add(bottomRule);

            host.Controls.Add(_fsNewTripsBar);
            SupeyDarkScrollBars.Apply(_fsNewTripsBar);
        }

        private void FsShowNewTripsBar(IList<MCDownloadedTrip> newTrips)
        {
            if (_fsNewTripsBar == null)
                return;

            if (newTrips == null || newTrips.Count == 0)
            {
                FsHideNewTripsBar();
                return;
            }

            _fsNewTripsNavList = new List<MCDownloadedTrip>(newTrips);
            _fsNewTripsNavIndex = -1;

            _fsNewTripsBarLine1.Text = "NEW  ·  " + newTrips.Count + " trip"
                + (newTrips.Count == 1 ? "" : "s")
                + " from Modivcare added to Reserves — use ‹ › to step through each one";
            _fsNewTripsBarLine2.Text = FsFormatNewTripsSummary(newTrips);
            _fsNewTripsBar.Visible = true;
            _fsNewTripsBar.Height = FsCutTripBarHeight;
        }

        private void FsHideNewTripsBar()
        {
            if (_fsNewTripsBar == null)
                return;
            _fsNewTripsBar.Visible = false;
            _fsNewTripsBar.Height = 0;
            _fsNewTripsBarLine1.Text = string.Empty;
            _fsNewTripsBarLine2.Text = string.Empty;
            _fsNewTripsNavList = null;
            _fsNewTripsNavIndex = -1;
        }

        private void FsStepThroughNewTrips(int delta)
        {
            var trips = _fsNewTripsNavList;
            if (trips == null || trips.Count == 0)
                return;

            if (_fsNewTripsNavIndex < 0)
                _fsNewTripsNavIndex = delta >= 0 ? 0 : trips.Count - 1;
            else
                _fsNewTripsNavIndex = (_fsNewTripsNavIndex + delta + trips.Count) % trips.Count;

            var trip = trips[_fsNewTripsNavIndex];
            if (trip == null)
                return;

            if (!string.Equals(_fsActiveDriverTab, "Reserves", StringComparison.OrdinalIgnoreCase)
                && _fsLinesByTab != null
                && _fsLinesByTab.ContainsKey("Reserves"))
            {
                SelectFsDriverTab("Reserves");
            }

            SelectFsTripInListView(trip);

            _fsNewTripsBarLine2.Text = "Trip " + (_fsNewTripsNavIndex + 1) + " of " + trips.Count
                + "  ·  " + FsFormatNewTripEntry(trip);
        }

        private static string FsFormatNewTripsSummary(IList<MCDownloadedTrip> trips)
        {
            var parts = new List<string>();
            const int maxListed = 4;

            for (int i = 0; i < trips.Count && parts.Count < maxListed; i++)
            {
                var trip = trips[i];
                if (trip == null)
                    continue;
                parts.Add(FsFormatNewTripEntry(trip));
            }

            int more = trips.Count - parts.Count;
            string text = string.Join("  ·  ", parts);
            if (more > 0)
                text += "  ·  +" + more + " more";
            return text;
        }

        private static string FsFormatNewTripEntry(MCDownloadedTrip trip)
        {
            if (trip == null)
                return "";

            string num = (trip.TripNumber ?? "").Trim();
            string client = (trip.ClientFullName ?? "").Trim();
            if (client.Length == 0)
                client = ((trip.ClientFirstName ?? "") + " " + (trip.ClientLastName ?? "")).Trim();

            string entry = num.Length > 0 ? num : "(no trip #)";
            if (client.Length > 0)
                entry += " " + client;

            string pu = FormatTimeOnly(trip.PUTime);
            if (!string.IsNullOrWhiteSpace(pu))
                entry += " · PU " + pu.Trim();

            return entry;
        }
    }
}
