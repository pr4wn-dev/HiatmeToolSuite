using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    public partial class Form1
    {
        private ContextMenuStrip _fsTripsCtxMenu;
        private ToolStripMenuItem _fsTripsCtxBanClient;
        private ToolStripMenuItem _fsTripsCtxUnbanClient;
        private ToolStripMenuItem _fsTripsCtxFocusMap;
        private ToolStripMenuItem _fsTripsCtxCopyForAi;
        private ToolStripMenuItem _fsTripsCtxCopyCurrentTab;
        private ToolStripMenuItem _fsTripsCtxCopySelectedTrip;
        private ToolStripMenuItem _fsTripsCtxAutoSortGroup;
        private ToolStripMenuItem _fsTripsCtxGeocodeDriverHome;
        private ToolStripMenuItem _fsTripsCtxCutTrip;
        private ToolStripMenuItem _fsTripsCtxInsertAbove;
        private ToolStripMenuItem _fsTripsCtxInsertBelow;
        private ToolStripMenuItem _fsTripsCtxClearCut;
        private MCDownloadedTrip _fsTripsCtxTrip;
        private SupeyTripCluster _fsTripsCtxGroup;

        private void BuildFsTripsContextMenu()
        {
            _fsTripsCtxMenu = new ContextMenuStrip
            {
                Renderer = new DarkContextMenuRenderer(),
                BackColor = DarkContextMenuRenderer.Background,
                ForeColor = DarkContextMenuRenderer.ForeColor,
                ShowImageMargin = true,
            };

            _fsTripsCtxBanClient = new ToolStripMenuItem("Ban client")
            {
                BackColor = DarkContextMenuRenderer.Background,
                ForeColor = DarkContextMenuRenderer.ForeColor,
                Image = MenuIconFactory.GetUnassignIcon(),
            };
            _fsTripsCtxBanClient.Click += (s, e) =>
            {
                if (_fsTripsCtxTrip != null)
                    FsBanClientFromTrip(_fsTripsCtxTrip, quietWhenMissing: true);
            };

            _fsTripsCtxUnbanClient = new ToolStripMenuItem("Remove from banned list")
            {
                BackColor = DarkContextMenuRenderer.Background,
                ForeColor = DarkContextMenuRenderer.ForeColor,
                Image = MenuIconFactory.GetAssignIcon(),
            };
            _fsTripsCtxUnbanClient.Click += (s, e) =>
            {
                if (_fsTripsCtxTrip != null)
                    FsUnbanClientFromTrip(_fsTripsCtxTrip);
            };

            _fsTripsCtxFocusMap = new ToolStripMenuItem("Show on map")
            {
                BackColor = DarkContextMenuRenderer.Background,
                ForeColor = DarkContextMenuRenderer.ForeColor,
                Image = MenuIconFactory.GetLocateIcon(),
            };
            _fsTripsCtxFocusMap.Click += (s, e) =>
            {
                if (_fsTripsCtxTrip != null && _fsMap != null && _fsMap.Visible)
                    _fsMap.FocusTrip(_fsTripsCtxTrip);
            };

            _fsTripsCtxCopyForAi = new ToolStripMenuItem("Copy for AI review (Cursor)")
            {
                BackColor = DarkContextMenuRenderer.Background,
                ForeColor = DarkContextMenuRenderer.ForeColor,
            };
            _fsTripsCtxCopyForAi.Click += (s, e) => FsCopyScheduleForAiReviewToClipboard();

            _fsTripsCtxCopyCurrentTab = new ToolStripMenuItem("Copy current tab")
            {
                BackColor = DarkContextMenuRenderer.Background,
                ForeColor = DarkContextMenuRenderer.ForeColor,
            };
            _fsTripsCtxCopyCurrentTab.Click += (s, e) => FsCopyCurrentTabToClipboard();

            _fsTripsCtxCopySelectedTrip = new ToolStripMenuItem("Copy selected trip")
            {
                BackColor = DarkContextMenuRenderer.Background,
                ForeColor = DarkContextMenuRenderer.ForeColor,
            };
            _fsTripsCtxCopySelectedTrip.Click += (s, e) => FsCopySelectedTripToClipboard();

            _fsTripsCtxAutoSortGroup = new ToolStripMenuItem("Auto-sort group for best route efficiency")
            {
                BackColor = DarkContextMenuRenderer.Background,
                ForeColor = DarkContextMenuRenderer.ForeColor,
            };
            _fsTripsCtxAutoSortGroup.Click += (s, e) =>
            {
                if (_fsTripsCtxGroup != null)
                    _ = FsAutoSortGroupForEfficiencyAsync(_fsTripsCtxGroup);
            };

            _fsTripsCtxGeocodeDriverHome = new ToolStripMenuItem("Geocode driver home")
            {
                BackColor = DarkContextMenuRenderer.Background,
                ForeColor = DarkContextMenuRenderer.ForeColor,
            };
            _fsTripsCtxGeocodeDriverHome.Click += (s, e) =>
            {
                _ = FsGeocodeActiveTabDriverHomeAsync();
            };

            _fsTripsCtxCutTrip = new ToolStripMenuItem("Cut trip")
            {
                BackColor = DarkContextMenuRenderer.Background,
                ForeColor = DarkContextMenuRenderer.ForeColor,
            };
            _fsTripsCtxCutTrip.Click += (s, e) => FsCutSelectedTrip();

            _fsTripsCtxInsertAbove = new ToolStripMenuItem("Insert above")
            {
                BackColor = DarkContextMenuRenderer.Background,
                ForeColor = DarkContextMenuRenderer.ForeColor,
            };
            _fsTripsCtxInsertAbove.Click += (s, e) => FsInsertCutTrip(below: false);

            _fsTripsCtxInsertBelow = new ToolStripMenuItem("Insert below")
            {
                BackColor = DarkContextMenuRenderer.Background,
                ForeColor = DarkContextMenuRenderer.ForeColor,
            };
            _fsTripsCtxInsertBelow.Click += (s, e) => FsInsertCutTrip(below: true);

            _fsTripsCtxClearCut = new ToolStripMenuItem("Clear cut trip")
            {
                BackColor = DarkContextMenuRenderer.Background,
                ForeColor = DarkContextMenuRenderer.ForeColor,
            };
            _fsTripsCtxClearCut.Click += (s, e) =>
            {
                FsClearCutTrip();
                SetScheduleBuilderStatus("Cut trip cleared.");
            };

            _fsTripsCtxMenu.Items.Add(_fsTripsCtxBanClient);
            _fsTripsCtxMenu.Items.Add(_fsTripsCtxUnbanClient);
            _fsTripsCtxMenu.Items.Add(new ToolStripSeparator());
            _fsTripsCtxMenu.Items.Add(_fsTripsCtxCutTrip);
            _fsTripsCtxMenu.Items.Add(_fsTripsCtxInsertAbove);
            _fsTripsCtxMenu.Items.Add(_fsTripsCtxInsertBelow);
            _fsTripsCtxMenu.Items.Add(_fsTripsCtxClearCut);
            _fsTripsCtxMenu.Items.Add(new ToolStripSeparator());
            _fsTripsCtxMenu.Items.Add(_fsTripsCtxAutoSortGroup);
            _fsTripsCtxMenu.Items.Add(_fsTripsCtxGeocodeDriverHome);
            _fsTripsCtxMenu.Items.Add(new ToolStripSeparator());
            _fsTripsCtxMenu.Items.Add(_fsTripsCtxFocusMap);
            _fsTripsCtxMenu.Items.Add(new ToolStripSeparator());
            _fsTripsCtxMenu.Items.Add(_fsTripsCtxCopyForAi);
            _fsTripsCtxMenu.Items.Add(_fsTripsCtxCopyCurrentTab);
            _fsTripsCtxMenu.Items.Add(_fsTripsCtxCopySelectedTrip);
        }

        private async void FsTripsLv_MouseUp_ShowContextMenu(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right || _fsTripsLv == null) return;

            try
            {
                await ScheduleOsrmGate.ProbePreviewServicesAsync(
                    HiatmeAiSettings.Load(), CancellationToken.None).ConfigureAwait(true);
            }
            catch { }

            var hit = _fsTripsLv.HitTest(e.Location);
            _fsTripsCtxTrip = null;
            _fsTripsCtxGroup = null;
            _fsTripsCtxHitItem = hit.Item;
            if (hit.Item != null)
            {
                if (hit.Item.Tag is FsPreviewNoteTag noteTag)
                    _fsTripsCtxGroup = noteTag.Group;
                else if (hit.Item.Tag is FsPreviewTripTag tripTag)
                {
                    _fsTripsCtxTrip = tripTag.Trip;
                    _fsTripsCtxGroup = tripTag.Group;
                }
                else
                    _fsTripsCtxTrip = GetFsTripFromListItem(hit.Item);

                if (_fsTripsCtxTrip != null)
                {
                    hit.Item.Selected = true;
                    hit.Item.Focused = true;
                }
            }

            bool hasTrip = _fsTripsCtxTrip != null;
            bool isBanned = hasTrip && ScheduleBuilderBannedClients.IsBanned(_fsTripsCtxTrip);
            bool isReserves = _fsActiveDriverTab != null
                && _fsActiveDriverTab.Equals("Reserves", StringComparison.OrdinalIgnoreCase);
            bool canSortGroup = _fsHasPreview
                && !isReserves
                && _fsTripsCtxGroup != null
                && _fsTripsCtxGroup.Trips != null
                && _fsTripsCtxGroup.Trips.Count >= 2;

            bool hasBuild = _fsHasPreview && fsbuilder != null;
            bool canMoveTrips = hasBuild && !string.IsNullOrWhiteSpace(_fsActiveDriverTab);

            EnsureFsDriverRosterLoaded();
            var activeDriverProfile = ScheduleBuilderDriverMapRouting.FindProfileForScheduleTab(
                _supeyRoster, _fsActiveDriverTab);
            bool hasHomeAddress = activeDriverProfile != null
                && (!string.IsNullOrWhiteSpace(activeDriverProfile.HomeStreet)
                    || !string.IsNullOrWhiteSpace(activeDriverProfile.HomeCity));
            bool canGeocodeDriverHome = !isReserves && hasHomeAddress && ScheduleOsrmGate.PreviewGeoOk;
            bool canInsertCut = FsCanInsertCutTripAtContext();

            _fsTripsCtxCutTrip.Enabled = hasTrip && canMoveTrips && !FsHasCutTrip;
            _fsTripsCtxInsertAbove.Enabled = canInsertCut;
            _fsTripsCtxInsertBelow.Enabled = canInsertCut;
            _fsTripsCtxClearCut.Enabled = FsHasCutTrip;
            _fsTripsCtxInsertAbove.Text = FsHasCutTrip
                ? "Insert above" + FsCutTripMenuSuffix()
                : "Insert above";
            _fsTripsCtxInsertBelow.Text = FsHasCutTrip
                ? "Insert below" + FsCutTripMenuSuffix()
                : "Insert below";

            _fsTripsCtxBanClient.Enabled = hasTrip;
            _fsTripsCtxUnbanClient.Enabled = hasTrip && isBanned;
            _fsTripsCtxFocusMap.Enabled = hasTrip
                && _fsMap != null
                && _fsMap.Visible
                && ScheduleOsrmGate.PreviewRoutingOk;
            _fsTripsCtxAutoSortGroup.Enabled = canSortGroup && ScheduleOsrmGate.PreviewRoutingOk;
            _fsTripsCtxGeocodeDriverHome.Enabled = canGeocodeDriverHome;
            _fsTripsCtxCopyForAi.Enabled = hasBuild;
            _fsTripsCtxCopyCurrentTab.Enabled = hasBuild;
            _fsTripsCtxCopySelectedTrip.Enabled = hasTrip;

            if (canSortGroup)
            {
                _fsTripsCtxAutoSortGroup.Text = ScheduleOsrmGate.PreviewRoutingOk
                    ? "Auto-sort group "
                        + _fsTripsCtxGroup.GroupNumber + " for best route efficiency (100%)"
                    : "Auto-sort group (road routing offline)";
            }
            else
            {
                _fsTripsCtxAutoSortGroup.Text = "Auto-sort group for best route efficiency (100%)";
            }

            if (canGeocodeDriverHome && activeDriverProfile != null)
            {
                _fsTripsCtxGeocodeDriverHome.Text = "Geocode driver home — "
                    + (activeDriverProfile.Name ?? _fsActiveDriverTab ?? "driver").Trim();
            }
            else if (!ScheduleOsrmGate.PreviewGeoOk && hasHomeAddress && !isReserves)
            {
                _fsTripsCtxGeocodeDriverHome.Text = "Geocode driver home (office server offline)";
            }
            else
            {
                _fsTripsCtxGeocodeDriverHome.Text = "Geocode driver home";
            }

            if (hasTrip)
            {
                string name = (_fsTripsCtxTrip.ClientFullName ?? "").Trim();
                string age = string.IsNullOrWhiteSpace(_fsTripsCtxTrip.Age) ? "" : " · age " + _fsTripsCtxTrip.Age;
                _fsTripsCtxBanClient.Text = isBanned
                    ? "Ban client (already banned)"
                    : "Ban client — " + name + age;
                _fsTripsCtxUnbanClient.Text = "Remove ban — " + name + age;
            }
            else
            {
                _fsTripsCtxBanClient.Text = "Ban client";
                _fsTripsCtxUnbanClient.Text = "Remove from banned list";
            }

            _fsTripsCtxMenu.Show(_fsTripsLv, e.Location);
        }

        private string FsCutTripMenuSuffix()
        {
            if (_fsCutTrip == null) return "";
            string num = (_fsCutTrip.TripNumber ?? "").Trim();
            return string.IsNullOrEmpty(num) ? "" : " (" + num + ")";
        }

        private void FsCopyScheduleForAiReviewToClipboard()
        {
            if (!_fsHasPreview || fsbuilder == null)
            {
                MessageBox.Show(this, "Run BUILD first, then copy for AI review.", "Schedule Builder",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            try
            {
                DateTime date = fsbdatepicker?.Value ?? DateTime.Today;
                string folder = date.DayOfWeek.ToString();
                string text = ScheduleBuilderReviewExport.BuildFull(
                    date, folder, fsbuilder, _fsLinesByTab, _fsActiveDriverTab);
                Clipboard.SetText(text);
                SetScheduleBuilderStatus("Copied full schedule for AI review — paste into Cursor chat.");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not copy to clipboard:\n\n" + ex.Message, "Schedule Builder",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void FsCopyCurrentTabToClipboard()
        {
            if (!_fsHasPreview || fsbuilder == null || string.IsNullOrWhiteSpace(_fsActiveDriverTab))
            {
                MessageBox.Show(this, "Run BUILD and select a driver or Reserves tab first.", "Schedule Builder",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (!_fsLinesByTab.TryGetValue(_fsActiveDriverTab, out var lines))
                lines = new System.Collections.Generic.List<ScheduleBuilderPreviewLine>();
            try
            {
                DateTime date = fsbdatepicker?.Value ?? DateTime.Today;
                string text = ScheduleBuilderReviewExport.BuildTab(date, _fsActiveDriverTab, lines, fsbuilder);
                Clipboard.SetText(text);
                SetScheduleBuilderStatus("Copied \"" + _fsActiveDriverTab + "\" tab to clipboard.");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not copy to clipboard:\n\n" + ex.Message, "Schedule Builder",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void FsCopySelectedTripToClipboard()
        {
            if (_fsTripsCtxTrip == null) return;
            try
            {
                string text = ScheduleBuilderReviewExport.BuildSingleTrip(_fsTripsCtxTrip, _fsActiveDriverTab);
                Clipboard.SetText(text);
                SetScheduleBuilderStatus("Copied trip " + (_fsTripsCtxTrip.TripNumber ?? "") + " to clipboard.");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not copy to clipboard:\n\n" + ex.Message, "Schedule Builder",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private async Task FsAutoSortGroupForEfficiencyAsync(SupeyTripCluster group)
        {
            if (group?.Trips == null || group.Trips.Count < 2) return;
            if (!ScheduleOsrmGate.PreviewRoutingOk)
            {
                SetScheduleBuilderStatus("Auto-sort unavailable — road routing (OSRM) is offline.");
                return;
            }
            if (string.IsNullOrWhiteSpace(_fsActiveDriverTab)
                || _fsActiveDriverTab.Equals("Reserves", StringComparison.OrdinalIgnoreCase))
                return;

            string tab = _fsActiveDriverTab;
            if (!_fsLinesByTab.TryGetValue(tab, out var lines) || lines == null)
                return;

            int groupNumber = group.GroupNumber;
            MCDownloadedTrip ctxTrip = _fsTripsCtxTrip;

            SetScheduleBuilderStatus("Group " + groupNumber + " · finding best trip order…");

            EnsureFsDriverRosterLoaded();
            var driverProfile = ScheduleBuilderDriverMapRouting.FindProfileForScheduleTab(
                _supeyRoster, tab);
            GeoPoint? homeGeo = null;
            if (driverProfile != null)
            {
                try
                {
                    await HiatmeGeoSettings.RefreshConnectivityAsync(
                        HiatmeAiSettings.Load(), CancellationToken.None).ConfigureAwait(true);
                }
                catch { }

                homeGeo = await ScheduleBuilderDriverMapRouting.ResolveHomeGeoAsync(
                    driverProfile, CancellationToken.None).ConfigureAwait(true);
            }

            var dayPosition = ScheduleBuilderDriverMapRouting.GroupDayPosition.Middle;
            if (_fsGroupsByTab.TryGetValue(tab, out var tabGroups) && tabGroups != null)
            {
                int groupIndex = FindFsGroupIndex(tabGroups, group);
                dayPosition = ScheduleBuilderDriverMapRouting.ResolveDayPosition(
                    groupIndex, tabGroups.Count);
            }

            if (homeGeo.HasValue && !SupeyMapWorkspace.IsValidGeoPoint(homeGeo.Value))
                homeGeo = null;

            var (bestOrder, scorePercent, alreadyOptimal, approx) =
                await ScheduleBuilderMapMileage.FindBestTripOrderAsync(
                    group, homeGeo, dayPosition, CancellationToken.None).ConfigureAwait(true);

            if (bestOrder == null || bestOrder.Count == 0)
            {
                SetScheduleBuilderStatus("Group " + group.GroupNumber
                    + " · could not sort (trips need geocoded PU/DO pins).");
                return;
            }

            if (alreadyOptimal)
            {
                FsSelectGroupInListView(groupNumber);
                FsTripsLv_SelectionChangedUpdateMap();
                SetScheduleBuilderStatus("Group " + groupNumber
                    + " is already at 100% route efficiency.");
                return;
            }

            if (!ScheduleBuilderPreviewDrag.ApplyTripOrderToGroup(
                    lines, groupNumber, bestOrder))
            {
                SetScheduleBuilderStatus("Group " + groupNumber + " · could not apply sort.");
                return;
            }

            lines = ScheduleBuilderTemplateSlots.CollapseConsecutivePreviewGaps(lines);
            FsCommitPreviewLinesForTab(tab, lines);

            ShowFsTripsForTab(tab);
            await RefreshFsMapForCurrentTabAsync().ConfigureAwait(true);

            if (ctxTrip != null)
                SelectFsTripInListView(ctxTrip);
            else
                FsSelectGroupInListView(groupNumber);
            FsTripsLv_SelectionChangedUpdateMap();

            string scoreText = scorePercent.HasValue
                ? scorePercent.Value.ToString("0") + "% route efficiency"
                : "sorted";
            if (approx)
                scoreText += " (approx)";
            SetScheduleBuilderStatus("Group " + groupNumber + " auto-sorted · " + scoreText + ".");
        }

        private async Task FsGeocodeActiveTabDriverHomeAsync()
        {
            await FsGeocodeDriverHomeAsync(
                ScheduleBuilderDriverMapRouting.FindProfileForScheduleTab(_supeyRoster, _fsActiveDriverTab),
                _fsActiveDriverTab).ConfigureAwait(true);
        }

        internal async Task FsGeocodeDriverHomeAsync(SupeyDriverProfile profile, string tabLabel = null)
        {
            if (profile == null)
            {
                SetScheduleBuilderStatus("No driver roster match"
                    + (string.IsNullOrWhiteSpace(tabLabel) ? "." : " for tab \"" + tabLabel.Trim() + "\"."));
                return;
            }

            if (!ScheduleOsrmGate.PreviewGeoOk)
            {
                SetScheduleBuilderStatus("Geocode unavailable — office AI server is offline.");
                return;
            }

            if (string.IsNullOrWhiteSpace(profile.HomeStreet) && string.IsNullOrWhiteSpace(profile.HomeCity))
            {
                SetScheduleBuilderStatus((profile.Name ?? "Driver") + " has no home address in the roster.");
                return;
            }

            string who = (profile.Name ?? tabLabel ?? "Driver").Trim();
            SetScheduleBuilderStatus("Geocoding home for " + who + "…");

            GeoPoint? geo;
            try
            {
                geo = await ScheduleBuilderDriverMapRouting.ResolveHomeGeoAsync(
                    profile, CancellationToken.None).ConfigureAwait(true);
            }
            catch
            {
                geo = null;
            }

            if (geo.HasValue && SupeyMapWorkspace.IsValidGeoPoint(geo.Value))
            {
                SetScheduleBuilderStatus(who + " · home geocoded.");
                RefreshFsMapIfDriverTabActive();
                return;
            }

            SetScheduleBuilderStatus(who + " · could not geocode home (check address or server).");
        }

        private void FsCommitPreviewLinesForTab(string tab, List<ScheduleBuilderPreviewLine> lines)
        {
            _fsLinesByTab[tab] = lines;

            if (fsbuilder?.PreviewDriverLines != null)
            {
                var dict = fsbuilder.PreviewDriverLines as Dictionary<string, List<ScheduleBuilderPreviewLine>>;
                if (dict != null && !tab.Equals("Reserves", StringComparison.OrdinalIgnoreCase))
                    dict[tab] = lines;
            }

            if (tab.Equals("Reserves", StringComparison.OrdinalIgnoreCase))
                fsbuilder?.ApplyPreviewReserveLines(lines);

            if (fsbuilder?.driverTripList != null)
            {
                var trips = new List<MCDownloadedTrip>();
                foreach (var line in lines)
                {
                    if (line?.Kind == ScheduleBuilderPreviewLine.LineKind.Trip && line.Trip != null)
                        trips.Add(line.Trip);
                }
                fsbuilder.driverTripList[tab] = trips;
            }
        }
    }
}
