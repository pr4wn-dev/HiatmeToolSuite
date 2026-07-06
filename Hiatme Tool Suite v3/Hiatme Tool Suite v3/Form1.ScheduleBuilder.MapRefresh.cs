using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Hiatme_Tool_Suite_v3
{
    public partial class Form1
    {
        /// <summary>While non-zero, map OSRM upgrade is in flight — defer mileage HUD work.</summary>
        private int _fsMapOsrmLoadGen;

        /// <summary>Cancel token for batch map preload after schedule bind/load.</summary>
        private int _fsMapPreloadGen;

        private volatile bool _fsMapPreloadRunning;

        /// <summary>Refresh generation that owns the map-pane loading veil (paired Pop).</summary>
        private int _fsMapLoadingRefreshGen;

        private int _fsMapStatusReportTick;
        private string _fsMapStatusReportPending;

        private int _fsMapLoadingMessageGen;

        private CancellationTokenSource _fsMapWorkCts;
        private readonly object _fsMapWorkCtsLock = new object();

        private System.Windows.Forms.Timer _fsMapRefreshDebounceTimer;
        private volatile bool _fsMapRefreshDebouncePending;

        private sealed class FsMapRefreshPayload
        {
            public bool Aborted;
            public bool RoutingOk = true;
            public string RoutingDetail;
            public Dictionary<string, GeoPoint> Pickup;
            public Dictionary<string, GeoPoint> Dropoff;
            public List<SupeyTripCluster> Groups;
            public SupeyDriverPlan Plan;
            public SupeyDriverProfile DriverProfile;
            public string StatusMessage;
            public bool HasOsrmRoutes;
        }

        private bool IsFsMapRefreshStale(int gen, string tabName = null)
        {
            if (gen != _fsMapRefreshGen)
                return true;
            if (tabName != null
                && !string.Equals(tabName, _fsActiveDriverTab, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        private void FsMapBeginInvoke(Action action)
        {
            if (action == null)
                return;
            if (IsDisposed || !IsHandleCreated)
                return;
            if (!InvokeRequired)
                action();
            else
                BeginInvoke(action);
        }

        private void FsMapReportStatusThrottled(int gen, string tabName, string message)
        {
            if (IsFsMapRefreshStale(gen, tabName) || string.IsNullOrWhiteSpace(message))
                return;

            _fsMapStatusReportPending = tabName + " · " + message.Trim();
            int now = Environment.TickCount;
            if (unchecked(now - _fsMapStatusReportTick) < 400 && _fsMapStatusReportPending != null)
                return;

            _fsMapStatusReportTick = now;
            string status = _fsMapStatusReportPending;
            _fsMapStatusReportPending = null;
            FsMapBeginInvoke(() =>
            {
                if (IsFsMapRefreshStale(gen, tabName))
                    return;
                SetScheduleBuilderStatus(status);
            });
        }

        private void FsMapPushRouteLoading(int gen, string message)
        {
            if (_fsMap == null || string.IsNullOrWhiteSpace(message))
                return;

            _fsMapLoadingRefreshGen = gen;
            string msg = message.Trim();
            FsMapBeginInvoke(() =>
            {
                if (_fsMap == null || _fsMapLoadingRefreshGen != gen)
                    return;
                _fsMap.PushMapLoading(msg);
            });
        }

        private void FsMapSetRouteLoadingMessage(int gen, string message)
        {
            if (_fsMap == null || _fsMapLoadingRefreshGen != gen || string.IsNullOrWhiteSpace(message))
                return;

            _fsMapLoadingMessageGen = gen;
            string msg = message.Trim();
            FsMapBeginInvoke(() =>
            {
                if (_fsMap == null || _fsMapLoadingRefreshGen != gen)
                    return;
                _fsMap.SetMapLoadingMessage(msg);
            });
        }

        private void FsMapTryPopRouteLoading(int gen)
        {
            if (_fsMapLoadingRefreshGen != gen)
                return;

            _fsMapLoadingRefreshGen = 0;
            FsMapBeginInvoke(() => _fsMap?.PopMapLoading());
        }

        private static FsMapRefreshPayload FsMapAbortedPayload()
        {
            return new FsMapRefreshPayload { Aborted = true };
        }

        private static List<SupeyTripCluster> BuildFsMapGroupsFromLines(
            IList<ScheduleBuilderPreviewLine> lines,
            bool isReservesTab,
            bool showGroupColors)
        {
            if (lines == null)
                return new List<SupeyTripCluster>();

            if (isReservesTab)
                return ScheduleBuilderPreviewGroups.BuildTripFlatClustersFromPreviewLines(lines);

            if (showGroupColors)
                return ScheduleBuilderPreviewGroups.BuildFromPreviewLines(lines);

            return ScheduleBuilderPreviewGroups.BuildTripFlatClustersFromPreviewLines(lines);
        }

        private static string BuildFsMapStatusMessage(
            string tabName,
            bool isReservesTab,
            bool showGroupColors,
            int pinCount,
            int grpCount,
            int roadGroups,
            int straightGroups,
            SupeyDriverPlan plan,
            SupeyDriverProfile driverProfile,
            bool osrmPending)
        {
            if (pinCount <= 0)
            {
                if (!HiatmeGeoSettings.UseServer && HiatmeGeoSettings.ServerOnly)
                    return tabName + " map · no pins (office server offline — BUILD/SAVE still work).";
                return tabName + " map · no pins (geocode cache empty — BUILD/SAVE still work).";
            }

            string routes;
            if (osrmPending)
                routes = "road routes loading…";
            else if (roadGroups > 0 && straightGroups > 0)
                routes = roadGroups + " road, " + straightGroups + " straight fallback";
            else if (straightGroups > 0)
                routes = straightGroups + " straight (OSRM unavailable)";
            else
                routes = "road routes";

            bool tripFlat = isReservesTab || !showGroupColors;
            string countLabel = tripFlat
                ? grpCount + " trip(s), "
                : grpCount + " group(s), ";
            return tabName + " map · " + countLabel
                + pinCount + " pin(s), " + routes
                + (plan != null && plan.HomeGeo.HasValue ? ", home pin" : FormatFsHomePinHint(driverProfile, tabName))
                + ".";
        }

        private bool IsFsMapPreloadStale(int preloadGen) =>
            preloadGen != _fsMapPreloadGen || IsDisposed;

        private CancellationToken ReplaceFsMapWorkToken()
        {
            lock (_fsMapWorkCtsLock)
            {
                try { _fsMapWorkCts?.Cancel(); } catch { }
                _fsMapWorkCts?.Dispose();
                _fsMapWorkCts = new CancellationTokenSource();
                return _fsMapWorkCts.Token;
            }
        }

        private void CancelFsMapWorkToken()
        {
            lock (_fsMapWorkCtsLock)
            {
                try { _fsMapWorkCts?.Cancel(); } catch { }
                _fsMapWorkCts?.Dispose();
                _fsMapWorkCts = null;
            }
        }

        private void CancelFsMapPreloadIfRunning()
        {
            if (!_fsMapPreloadRunning)
                return;

            Interlocked.Increment(ref _fsMapPreloadGen);
            _fsMapPreloadRunning = false;
            CancelFsMapWorkToken();

            int loadingGen = _fsMapLoadingRefreshGen;
            if (loadingGen != 0)
                FsMapTryPopRouteLoading(loadingGen);
            if (_fsMapOsrmLoadGen != 0)
                _fsMapOsrmLoadGen = 0;
        }

        private void EnsureFsMapRefreshDebounceTimer()
        {
            if (_fsMapRefreshDebounceTimer != null)
                return;

            _fsMapRefreshDebounceTimer = new System.Windows.Forms.Timer { Interval = 300 };
            _fsMapRefreshDebounceTimer.Tick += async (s, e) =>
            {
                _fsMapRefreshDebounceTimer.Stop();
                if (!_fsMapRefreshDebouncePending || _fsMap == null)
                    return;

                _fsMapRefreshDebouncePending = false;
                try
                {
                    await RefreshFsMapForCurrentTabAsync().ConfigureAwait(true);
                }
                catch
                {
                    /* desk preview — stale refresh superseded */
                }
            };
        }

        /// <summary>Coalesce rapid map refresh requests (drag, cut/paste, settings toggles).</summary>
        private void RequestFsMapRefresh()
        {
            if (_fsMap == null || string.IsNullOrWhiteSpace(_fsActiveDriverTab))
                return;
            if (_fsMapPreloadRunning)
                return;

            EnsureFsMapRefreshDebounceTimer();
            _fsMapRefreshDebouncePending = true;
            _fsMapRefreshDebounceTimer.Stop();
            _fsMapRefreshDebounceTimer.Start();
        }

        private static List<MCDownloadedTrip> CollectAllFsMapTripsDistinct(
            IDictionary<string, List<ScheduleBuilderPreviewLine>> linesByTab)
        {
            var trips = new List<MCDownloadedTrip>();
            if (linesByTab == null || linesByTab.Count == 0)
                return trips;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in linesByTab)
            {
                foreach (var trip in CollectFsMapTrips(kv.Value))
                {
                    if (trip == null)
                        continue;
                    string key = (trip.TripNumber ?? "").Trim();
                    if (key.Length == 0 || !seen.Add(key))
                        continue;
                    trips.Add(trip);
                }
            }

            return trips;
        }

        /// <summary>Build map payload for one tab using shared geocode results (no routing probe).</summary>
        private async Task<FsMapRefreshPayload> BuildFsMapPayloadForTabFromGeocodesAsync(
            string tabName,
            IList<ScheduleBuilderPreviewLine> lines,
            bool isReservesTab,
            bool showGroupColors,
            Dictionary<string, GeoPoint> pickup,
            Dictionary<string, GeoPoint> dropoff,
            SupeyDriverProfile driverProfile,
            int gen,
            bool checkRefreshStale,
            CancellationToken token)
        {
            if (checkRefreshStale && (IsFsMapRefreshStale(gen, tabName) || token.IsCancellationRequested))
                return FsMapAbortedPayload();

            var groups = BuildFsMapGroupsFromLines(lines, isReservesTab, showGroupColors);
            foreach (var g in groups)
            {
                if (g == null) continue;
                ScheduleBuilderPreviewGroups.ApplyGeocodes(g, pickup, dropoff);
            }

            GeoPoint? homeGeo = null;
            if (!isReservesTab && driverProfile != null)
            {
                homeGeo = await ScheduleBuilderDriverMapRouting.ResolveHomeGeoAsync(
                    driverProfile, token).ConfigureAwait(false);
            }

            if (checkRefreshStale && (IsFsMapRefreshStale(gen, tabName) || token.IsCancellationRequested))
                return FsMapAbortedPayload();

            var routeHome = (!isReservesTab && showGroupColors) ? homeGeo : (GeoPoint?)null;
            ScheduleBuilderPreviewGroups.BuildDeskRoutePolylines(groups, routeHome);

            if (driverProfile != null && string.IsNullOrWhiteSpace(driverProfile.ScheduleTabKey))
                driverProfile.ScheduleTabKey = tabName;

            var plan = new SupeyDriverPlan
            {
                Driver = driverProfile ?? new SupeyDriverProfile { Name = tabName, ScheduleTabKey = tabName },
            };
            plan.Groups.AddRange(groups);
            if (!isReservesTab && homeGeo.HasValue && SupeyMapWorkspace.IsValidGeoPoint(homeGeo.Value))
                plan.HomeGeo = homeGeo;

            int pinCount = (pickup?.Count ?? 0) + (dropoff?.Count ?? 0);
            string statusMessage = BuildFsMapStatusMessage(
                tabName, isReservesTab, showGroupColors, pinCount, plan.Groups.Count,
                0, plan.Groups.Count, plan, driverProfile, osrmPending: true);

            return new FsMapRefreshPayload
            {
                Pickup = pickup,
                Dropoff = dropoff,
                Groups = plan.Groups,
                Plan = plan,
                DriverProfile = driverProfile,
                StatusMessage = statusMessage,
                HasOsrmRoutes = false,
                RoutingOk = true,
            };
        }

        /// <summary>Geocode + desk routes for one tab (probe routing only when gate is cold).</summary>
        private async Task<FsMapRefreshPayload> BuildFsMapPreviewPayloadAsync(
            string tabName,
            IList<ScheduleBuilderPreviewLine> lines,
            List<MCDownloadedTrip> trips,
            bool isReservesTab,
            bool showGroupColors,
            SupeyDriverProfile driverProfile,
            int gen,
            CancellationToken token)
        {
            if (IsFsMapRefreshStale(gen, tabName) || token.IsCancellationRequested)
                return FsMapAbortedPayload();

            if (!ScheduleOsrmGate.PreviewRoutingOk)
            {
                FsMapSetRouteLoadingMessage(gen, "Checking routing…");
                var (routingOk, routingDetail) = await ScheduleOsrmGate.ProbePreviewRoutingAsync(
                    HiatmeAiSettings.Load(), token).ConfigureAwait(false);

                if (IsFsMapRefreshStale(gen, tabName) || token.IsCancellationRequested)
                    return FsMapAbortedPayload();

                if (!routingOk)
                {
                    return new FsMapRefreshPayload
                    {
                        RoutingOk = false,
                        RoutingDetail = routingDetail,
                    };
                }
            }

            FsMapSetRouteLoadingMessage(gen, "Geocoding trips…");
            var pickup = new Dictionary<string, GeoPoint>(StringComparer.OrdinalIgnoreCase);
            var dropoff = new Dictionary<string, GeoPoint>(StringComparer.OrdinalIgnoreCase);
            await ScheduleBuilderMapGeocode.ResolveTripsForMapAsync(
                trips, pickup, dropoff, token).ConfigureAwait(false);

            if (IsFsMapRefreshStale(gen, tabName) || token.IsCancellationRequested)
                return FsMapAbortedPayload();

            return await BuildFsMapPayloadForTabFromGeocodesAsync(
                tabName,
                lines,
                isReservesTab,
                showGroupColors,
                pickup,
                dropoff,
                driverProfile,
                gen,
                checkRefreshStale: true,
                token).ConfigureAwait(false);
        }

        /// <summary>Upgrade preview routes to OSRM on a worker thread (group tours only).</summary>
        private async Task<(int roadGroups, int straightGroups)> UpgradeFsMapPayloadWithOsrmAsync(
            FsMapRefreshPayload payload,
            bool isReservesTab,
            bool showGroupColors,
            int gen,
            CancellationToken token)
        {
            if (payload?.Groups == null || payload.Groups.Count == 0)
                return (0, 0);

            if (token.IsCancellationRequested)
                return (0, 0);

            GeoPoint? homeGeo = payload.Plan?.HomeGeo;
            var routeHome = (!isReservesTab && showGroupColors) ? homeGeo : (GeoPoint?)null;
            var routeProgress = new Progress<(int Done, int Total)>(p =>
            {
                string msg = p.Total > 1
                    ? "Loading road routes… " + p.Done + "/" + p.Total
                    : "Loading road routes…";
                FsMapSetRouteLoadingMessage(gen, msg);
            });

            Task<(int roadGroups, int straightGroups)> groupRoutesTask =
                ScheduleBuilderPreviewGroups.BuildOsrmRoutePolylinesAsync(
                    payload.Groups, routeHome, token, routeProgress);

            Task tripLegsTask = Task.CompletedTask;
            if (showGroupColors && !isReservesTab)
            {
                tripLegsTask = ScheduleBuilderPreviewGroups.BuildTripLegPolylinesAsync(
                    payload.Groups, token);
            }

            await Task.WhenAll(groupRoutesTask, tripLegsTask).ConfigureAwait(false);
            return groupRoutesTask.Result;
        }

        private void QueueFsMapRefreshApply(
            int gen,
            string tabName,
            bool isReservesTab,
            bool showGroupColors,
            FsMapRefreshPayload payload,
            bool fromCache,
            bool finalizeSelectionSync)
        {
            if (payload == null)
                return;

            if (payload.Aborted)
                return;

            if (!payload.RoutingOk)
            {
                FsMapBeginInvoke(() =>
                {
                    if (IsFsMapRefreshStale(gen, tabName) || _fsMap == null)
                        return;

                    SetFsMapPreviewAvailable(false, payload.RoutingDetail);
                    SetScheduleBuilderStatus(tabName
                        + " · map hidden (road routing offline — trip list still works).");
                });
                FsMapTryPopRouteLoading(gen);
                return;
            }

            FsMapBeginInvoke(() =>
            {
                if (IsFsMapRefreshStale(gen, tabName) || _fsMap == null)
                    return;

                ApplyFsMapRefreshPayloadCore(
                    gen,
                    tabName,
                    isReservesTab,
                    showGroupColors,
                    payload,
                    fromCache,
                    finalizeSelectionSync);
            });
        }

        private void ApplyFsMapRefreshPayloadCore(
            int gen,
            string tabName,
            bool isReservesTab,
            bool showGroupColors,
            FsMapRefreshPayload payload,
            bool fromCache,
            bool finalizeSelectionSync)
        {
            if (_fsMap == null || payload?.Plan == null)
                return;

            _fsMapPickupByTrip = payload.Pickup ?? new Dictionary<string, GeoPoint>(StringComparer.OrdinalIgnoreCase);
            _fsMapDropoffByTrip = payload.Dropoff ?? new Dictionary<string, GeoPoint>(StringComparer.OrdinalIgnoreCase);
            if (payload.Groups != null)
                _fsGroupsByTab[tabName] = payload.Groups;

            SetFsMapPreviewAvailable(true, showGroupKey: !isReservesTab && showGroupColors);
            _fsMap.TripFlatMapMode = isReservesTab || !showGroupColors;
            _fsMap.UseGroupRouteColors = !isReservesTab && _fsShowGroupColors;

            bool centerMaineAfterBuild = _fsCenterMaineAfterBuild;
            if (centerMaineAfterBuild)
                _fsCenterMaineAfterBuild = false;

            _fsMap.ShowDriverPlan(
                payload.Plan,
                autoFitViewport: !centerMaineAfterBuild,
                restoreSavedLegend: !_fsShowAllGroupsOnNextMapLoad);

            if (centerMaineAfterBuild || !SupeyMapWorkspace.HasValidMapPins(payload.Plan))
                _fsMap.CenterOnMaineHub();

            if (!string.IsNullOrWhiteSpace(payload.StatusMessage))
                SetScheduleBuilderStatus(payload.StatusMessage);

            if (payload.HasOsrmRoutes && !fromCache)
            {
                SaveFsTabMapCache(
                    tabName,
                    isReservesTab,
                    payload.Plan,
                    payload.Pickup,
                    payload.Dropoff,
                    payload.StatusMessage);
            }

            if (finalizeSelectionSync)
            {
                FinalizeFsMapAfterRefresh();
                FsMapTryPopRouteLoading(gen);
            }
        }

        private void FsMapClearEmptyOnUi(string tabName)
        {
            if (_fsMap == null)
                return;

            _fsMap.SaveLegendSnapshotForTabIfLoaded(tabName);
            _fsMap.Clear();
            _fsMap.ClearMileageHud();
            _fsMapPickupByTrip.Clear();
            _fsMapDropoffByTrip.Clear();
            SetFsMapPreviewAvailable(ScheduleOsrmGate.PreviewRoutingOk);
            _fsMap.CenterOnMaineHub();
        }

        private async Task RefreshFsMapCoreAsync(
            int gen,
            string tabName,
            CancellationToken token,
            bool reuseExistingLoadingOverlay = false)
        {
            bool isReservesTab = tabName.Equals("Reserves", StringComparison.OrdinalIgnoreCase);

            if (!_fsLinesByTab.TryGetValue(tabName, out var lines))
            {
                FsMapBeginInvoke(() => FsMapClearEmptyOnUi(tabName));
                return;
            }

            var trips = CollectFsMapTrips(lines);
            if (trips.Count == 0)
            {
                FsMapBeginInvoke(() => FsMapClearEmptyOnUi(tabName));
                if (reuseExistingLoadingOverlay)
                    FsMapTryPopRouteLoading(gen);
                return;
            }

            if (TryRestoreFsTabMapFromCache(tabName, gen, isReservesTab))
                return;

            bool showGroupColors = FsShowGroupColorsEnabled;
            EnsureFsDriverRosterLoaded();
            SupeyDriverProfile driverProfile = null;
            if (!isReservesTab)
            {
                driverProfile = ScheduleBuilderDriverMapRouting.FindProfileForScheduleTab(
                    _supeyRoster, tabName);
            }

            if (!reuseExistingLoadingOverlay)
            {
                _fsMapOsrmLoadGen = gen;
                FsMapPushRouteLoading(gen, "Loading map…");
            }
            else
            {
                _fsMapOsrmLoadGen = gen;
            }

            bool dismissLoadingInFinally = !reuseExistingLoadingOverlay;
            try
            {
                FsMapRefreshPayload payload = await Task.Run(async () =>
                    await BuildFsMapPreviewPayloadAsync(
                        tabName, lines, trips, isReservesTab, showGroupColors, driverProfile, gen, token)
                        .ConfigureAwait(false), token).ConfigureAwait(false);

                if (IsFsMapRefreshStale(gen, tabName) || payload.Aborted || token.IsCancellationRequested)
                {
                    if (reuseExistingLoadingOverlay)
                        FsMapTryPopRouteLoading(gen);
                    return;
                }

                if (!payload.RoutingOk)
                {
                    dismissLoadingInFinally = false;
                    QueueFsMapRefreshApply(gen, tabName, isReservesTab, showGroupColors, payload, false, true);
                    return;
                }

                if (IsFsMapRefreshStale(gen, tabName) || token.IsCancellationRequested)
                {
                    if (reuseExistingLoadingOverlay)
                        FsMapTryPopRouteLoading(gen);
                    return;
                }

                FsMapSetRouteLoadingMessage(gen, "Loading road routes…");
                FsMapReportStatusThrottled(gen, tabName, "loading road routes…");

                var routeCounts = await Task.Run(async () =>
                    await UpgradeFsMapPayloadWithOsrmAsync(
                        payload, isReservesTab, showGroupColors, gen, token).ConfigureAwait(false), token).ConfigureAwait(false);

                if (IsFsMapRefreshStale(gen, tabName) || token.IsCancellationRequested)
                {
                    if (reuseExistingLoadingOverlay)
                        FsMapTryPopRouteLoading(gen);
                    return;
                }

                payload.HasOsrmRoutes = true;
                int pinCount = (payload.Pickup?.Count ?? 0) + (payload.Dropoff?.Count ?? 0);
                payload.StatusMessage = BuildFsMapStatusMessage(
                    tabName,
                    isReservesTab,
                    showGroupColors,
                    pinCount,
                    payload.Groups?.Count ?? 0,
                    routeCounts.roadGroups,
                    routeCounts.straightGroups,
                    payload.Plan,
                    payload.DriverProfile,
                    osrmPending: false);

                dismissLoadingInFinally = false;
                QueueFsMapRefreshApply(
                    gen, tabName, isReservesTab, showGroupColors, payload, false, finalizeSelectionSync: true);
            }
            finally
            {
                if (_fsMapOsrmLoadGen == gen)
                    _fsMapOsrmLoadGen = 0;
                if (dismissLoadingInFinally)
                    FsMapTryPopRouteLoading(gen);
            }
        }

        private void StartFsMapPreloadAfterScheduleBind(IReadOnlyList<string> tabNames)
        {
            if (_fsMap == null || tabNames == null || tabNames.Count == 0)
                return;
            if (string.IsNullOrWhiteSpace(_fsActiveDriverTab))
                return;

            // Load only the active tab; other tabs refresh lazily on first visit.
            _ = RefreshFsMapForCurrentTabAsync();
        }
    }
}
