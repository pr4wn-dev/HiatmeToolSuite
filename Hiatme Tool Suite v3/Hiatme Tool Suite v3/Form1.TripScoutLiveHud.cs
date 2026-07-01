using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    partial class Form1
    {
        private const int TripScoutLivePollIntervalMs = 60_000;

        private System.Windows.Forms.Timer _tripScoutLivePollTimer;
        private bool _tripScoutLivePollInFlight;
        private string _tripScoutLiveServerHash;
        private string _tripScoutLiveBellHash;
        private string _tripScoutLivePollServiceDate;

        private bool TripScoutLivePanelEnabled => tsLivePanelSwitch != null && tsLivePanelSwitch.Checked;

        private void tsLivePanelSwitch_CheckedChanged(object sender, EventArgs e)
        {
            StyleTripScoutLiveToolbar(TripScoutLivePanelEnabled);
            TripScoutSyncLiveBellVisibility();
            LayoutTripScoutToolbarControls();
            if (tslv != null && !tslv.IsDisposed)
                tslv.PostPaintItems = null;

            if (TripScoutLivePanelEnabled)
                StartTripScoutLivePolling();
            else
                StopTripScoutLivePolling();
        }

        private void StartTripScoutLivePolling()
        {
            EnsureTripScoutLivePollTimer();
            _tripScoutLiveServerHash = null;
            _tripScoutLiveBellHash = null;
            _tripScoutLivePollServiceDate = TripScoutSelectedServiceDateIso();
            _tripScoutLivePollTimer.Start();
            _ = TripScoutLivePollServerAsync();
        }

        private void StopTripScoutLivePolling()
        {
            _tripScoutLivePollTimer?.Stop();
            _tripScoutLivePollInFlight = false;
        }

        private void EnsureTripScoutLivePollTimer()
        {
            if (_tripScoutLivePollTimer != null)
                return;

            _tripScoutLivePollTimer = new System.Windows.Forms.Timer { Interval = TripScoutLivePollIntervalMs };
            _tripScoutLivePollTimer.Tick += (_, __) =>
            {
                if (!TripScoutLivePanelEnabled || _tripScoutLivePollInFlight)
                    return;
                _ = TripScoutLivePollServerAsync();
            };
        }

        /// <summary>Call after a fresh WellRyde LOAD so the next server poll re-baselines.</summary>
        internal void TripScoutNotifyTripsLoadedFromWellRyde()
        {
            _tripScoutLiveServerHash = null;
            _tripScoutLiveBellHash = null;
            _tripScoutLivePollServiceDate = TripScoutSelectedServiceDateIso();
            TripScoutResetActivityState();
        }

        private string TripScoutSelectedServiceDateIso()
        {
            if (tsdatepicker == null || tsdatepicker.IsDisposed)
                return DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            return tsdatepicker.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        private HiatmeAiSettings TripScoutAiSettings()
        {
            if (_supeyAiSettings == null)
                _supeyAiSettings = HiatmeAiSettings.Load();
            return _supeyAiSettings;
        }

        private async Task TripScoutLivePollServerAsync()
        {
            if (!TripScoutLivePanelEnabled || _tripScoutLivePollInFlight)
                return;

            if (IsDisposed || tslv == null || tslv.IsDisposed)
                return;

            string serviceDate = TripScoutSelectedServiceDateIso();
            if (!string.Equals(_tripScoutLivePollServiceDate, serviceDate, StringComparison.Ordinal))
            {
                _tripScoutLiveServerHash = null;
                _tripScoutLivePollServiceDate = serviceDate;
            }

            _tripScoutLivePollInFlight = true;
            string checkedAt = DateTime.Now.ToString("h:mm:ss tt", CultureInfo.CurrentCulture);
            string checkingMsg = "Live panel: checking server for trip updates (" + serviceDate + ")…";

            try
            {
                if (InvokeRequired)
                    BeginInvoke(new Action(() => StartTripScoutStatusSpinner(checkingMsg)));
                else
                    StartTripScoutStatusSpinner(checkingMsg);

                var settings = TripScoutAiSettings();
                if (settings == null || string.IsNullOrWhiteSpace(settings.BaseUrl))
                {
                    FinishTripScoutLivePoll(
                        "Live panel: AI server not configured — set URL in AI Assistant (" + checkedAt + ").");
                    return;
                }

                if (_tripScoutAllTrips == null || _tripScoutAllTrips.Count == 0)
                {
                    FinishTripScoutLivePoll(
                        "Live panel: load trips first, then server updates will apply (" + checkedAt + ").");
                    return;
                }

                var status = await HiatmeAiClient.GetTripScoutServerStatusAsync(
                    settings, serviceDate, CancellationToken.None).ConfigureAwait(true);

                if (!status.Ok)
                {
                    FinishTripScoutLivePoll(
                        "Live panel: server check failed — " + (status.Error ?? "unknown error") + " (" + checkedAt + ").");
                    return;
                }

                if (!status.Available)
                {
                    FinishTripScoutLivePoll(
                        "Live panel: server has no WellRyde data for " + serviceDate + " yet (" + checkedAt + ").");
                    return;
                }

                string hash = status.ContentHash ?? "";
                if (!string.IsNullOrEmpty(_tripScoutLiveServerHash) &&
                    string.Equals(_tripScoutLiveServerHash, hash, StringComparison.Ordinal))
                {
                    await TripScoutRefreshActivityAfterLivePollAsync(settings, serviceDate)
                        .ConfigureAwait(true);
                    RefreshTripScoutListViewKeepingFilter();
                    TripScoutApplyRowHighlights();

                    string bellNote = TripScoutPeekBellStatusNote();
                    string changeNote = TripScoutPeekChangesStatusNote();
                    string extras = JoinTripScoutStatusNotes(changeNote, bellNote);
                    if (!string.IsNullOrWhiteSpace(extras))
                    {
                        FinishTripScoutLivePoll(
                            "Live panel: no trip changes on server (" + status.TripCount
                            + " trips, checked " + checkedAt + "). " + extras);
                        return;
                    }
                    FinishTripScoutLivePoll(
                        "Live panel: no trip changes on server (" + status.TripCount + " trips, checked " + checkedAt + ").");
                    return;
                }

                UpdateTripScoutStatus("Live panel: server changes detected — downloading trip updates…");
                var payload = await HiatmeAiClient.GetTripScoutServerTripsAsync(
                    settings, serviceDate, CancellationToken.None).ConfigureAwait(true);

                if (!payload.Ok || !payload.Available)
                {
                    FinishTripScoutLivePoll(
                        "Live panel: could not download server trips — " + (payload.Error ?? "unavailable") + " (" + checkedAt + ").");
                    return;
                }

                int changed = TripScoutMergeServerTrips(payload.Trips);
                _tripScoutLiveServerHash = payload.ContentHash ?? hash;

                await TripScoutRefreshActivityAfterLivePollAsync(settings, serviceDate)
                    .ConfigureAwait(true);
                RefreshTripScoutListViewKeepingFilter();
                TripScoutApplyRowHighlights();

                string finish = "Live panel: updated " + changed + " trip(s) from server ("
                    + payload.TripCount + " on server, checked " + checkedAt + ").";
                string notes = JoinTripScoutStatusNotes(
                    TripScoutPeekChangesStatusNote(),
                    TripScoutPeekBellStatusNote());
                if (!string.IsNullOrWhiteSpace(notes))
                    finish += " " + notes;
                FinishTripScoutLivePoll(finish);
            }
            catch (Exception ex)
            {
                FinishTripScoutLivePoll("Live panel: server check error — " + ex.Message + " (" + checkedAt + ").");
            }
            finally
            {
                _tripScoutLivePollInFlight = false;
            }
        }

        private static string JoinTripScoutStatusNotes(params string[] parts)
        {
            var kept = parts.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
            return kept.Count == 0 ? "" : string.Join(" ", kept);
        }

        private void FinishTripScoutLivePoll(string finalStatus)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => StopTripScoutStatusSpinner(finalStatus)));
                return;
            }
            StopTripScoutStatusSpinner(finalStatus);
        }

        private int TripScoutMergeServerTrips(IList<HiatmeAiClient.TripScoutServerTripRow> rows)
        {
            if (rows == null || rows.Count == 0 || _tripScoutAllTrips == null)
                return 0;

            var index = new Dictionary<string, WRDownloadedTrip>(StringComparer.OrdinalIgnoreCase);
            foreach (var trip in _tripScoutAllTrips)
            {
                string key = TripScoutTripKey(trip);
                if (!string.IsNullOrEmpty(key) && !index.ContainsKey(key))
                    index[key] = trip;
            }

            int touched = 0;
            foreach (var row in rows)
            {
                if (row == null || string.IsNullOrWhiteSpace(row.TripNo))
                    continue;

                string key = row.TripNo.Trim();
                WRDownloadedTrip trip;
                if (!index.TryGetValue(key, out trip))
                {
                    trip = new WRDownloadedTrip();
                    _tripScoutAllTrips.Add(trip);
                    index[key] = trip;
                }

                if (TripScoutApplyServerRow(trip, row))
                    touched++;
            }

            return touched;
        }

        private static string TripScoutTripKey(WRDownloadedTrip trip)
        {
            if (trip == null)
                return "";
            if (!string.IsNullOrWhiteSpace(trip.TripNumber))
                return trip.TripNumber.Trim();
            if (!string.IsNullOrWhiteSpace(trip.TripUUID))
                return trip.TripUUID.Trim();
            return "";
        }

        private static bool TripScoutApplyServerRow(
            WRDownloadedTrip trip,
            HiatmeAiClient.TripScoutServerTripRow row)
        {
            bool changed = false;

            changed |= AssignIfDifferent(trip.TripNumber, row.TripNo, v => trip.TripNumber = v);
            changed |= AssignIfDifferent(trip.TripUUID, row.TripUuid, v => trip.TripUUID = v);
            changed |= AssignIfDifferent(trip.DriverName, row.Driver, v => trip.DriverName = v);
            changed |= AssignIfDifferent(trip.ClientName, row.Client, v => trip.ClientName = v);
            changed |= AssignIfDifferent(trip.Status, row.Status, v => trip.Status = v);
            changed |= AssignIfDifferent(trip.PUTime, TripScoutIsoToDisplayTime(row.SchedPuIso), v => trip.PUTime = v);
            changed |= AssignIfDifferent(trip.DOTime, TripScoutIsoToDisplayTime(row.SchedDoIso), v => trip.DOTime = v);
            changed |= AssignIfDifferent(trip.ActualPUTime, TripScoutIsoToDisplayTime(row.ActualPuIso), v => trip.ActualPUTime = v);
            changed |= AssignIfDifferent(trip.ActualDOTime, TripScoutIsoToDisplayTime(row.ActualDoIso), v => trip.ActualDOTime = v);
            changed |= AssignIfIncomingNonEmpty(trip.PUStreet, row.PuStreet, v => trip.PUStreet = v);
            changed |= AssignIfIncomingNonEmpty(trip.PUCity, row.PuCity, v => trip.PUCity = v);
            changed |= AssignIfIncomingNonEmpty(trip.DOStreet, row.DoStreet, v => trip.DOStreet = v);
            changed |= AssignIfIncomingNonEmpty(trip.DOCITY, row.DoCity, v => trip.DOCITY = v);

            string miles = row.Miles.HasValue
                ? row.Miles.Value.ToString(CultureInfo.InvariantCulture)
                : "";
            changed |= AssignIfDifferent(trip.Miles, miles, v => trip.Miles = v);

            if (row.Alerts != null && row.Alerts.Count > 0)
            {
                var incoming = row.Alerts.Where(a => !string.IsNullOrWhiteSpace(a)).ToList();
                if (trip.Alerts == null)
                    trip.Alerts = new List<string>();
                if (!trip.Alerts.SequenceEqual(incoming))
                {
                    trip.Alerts = incoming;
                    changed = true;
                }
            }

            return changed;
        }

        private static bool AssignIfDifferent(string current, string value, Action<string> assign)
        {
            string next = value ?? "";
            if (string.Equals(current ?? "", next, StringComparison.Ordinal))
                return false;
            assign(next);
            return true;
        }

        /// <summary>Live merge must not wipe LOAD columns when the server omits address parts.</summary>
        private static bool AssignIfIncomingNonEmpty(string current, string value, Action<string> assign)
        {
            string next = value ?? "";
            if (string.IsNullOrWhiteSpace(next))
                return false;
            if (string.Equals(current ?? "", next, StringComparison.Ordinal))
                return false;
            assign(next);
            return true;
        }

        private static string TripScoutIsoToDisplayTime(string iso)
        {
            if (string.IsNullOrWhiteSpace(iso))
                return "";

            if (DateTime.TryParse(
                    iso,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out var dt))
            {
                return dt.ToString("h:mm tt", CultureInfo.InvariantCulture);
            }

            return iso;
        }

        private void StyleTripScoutLiveToolbar(bool live)
        {
            if (_tripScoutToolbarPanel == null)
                return;

            _tripScoutToolbarPanel.BackColor = live
                ? BlendTheme(SupeyTheme.SurfaceHeader, Color.FromArgb(8, 18, 32), 0.3f)
                : SupeyTheme.SurfaceHeader;

            if (_tripScoutToolbarTitle != null)
                _tripScoutToolbarTitle.ForeColor = live
                    ? BlendTheme(SupeyTheme.TextPrimary, SupeyTheme.AccentPrimary, 0.12f)
                    : SupeyTheme.TextPrimary;
        }

        private static Color BlendTheme(Color a, Color b, float t)
        {
            t = Math.Max(0f, Math.Min(1f, t));
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }
    }
}
