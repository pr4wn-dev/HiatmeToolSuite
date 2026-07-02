using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    public partial class Form1
    {
        private Panel _fsNewTripsBar;
        private Label _fsNewTripsBarLine1;
        private Label _fsNewTripsBarLine2;

        /// <summary>
        /// After a schedule load, pull the current Modivcare trip list for the service date and add any
        /// trips not on the schedule anywhere to the Reserves tab. Reuses the live Modivcare session
        /// (only re-logs in when the session is dead). Fails soft — the load continues without the check.
        /// </summary>
        private async Task<string> FsSyncNewModivcareTripsAsync(DateTime serviceDate)
        {
            if (fsbuilder == null || _fsLinesByTab == null || _fsLinesByTab.Count == 0)
                return "";

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
                return " New-trip check skipped — Modivcare not available.";

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
                return " New-trip check skipped — Modivcare download failed.";
            }

            if (downloaded == null || downloaded.Count == 0)
                return " New-trip check — Modivcare returned no trips for the date.";

            ProbeStatus("Comparing Modivcare trips against the schedule…");

            var onSchedule = FsCollectTripKeysOnSchedule();
            var newTrips = new List<MCDownloadedTrip>();
            foreach (var trip in downloaded)
            {
                string key = ScheduleBuilderReroutedTrips.TripNumberKey(trip?.TripNumber);
                if (key.Length == 0 || !onSchedule.Add(key))
                    continue;
                newTrips.Add(trip);
            }

            if (newTrips.Count == 0)
            {
                FsHideNewTripsBar();
                return " No new Modivcare trips.";
            }

            ProbeStatus("Adding " + newTrips.Count + " new trip"
                + (newTrips.Count == 1 ? "" : "s") + " to Reserves…");

            if (!_fsLinesByTab.TryGetValue("Reserves", out var reserveLines) || reserveLines == null)
                reserveLines = new List<ScheduleBuilderPreviewLine>();

            foreach (var trip in newTrips)
            {
                ScheduleBuilderReserveBuckets.InsertNewDownloadTripIntoReserveLines(reserveLines, trip);
                if (fsbuilder.MCTripList != null && !fsbuilder.MCTripList.Contains(trip))
                    fsbuilder.MCTripList.Add(trip);
            }

            FsCommitPreviewLinesForTab("Reserves", reserveLines);
            FsShowNewTripsBar(newTrips);

            return " New trips — " + newTrips.Count + " added to Reserves from Modivcare.";
        }

        /// <summary>Every trip number currently anywhere on the schedule (driver tabs + all Reserves sections + buckets).</summary>
        private HashSet<string> FsCollectTripKeysOnSchedule()
        {
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void Track(MCDownloadedTrip trip)
            {
                string key = ScheduleBuilderReroutedTrips.TripNumberKey(trip?.TripNumber);
                if (key.Length > 0)
                    keys.Add(key);
            }

            foreach (var kv in _fsLinesByTab)
            {
                var lines = kv.Value;
                if (lines == null)
                    continue;
                foreach (var line in lines)
                {
                    if (line?.Kind == ScheduleBuilderPreviewLine.LineKind.Trip)
                        Track(line.Trip);
                }
            }

            // Bucket lists too — banned/no-go trips can be off the preview lines.
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
                    Track(trip);
            }

            return keys;
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

            _fsNewTripsBarLine1.Text = "NEW  ·  " + newTrips.Count + " trip"
                + (newTrips.Count == 1 ? "" : "s")
                + " from Modivcare added to Reserves — click to view";
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

                parts.Add(entry);
            }

            int more = trips.Count - parts.Count;
            string text = string.Join("  ·  ", parts);
            if (more > 0)
                text += "  ·  +" + more + " more";
            return text;
        }
    }
}
