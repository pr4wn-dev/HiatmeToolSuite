using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using GMap.NET;
using GMap.NET.MapProviders;
using GMap.NET.WindowsForms;
using GMap.NET.WindowsForms.Markers;
using MaterialSkin;
using MaterialSkin.Controls;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Modeless map popup that shows the live AVL position for a single WellRyde driver.
    /// Caller passes a <see cref="WellRydePortalSession"/> (signed-in) plus the driver's portal
    /// id/name; the form fetches <c>/portal/avl/avlinitiate</c> on open and on demand,
    /// drops a marker, and recenters the map. Optional 30s auto-refresh keeps the marker fresh.
    /// </summary>
    /// <remarks>
    /// OpenStreetMap is the tile provider — free, no API key, sufficient for "where's the driver"
    /// at city zoom levels. Tiles are server+disk cached so reopening the same area is instant.
    /// </remarks>
    internal partial class DriverLocationMapForm : MaterialForm
    {
        private readonly WellRydePortalSession _session;
        private readonly string _driverId;
        private readonly string _driverDisplayName;
        private GMapOverlay _markerOverlay;
        private GMapMarker _driverMarker;
        private bool _isFetching;
        private CancellationTokenSource _fetchCts;

        // Most-recent successful AVL row for this driver. Used by the ETA estimator so it doesn't
        // have to re-hit the portal — auto-refresh keeps this current.
        private WRDriverPosition _lastPosition;
        private DateTime _lastPositionAtLocal;
        private WellRydeUserDetail _userProfile;
        private bool _userProfileLoaded;
        private bool _refreshProfileOnNextFetch;

        // Cached ETA result so toggling the horizon filter doesn't drop the badges; we reapply on
        // every PopulateTripsPanel pass. Cleared by a new "Estimate ETAs" run.
        private EtaPlanner.Result _lastEtaResult;
        private CancellationTokenSource _etaCts;
        private bool _etaInFlight;

        // ToolTip surface for the ETA status label — long error messages ("Routing service
        // returned HTTP 414 — chain too long...") get truncated even on the wider 2-line label,
        // so the full text is also pushed into a hover tooltip the user can read at leisure.
        private ToolTip _etaStatusTip;

        // GDI resources shared by every marker tooltip we attach. Cached here (and disposed in
        // OnFormClosed) so the auto-refresh loop doesn't leak a brush/pen/font on every poll.
        private Font _tooltipTitleFont;
        private Font _tooltipDetailFont;

        // Fonts owned by the trip-card list on the right side. Created once, reused for every
        // card, disposed with the form.
        private Font _cardTitleFont;
        private Font _cardClientFont;
        private Font _cardDetailFont;
        private Font _cardPillFont;

        // Snapshot of the trip rows that belong to this driver, taken once at construction time so
        // refreshing the AVL position doesn't have to re-scan Form1's master list.
        private readonly List<WRDownloadedTrip> _driverTrips;

        /// <summary>
        /// Saved horizon dropdown index so the user's filter choice persists across every locate-
        /// driver popup in the same session. Default 1 = "Next 1 hour", which matches how
        /// dispatch generally answers "where's my driver" questions.
        /// </summary>
        private static int _horizonPreferenceIndex = 1;

        /// <summary>
        /// Items shown in <see cref="_tripsHorizonCombo"/>. Their order is meaningful — index 0
        /// is the tightest window, index N-1 ("All trips") disables filtering entirely.
        /// </summary>
        private static readonly string[] HorizonItems =
        {
            "Active + next 30 min",
            "Active + next 1 hour",
            "Active + next 2 hours",
            "All trips (incl. done)",
        };

        /// <summary>
        /// Maps a dropdown index to a TimeSpan horizon, or <c>null</c> for "All trips" which means
        /// no time filter at all (and additionally re-includes Completed/Cancelled rows).
        /// </summary>
        private static TimeSpan? GetHorizonForIndex(int index)
        {
            switch (index)
            {
                case 0: return TimeSpan.FromMinutes(30);
                case 1: return TimeSpan.FromMinutes(60);
                case 2: return TimeSpan.FromMinutes(120);
                default: return null;
            }
        }

        // Live indicator color (the small circle next to the driver name). Repainted by
        // OnStatusDotPaint; updated whenever a fresh AVL response is rendered.
        private Color _statusDotColor = Color.FromArgb(110, 110, 110);

        // Tracks whether we've ever rendered a marker for this session. The first render
        // recenters/zooms the map; subsequent auto-refresh ticks leave the user's pan/zoom alone
        // unless they hit Refresh manually.
        private bool _hasRenderedMarker;

        public DriverLocationMapForm(WellRydePortalSession session, string driverId, string driverDisplayName,
            IEnumerable<WRDownloadedTrip> dayTrips)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _driverId = driverId;
            _driverDisplayName = driverDisplayName ?? "(unknown driver)";
            _driverTrips = FilterTripsForDriver(dayTrips, _driverDisplayName);

            InitializeComponent();

            try
            {
                var mgr = MaterialSkinManager.Instance;
                mgr.AddFormToManage(this);
                mgr.Theme = MaterialSkinManager.Themes.DARK;
                mgr.ColorScheme = new ColorScheme(Primary.Grey900, Primary.Grey800, Primary.BlueGrey500, Accent.Lime700, TextShade.WHITE);
            }
            catch
            {
                // Skinning is cosmetic; never fail to open the map because of it.
            }

            string cleanName = CleanDisplayName(_driverDisplayName);
            Text = "Driver: " + cleanName;
            _driverNameLabel.Text = cleanName;
            _statusLabel.Text = "Loading position...";
            _lastReportedLabel.Text = "";

            // Two-font tooltip: bolder title for the driver name, lighter weight for the details.
            // Owned by the form so DriverMarkerToolTip can be rebuilt cheaply each render.
            _tooltipTitleFont = new Font("Segoe UI Semibold", 10f);
            _tooltipDetailFont = new Font("Segoe UI", 9f);

            _cardTitleFont = new Font("Segoe UI Semibold", 10.5f);
            _cardClientFont = new Font("Segoe UI Semibold", 9.5f);
            _cardDetailFont = new Font("Segoe UI", 8.75f);
            _cardPillFont = new Font("Segoe UI Semibold", 7.5f);

            // Long ETA-status messages still might not fit even on a 2-line label, so a tooltip
            // backs them up. AutoPopDelay is generous (30s) because dispatch sometimes wants to
            // dwell on an error long enough to read it carefully.
            _etaStatusTip = new ToolTip
            {
                AutoPopDelay = 30000,
                InitialDelay = 350,
                ReshowDelay = 200,
                ShowAlways = true,
            };

            // Populate horizon dropdown and honor whatever filter the user picked last.
            _tripsHorizonCombo.Items.AddRange(HorizonItems);
            int idx = _horizonPreferenceIndex;
            if (idx < 0 || idx >= _tripsHorizonCombo.Items.Count) idx = 1;
            _tripsHorizonCombo.SelectedIndex = idx;

            ConfigureMapDefaults();
            PopulateTripsPanel();
        }

        private void OnTripFilterChanged(object sender, EventArgs e)
        {
            _horizonPreferenceIndex = _tripsHorizonCombo.SelectedIndex;
            PopulateTripsPanel();
        }

        /// <summary>
        /// Custom paint so the horizon dropdown matches the dark theme — default ComboBox uses
        /// system colors for the dropdown list which look out of place against the dark panel.
        /// Selected item gets the same RoyalBlue we use elsewhere for highlights.
        /// </summary>
        private void OnTripHorizonComboDrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            using (var bg = new SolidBrush(selected ? Color.RoyalBlue : Color.FromArgb(60, 60, 60)))
                e.Graphics.FillRectangle(bg, e.Bounds);
            string text = _tripsHorizonCombo.Items[e.Index]?.ToString() ?? "";
            using (var fg = new SolidBrush(Color.Gainsboro))
            using (var fmt = new StringFormat { LineAlignment = StringAlignment.Center })
            {
                var rect = new RectangleF(e.Bounds.Left + 6, e.Bounds.Top, e.Bounds.Width - 6, e.Bounds.Height);
                e.Graphics.DrawString(text, e.Font, fg, rect, fmt);
            }
        }

        /// <summary>
        /// Pull the rows assigned to this driver out of the day's master trip list. Whitespace and
        /// case are normalized because the AVL feed sometimes pads names with double spaces and
        /// the listview rows use whatever the portal returned verbatim.
        /// </summary>
        private static List<WRDownloadedTrip> FilterTripsForDriver(IEnumerable<WRDownloadedTrip> source, string driverDisplayName)
        {
            var matches = new List<WRDownloadedTrip>();
            if (source == null || string.IsNullOrWhiteSpace(driverDisplayName)) return matches;
            string target = NormalizeName(driverDisplayName);
            foreach (var t in source)
            {
                if (t == null || string.IsNullOrWhiteSpace(t.DriverName)) continue;
                if (NormalizeName(t.DriverName) == target) matches.Add(t);
            }
            return matches;
        }

        /// <summary>
        /// Build the trip card list shown next to the map. Cards are sorted so the trips dispatch
        /// is most likely asking about (on board / at pickup) float to the top, with completed
        /// rides at the bottom. Renders on construction and on every toggle of the filter; the
        /// AVL auto-refresh loop doesn't touch this panel because trip status changes come from
        /// a fresh Trip Scout reload.
        /// </summary>
        private void PopulateTripsPanel()
        {
            if (_tripsFlow == null) return;
            _tripsFlow.SuspendLayout();
            try
            {
                _tripsFlow.Controls.Clear();
                if (_driverTrips.Count == 0)
                {
                    _tripsCountLabel.Text = "No trips for this driver in the loaded day.";
                    _tripsHorizonLabel.Visible = false;
                    _tripsHorizonCombo.Visible = false;
                    _etaButton.Visible = false;
                    _etaStatusLabel.Visible = false;
                    _tripsEmptyLabel.Text = "No trips assigned today";
                    _tripsEmptyLabel.Visible = true;
                    _tripsFlow.Visible = false;
                    return;
                }
                _tripsHorizonLabel.Visible = true;
                _tripsHorizonCombo.Visible = true;
                _etaButton.Visible = true;
                _etaStatusLabel.Visible = true;

                TimeSpan? horizon = GetHorizonForIndex(_tripsHorizonCombo.SelectedIndex);
                bool activeOnly = horizon.HasValue;
                DateTime now = DateTime.Now;

                var visible = new List<WRDownloadedTrip>(_driverTrips.Count);
                int hiddenDone = 0, hiddenCancelled = 0, hiddenFarFuture = 0;
                foreach (var trip in _driverTrips)
                {
                    if (!activeOnly)
                    {
                        visible.Add(trip);
                        continue;
                    }
                    var (phase, _, _) = TripPhaseClassifier.Classify(trip);
                    if (IsTripCurrentlyRelevant(trip, phase, now, horizon.Value))
                    {
                        visible.Add(trip);
                        continue;
                    }
                    switch (phase)
                    {
                        case TripPhase.Completed: hiddenDone++; break;
                        case TripPhase.Cancelled: hiddenCancelled++; break;
                        case TripPhase.Scheduled: hiddenFarFuture++; break;
                        default: hiddenFarFuture++; break;
                    }
                }

                _tripsCountLabel.Text = BuildTripCountSummary(activeOnly, _driverTrips, visible.Count,
                    hiddenDone, hiddenCancelled, hiddenFarFuture);

                if (visible.Count == 0)
                {
                    _tripsFlow.Visible = false;
                    _tripsEmptyLabel.Text = activeOnly
                        ? "Nothing active in this window." + Environment.NewLine + "Try a wider \"Show\" range above."
                        : "No trips assigned today";
                    _tripsEmptyLabel.Visible = true;
                    return;
                }

                _tripsEmptyLabel.Visible = false;
                _tripsFlow.Visible = true;
                foreach (var trip in TripPhaseClassifier.Sort(visible))
                {
                    var (phase, label, color) = TripPhaseClassifier.Classify(trip);
                    bool reservesEta = IsEtaTargetPhase(phase);
                    var card = new TripCardControl(trip, phase, label, color,
                        _cardTitleFont, _cardClientFont, _cardDetailFont, _cardPillFont,
                        reservesEtaRow: reservesEta);
                    _tripsFlow.Controls.Add(card);
                }

                // Reapply any previously-computed ETAs — toggling the horizon dropdown rebuilds
                // every card, but we don't want the user to lose their badges and have to click
                // "Estimate ETAs" again.
                if (_lastEtaResult != null) ApplyEtasToCards(_lastEtaResult);
            }
            finally
            {
                _tripsFlow.ResumeLayout();
            }
        }

        /// <summary>Phases for which the ETA row is meaningful (Scheduled PU, OnBoard/AtPickup DO).</summary>
        private static bool IsEtaTargetPhase(TripPhase phase)
        {
            return phase == TripPhase.Scheduled
                || phase == TripPhase.OnBoard
                || phase == TripPhase.AtPickup
                || phase == TripPhase.Unknown;
        }

        /// <summary>
        /// Single point of update for the ETA status label so both the visible text and the
        /// hover tooltip stay in sync. The tooltip is what the user falls back to when the
        /// label clips a long HTTP error.
        /// </summary>
        private void SetEtaStatus(string text, bool isWarning)
        {
            string clean = text ?? "";
            if (_etaStatusLabel != null)
            {
                _etaStatusLabel.ForeColor = isWarning ? Color.FromArgb(255, 183, 77) : Color.Silver;
                _etaStatusLabel.Text = clean;
            }
            // The full message goes into the tooltip even when it fits in the label, so a hover
            // always reveals the same information without the user having to guess.
            try { _etaStatusTip?.SetToolTip(_etaStatusLabel, clean); } catch { /* tooltip is best-effort */ }
        }

        /// <summary>
        /// Click handler for the "Estimate ETAs" button. Validates we have a usable driver
        /// position, runs the planner, and pushes the result onto every visible card. Also
        /// gracefully handles the cases where the user closes the form mid-call or the public
        /// Nominatim/OSRM services are unreachable.
        /// </summary>
        private async void OnEstimateEtasClicked(object sender, EventArgs e)
        {
            if (_etaInFlight) return;

            // Need a driver GPS to start the chain from. If the AVL fetch hasn't landed yet (or
            // the driver is offline), tell the user instead of silently doing nothing.
            if (_lastPosition == null || !_lastPosition.HasValidLocation)
            {
                SetEtaStatus("Need a live driver position first (open or refresh until the marker shows).", isWarning: true);
                return;
            }

            // Warn (but proceed) if the driver's position is stale — ETA from where they were 2
            // hours ago is misleading. Threshold matches FreshSpeedWindow used in the status row.
            TimeSpan? age = null;
            DateTime? reported = _lastPosition.GetReportedLocalTime();
            if (reported.HasValue) age = DateTime.Now - reported.Value;
            bool stalePosition = !IsMovementReadingFresh(age);

            _etaInFlight = true;
            _etaButton.Enabled = false;
            SetEtaStatus("Estimating arrival times...", isWarning: false);

            // Wipe any previous results so the cards reset to "Estimating..." rather than showing
            // last-run values during the network round-trip.
            _lastEtaResult = null;
            foreach (Control c in _tripsFlow.Controls)
            {
                if (c is TripCardControl card) card.SetEtaComputing();
            }

            _etaCts?.Cancel();
            _etaCts?.Dispose();
            _etaCts = new CancellationTokenSource();
            var ct = _etaCts.Token;

            EtaPlanner.Result result = null;
            try
            {
                var origin = new GeoPoint(_lastPosition.Latitude, _lastPosition.Longitude);
                result = await EtaPlanner.EstimateAsync(origin, DateTime.Now, _driverTrips, ct).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                if (IsDisposed) return;
                SetEtaStatus("ETA estimate cancelled.", isWarning: false);
            }
            catch (Exception ex)
            {
                if (IsDisposed) return;
                SetEtaStatus("ETA failed: " + ex.Message, isWarning: true);
            }
            finally
            {
                _etaInFlight = false;
                if (!IsDisposed) _etaButton.Enabled = true;
            }

            if (IsDisposed || result == null) return;

            _lastEtaResult = result;
            ApplyEtasToCards(result);

            string suffix = stalePosition ? "  •  GPS is stale" : "";
            if (result.ComputedCount == 0 && !string.IsNullOrEmpty(result.DiagnosticMessage))
            {
                SetEtaStatus(result.DiagnosticMessage + suffix, isWarning: true);
            }
            else
            {
                string baseText = result.ComputedCount + " ETA" + (result.ComputedCount == 1 ? "" : "s") + " ready";
                if (result.SkippedCount > 0) baseText += "  •  " + result.SkippedCount + " skipped";
                SetEtaStatus(baseText + suffix, isWarning: false);
            }
        }

        /// <summary>Push a planner result onto whichever cards are currently in the flow panel.</summary>
        private void ApplyEtasToCards(EtaPlanner.Result result)
        {
            foreach (Control c in _tripsFlow.Controls)
            {
                if (!(c is TripCardControl card) || !card.ReservesEtaRow) continue;
                string key = EtaPlanner.GetTripKey(card.Trip);
                if (result.EtaByTrip.TryGetValue(key, out var info))
                {
                    card.SetEta(info.EstimatedLocalTime, info.FromNow, info.Label);
                }
                else if (result.FailureByTrip.TryGetValue(key, out var reason))
                {
                    card.SetEtaFailed(DescribeFailure(reason));
                }
                else
                {
                    // Card wasn't part of the planner output at all (e.g. no scheduled time on
                    // either end so it didn't make it into the chain). Reset rather than leaving
                    // a stale "Estimating..." badge.
                    card.ResetEta();
                }
            }
        }

        private static string DescribeFailure(EtaPlanner.FailReason reason)
        {
            switch (reason)
            {
                case EtaPlanner.FailReason.NoAddress: return "ETA unavailable: address not found";
                case EtaPlanner.FailReason.ChainBroken: return "ETA unavailable: address ahead failed";
                case EtaPlanner.FailReason.RoutingUnavailable: return "ETA unavailable: routing offline";
                default: return "ETA unavailable";
            }
        }

        /// <summary>
        /// Decide whether a trip should appear in the filtered view for the chosen horizon. Phases
        /// where the driver is actively engaged (on board, at pickup/dropoff, unknown) always
        /// show, as do past-due Scheduled trips — those are the rides dispatch is most likely
        /// investigating. Future Scheduled trips show only if their PU time falls within
        /// <paramref name="horizon"/> from now.
        /// </summary>
        private static bool IsTripCurrentlyRelevant(WRDownloadedTrip trip, TripPhase phase, DateTime now, TimeSpan horizon)
        {
            switch (phase)
            {
                case TripPhase.Completed:
                case TripPhase.Cancelled:
                    return false;
                case TripPhase.OnBoard:
                case TripPhase.AtPickup:
                case TripPhase.AtDropoff:
                case TripPhase.Unknown:
                    return true;
                case TripPhase.Scheduled:
                    if (!DateTime.TryParse(trip.PUTime, CultureInfo.CurrentCulture,
                            System.Globalization.DateTimeStyles.None, out var puTime))
                        return true; // unparseable PU times — show, the user can decide.
                    if (puTime <= now) return true; // past-due: definitely relevant.
                    return (puTime - now) <= horizon;
                default:
                    return true;
            }
        }

        /// <summary>Adaptive summary line: "Showing 3 of 7 (4 hidden)" when filtered, breakdown when not.</summary>
        private static string BuildTripCountSummary(bool activeOnly, List<WRDownloadedTrip> all, int visibleCount,
            int hiddenDone, int hiddenCancelled, int hiddenFarFuture)
        {
            if (activeOnly)
            {
                int hidden = hiddenDone + hiddenCancelled + hiddenFarFuture;
                if (hidden == 0)
                    return "All " + all.Count + " trip" + (all.Count == 1 ? "" : "s") + " active";
                var parts = new List<string>(3);
                if (hiddenDone > 0) parts.Add(hiddenDone + " done");
                if (hiddenCancelled > 0) parts.Add(hiddenCancelled + " cancelled");
                if (hiddenFarFuture > 0) parts.Add(hiddenFarFuture + " later today");
                return "Showing " + visibleCount + " of " + all.Count +
                       "  •  hidden: " + string.Join(", ", parts);
            }

            int onBoard = 0, atPickup = 0, atDropoff = 0, scheduled = 0, completed = 0, cancelled = 0;
            foreach (var t in all)
            {
                var (p, _, _) = TripPhaseClassifier.Classify(t);
                switch (p)
                {
                    case TripPhase.OnBoard: onBoard++; break;
                    case TripPhase.AtPickup: atPickup++; break;
                    case TripPhase.AtDropoff: atDropoff++; break;
                    case TripPhase.Scheduled: scheduled++; break;
                    case TripPhase.Completed: completed++; break;
                    case TripPhase.Cancelled: cancelled++; break;
                }
            }
            var bits = new List<string>(6);
            if (onBoard + atDropoff > 0) bits.Add((onBoard + atDropoff) + " in progress");
            if (atPickup > 0) bits.Add(atPickup + " at pickup");
            if (scheduled > 0) bits.Add(scheduled + " upcoming");
            if (completed > 0) bits.Add(completed + " done");
            if (cancelled > 0) bits.Add(cancelled + " cancelled");
            string summary = bits.Count == 0 ? "" : "  •  " + string.Join("  •  ", bits);
            return all.Count + " trip" + (all.Count == 1 ? "" : "s") + summary;
        }

        private void ConfigureMapDefaults()
        {
            // OSM tile fetches require a UA + Referer per their tile usage policy; the initializer
            // sets both globally and isolates our tile cache so previously-blocked tiles don't
            // render. Idempotent — Program.Main also calls this on startup.
            GMapInitializer.EnsureInitialized();
            try
            {
                _gmap.MapProvider = GMapProviders.OpenStreetMap;
            }
            catch
            {
                // If GMap can't init the provider (rare, e.g. read-only profile), keep going with whatever default the control has.
            }
            _gmap.ShowCenter = false;
            _gmap.DragButton = MouseButtons.Left;
            _gmap.MinZoom = 2;
            _gmap.MaxZoom = 18;
            _gmap.Zoom = 12;
            _gmap.Position = new PointLatLng(39.0, -95.0);

            _markerOverlay = new GMapOverlay("driverMarker");
            _gmap.Overlays.Add(_markerOverlay);
        }

        protected override async void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (_autoRefreshCheck.Checked)
                _refreshTimer.Start();
            await FetchAndRenderAsync(recenter: true).ConfigureAwait(true);
        }

        private async Task FetchAndRenderAsync(bool recenter)
        {
            if (_isFetching) return;
            _isFetching = true;
            _fetchCts?.Dispose();
            _fetchCts = new CancellationTokenSource();
            var ct = _fetchCts.Token;

            _refreshButton.Enabled = false;
            _statusLabel.Text = "Loading position...";
            if (_refreshProfileOnNextFetch)
            {
                _userProfileLoaded = false;
                _refreshProfileOnNextFetch = false;
            }

            try
            {
                await EnsureUserProfileAsync(ct).ConfigureAwait(true);
                if (IsDisposed || Disposing) return;

                var positions = await _session.GetDriverPositionsAsync(ct).ConfigureAwait(true);
                if (IsDisposed || Disposing) return;

                WRDriverPosition match = null;
                if (positions != null && !string.IsNullOrEmpty(_driverId))
                {
                    foreach (var p in positions)
                    {
                        if (p == null) continue;
                        if (string.Equals(p.DriverId, _driverId, StringComparison.OrdinalIgnoreCase))
                        {
                            match = p;
                            break;
                        }
                    }
                }
                // Fallback: portal trip "Driver" name compared to AVL "drivername" (collapse
                // double spaces because the AVL feed often pads first/last names with extras).
                if (match == null && positions != null && !string.IsNullOrWhiteSpace(_driverDisplayName))
                {
                    string target = NormalizeName(_driverDisplayName);
                    foreach (var p in positions)
                    {
                        if (p == null) continue;
                        if (NormalizeName(p.DriverName) == target)
                        {
                            match = p;
                            break;
                        }
                    }
                }

                RenderPosition(match, recenter);
            }
            catch (OperationCanceledException)
            {
                // Form closed mid-fetch; harmless.
            }
            catch (Exception ex)
            {
                _statusLabel.Text = "Failed to load position: " + ex.Message;
                ApplyStatusDotColor(StatusColors.Unknown);
                ApplyDiagnostics(null, foundOnAvlFeed: false);
            }
            finally
            {
                _isFetching = false;
                if (!IsDisposed) _refreshButton.Enabled = true;
            }
        }

        private async Task EnsureUserProfileAsync(CancellationToken ct)
        {
            if (_userProfileLoaded || string.IsNullOrWhiteSpace(_driverId))
                return;
            _userProfileLoaded = true;
            try
            {
                var result = await _session.GetUserDetailHtmlAsync(_driverId, ct).ConfigureAwait(true);
                if (result.IsSuccess && !string.IsNullOrEmpty(result.HtmlBody))
                    _userProfile = WellRydeUserParser.ParseUserDetail(_driverId, result.HtmlBody);
            }
            catch
            {
                // Profile is optional context for diagnostics only.
            }
        }

        private void ApplyDiagnostics(WRDriverPosition match, bool foundOnAvlFeed)
        {
            if (_diagnosticsHeadlineLabel == null || _diagnosticsBodyLabel == null)
                return;

            var diag = AvlDriverDiagnosticsBuilder.Build(match, _userProfile, foundOnAvlFeed);
            _diagnosticsHeadlineLabel.Text = "Connection status — " + diag.Headline;
            _diagnosticsHeadlineLabel.ForeColor = diag.AccentColor;
            _diagnosticsBodyLabel.Text = diag.FormatBody();
        }

        private void RenderPosition(WRDriverPosition match, bool recenter)
        {
            _markerOverlay.Markers.Clear();
            _driverMarker = null;

            if (match == null)
            {
                _statusLabel.Text = "Driver not visible on the live map (offline or out of range).";
                _lastReportedLabel.Text = "";
                _lastReportedLabel.ForeColor = StatusColors.MutedText;
                ApplyStatusDotColor(StatusColors.Unknown);
                ApplyDiagnostics(null, foundOnAvlFeed: false);
                return;
            }

            TimeSpan? age = null;
            DateTime? reported = match.GetReportedLocalTime();
            if (reported.HasValue) age = DateTime.Now - reported.Value;
            Color ageColor = StatusColors.ForAge(age);

            if (!match.HasValidLocation)
            {
                _statusLabel.Text = "Portal returned no location for this driver yet.";
                _lastReportedLabel.Text = FormatLastReported(reported, age);
                _lastReportedLabel.ForeColor = ageColor;
                ApplyStatusDotColor(ageColor);
                ApplyDiagnostics(match, foundOnAvlFeed: true);
                return;
            }

            // Cache for the ETA estimator. Saved before the marker swap so even if rendering
            // throws (rare GMap glitch), we still have a usable position for the next click.
            _lastPosition = match;
            _lastPositionAtLocal = DateTime.Now;

            var pt = new PointLatLng(match.Latitude, match.Longitude);
            string cleanName = CleanDisplayName(match.DriverName);
            _driverMarker = new GMarkerGoogle(pt, GMarkerGoogleType.red_pushpin)
            {
                // Set ToolTipText so MarkerTooltipMode still triggers the show/hide logic; the
                // custom tooltip below ignores this string and renders structured data instead.
                ToolTipText = cleanName,
                ToolTipMode = MarkerTooltipMode.OnMouseOver,
            };
            _driverMarker.ToolTip = new DriverMarkerToolTip(_driverMarker, _tooltipTitleFont, _tooltipDetailFont)
            {
                Title = string.IsNullOrWhiteSpace(cleanName) ? "Driver" : cleanName,
                Detail = BuildTooltipDetailLines(match, age),
                AccentColor = ageColor,
            };
            _markerOverlay.Markers.Add(_driverMarker);

            // Recenter on the first render and any explicit refresh; auto-refresh ticks leave the
            // user's chosen view alone so they can pan around without being yanked back every poll.
            if (recenter || !_hasRenderedMarker)
            {
                _gmap.Position = pt;
                if (_gmap.Zoom < 14) _gmap.Zoom = 14;
            }
            _hasRenderedMarker = true;

            _statusLabel.Text = BuildStatusLine(match, age);
            _lastReportedLabel.Text = FormatLastReported(reported, age);
            _lastReportedLabel.ForeColor = ageColor;
            ApplyStatusDotColor(ageColor);
            ApplyDiagnostics(match, foundOnAvlFeed: true);
        }

        /// <summary>
        /// Maximum age of an AVL ping for which we still trust the reported speed/movement.
        /// Beyond this we assume the driver went offline mid-route and pin the display to
        /// "Stopped" — the AVL feed otherwise keeps echoing their last known speed forever.
        /// Matches the <see cref="StatusColors.Live"/> threshold so the green dot semantically
        /// means "real-time speed shown".
        /// </summary>
        private static readonly TimeSpan FreshSpeedWindow = TimeSpan.FromMinutes(5);

        private static bool IsMovementReadingFresh(TimeSpan? age)
        {
            return age.HasValue && age.Value >= TimeSpan.Zero && age.Value <= FreshSpeedWindow;
        }

        private static System.Collections.Generic.List<string> BuildTooltipDetailLines(WRDriverPosition p, TimeSpan? age)
        {
            var lines = new System.Collections.Generic.List<string>(4);
            if (!string.IsNullOrWhiteSpace(p.VehicleName))
            {
                string vehicleLine = "Vehicle: " + p.VehicleName;
                if (!string.IsNullOrWhiteSpace(p.VehicleType))
                    vehicleLine += "  (" + p.VehicleType + ")";
                lines.Add(vehicleLine);
            }
            lines.Add(DescribeMovement(p, age));
            if (p.DelayTime > 0.5)
                lines.Add("Delayed " + ((int)System.Math.Round(p.DelayTime)).ToString(CultureInfo.InvariantCulture) + "m");
            if (age.HasValue)
                lines.Add("GPS updated " + FormatAge(age.Value));
            DateTime? connected = p.GetLastConnectedLocalTime();
            if (connected.HasValue)
            {
                TimeSpan connAge = DateTime.Now - connected.Value;
                lines.Add("App connected " + FormatAge(connAge));
            }
            return lines;
        }

        private static string BuildStatusLine(WRDriverPosition p, TimeSpan? age)
        {
            var sb = new System.Text.StringBuilder();
            if (!string.IsNullOrWhiteSpace(p.VehicleName))
                sb.Append("Vehicle ").Append(p.VehicleName);
            if (!string.IsNullOrWhiteSpace(p.VehicleType))
                sb.Append(sb.Length == 0 ? "" : "  •  ").Append(p.VehicleType);
            sb.Append(sb.Length == 0 ? "" : "  •  ").Append(DescribeMovement(p, age));
            // Surface delay if the AVL row reports one — anything > 0 minutes is operationally relevant.
            if (p.DelayTime > 0.5)
                sb.Append("  •  ").Append("Delayed ").Append(((int)Math.Round(p.DelayTime)).ToString(CultureInfo.InvariantCulture)).Append("m");
            else if (!string.IsNullOrWhiteSpace(p.EtaCode) &&
                     !string.Equals(p.EtaCode, "NOASSIGNMENT", StringComparison.OrdinalIgnoreCase) &&
                     !string.Equals(p.EtaCode, "ONTIME", StringComparison.OrdinalIgnoreCase))
            {
                sb.Append("  •  ").Append(p.EtaCode);
            }
            if (!string.IsNullOrWhiteSpace(p.TransportProviderName))
                sb.Append("  •  ").Append(p.TransportProviderName);
            return sb.ToString();
        }

        /// <summary>
        /// Single source of truth for how speed/movement is rendered. AVL feeds keep echoing the
        /// last observed speed even after a driver disconnects, so we only show real numbers when
        /// the ping is fresh; otherwise the driver is reported as stopped (which the live dot +
        /// "Last reported" timestamp will further qualify as recent vs. stale).
        /// </summary>
        private static string DescribeMovement(WRDriverPosition p, TimeSpan? age)
        {
            if (!IsMovementReadingFresh(age))
                return "Stopped";
            double speed = p.Speed;
            if (double.IsNaN(speed) || double.IsInfinity(speed) || speed < 0.5)
                return "Stopped";
            return "Speed " + FormatSpeed(speed);
        }

        private static string FormatSpeed(double speed)
        {
            if (double.IsNaN(speed) || double.IsInfinity(speed)) return "—";
            return speed.ToString("0.0", CultureInfo.InvariantCulture) + " mph";
        }

        private static string FormatAge(TimeSpan age)
        {
            if (age.TotalSeconds < 0) return "just now";
            if (age.TotalMinutes < 1) return ((int)age.TotalSeconds) + "s ago";
            if (age.TotalHours < 1) return ((int)age.TotalMinutes) + "m ago";
            if (age.TotalDays < 1) return ((int)age.TotalHours) + "h ago";
            return ((int)age.TotalDays) + "d ago";
        }

        private static string FormatLastReported(DateTime? local, TimeSpan? age)
        {
            if (!local.HasValue) return "Last reported: (unknown)";
            string ageText = age.HasValue ? FormatAge(age.Value) : "";
            return "Last reported: " + local.Value.ToString("g") +
                   (string.IsNullOrEmpty(ageText) ? "" : "  (" + ageText + ")");
        }

        /// <summary>Trim and collapse runs of whitespace inside the AVL/portal driver name (the AVL feed often pads with double spaces).</summary>
        private static string CleanDisplayName(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            var parts = raw.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            return string.Join(" ", parts);
        }

        /// <summary>Lowercased + whitespace-collapsed name used purely as a roster lookup key.</summary>
        private static string NormalizeName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            var parts = name.Trim().ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            return string.Join(" ", parts);
        }

        /// <summary>Drives the live indicator + the "Last reported" label so the user can tell at a glance how fresh the position is.</summary>
        private static class StatusColors
        {
            public static readonly Color Live = Color.FromArgb(76, 175, 80);     // green   — < 5 min
            public static readonly Color Recent = Color.FromArgb(255, 193, 7);   // amber   — < 1 hr
            public static readonly Color Stale = Color.FromArgb(229, 57, 53);    // red     — > 1 hr
            public static readonly Color Unknown = Color.FromArgb(110, 110, 110);// gray    — no data
            public static readonly Color MutedText = Color.Silver;

            public static Color ForAge(TimeSpan? age)
            {
                if (!age.HasValue) return Unknown;
                double mins = age.Value.TotalMinutes;
                if (mins < 0) mins = 0;
                if (mins <= 5) return Live;
                if (mins <= 60) return Recent;
                return Stale;
            }
        }

        private void ApplyStatusDotColor(Color color)
        {
            _statusDotColor = color;
            if (_statusDot != null && !_statusDot.IsDisposed) _statusDot.Invalidate();
        }

        /// <summary>
        /// Paint the small live-status circle next to the driver name. Two passes: a 1px ring of
        /// the same color at lower alpha (so the dot reads against any header redraw artifacts),
        /// then the solid fill on top.
        /// </summary>
        private void OnStatusDotPaint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var bounds = _statusDot.ClientRectangle;
            int pad = 1;
            var rect = new Rectangle(bounds.X + pad, bounds.Y + pad,
                bounds.Width - 2 * pad, bounds.Height - 2 * pad);
            using (var halo = new SolidBrush(Color.FromArgb(60, _statusDotColor)))
                g.FillEllipse(halo, Rectangle.Inflate(rect, 1, 1));
            using (var fill = new SolidBrush(_statusDotColor))
                g.FillEllipse(fill, rect);
        }

        private async void OnRefreshClicked(object sender, EventArgs e)
        {
            _refreshProfileOnNextFetch = true;
            await FetchAndRenderAsync(recenter: true).ConfigureAwait(true);
        }

        private void OnCloseClicked(object sender, EventArgs e)
        {
            Close();
        }

        private void OnAutoRefreshChanged(object sender, EventArgs e)
        {
            if (_autoRefreshCheck.Checked) _refreshTimer.Start();
            else _refreshTimer.Stop();
        }

        private async void OnAutoRefreshTick(object sender, EventArgs e)
        {
            // Auto-refresh keeps the marker fresh without yanking the camera around.
            await FetchAndRenderAsync(recenter: false).ConfigureAwait(true);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            try { _refreshTimer.Stop(); } catch { /* timer may already be disposed */ }
            try { _fetchCts?.Cancel(); } catch { /* token may already be disposed */ }
            try { _etaCts?.Cancel(); } catch { /* same — best-effort cancellation */ }
            try
            {
                // Markers reference these fonts by field — drop the marker first, then the GDI
                // resources are unowned and safe to dispose. Order matters: disposing a font
                // still in use would crash the next paint.
                _markerOverlay?.Markers.Clear();
                _driverMarker = null;
                _tooltipTitleFont?.Dispose();
                _tooltipDetailFont?.Dispose();
                // Cards on _tripsFlow share these fonts; clearing the controls first releases the
                // references so disposing the fonts is safe.
                if (_tripsFlow != null)
                {
                    foreach (Control c in _tripsFlow.Controls) c.Dispose();
                    _tripsFlow.Controls.Clear();
                }
                _cardTitleFont?.Dispose();
                _cardClientFont?.Dispose();
                _cardDetailFont?.Dispose();
                _cardPillFont?.Dispose();
                _etaStatusTip?.Dispose();
            }
            catch { /* dispose is best-effort */ }
            base.OnFormClosed(e);
        }
    }
}
