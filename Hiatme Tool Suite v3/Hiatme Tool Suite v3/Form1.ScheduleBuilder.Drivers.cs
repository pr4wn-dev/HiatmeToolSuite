using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    public partial class Form1
    {
        private SupeyListView _fsDriversLv;
        private Label _fsDriversFooter;
        private Label _fsDriversEmptyHint;
        private SupeyButton _fsDriverPullBtn;
        private SupeyButton _fsDriverAddBtn;
        private SupeyButton _fsDriverEditBtn;
        private SupeyButton _fsDriverRemoveBtn;
        private SupeyButton _fsDriverSaveBtn;
        private bool _fsDriverRosterLoaded;
        private ContextMenuStrip _fsDriversCtxMenu;
        private ToolStripMenuItem _fsDriversCtxGeocodeHome;

        private void EnsureFsDriverRosterLoaded()
        {
            if (_fsDriverRosterLoaded) return;
            if (!_supeyRosterLoadedFromDisk)
            {
                string warning;
                _supeyRoster = SupeyDriverRosterStore.Load(out warning);
                _supeyRosterLoadedFromDisk = true;
                ScheduleBuilderDriverEmailsRegistry.ApplyLocalRegistryToRoster(_supeyRoster);
                if (!string.IsNullOrEmpty(warning))
                    SetScheduleBuilderStatus(warning);
            }
            else
            {
                ScheduleBuilderDriverEmailsRegistry.ApplyLocalRegistryToRoster(_supeyRoster);
            }

            _fsDriverRosterLoaded = true;
        }

        private async Task SyncFsDriverEmailsAsync(bool reportOffline = false)
        {
            EnsureFsDriverRosterLoaded();
            var settings = HiatmeAiSettings.Load();
            var result = await ScheduleBuilderDriverEmailsRegistry.SyncWithServerAsync(
                settings,
                _supeyRoster,
                Environment.UserName).ConfigureAwait(true);

            if (result.RosterChanged)
            {
                SupeyDriverRosterStore.Save(_supeyRoster);
                RebuildFsDriversList();
                if (_supeyDriversLv != null)
                    RebuildSupeyDriversList();
            }

            if (reportOffline && result.ServerUsed && result.ServerUnreachable)
                SetScheduleBuilderStatus("Driver emails — using local copy (server unavailable).");
        }

        private void PersistFsDriverEmailsLocally()
        {
            try
            {
                EnsureFsDriverRosterLoaded();
                if (_supeyRoster == null || _supeyRoster.Count == 0)
                    return;
                ScheduleBuilderDriverEmailsRegistry.UpdateLocalFromRoster(_supeyRoster, Environment.UserName);
                ScheduleBuilderDriverEmailsRegistry.TryPushToServerFireAndForget(null, Environment.UserName);
            }
            catch
            {
                /* best-effort — roster JSON already saved */
            }
        }

        private void BuildFsDriversPanel(Panel host)
        {
            var btnRow = new TableLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 132,
                BackColor = SupeyTheme.SurfaceHeader,
                Padding = new Padding(10, 10, 10, 10),
                ColumnCount = 4,
                RowCount = 3,
            };
            for (int i = 0; i < 4; i++)
                btnRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
            btnRow.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));
            btnRow.RowStyles.Add(new RowStyle(SizeType.Absolute, 36f));
            btnRow.RowStyles.Add(new RowStyle(SizeType.Absolute, 24f));

            _fsDriverPullBtn = new SupeyButton
            {
                Text = "PULL FROM WELLRYDE",
                Kind = SupeyButton.Variant.Primary,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 0, 6),
            };
            _fsDriverPullBtn.Click += async (s, e) => await OnFsPullFromWellRydeAsync();

            _fsDriverAddBtn = new SupeyButton
            {
                Text = "ADD",
                Kind = SupeyButton.Variant.Secondary,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 4, 0),
            };
            _fsDriverAddBtn.Click += (s, e) => OnFsDriverAdd();

            _fsDriverEditBtn = new SupeyButton
            {
                Text = "EDIT",
                Kind = SupeyButton.Variant.Secondary,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 4, 0),
            };
            _fsDriverEditBtn.Click += async (s, e) => await OnFsDriverEditAsync();

            _fsDriverRemoveBtn = new SupeyButton
            {
                Text = "REMOVE",
                Kind = SupeyButton.Variant.Outlined,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 4, 0),
            };
            _fsDriverRemoveBtn.Click += (s, e) => OnFsDriverRemove();

            _fsDriverSaveBtn = new SupeyButton
            {
                Text = "SAVE",
                Kind = SupeyButton.Variant.Secondary,
                Dock = DockStyle.Fill,
            };
            _fsDriverSaveBtn.Click += (s, e) => SaveFsDriverRosterToDisk(showOk: true);

            _fsDriversFooter = new Label
            {
                Text = "0 drivers",
                Dock = DockStyle.Fill,
                ForeColor = SupeyTheme.TextMuted,
                BackColor = SupeyTheme.SurfaceHeader,
                Font = SupeyTheme.CaptionFont,
                TextAlign = ContentAlignment.MiddleLeft,
            };

            btnRow.Controls.Add(_fsDriverPullBtn, 0, 0);
            btnRow.SetColumnSpan(_fsDriverPullBtn, 4);
            btnRow.Controls.Add(_fsDriverAddBtn, 0, 1);
            btnRow.Controls.Add(_fsDriverEditBtn, 1, 1);
            btnRow.Controls.Add(_fsDriverRemoveBtn, 2, 1);
            btnRow.Controls.Add(_fsDriverSaveBtn, 3, 1);
            btnRow.Controls.Add(_fsDriversFooter, 0, 2);
            btnRow.SetColumnSpan(_fsDriversFooter, 4);

            _fsDriversLv = new SupeyListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                BackColor = SupeyTheme.ListBody,
                ForeColor = SupeyTheme.ListText,
                FullRowSelect = true,
                GridLines = true,
                HideSelection = false,
                MultiSelect = false,
                Font = new Font("Archivo Medium", 10f),
                OwnerDraw = true,
                UseCompatibleStateImageBehavior = false,
            };
            _fsDriversLv.DrawColumnHeader += FsDriversLv_DrawColumnHeader;
            _fsDriversLv.DrawItem += FsDriversLv_DrawItem;
            _fsDriversLv.DrawSubItem += FsDriversLv_DrawSubItem;
            _fsDriversLv.Columns.AddRange(new[]
            {
                new ColumnHeader { Text = "Driver", Width = 120 },
                new ColumnHeader { Text = "Email", Width = 160 },
                new ColumnHeader { Text = "Home", Width = 120 },
                new ColumnHeader { Text = "Cap", Width = 40 },
                new ColumnHeader { Text = "Shift", Width = 90 },
            });
            _fsDriversLv.DoubleClick += async (s, e) => await OnFsDriverEditAsync();

            BuildFsDriversContextMenu();
            _fsDriversLv.MouseUp += FsDriversLv_MouseUp_ShowContextMenu;

            _fsDriversEmptyHint = new Label
            {
                Dock = DockStyle.Fill,
                Text = "No drivers yet\n\nMatch names to driver tabs.\nADD or PULL FROM WELLRYDE.",
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = SupeyTheme.TextSecondary,
                BackColor = SupeyTheme.ListBody,
                Font = new Font("Segoe UI", 10f),
                Visible = true,
            };

            host.Controls.Add(_fsDriversEmptyHint);
            host.Controls.Add(_fsDriversLv);
            host.Controls.Add(btnRow);
            _fsDriversEmptyHint.BringToFront();

            try
            {
                ListViewSorter.Attach(_fsDriversLv);
                ListViewMinWidthEnforcer.Attach(_fsDriversLv);
                ListViewHeaderEmptyAreaPainter.Attach(_fsDriversLv);
            }
            catch { }

            RebuildFsDriversList();
        }

        private void RebuildFsDriversList()
        {
            if (_fsDriversLv == null) return;
            _fsDriversLv.BeginUpdate();
            _fsDriversLv.Items.Clear();
            foreach (var d in _supeyRoster)
            {
                if (d == null) continue;
                string home = ShortHomeLabel(d);
                string email = (d.Email ?? "").Trim();
                string shift = (d.ShiftStart ?? "—") + "-" + (d.ShiftEnd ?? "—");
                var item = new ListViewItem(new[] { d.Name ?? "", email, home, d.CapacityPassengers.ToString(), shift })
                {
                    Tag = d,
                };
                _fsDriversLv.Items.Add(item);
            }
            _fsDriversLv.EndUpdate();
            ListViewMinWidthEnforcer.Recompute(_fsDriversLv);
            UpdateFsSidePanelDriversWidth();

            int n = _supeyRoster.Count;
            _fsDriversFooter.Text = n + " driver" + (n == 1 ? "" : "s")
                + (_supeyRosterLastSaved == DateTime.MinValue ? "" : " · saved " + _supeyRosterLastSaved.ToString("HH:mm"));
            if (_fsDriversEmptyHint != null)
                _fsDriversEmptyHint.Visible = n == 0;
        }

        private void UpdateFsSidePanelDriversWidth()
        {
            if (_fsDriversLv == null || _fsSideTabPanel == null) return;
            int colTotal = 0;
            foreach (ColumnHeader col in _fsDriversLv.Columns)
                colTotal += col.Width;
            int needed = colTotal + SystemInformation.VerticalScrollBarWidth + 8;
            _fsSideTabPanel.SetPageRecommendedWidth(FsSidePageDrivers, needed);
        }

        private static string ShortHomeLabel(SupeyDriverProfile d)
        {
            if (d == null) return "";
            string city = (d.HomeCity ?? "").Trim();
            string street = (d.HomeStreet ?? "").Trim();
            if (city.Length > 0 && street.Length > 0)
            {
                if (street.Length > 18) street = street.Substring(0, 17) + "…";
                return street + ", " + city;
            }
            return d.FormatHomeOneLine();
        }

        private void SaveFsDriverRosterToDisk(bool showOk)
        {
            var saved = SupeyDriverRosterStore.Save(_supeyRoster);
            if (saved.Ok)
            {
                PersistFsDriverEmailsLocally();
                _supeyRosterLastSaved = saved.SavedAtLocal;
                RebuildFsDriversList();
                if (_supeyDriversLv != null)
                    RebuildSupeyDriversList();
                if (showOk)
                    SetScheduleBuilderStatus("Driver roster saved.");
                RefreshFsMapIfDriverTabActive();
            }
            else
            {
                SetScheduleBuilderStatus(saved.ErrorMessage);
            }
        }

        /// <summary>WellRyde roster refresh + home geocode during BUILD/LOAD; skips quietly on portal errors.</summary>
        internal async Task<FsDriverBuildSyncResult> SyncFsDriversDuringBuildAsync(
            IEnumerable<string> driverTabNames,
            Action<string> reportStatus = null)
        {
            var result = new FsDriverBuildSyncResult();
            EnsureFsDriverRosterLoaded();

            var tabs = (driverTabNames ?? Array.Empty<string>())
                .Where(n => !string.IsNullOrWhiteSpace(n)
                    && !n.Equals("Reserves", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (tabs.Count == 0)
                return result;

            reportStatus?.Invoke("Loading driver roster…");
            bool rosterChanged = false;

            bool wellRydeOk = await TryWellRydeSessionForFsBuildAsync().ConfigureAwait(true);
            List<WellRydeUserSummary> summaries = null;
            if (wellRydeOk)
            {
                reportStatus?.Invoke("Syncing drivers from WellRyde…");
                try
                {
                    summaries = await WellRydeUserSecLookup.LoadAllUserSummariesAsync(
                        _wellRydeSession, CancellationToken.None).ConfigureAwait(true);
                }
                catch
                {
                    summaries = null;
                }
            }
            else
            {
                result.WellRydeSkipped = true;
            }

            foreach (string tab in tabs)
            {
                var profile = ScheduleBuilderDriverMapRouting.FindProfileForScheduleTab(_supeyRoster, tab);
                bool isNew = profile == null;

                if (profile == null && summaries != null && summaries.Count > 0)
                {
                    var hit = WellRydeUserSecLookup.MatchProfile(
                        new SupeyDriverProfile { Name = tab.Trim() }, summaries);
                    if (hit != null)
                        profile = FindRosterDriverBySecIdOrName(hit.SecId, hit.FullName);
                }

                if (profile == null)
                {
                    profile = new SupeyDriverProfile { Name = tab.Trim(), ScheduleTabKey = tab.Trim() };
                    isNew = true;
                }
                else
                {
                    if (!string.Equals((profile.ScheduleTabKey ?? "").Trim(), tab.Trim(),
                            StringComparison.OrdinalIgnoreCase))
                    {
                        profile.ScheduleTabKey = tab.Trim();
                        rosterChanged = true;
                    }
                }

                if (summaries != null && summaries.Count > 0)
                {
                    if (string.IsNullOrWhiteSpace(profile.WellRydeSecId))
                    {
                        var hit = WellRydeUserSecLookup.MatchProfile(profile, summaries)
                            ?? WellRydeUserSecLookup.MatchProfile(
                                new SupeyDriverProfile { Name = tab.Trim() }, summaries);
                        if (hit != null && hit.IsEligibleForSchedule)
                        {
                            profile.WellRydeSecId = hit.SecId;
                            profile.WellRydeUsername = hit.Username;
                            rosterChanged = true;
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(profile.WellRydeSecId))
                    {
                        if (await TryImportWellRydeDetailAsync(
                                profile, profile.WellRydeSecId, isNew, CancellationToken.None)
                            .ConfigureAwait(true))
                        {
                            profile.ScheduleTabKey = tab.Trim();
                            if (isNew)
                            {
                                _supeyRoster.Add(profile);
                                result.ImportedFromWellRyde++;
                            }
                            else
                                result.RefreshedFromWellRyde++;
                            rosterChanged = true;
                        }
                    }
                    else if (isNew && !string.IsNullOrWhiteSpace(profile.HomeStreet))
                    {
                        _supeyRoster.Add(profile);
                        rosterChanged = true;
                    }
                }
            }

            reportStatus?.Invoke("Geocoding driver homes…");
            if (!HiatmeGeoSettings.UseServer && HiatmeGeoSettings.ServerOnly)
            {
                reportStatus?.Invoke("Driver home geocode skipped — office server offline.");
            }
            else
            {
                foreach (string tab in tabs)
                {
                    var profile = ScheduleBuilderDriverMapRouting.FindProfileForScheduleTab(_supeyRoster, tab);
                    if (profile == null) continue;
                    try
                    {
                        var geo = await ScheduleBuilderDriverMapRouting.ResolveHomeGeoAsync(
                            profile, CancellationToken.None).ConfigureAwait(false);
                        if (geo.HasValue)
                            result.HomesGeocoded++;
                    }
                    catch { }
                }
            }

            if (rosterChanged)
                SaveFsDriverRosterToDisk(showOk: false);
            else
                RebuildFsDriversList();

            return result;
        }

        internal static string FormatFsDriverSyncNote(FsDriverBuildSyncResult sync)
        {
            if (sync == null) return "";
            if (sync.WellRydeSkipped && sync.HomesGeocoded == 0 && sync.ImportedFromWellRyde == 0
                && sync.RefreshedFromWellRyde == 0)
                return " Drivers: saved roster (WellRyde skipped).";

            var parts = new List<string>();
            if (sync.RefreshedFromWellRyde > 0)
                parts.Add(sync.RefreshedFromWellRyde + " refreshed");
            if (sync.ImportedFromWellRyde > 0)
                parts.Add(sync.ImportedFromWellRyde + " imported");
            if (sync.HomesGeocoded > 0)
                parts.Add(sync.HomesGeocoded + " home" + (sync.HomesGeocoded == 1 ? "" : "s") + " geocoded");
            if (parts.Count == 0)
                return sync.WellRydeSkipped
                    ? " Drivers: roster loaded (WellRyde skipped)."
                    : " Drivers: roster synced.";
            return " Drivers: " + string.Join(", ", parts) + ".";
        }

        private async Task<bool> TryWellRydeSessionForFsBuildAsync()
        {
            if (_wellRydePanelSessionActive && _wellRydeSession != null)
            {
                try
                {
                    var nu = await _wellRydeSession.GetPortalNuAsync().ConfigureAwait(true);
                    if (nu.IsSuccess && _wellRydeSession.IsPortalNuPageAuthenticated())
                        return true;
                }
                catch { }
                InvalidateWellRydePortalSession();
            }

            if (!TryGetWellRydeCredentials(out string companycode, out string username, out string password))
                return false;

            try
            {
                string err = await TryWellRydePortalHttpLoginAsync(companycode, username, password)
                    .ConfigureAwait(true);
                if (err != null)
                    return false;
                _wellRydePanelSessionActive = true;
                return _wellRydeSession != null;
            }
            catch
            {
                return false;
            }
        }

        private async Task<bool> TryImportWellRydeDetailAsync(
            SupeyDriverProfile profile,
            string secId,
            bool isNewDriver,
            CancellationToken token)
        {
            if (_wellRydeSession == null || profile == null)
                return false;

            string sec = WellRydePortalSession.NormalizeUserSecId(secId);
            if (sec.Length == 0)
                return false;

            try
            {
                var res = await _wellRydeSession.GetUserDetailHtmlAsync(sec, token).ConfigureAwait(false);
                if (!res.IsSuccess || string.IsNullOrWhiteSpace(res.HtmlBody))
                    return false;
                var detail = WellRydeUserParser.ParseUserDetail(sec, res.HtmlBody);
                SupeyWellRydeRosterMerge.ApplyPortalDetail(detail, profile, isNewDriver: isNewDriver);
                return true;
            }
            catch
            {
                return false;
            }
        }

        internal sealed class FsDriverBuildSyncResult
        {
            public int RefreshedFromWellRyde;
            public int ImportedFromWellRyde;
            public int HomesGeocoded;
            public bool WellRydeSkipped;
        }

        internal static string FormatFsHomePinHint(SupeyDriverProfile profile, string tabName = null)
        {
            if (profile == null)
                return ", no driver roster match"
                    + (string.IsNullOrWhiteSpace(tabName) ? "" : " for tab \"" + tabName.Trim() + "\"");
            if (string.IsNullOrWhiteSpace(profile.HomeStreet) && string.IsNullOrWhiteSpace(profile.HomeCity))
                return ", no home address in roster";
            return ", home not geocoded";
        }

        private void RefreshFsMapIfDriverTabActive()
        {
            if (_fsHasPreview && !string.IsNullOrWhiteSpace(_fsActiveDriverTab)
                && !_fsActiveDriverTab.Equals("Reserves", StringComparison.OrdinalIgnoreCase))
                _ = RefreshFsMapForCurrentTabAsync();
        }

        private void OnFsDriverAdd()
        {
            using (var ed = new SupeyDriverEditorForm(null))
            {
                if (ed.ShowDialog(this) != DialogResult.OK || ed.Result == null) return;
                _supeyRoster.Add(ed.Result);
                RebuildFsDriversList();
                if (_supeyDriversLv != null) RebuildSupeyDriversList();
                SaveFsDriverRosterToDisk(showOk: false);
            }
        }

        private async Task OnFsDriverEditAsync()
        {
            if (_fsDriversLv == null || _fsDriversLv.SelectedItems.Count == 0) return;
            var existing = _fsDriversLv.SelectedItems[0].Tag as SupeyDriverProfile;
            if (existing == null) return;

            if (!string.IsNullOrWhiteSpace(existing.WellRydeSecId))
            {
                SetScheduleBuilderStatus("Loading " + (existing.Name ?? "driver") + " from WellRyde…");
                bool pulled = await TryRefreshSupeyDriverFromWellRydeAsync(existing).ConfigureAwait(true);
                if (!pulled)
                {
                    var dr = MessageBox.Show(this,
                        "Could not load the latest profile from WellRyde.\r\n\r\n"
                        + "Edit anyway using the last data saved on this PC?",
                        "Schedule Builder — WellRyde",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning,
                        MessageBoxDefaultButton.Button2);
                    if (dr != DialogResult.Yes)
                    {
                        SetScheduleBuilderStatus("Edit canceled — WellRyde unavailable.");
                        return;
                    }
                }
                else
                {
                    RebuildFsDriversList();
                    SetScheduleBuilderStatus("Loaded from WellRyde — capacity and shift unchanged (local).");
                }
            }

            using (var ed = new SupeyDriverEditorForm(existing))
            {
                if (ed.ShowDialog(this) != DialogResult.OK || ed.Result == null) return;
                await CommitDriverEditAsync(
                    existing,
                    ed.Result,
                    ed.SaveToWellRyde,
                    () =>
                    {
                        RebuildFsDriversList();
                        if (_supeyDriversLv != null)
                            RebuildSupeyDriversList();
                    }).ConfigureAwait(true);
            }

            RefreshFsMapIfDriverTabActive();
        }

        private void OnFsDriverRemove()
        {
            if (_fsDriversLv == null || _fsDriversLv.SelectedItems.Count == 0) return;
            var existing = _fsDriversLv.SelectedItems[0].Tag as SupeyDriverProfile;
            if (existing == null) return;
            var dr = MessageBox.Show(this,
                "Remove " + (existing.Name ?? "driver") + " from the roster?",
                "Schedule Builder — Drivers",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2);
            if (dr != DialogResult.Yes) return;
            _supeyRoster.Remove(existing);
            RebuildFsDriversList();
            if (_supeyDriversLv != null) RebuildSupeyDriversList();
            SaveFsDriverRosterToDisk(showOk: false);
            RefreshFsMapIfDriverTabActive();
        }

        private async Task OnFsPullFromWellRydeAsync()
        {
            SetScheduleBuilderStatus("WellRyde sign-in…");
            bool ok;
            try
            {
                ok = await EnsureWellRydePortalSessionForBillingAsync();
            }
            catch (Exception ex)
            {
                SetScheduleBuilderStatus("WellRyde sign-in failed — roster on disk unchanged.");
                WellRydePortalLog.CopyErrorReport("Schedule Builder pull — sign-in failed", ex);
                return;
            }

            if (!ok || _wellRydeSession == null)
            {
                SetScheduleBuilderStatus("WellRyde unavailable — use saved roster or ADD manually.");
                return;
            }

            var alreadyImportedSecIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var d in _supeyRoster)
            {
                if (d != null && !string.IsNullOrEmpty(d.WellRydeSecId))
                    alreadyImportedSecIds.Add(d.WellRydeSecId);
            }

            using (var dlg = new SupeyImportFromWellRydeForm(_wellRydeSession, alreadyImportedSecIds))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK)
                    return;
                var picks = dlg.SelectedDetails ?? new List<WellRydeUserDetail>();
                if (picks.Count == 0) return;

                int added = 0, updated = 0;
                foreach (var detail in picks)
                {
                    if (detail == null || string.IsNullOrEmpty(detail.SecId)) continue;
                    var existing = FindRosterDriverBySecIdOrName(detail.SecId, detail.FullName);
                    if (existing != null)
                    {
                        SupeyWellRydeRosterMerge.ApplyPortalDetail(detail, existing, isNewDriver: false);
                        updated++;
                    }
                    else
                    {
                        var profile = new SupeyDriverProfile();
                        SupeyWellRydeRosterMerge.ApplyPortalDetail(detail, profile, isNewDriver: true);
                        _supeyRoster.Add(profile);
                        added++;
                    }
                }

                RebuildFsDriversList();
                if (_supeyDriversLv != null) RebuildSupeyDriversList();
                SaveFsDriverRosterToDisk(showOk: false);
                SetScheduleBuilderStatus(
                    "WellRyde: " + added + " new"
                    + (updated > 0 ? ", " + updated + " updated" : "")
                    + " — names must match driver tabs for map home pins.");
            }

            RefreshFsMapIfDriverTabActive();
        }

        private void BuildFsDriversContextMenu()
        {
            _fsDriversCtxMenu = new ContextMenuStrip
            {
                Renderer = new DarkContextMenuRenderer(),
                BackColor = DarkContextMenuRenderer.Background,
                ForeColor = DarkContextMenuRenderer.ForeColor,
            };

            _fsDriversCtxGeocodeHome = new ToolStripMenuItem("Geocode driver home")
            {
                BackColor = DarkContextMenuRenderer.Background,
                ForeColor = DarkContextMenuRenderer.ForeColor,
            };
            _fsDriversCtxGeocodeHome.Click += (s, e) =>
            {
                if (_fsDriversLv?.SelectedItems.Count > 0
                    && _fsDriversLv.SelectedItems[0].Tag is SupeyDriverProfile profile)
                    _ = FsGeocodeDriverHomeAsync(profile);
            };

            _fsDriversCtxMenu.Items.Add(_fsDriversCtxGeocodeHome);
        }

        private async void FsDriversLv_MouseUp_ShowContextMenu(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right || _fsDriversLv == null) return;

            var hit = _fsDriversLv.HitTest(e.Location);
            if (hit.Item != null)
            {
                hit.Item.Selected = true;
                hit.Item.Focused = true;
            }

            try
            {
                await ScheduleOsrmGate.ProbePreviewServicesAsync(
                    HiatmeAiSettings.Load(), CancellationToken.None).ConfigureAwait(true);
            }
            catch { }

            var profile = hit.Item?.Tag as SupeyDriverProfile;
            bool hasHome = profile != null
                && (!string.IsNullOrWhiteSpace(profile.HomeStreet)
                    || !string.IsNullOrWhiteSpace(profile.HomeCity));
            bool canGeocode = profile != null && hasHome && ScheduleOsrmGate.PreviewGeoOk;

            _fsDriversCtxGeocodeHome.Enabled = canGeocode;
            if (canGeocode)
            {
                _fsDriversCtxGeocodeHome.Text = "Geocode driver home — "
                    + (profile.Name ?? "driver").Trim();
            }
            else if (profile != null && hasHome && !ScheduleOsrmGate.PreviewGeoOk)
            {
                _fsDriversCtxGeocodeHome.Text = "Geocode driver home (office server offline)";
            }
            else
            {
                _fsDriversCtxGeocodeHome.Text = "Geocode driver home";
            }

            _fsDriversCtxMenu.Show(_fsDriversLv, e.Location);
        }

        private void FsDriversLv_DrawColumnHeader(object sender, DrawListViewColumnHeaderEventArgs e)
        {
            SupeyListViewHelpers.DrawColumnHeader(e);
        }

        private void FsDriversLv_DrawItem(object sender, DrawListViewItemEventArgs e)
        {
            SupeyListViewHelpers.SuppressDefaultDrawItem(e);
        }

        private void FsDriversLv_DrawSubItem(object sender, DrawListViewSubItemEventArgs e)
        {
            bool sel = e.Item != null && e.Item.Selected;
            SupeyListViewHelpers.DrawSubItemCellBackground(e, sel ? SupeyTheme.ListSelected : SupeyTheme.ListBody);

            var bounds = new Rectangle(e.Bounds.Left + 6, e.Bounds.Top, e.Bounds.Width - 6, e.Bounds.Height);
            TextRenderer.DrawText(
                e.Graphics,
                e.SubItem?.Text ?? "",
                _fsDriversLv?.Font ?? ListViewOwnerDrawFonts.Cell,
                bounds,
                sel ? SupeyTheme.ListSelectedText : SupeyTheme.ListText,
                TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter
                    | TextFormatFlags.WordEllipsis | TextFormatFlags.GlyphOverhangPadding);

            SupeyListViewHelpers.DrawCellGridLines(e.Graphics, e.Bounds, _fsDriversLv);
        }
    }
}
