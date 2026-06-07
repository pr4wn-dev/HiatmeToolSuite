using System;

using System.Collections.Generic;

using System.Drawing;

using System.Linq;

using System.Threading.Tasks;

using System.Windows.Forms;



namespace Hiatme_Tool_Suite_v3

{

    public partial class Form1

    {

        private const int FsRulesPad = 10;

        private const int FsRulesTabBtnHeight = 26;

        private const int FsRulesActionBtnHeight = 30;

        private const int FsRulesFooterGap = 8;



        private static readonly Font FsRulesActionBtnFont = new Font("Segoe UI Semibold", 9.25f);



        private const int FsSidePageRules = 0;
        private const int FsSidePageDrivers = 1;
        private const int FsSidePageGroupKey = 2;
        private const int FsSidePageSettings = 3;

        private SupeyIconTabSidePanel _fsSideTabPanel;
        private Splitter _fsSideSplitter;
        private Panel _fsMapWorkPanel;



        private SupeyButton _fsRulesTabNoGo;

        private SupeyButton _fsRulesTabBanned;

        private Panel _fsRulesNoGoHost;

        private Panel _fsRulesBannedHost;

        private ListBox _fsNoGoList;

        private TextBox _fsNoGoAddTxt;

        private Label _fsNoGoStatusLbl;

        private ListBox _fsBannedList;

        private Label _fsBannedStatusLbl;

        private HiatmeAiSettings _fsRulesAiSettings;



        private void BuildFsRulesWorkspaceDock()

        {

            _fsMapWorkPanel = new Panel

            {

                Dock = DockStyle.Fill,

                BackColor = SupeyTheme.SurfaceBase,

            };

            SupeyCollapsibleSideLayout.EnsureWired(_fsMapWorkPanel);

            _fsSideTabPanel = new SupeyIconTabSidePanel();

            var rulesHost = _fsSideTabPanel.AddPage("Rules", "📋", "No-go and banned clients", recommendedExpandedWidth: 320);
            BuildFsRulesPanel(rulesHost);

            EnsureFsDriverRosterLoaded();
            var driversHost = _fsSideTabPanel.AddPage("Drivers", "👤", "Driver roster for BUILD", recommendedExpandedWidth: 430);
            BuildFsDriversPanel(driversHost);

            var groupKeyHost = _fsSideTabPanel.AddPage("Group key", "🎨", "Show or hide groups on the map", recommendedExpandedWidth: 288);
            groupKeyHost.Controls.Add(_fsMap.GroupKeyContentPanel);
            _fsMap.GroupKeyContentPanel.Dock = DockStyle.Fill;
            _fsMap.SetGroupKeyEmbedded(true);
            groupKeyHost.Resize += (s, e) => _fsMap.RelayoutGroupKeyIfNeeded();

            var settingsHost = _fsSideTabPanel.AddPage("Settings", "⚙", "Trip list and map display", recommendedExpandedWidth: 260);
            BuildFsSettingsPanel(settingsHost);

            _fsSideTabPanel.FinalizePages(FsSidePageGroupKey);

            _fsSideSplitter = MakeFsDockSplitter(DockStyle.Right, _fsSideTabPanel.Panel);

            BuildFsMapOfflineOverlay();

            _fsMapWorkPanel.Controls.Add(_fsMap);
            _fsMapWorkPanel.Controls.Add(_fsMapOfflineOverlay);
            _fsMapWorkPanel.Controls.Add(_fsSideSplitter);
            _fsMapWorkPanel.Controls.Add(_fsSideTabPanel.Panel);
        }



        private Splitter MakeFsDockSplitter(DockStyle dock, SupeyCollapsiblePanel target) =>
            SupeyCollapsiblePanel.CreateDockSplitter(dock, target, minExtra: 280, layoutRoot: _fsMapWorkPanel);



        private void BuildFsRulesPanel(Panel host)

        {

            host.BackColor = SupeyTheme.Surface;

            host.Padding = new Padding(FsRulesPad, FsRulesPad, FsRulesPad, FsRulesPad);



            var summary = MakeFsRulesHintLabel(

                "No-go and banned clients apply on BUILD. No-go list is shared with Supey.");

            summary.Dock = DockStyle.Top;

            summary.Height = 36;



            var tabBar = new Panel

            {

                Dock = DockStyle.Top,

                Height = FsRulesTabBtnHeight + 10,

                BackColor = SupeyTheme.Surface,

                Padding = new Padding(0, 4, 0, 0),

            };

            var tabInner = new FlowLayoutPanel

            {

                Dock = DockStyle.Fill,

                BackColor = SupeyTheme.Surface,

                FlowDirection = FlowDirection.LeftToRight,

                WrapContents = false,

            };

            _fsRulesTabNoGo = MakeFsRulesTabButton("No-go", true);

            _fsRulesTabBanned = MakeFsRulesTabButton("Banned", false);

            _fsRulesTabNoGo.Click += (s, e) => ShowFsRulesTab(0);

            _fsRulesTabBanned.Click += (s, e) => ShowFsRulesTab(1);

            tabInner.Controls.Add(_fsRulesTabNoGo);

            tabInner.Controls.Add(_fsRulesTabBanned);

            tabBar.Controls.Add(tabInner);



            var tabRule = new Panel

            {

                Dock = DockStyle.Top,

                Height = 1,

                BackColor = SupeyTheme.Divider,

            };



            var body = new Panel

            {

                Dock = DockStyle.Fill,

                BackColor = SupeyTheme.Surface,

                Padding = new Padding(0, FsRulesFooterGap, 0, 0),

            };

            _fsRulesNoGoHost = new Panel { Dock = DockStyle.Fill, BackColor = SupeyTheme.Surface };

            _fsRulesBannedHost = new Panel { Dock = DockStyle.Fill, BackColor = SupeyTheme.Surface, Visible = false };

            BuildFsRulesNoGoTab(_fsRulesNoGoHost);

            BuildFsRulesBannedTab(_fsRulesBannedHost);

            body.Controls.Add(_fsRulesBannedHost);

            body.Controls.Add(_fsRulesNoGoHost);



            host.Controls.Add(body);

            host.Controls.Add(tabRule);

            host.Controls.Add(tabBar);

            host.Controls.Add(summary);



            _ = RefreshFsNoGoListAsync();

            RefreshFsBannedList();

        }



        /// <summary>Same sizing as <see cref="RebuildFsDriverTabs"/> driver tab chips.</summary>

        private static SupeyButton MakeFsRulesTabButton(string text, bool selected)

        {

            int textW = TextRenderer.MeasureText(text, SupeyTheme.BodyFont).Width;

            return new SupeyButton

            {

                Text = text,

                Size = new Size(Math.Min(120, Math.Max(72, textW + 20)), FsRulesTabBtnHeight),

                Margin = new Padding(0, 0, 6, 0),

                Kind = selected ? SupeyButton.Variant.Primary : SupeyButton.Variant.Secondary,

            };

        }



        private void ShowFsRulesTab(int tabIndex)

        {

            if (_fsRulesNoGoHost != null) _fsRulesNoGoHost.Visible = tabIndex == 0;

            if (_fsRulesBannedHost != null) _fsRulesBannedHost.Visible = tabIndex == 1;

            StyleFsRulesTab(_fsRulesTabNoGo, tabIndex == 0);

            StyleFsRulesTab(_fsRulesTabBanned, tabIndex == 1);

            if (tabIndex == 0)

                _ = RefreshFsNoGoListAsync();

            else

                RefreshFsBannedList();

        }



        private static void StyleFsRulesTab(SupeyButton tab, bool selected)

        {

            if (tab == null) return;

            tab.Kind = selected ? SupeyButton.Variant.Primary : SupeyButton.Variant.Secondary;

        }



        private static SupeyButton MakeFsRulesActionButton(string text, SupeyButton.Variant kind = SupeyButton.Variant.Secondary)

        {

            int textW = TextRenderer.MeasureText(text, FsRulesActionBtnFont).Width;

            var btn = new SupeyButton

            {

                Text = text,

                Kind = kind,

                Size = new Size(Math.Max(76, textW + 24), FsRulesActionBtnHeight),

                Margin = Padding.Empty,

            };

            if (kind == SupeyButton.Variant.Outlined)

                btn.ForeColor = SupeyTheme.ErrorText;

            return btn;

        }



        private static Label MakeFsRulesSectionLabel(string text) =>

            new Label

            {

                Text = text,

                Dock = DockStyle.Top,

                Height = 22,

                ForeColor = SupeyTheme.TextPrimary,

                BackColor = SupeyTheme.Surface,

                Font = SupeyTheme.SubHeaderFont,

                Padding = new Padding(0, 0, 0, 2),

            };



        private static Label MakeFsRulesHintLabel(string text) =>

            new Label

            {

                Text = text,

                Dock = DockStyle.Top,

                AutoSize = false,

                ForeColor = SupeyTheme.TextSecondary,

                BackColor = SupeyTheme.Surface,

                Font = SupeyTheme.CaptionFont,

                Padding = new Padding(0, 0, 0, 4),

            };



        private static Label MakeFsRulesFieldLabel(string text) =>

            new Label

            {

                Text = text,

                Dock = DockStyle.Top,

                Height = 18,

                ForeColor = SupeyTheme.TextMuted,

                BackColor = SupeyTheme.Surface,

                Font = SupeyTheme.CaptionFont,

                Padding = new Padding(0, 4, 0, 2),

            };



        private static Panel WrapFsRulesList(ListBox list)

        {

            var frame = new Panel

            {

                Dock = DockStyle.Fill,

                BackColor = SupeyTheme.Surface,

                Padding = new Padding(0, 4, 0, FsRulesFooterGap),

            };

            var border = new Panel

            {

                Dock = DockStyle.Fill,

                BackColor = SupeyTheme.BorderSubtle,

                Padding = new Padding(1),

            };

            list.Dock = DockStyle.Fill;

            list.BackColor = SupeyTheme.ListBody;

            list.ForeColor = SupeyTheme.ListText;

            list.BorderStyle = BorderStyle.None;

            list.IntegralHeight = false;

            list.Font = SupeyTheme.BodyFont;

            border.Controls.Add(list);

            frame.Controls.Add(border);

            return frame;

        }



        private static Panel MakeFsRulesButtonRow(params SupeyButton[] buttons)

        {

            int n = buttons?.Length ?? 0;

            var row = new Panel

            {

                Dock = DockStyle.Bottom,

                Height = FsRulesActionBtnHeight + FsRulesFooterGap,

                BackColor = SupeyTheme.Surface,

                Padding = new Padding(0, FsRulesFooterGap, 0, 0),

            };

            if (n == 0) return row;



            var table = new TableLayoutPanel

            {

                Dock = DockStyle.Fill,

                ColumnCount = n,

                RowCount = 1,

                BackColor = SupeyTheme.Surface,

            };

            for (int i = 0; i < n; i++)

            {

                table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / n));

                var btn = buttons[i];

                btn.Dock = DockStyle.Fill;

                btn.Margin = new Padding(i == 0 ? 0 : 4, 0, i == n - 1 ? 0 : 4, 0);

                table.Controls.Add(btn, i, 0);

            }

            row.Controls.Add(table);

            return row;

        }



        private static Panel MakeFsRulesAddRow(TextBox textBox, SupeyButton addButton, string fieldLabel)

        {

            int addW = Math.Max(72, addButton.Width);

            var block = new Panel

            {

                Dock = DockStyle.Bottom,

                Height = 68,

                BackColor = SupeyTheme.Surface,

                Padding = new Padding(0, 0, 0, FsRulesFooterGap),

            };

            block.Controls.Add(MakeFsRulesFieldLabel(fieldLabel));



            var inputRow = new TableLayoutPanel

            {

                Dock = DockStyle.Bottom,

                Height = FsRulesActionBtnHeight,

                ColumnCount = 2,

                RowCount = 1,

                BackColor = SupeyTheme.Surface,

            };

            inputRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            inputRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, addW));

            inputRow.RowStyles.Add(new RowStyle(SizeType.Absolute, FsRulesActionBtnHeight));



            textBox.Multiline = true;

            textBox.ScrollBars = ScrollBars.None;

            textBox.Font = SupeyTheme.BodyFont;

            textBox.BackColor = SupeyTheme.ListBody;

            textBox.ForeColor = SupeyTheme.TextPrimary;

            textBox.BorderStyle = BorderStyle.None;

            textBox.Dock = DockStyle.Fill;



            var textCell = new Panel

            {

                Dock = DockStyle.Fill,

                Margin = new Padding(0, 0, 6, 0),

                BackColor = SupeyTheme.BorderSubtle,

                Padding = new Padding(1),

            };

            textCell.Controls.Add(textBox);



            addButton.Dock = DockStyle.Fill;

            addButton.Margin = Padding.Empty;



            inputRow.Controls.Add(textCell, 0, 0);

            inputRow.Controls.Add(addButton, 1, 0);

            block.Controls.Add(inputRow);

            return block;

        }



        private void BuildFsRulesNoGoTab(Panel host)

        {

            host.Padding = new Padding(0);

            host.BackColor = SupeyTheme.Surface;



            _fsNoGoStatusLbl = MakeFsRulesHintLabel("Towns we do not service — PU or DO city match.");

            _fsNoGoStatusLbl.Height = 40;



            _fsNoGoList = new ListBox();

            var listFrame = WrapFsRulesList(_fsNoGoList);



            _fsNoGoAddTxt = new TextBox();

            var addBtn = MakeFsRulesActionButton("Add", SupeyButton.Variant.Primary);

            addBtn.Click += async (s, e) => await FsNoGoAddAsync().ConfigureAwait(true);

            _fsNoGoAddTxt.KeyDown += async (s, e) =>

            {

                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await FsNoGoAddAsync().ConfigureAwait(true); }

            };

            var addBlock = MakeFsRulesAddRow(_fsNoGoAddTxt, addBtn, "Add town");



            var removeBtn = MakeFsRulesActionButton("Remove", SupeyButton.Variant.Outlined);

            removeBtn.Click += async (s, e) => await FsNoGoRemoveAsync().ConfigureAwait(true);

            var saveBtn = MakeFsRulesActionButton("Save", SupeyButton.Variant.Primary);

            saveBtn.Click += async (s, e) => await FsNoGoSaveAsync().ConfigureAwait(true);

            var refreshBtn = MakeFsRulesActionButton("Refresh", SupeyButton.Variant.Secondary);

            refreshBtn.Click += async (s, e) => await RefreshFsNoGoListAsync().ConfigureAwait(true);

            var btnRow = MakeFsRulesButtonRow(removeBtn, saveBtn, refreshBtn);



            host.Controls.Add(listFrame);

            host.Controls.Add(btnRow);

            host.Controls.Add(addBlock);

            host.Controls.Add(_fsNoGoStatusLbl);

            host.Controls.Add(MakeFsRulesSectionLabel("No-go towns"));

        }



        private void BuildFsRulesBannedTab(Panel host)

        {

            host.Padding = new Padding(0);

            host.BackColor = SupeyTheme.Surface;



            _fsBannedStatusLbl = MakeFsRulesHintLabel(

                "Ban by client name + age. Right-click a trip or select one below, then Ban client.");

            _fsBannedStatusLbl.Height = 44;



            _fsBannedList = new ListBox();

            var listFrame = WrapFsRulesList(_fsBannedList);



            var banBtn = MakeFsRulesActionButton("Ban client", SupeyButton.Variant.Primary);

            banBtn.Click += (s, e) => FsBanClientFromSelectedTrip();

            var removeBtn = MakeFsRulesActionButton("Remove", SupeyButton.Variant.Outlined);

            removeBtn.Click += (s, e) => FsBannedRemoveSelected();

            var btnRow = MakeFsRulesButtonRow(banBtn, removeBtn);



            host.Controls.Add(listFrame);

            host.Controls.Add(btnRow);

            host.Controls.Add(_fsBannedStatusLbl);

            host.Controls.Add(MakeFsRulesSectionLabel("Banned clients"));

        }



        private async Task RefreshFsNoGoListAsync()

        {

            if (_fsNoGoList == null) return;

            if (_fsRulesAiSettings == null)

                _fsRulesAiSettings = HiatmeAiSettings.Load();



            IList<string> areas;

            string status;

            if (HiatmeGeoSettings.UseServer)

            {

                bool pushed = await SupeyOutOfArea.TrySyncLocalFileToServerAsync(_fsRulesAiSettings).ConfigureAwait(true);

                areas = await HiatmeAiClient.GetOutOfAreaAsync(_fsRulesAiSettings).ConfigureAwait(true);

                SupeyOutOfArea.TrySaveLocalFallback(areas);

                status = areas.Count + " from server.";

                if (pushed) status += " Offline edits synced.";

            }

            else

            {

                areas = SupeyOutOfArea.LoadLocalFallback();

                SupeyOutOfArea.SetCachedAreas(areas);

                status = areas.Count + " from local file.";

            }



            _fsNoGoList.Items.Clear();

            foreach (var a in areas)

                _fsNoGoList.Items.Add(a);

            if (_fsNoGoStatusLbl != null)

                _fsNoGoStatusLbl.Text = status;

        }



        private List<string> FsNoGoSnapshot()

        {

            var list = new List<string>();

            if (_fsNoGoList == null) return list;

            foreach (var item in _fsNoGoList.Items)

            {

                var s = (item?.ToString() ?? "").Trim();

                if (s.Length > 0) list.Add(s);

            }

            return SupeyOutOfArea.NormalizeAreas(list);

        }



        private async Task FsNoGoAddAsync()

        {

            if (_fsNoGoAddTxt == null || _fsNoGoList == null) return;

            var name = (_fsNoGoAddTxt.Text ?? "").Trim();

            if (name.Length == 0) return;

            foreach (var item in _fsNoGoList.Items)

            {

                if (string.Equals(item?.ToString(), name, StringComparison.OrdinalIgnoreCase))

                {

                    _fsNoGoAddTxt.Clear();

                    return;

                }

            }

            _fsNoGoList.Items.Add(name);

            _fsNoGoAddTxt.Clear();

            await FsNoGoSaveAsync().ConfigureAwait(true);

        }



        private async Task FsNoGoRemoveAsync()

        {

            if (_fsNoGoList == null || _fsNoGoList.SelectedIndices.Count == 0) return;

            foreach (var i in _fsNoGoList.SelectedIndices.Cast<int>().OrderByDescending(x => x))

                _fsNoGoList.Items.RemoveAt(i);

            await FsNoGoSaveAsync().ConfigureAwait(true);

        }



        private async Task FsNoGoSaveAsync()

        {

            if (_fsRulesAiSettings == null)

                _fsRulesAiSettings = HiatmeAiSettings.Load();

            var areas = FsNoGoSnapshot();

            if (areas.Count == 0)

            {

                MessageBox.Show(this, "Add at least one town name before saving.", "No-go areas",

                    MessageBoxButtons.OK, MessageBoxIcon.Information);

                return;

            }



            if (HiatmeGeoSettings.UseServer)

            {

                bool ok = await HiatmeAiClient.SetOutOfAreaAsync(_fsRulesAiSettings, areas).ConfigureAwait(true);

                if (ok)

                {

                    SupeyOutOfArea.TrySaveLocalFallback(areas);

                    if (_fsNoGoStatusLbl != null)

                        _fsNoGoStatusLbl.Text = "Saved to office server (shared with Supey).";

                    return;

                }

            }



            if (SupeyOutOfArea.TrySaveLocalFallback(areas))

            {

                if (_fsNoGoStatusLbl != null)

                    _fsNoGoStatusLbl.Text =

                        "Saved locally — syncs on BUILD or Refresh when the panel is online.";

            }

            SupeyOutOfArea.SetCachedAreas(areas);

        }



        private void RefreshFsBannedList()

        {

            if (_fsBannedList == null) return;

            ScheduleBuilderBannedClients.ReloadCache();

            _fsBannedList.Items.Clear();

            foreach (var c in ScheduleBuilderBannedClients.CachedClients)

                _fsBannedList.Items.Add(ScheduleBuilderBannedClients.FormatListLabel(c));

            if (_fsBannedStatusLbl != null)

            {

                int n = ScheduleBuilderBannedClients.CachedClients.Count;

                _fsBannedStatusLbl.Text = n == 0

                    ? "No banned clients yet."

                    : n + " banned — applies on next BUILD.";

            }

        }



        private void FsBanClientFromSelectedTrip()

        {

            FsBanClientFromTrip(GetFsSelectedTrip(), quietWhenMissing: false);

        }



        internal void FsBanClientFromTrip(MCDownloadedTrip trip, bool quietWhenMissing = true)

        {

            if (trip == null)

            {

                if (!quietWhenMissing)

                {

                    MessageBox.Show(this,

                        "Select a trip row in the list below (any driver or Reserves tab), then click Ban client.",

                        "Banned clients", MessageBoxButtons.OK, MessageBoxIcon.Information);

                }

                return;

            }

            if (!ScheduleBuilderBannedClients.TryAddFromTrip(trip))

            {

                MessageBox.Show(this, "Could not ban — trip needs a client name.", "Banned clients",

                    MessageBoxButtons.OK, MessageBoxIcon.Warning);

                return;

            }

            RefreshFsBannedList();

            if (_fsHasPreview && fsbuilder != null)
                FsApplyBannedClientToPreview();

            SetScheduleBuilderStatus("Banned " + (trip.ClientFullName ?? "client")

                + (string.IsNullOrWhiteSpace(trip.Age) ? "" : " · age " + trip.Age)

                + (_fsHasPreview ? " — moved to Reserves → Reroutes." : " — applies on next BUILD."));
        }



        private void FsApplyBannedClientToPreview()

        {

            if (!_fsHasPreview || fsbuilder == null) return;

            fsbuilder.RemoveBannedTripsFromDriverPreview();

            if (fsbuilder.PreviewDriverLines != null)

            {

                foreach (var kv in fsbuilder.PreviewDriverLines)

                {

                    if (kv.Key.Equals("Reserves", StringComparison.OrdinalIgnoreCase)) continue;

                    _fsLinesByTab[kv.Key] = kv.Value ?? new List<ScheduleBuilderPreviewLine>();

                }

            }

            _fsLinesByTab["Reserves"] = ScheduleBuilderReserveBuckets.BuildReservePreviewLines(

                fsbuilder.PreviewReserves,

                fsbuilder.PreviewReservesReroute,

                banned: null,

                fsbuilder.PreviewReservesWillCalls,

                fsbuilder.WillCallsInDownloadCount);

            ShowFsTripsForTab(string.IsNullOrWhiteSpace(_fsActiveDriverTab) ? "Reserves" : _fsActiveDriverTab);

        }



        internal void FsUnbanClientFromTrip(MCDownloadedTrip trip)

        {

            if (trip == null) return;

            if (!ScheduleBuilderBannedClients.TryRemoveFromTrip(trip))

            {

                SetScheduleBuilderStatus("Client is not on the banned list.");

                return;

            }

            RefreshFsBannedList();

            SetScheduleBuilderStatus("Removed ban for " + (trip.ClientFullName ?? "client")

                + (string.IsNullOrWhiteSpace(trip.Age) ? "" : " · age " + trip.Age) + ".");

        }



        private void FsBannedRemoveSelected()

        {

            if (_fsBannedList == null || _fsBannedList.SelectedIndex < 0) return;

            if (ScheduleBuilderBannedClients.RemoveAt(_fsBannedList.SelectedIndex))

                RefreshFsBannedList();

        }



        private MCDownloadedTrip GetFsSelectedTrip()

        {

            if (_fsTripsLv == null || _fsTripsLv.SelectedItems.Count == 0) return null;

            return GetFsTripFromListItem(_fsTripsLv.SelectedItems[0]);

        }



        internal static MCDownloadedTrip GetFsTripFromListItem(ListViewItem item)

        {

            if (item?.Tag is FsPreviewTripTag tag)

                return tag.Trip;

            return item?.Tag as MCDownloadedTrip;

        }

    }

}


