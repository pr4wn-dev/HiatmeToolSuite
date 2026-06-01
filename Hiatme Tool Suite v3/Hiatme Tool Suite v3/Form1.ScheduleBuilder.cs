using System;

using System.Collections.Generic;

using System.Drawing;

using System.Linq;

using System.Threading;

using System.Threading.Tasks;

using System.Windows.Forms;

using MaterialSkin.Controls;



namespace Hiatme_Tool_Suite_v3

{

    /// <summary>Schedule Builder tab — Supey-style map + collapsible trips; build preview by driver tab.</summary>

    public partial class Form1

    {

        private Panel _fsToolbarPanel;

        private Panel _fsMainHost;

        private SplitContainer _fsMainSplit;

        private SupeyMapWorkspace _fsMap;

        private SupeyCollapsiblePanel _fsTripsCollapsible;

        private Panel _fsDriverTabStrip;

        private FlowLayoutPanel _fsDriverTabFlow;

        private readonly Dictionary<string, SupeyButton> _fsDriverTabButtons =

            new Dictionary<string, SupeyButton>(StringComparer.OrdinalIgnoreCase);

        private List<string> _fsDriverTabOrder = new List<string>();

        private string _fsActiveDriverTab;

        private SupeyListView _fsTripsLv;

        private Label _fsToolbarStatusLbl;

        private SupeyButton _fsBuildBtn;

        private SupeyButton _fsSaveBtn;

        private bool _fsPreviewUiReady;

        private bool _fsDefaultSplitApplied;

        private bool _fsUserAdjustedMainSplit;

        private bool _applyingFsDefaultSplit;

        private int _fsSavedMapSplitterDistance;

        private int _fsMapRefreshGen;



        private readonly Dictionary<string, List<ScheduleBuilderPreviewLine>> _fsLinesByTab =

            new Dictionary<string, List<ScheduleBuilderPreviewLine>>(StringComparer.OrdinalIgnoreCase);



        private sealed class FsPreviewGapTag

        {

            public string NoteText { get; }

            public FsPreviewGapTag(string noteText) => NoteText = noteText ?? "";

        }



        private void InitializeScheduleBuilderTab()

        {

            if (_fsPreviewUiReady || materialCard14 == null || tabPage6 == null) return;



            StyleScheduleBuilderChrome();

            BuildFsToolbar();

            BuildFsWorkspace();



            materialCard14.Controls.Clear();

            materialCard14.Controls.Add(_fsMainHost);

            materialCard14.Controls.Add(_fsToolbarPanel);



            tabPage6.VisibleChanged += (s, e) =>

            {

                if (tabPage6.Visible)

                    EnsureFsSplitDistance();

            };

            BeginInvoke(new Action(EnsureFsSplitDistance));

            SupeyDarkScrollBars.Apply(tabPage6);



            _fsPreviewUiReady = true;

            SetScheduleBuilderStatus("Ready. Pick a service date and click BUILD.");

        }



        private void StyleScheduleBuilderChrome()

        {

            tabPage6.BackColor = SupeyTheme.SurfaceBase;

            tabPage6.UseVisualStyleBackColor = false;



            materialCard14.Dock = DockStyle.Fill;

            materialCard14.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;

            materialCard14.Margin = new Padding(0);

            materialCard14.Padding = new Padding(0);

            materialCard14.BackColor = SupeyTheme.SurfaceBase;



            if (materialCard15 != null)

                materialCard15.Visible = false;

        }



        private void BuildFsToolbar()

        {

            if (fsbtn != null) fsbtn.Visible = false;

            if (fsExportHintLbl != null) fsExportHintLbl.Visible = false;



            _fsToolbarPanel = new Panel

            {

                Dock = DockStyle.Top,

                Height = 56,

                BackColor = SupeyTheme.SurfaceHeader,

                Padding = new Padding(0),

            };

            var divider = new Panel

            {

                Dock = DockStyle.Bottom,

                Height = 1,

                BackColor = SupeyTheme.Divider,

            };



            var leftFlow = new FlowLayoutPanel

            {

                Dock = DockStyle.Left,

                FlowDirection = FlowDirection.LeftToRight,

                WrapContents = false,

                AutoSize = true,

                BackColor = SupeyTheme.SurfaceHeader,

                Padding = new Padding(12, 12, 0, 0),

                Width = 620,

            };



            var rightFlow = new FlowLayoutPanel

            {

                Dock = DockStyle.Right,

                FlowDirection = FlowDirection.RightToLeft,

                WrapContents = false,

                AutoSize = true,

                BackColor = SupeyTheme.SurfaceHeader,

                Padding = new Padding(0, 12, 12, 0),

                Width = 720,

            };



            var dateLabel = new Label

            {

                Text = "Service date",

                AutoSize = true,

                ForeColor = SupeyTheme.TextSecondary,

                BackColor = SupeyTheme.SurfaceHeader,

                Font = SupeyTheme.CaptionFont,

                Margin = new Padding(0, 8, 10, 0),

            };



            if (fsbdatepicker != null)

            {

                fsbdatepicker.Margin = new Padding(0);

                fsbdatepicker.Size = new Size(232, 30);

                fsbdatepicker.BorderColor = SupeyTheme.BorderSubtle;

                fsbdatepicker.BorderSize = 1;

                fsbdatepicker.Font = new Font("Segoe UI", 9.5f);

                fsbdatepicker.SkinColor = SupeyTheme.SurfaceElevated;

                fsbdatepicker.TextColor = SupeyTheme.TextPrimary;

            }



            _fsBuildBtn = new SupeyButton

            {

                Text = "BUILD",

                Kind = SupeyButton.Variant.Primary,

                Size = new Size(96, 30),

                Margin = new Padding(0, 1, 6, 0),

            };

            _fsBuildBtn.Click += fsBuildBtn_Click;



            _fsSaveBtn = new SupeyButton

            {

                Text = "SAVE SCHEDULE",

                Kind = SupeyButton.Variant.Secondary,

                Size = new Size(148, 30),

                Margin = new Padding(0, 1, 0, 0),

                Enabled = false,

            };

            var saveTip = new ToolTip { AutoPopDelay = 12000, InitialDelay = 400 };

            saveTip.SetToolTip(_fsSaveBtn, "Save as Excel workbook — coming soon.");



            _fsToolbarStatusLbl = new Label

            {

                Text = "Ready",

                AutoSize = false,

                Width = 520,

                Height = 28,

                ForeColor = SupeyTheme.TextSecondary,

                BackColor = SupeyTheme.SurfaceHeader,

                TextAlign = ContentAlignment.MiddleRight,

                Font = SupeyTheme.BodyFont,

                Margin = new Padding(0, 3, 0, 0),

            };



            leftFlow.Controls.Add(dateLabel);

            if (fsbdatepicker != null)

                leftFlow.Controls.Add(fsbdatepicker);

            leftFlow.Controls.Add(MakeFsToolbarSeparator());

            leftFlow.Controls.Add(_fsBuildBtn);

            leftFlow.Controls.Add(_fsSaveBtn);



            rightFlow.Controls.Add(_fsToolbarStatusLbl);



            _fsToolbarPanel.Controls.Add(rightFlow);

            _fsToolbarPanel.Controls.Add(leftFlow);

            _fsToolbarPanel.Controls.Add(divider);

            _fsToolbarPanel.Resize += (s, e) => LayoutFsToolbarStatusWidth();

            LayoutFsToolbarStatusWidth();

        }



        private void LayoutFsToolbarStatusWidth()

        {

            if (_fsToolbarPanel == null || _fsToolbarStatusLbl == null) return;

            int pad = 12;

            int reservedLeft = 640;

            int w = Math.Max(180, _fsToolbarPanel.ClientSize.Width - reservedLeft - pad);

            _fsToolbarStatusLbl.Width = w;

        }



        private static Panel MakeFsToolbarSeparator()

        {

            return new Panel

            {

                Width = 1,

                Height = 24,

                BackColor = SupeyTheme.Divider,

                Margin = new Padding(4, 6, 12, 0),

            };

        }



        private void SetScheduleBuilderStatus(string text)

        {

            if (_fsToolbarStatusLbl == null) return;

            if (InvokeRequired)

            {

                try { BeginInvoke((Action)(() => SetScheduleBuilderStatus(text))); }

                catch { /* form closing */ }

                return;

            }

            _fsToolbarStatusLbl.Text = text ?? "";

        }



        private void BuildFsWorkspace()

        {

            _fsMainHost = new Panel

            {

                Dock = DockStyle.Fill,

                BackColor = SupeyTheme.SurfaceBase,

            };



            _fsMainSplit = new SplitContainer

            {

                Dock = DockStyle.Fill,

                Orientation = Orientation.Horizontal,

                BackColor = SupeyTheme.Divider,

                Panel1MinSize = 120,

                Panel2MinSize = 72,

                SplitterWidth = 6,

                FixedPanel = FixedPanel.None,

            };

            _fsMainSplit.Panel1.BackColor = SupeyTheme.SurfaceBase;

            _fsMainSplit.Panel2.BackColor = SupeyTheme.Surface;

            _fsMainSplit.SizeChanged += (s, e) => OnFsMainSplitLayoutChanged();

            _fsMainSplit.SplitterMoved += (s, e) => OnFsMainSplitSplitterMoved();



            _fsMap = new SupeyMapWorkspace { Dock = DockStyle.Fill };

            _fsMap.SetSupeyStatusOnHost = msg =>

            {

                if (!string.IsNullOrWhiteSpace(msg))

                    SetScheduleBuilderStatus(msg);

            };

            _fsMainSplit.Panel1.Controls.Add(_fsMap);



            _fsTripsCollapsible = new SupeyCollapsiblePanel

            {

                Title = "Trips",

                Dock = DockStyle.Fill,

                ExpandedHeight = 280,

                MinExpandedHeight = 120,

            };

            _fsTripsCollapsible.ExpandedChanged += OnFsTripsCollapsibleExpandedChanged;

            BuildFsTripsPanel(_fsTripsCollapsible.ContentPanel);

            _fsTripsCollapsible.ContentPanel.Padding = new Padding(0);

            _fsMainSplit.Panel2.Controls.Add(_fsTripsCollapsible);



            _fsMainHost.Controls.Add(_fsMainSplit);

        }



        private void BuildFsTripsPanel(Panel host)

        {

            host.BackColor = SupeyTheme.Surface;

            host.Padding = new Padding(0);



            _fsDriverTabStrip = new Panel

            {

                Dock = DockStyle.Top,

                Height = 34,

                BackColor = SupeyTheme.SurfaceHeader,

                Padding = new Padding(4, 4, 4, 0),

            };

            var tabDivider = new Panel

            {

                Dock = DockStyle.Bottom,

                Height = 1,

                BackColor = SupeyTheme.Divider,

            };

            _fsDriverTabFlow = new FlowLayoutPanel

            {

                Dock = DockStyle.Fill,

                FlowDirection = FlowDirection.LeftToRight,

                WrapContents = false,

                AutoScroll = true,

                BackColor = SupeyTheme.SurfaceHeader,

                Padding = new Padding(0),

            };

            _fsDriverTabStrip.Controls.Add(_fsDriverTabFlow);

            _fsDriverTabStrip.Controls.Add(tabDivider);

            SupeyDarkScrollBars.Apply(_fsDriverTabStrip);



            _fsTripsLv = new SupeyListView

            {

                Dock = DockStyle.Fill,

                View = View.Details,

                BackColor = SupeyTheme.ListBody,

                ForeColor = SupeyTheme.ListText,

                FullRowSelect = true,

                GridLines = true,

                HideSelection = false,

                MultiSelect = false,

                Font = new Font("Segoe UI", 9.5f),

                OwnerDraw = true,

                HeaderStyle = ColumnHeaderStyle.Clickable,

            };

            _fsTripsLv.DrawColumnHeader += FsTripsLv_DrawColumnHeader;

            _fsTripsLv.DrawItem += FsTripsLv_DrawItem;

            _fsTripsLv.DrawSubItem += FsTripsLv_DrawSubItem;

            ConfigureFsTripsListViewColumns();



            host.Controls.Add(_fsTripsLv);

            host.Controls.Add(_fsDriverTabStrip);



            ListViewMinWidthEnforcer.Attach(_fsTripsLv);

            ListViewHeaderEmptyAreaPainter.Attach(_fsTripsLv);



            _fsTripsLv.SelectedIndexChanged += (s, e) =>

            {

                if (_fsMap == null || _fsTripsLv.SelectedItems.Count == 0) return;

                var item = _fsTripsLv.SelectedItems[0];

                if (item.Tag is FsPreviewGapTag) return;

                var trip = item.Tag as MCDownloadedTrip;

                if (trip != null)

                    _fsMap.FocusTrip(trip);

            };

        }



        private void RebuildFsDriverTabs(IReadOnlyList<string> tabNames)

        {

            if (_fsDriverTabFlow == null) return;

            _fsDriverTabFlow.SuspendLayout();

            _fsDriverTabFlow.Controls.Clear();

            _fsDriverTabButtons.Clear();

            _fsDriverTabOrder = tabNames?.ToList() ?? new List<string>();



            foreach (string name in _fsDriverTabOrder)

            {

                if (string.IsNullOrWhiteSpace(name)) continue;

                int textW = TextRenderer.MeasureText(name, SupeyTheme.BodyFont).Width;

                var btn = new SupeyButton

                {

                    Text = name,

                    Tag = name,

                    Size = new Size(Math.Min(160, Math.Max(72, textW + 20)), 26),

                    Margin = new Padding(0, 0, 6, 0),

                    Kind = SupeyButton.Variant.Secondary,

                };

                btn.Click += FsDriverTabButton_Click;

                _fsDriverTabButtons[name] = btn;

                _fsDriverTabFlow.Controls.Add(btn);

            }



            _fsDriverTabFlow.ResumeLayout(true);

        }



        private void FsDriverTabButton_Click(object sender, EventArgs e)

        {

            if (sender is SupeyButton btn && btn.Tag is string name)

                SelectFsDriverTab(name);

        }



        private void SelectFsDriverTab(string tabName)

        {

            if (string.IsNullOrWhiteSpace(tabName)) return;

            _fsActiveDriverTab = tabName;

            foreach (var kv in _fsDriverTabButtons)

            {

                kv.Value.Kind = string.Equals(kv.Key, tabName, StringComparison.OrdinalIgnoreCase)

                    ? SupeyButton.Variant.Primary

                    : SupeyButton.Variant.Secondary;

            }

            ShowFsTripsForTab(tabName);

            _ = RefreshFsMapForCurrentTabAsync();

        }



        private int GetFsDriverTabPaletteIndex(string tabName)

        {

            if (tabName.Equals("Reserves", StringComparison.OrdinalIgnoreCase))

                return 8;

            if (_fsDriverTabOrder == null || _fsDriverTabOrder.Count == 0)

                return 1;

            int idx = _fsDriverTabOrder.FindIndex(n =>

                string.Equals(n, tabName, StringComparison.OrdinalIgnoreCase));

            return Math.Max(1, (idx < 0 ? 0 : idx % 8) + 1);

        }



        private void FsTripsLv_DrawColumnHeader(object sender, DrawListViewColumnHeaderEventArgs e)

        {

            SupeyListViewHelpers.DrawColumnHeader(e);

        }



        private void FsTripsLv_DrawItem(object sender, DrawListViewItemEventArgs e)

        {

            SupeyListViewHelpers.SuppressDefaultDrawItem(e);

        }



        private void FsTripsLv_DrawSubItem(object sender, DrawListViewSubItemEventArgs e)

        {

            bool sel = e.Item != null && e.Item.Selected;

            bool isGap = e.Item?.Tag is FsPreviewGapTag;

            Color rowBg = isGap ? SupeyTheme.SurfaceHeader : (sel ? SupeyTheme.ListSelected : SupeyTheme.ListBody);

            Color fill = rowBg;

            if (!isGap && !sel && e.ColumnIndex == 0 && e.Item != null

                && e.Item.BackColor != Color.Empty && e.Item.BackColor != SupeyTheme.ListBody)

            {

                fill = e.Item.BackColor;

            }



            SupeyListViewHelpers.DrawSubItemCellBackground(e, fill);



            var bounds = new Rectangle(e.Bounds.Left + 6, e.Bounds.Top, e.Bounds.Width - 6, e.Bounds.Height);

            Color textColor = sel ? SupeyTheme.ListSelectedText : SupeyTheme.ListText;

            if (isGap && !sel && e.ColumnIndex == 2)

                textColor = SupeyTheme.TextSecondary;



            Font drawFont = isGap && e.ColumnIndex == 2 && (e.Item?.Tag as FsPreviewGapTag)?.NoteText?.Length > 0

                ? new Font(_fsTripsLv.Font, FontStyle.Italic)

                : _fsTripsLv.Font;

            TextRenderer.DrawText(e.Graphics, e.SubItem?.Text ?? "", drawFont, bounds, textColor,

                TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter

                | TextFormatFlags.WordEllipsis | TextFormatFlags.GlyphOverhangPadding);



            SupeyListViewHelpers.DrawCellGridLines(e.Graphics, e.Bounds);

        }



        private void OnFsTripsCollapsibleExpandedChanged(object sender, EventArgs e)

        {

            if (_fsTripsCollapsible?.Expanded == true)

                RestoreFsTripsExpandedSplit();

            else

                ApplyFsTripsCollapsedSplit(saveSplitter: true);

        }



        private void OnFsMainSplitLayoutChanged()

        {

            if (_fsMainSplit == null) return;

            if (_fsTripsCollapsible != null && !_fsTripsCollapsible.Expanded)

                ApplyFsTripsCollapsedSplit(saveSplitter: false);

            else

                EnsureFsSplitDistance();

        }



        private void OnFsMainSplitSplitterMoved()

        {

            if (_applyingFsDefaultSplit || _fsMainSplit == null) return;

            if (_fsTripsCollapsible != null && !_fsTripsCollapsible.Expanded)

            {

                ApplyFsTripsCollapsedSplit(saveSplitter: false);

                return;

            }

            if (_fsDefaultSplitApplied)

                _fsUserAdjustedMainSplit = true;

        }



        /// <summary>Trips collapsed — map fills workspace; only the title bar stays at the bottom.</summary>

        private void ApplyFsTripsCollapsedSplit(bool saveSplitter)

        {

            if (_fsMainSplit == null || _fsTripsCollapsible == null) return;

            int total = _fsMainSplit.Height;

            if (total < 80) return;



            if (saveSplitter)

                _fsSavedMapSplitterDistance = _fsMainSplit.SplitterDistance;



            int strip = _fsTripsCollapsible.CollapsedThickness;

            _fsMainSplit.Panel2MinSize = strip;

            _fsMainSplit.FixedPanel = FixedPanel.Panel2;



            int mapH = total - strip - _fsMainSplit.SplitterWidth;

            if (mapH < _fsMainSplit.Panel1MinSize)

                mapH = _fsMainSplit.Panel1MinSize;



            _applyingFsDefaultSplit = true;

            try { _fsMainSplit.SplitterDistance = mapH; }

            finally { _applyingFsDefaultSplit = false; }

        }



        private void RestoreFsTripsExpandedSplit()

        {

            if (_fsMainSplit == null) return;



            _fsMainSplit.FixedPanel = FixedPanel.None;

            _fsMainSplit.Panel2MinSize = 72;



            _applyingFsDefaultSplit = true;

            try

            {

                if (_fsSavedMapSplitterDistance > 0)

                    _fsMainSplit.SplitterDistance = _fsSavedMapSplitterDistance;

                else

                    EnsureFsSplitDistance();

            }

            finally { _applyingFsDefaultSplit = false; }

        }



        private void EnsureFsSplitDistance()

        {

            if (_fsMainSplit == null || _fsUserAdjustedMainSplit || _fsDefaultSplitApplied) return;

            if (_fsTripsCollapsible != null && !_fsTripsCollapsible.Expanded) return;

            int total = _fsMainSplit.Height;

            if (total < 200) return;



            int tripsH = Math.Max(_fsMainSplit.Panel2MinSize,

                Math.Min(480, (int)(total * 0.38)));

            int mapH = total - tripsH - _fsMainSplit.SplitterWidth;

            if (mapH < _fsMainSplit.Panel1MinSize)

                mapH = _fsMainSplit.Panel1MinSize;



            _applyingFsDefaultSplit = true;

            try { _fsMainSplit.SplitterDistance = mapH; }

            finally { _applyingFsDefaultSplit = false; }

            _fsDefaultSplitApplied = true;

        }



        private void ConfigureFsTripsListViewColumns()

        {

            _fsTripsLv.Columns.Clear();

            _fsTripsLv.Columns.Add("Trip #", 90);

            _fsTripsLv.Columns.Add("Date", 72);

            _fsTripsLv.Columns.Add("Client", 160);

            _fsTripsLv.Columns.Add("PU Time", 72);

            _fsTripsLv.Columns.Add("PU Street", 140);

            _fsTripsLv.Columns.Add("PU City", 100);

            _fsTripsLv.Columns.Add("DO Time", 72);

            _fsTripsLv.Columns.Add("DO Street", 140);

            _fsTripsLv.Columns.Add("DO City", 100);

            _fsTripsLv.Columns.Add("Miles", 48);

            _fsTripsLv.Columns.Add("Comments", 200);

        }



        private async void fsBuildBtn_Click(object sender, EventArgs e)

        {

            SetScheduleBuilderStatus("Building schedule…");

            loadinggifhandler_showscreen();

            if (fsbdatepicker != null) fsbdatepicker.Enabled = false;

            if (_fsBuildBtn != null) _fsBuildBtn.Enabled = false;

            if (_fsSaveBtn != null) _fsSaveBtn.Enabled = false;



            string dayname = fsbdatepicker.Value.DayOfWeek.ToString();

            string day = fsbdatepicker.Value.Day.ToString();

            string nameofmonth = fsbdatepicker.Value.ToString("MMMM");

            string month = fsbdatepicker.Value.Month.ToString();

            string year = fsbdatepicker.Value.Year.ToString();



            try

            {

                await SetLoadingGifLabel("Checking connections");

                if (!await EnsureModivcareSessionAsync())

                {

                    loadinggifhandler_hidescreen();

                    EnableScheduleBuilderInputs(true);

                    SetScheduleBuilderStatus("Modivcare sign-in required.");

                    return;

                }



                fsbuilder = new FullScheduleBuilder(dayname, day, nameofmonth, month, year);

                fsbuilder.UpdateLoadingScreen += loadinggifhandler_update;

                fsbuilder.ShowLoadingScreen += loadinggifhandler_showscreen;

                fsbuilder.HideLoadingScreen += loadinggifhandler_hidescreen;



                await fsbuilder.BuildPreviewAsync(fsbdatepicker.Value, mcLoginHandler).ConfigureAwait(true);

                BindScheduleBuilderPreview(fsbuilder);

                int drivers = fsbuilder.PreviewDriverLines.Count;

                int trips = fsbuilder.PreviewDriverLines.Values.Sum(

                    l => l.Count(x => x?.Kind == ScheduleBuilderPreviewLine.LineKind.Trip));

                SetScheduleBuilderStatus("Built — " + drivers + " driver tab(s), "

                    + trips + " trip(s), " + fsbuilder.PreviewReserves.Count + " reserve(s).");

            }

            catch (ScheduleBuilderException ex)

            {

                loadinggifhandler_hidescreen();

                EnableScheduleBuilderInputs(true);

                MessageBox.Show(this,

                    "Schedule build stopped.\n\n" + ex.Message,

                    "Schedule Builder",

                    MessageBoxButtons.OK,

                    MessageBoxIcon.Warning);

                SetScheduleBuilderStatus("Build failed — see message.");

                return;

            }

            catch (Exception ex)

            {

                loadinggifhandler_hidescreen();

                EnableScheduleBuilderInputs(true);

                MessageBox.Show(this,

                    "Unexpected error while building schedule.\n\n" + ex.Message,

                    "Schedule Builder",

                    MessageBoxButtons.OK,

                    MessageBoxIcon.Error);

                SetScheduleBuilderStatus("Build failed.");

                return;

            }



            loadinggifhandler_hidescreen();

            EnableScheduleBuilderInputs(true);

        }



        private void EnableScheduleBuilderInputs(bool enabled)

        {

            if (fsbdatepicker != null) fsbdatepicker.Enabled = enabled;

            if (_fsBuildBtn != null) _fsBuildBtn.Enabled = enabled;

        }



        private void BindScheduleBuilderPreview(FullScheduleBuilder builder)

        {

            if (builder == null || _fsDriverTabFlow == null || _fsTripsLv == null)

                return;



            _fsLinesByTab.Clear();

            _fsTripsLv.Items.Clear();



            var driverNames = builder.PreviewDriverLines.Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();

            foreach (var name in driverNames)

            {

                var lines = builder.PreviewDriverLines[name];

                if (lines == null) lines = new List<ScheduleBuilderPreviewLine>();

                _fsLinesByTab[name] = lines;

            }



            var reserves = builder.PreviewReserves ?? new List<MCDownloadedTrip>();

            _fsLinesByTab["Reserves"] = reserves

                .Select(t => new ScheduleBuilderPreviewLine

                {

                    Kind = ScheduleBuilderPreviewLine.LineKind.Trip,

                    Trip = t,

                })

                .ToList();



            var tabNames = driverNames.Concat(new[] { "Reserves" }).ToList();

            RebuildFsDriverTabs(tabNames);

            if (tabNames.Count > 0)

                SelectFsDriverTab(tabNames[0]);

            else

                _fsActiveDriverTab = null;

        }



        private async Task RefreshFsMapForCurrentTabAsync()

        {

            if (_fsMap == null || string.IsNullOrWhiteSpace(_fsActiveDriverTab)) return;



            string tabName = _fsActiveDriverTab;

            if (string.IsNullOrEmpty(tabName) || !_fsLinesByTab.TryGetValue(tabName, out var lines))

            {

                _fsMap.Clear();

                return;

            }



            var trips = CollectFsMapTrips(lines);

            if (trips.Count == 0)

            {

                _fsMap.ShowScheduleBuilderTrips(tabName, Array.Empty<MCDownloadedTrip>(),

                    null, null, 1);

                return;

            }



            int gen = Interlocked.Increment(ref _fsMapRefreshGen);

            var pickup = new Dictionary<string, GeoPoint>(StringComparer.OrdinalIgnoreCase);

            var dropoff = new Dictionary<string, GeoPoint>(StringComparer.OrdinalIgnoreCase);



            foreach (var trip in trips)

            {

                if (trip == null) continue;

                string key = (trip.TripNumber ?? "").Trim();

                if (string.IsNullOrEmpty(key)) continue;



                var pu = await AddressGeocoder.ResolveTripEndpointAsync(trip.PUStreet, trip.PUCity, CancellationToken.None)

                    .ConfigureAwait(true);

                if (pu.HasValue)

                    pickup[key] = pu.Value;



                var dof = await AddressGeocoder.ResolveTripEndpointAsync(trip.DOStreet, trip.DOCITY, CancellationToken.None)

                    .ConfigureAwait(true);

                if (dof.HasValue)

                    dropoff[key] = dof.Value;

            }



            if (gen != _fsMapRefreshGen) return;



            int palette = GetFsDriverTabPaletteIndex(tabName);



            _fsMap.ShowScheduleBuilderTrips(tabName, trips, pickup, dropoff, palette);

        }



        private static List<MCDownloadedTrip> CollectFsMapTrips(IList<ScheduleBuilderPreviewLine> lines)

        {

            var trips = new List<MCDownloadedTrip>();

            if (lines == null) return trips;

            foreach (var line in lines)

            {

                if (line?.Kind == ScheduleBuilderPreviewLine.LineKind.Trip && line.Trip != null)

                    trips.Add(line.Trip);

            }

            return trips;

        }



        private void ShowFsTripsForTab(string tabName)

        {

            if (_fsTripsLv == null || string.IsNullOrEmpty(tabName))

                return;



            _fsTripsLv.ListViewItemSorter = null;

            _fsTripsLv.Sorting = SortOrder.None;

            _fsTripsLv.BeginUpdate();

            _fsTripsLv.Items.Clear();

            if (_fsLinesByTab.TryGetValue(tabName, out var lines) && lines != null)

            {

                lines = ScheduleBuilderTemplateSlots.CollapseConsecutivePreviewGaps(lines);

                foreach (var line in lines)

                {

                    if (line == null) continue;

                    if (line.Kind == ScheduleBuilderPreviewLine.LineKind.Gap)

                        AddFsPreviewGapRow(line.GapNoteText);

                    else if (line.Trip != null)

                        _fsTripsLv.Items.Add(CreateFsTripListItem(line.Trip));

                }

            }

            _fsTripsLv.EndUpdate();

            ListViewMinWidthEnforcer.ScheduleRecompute(_fsTripsLv);

        }



        private void AddFsPreviewGapRow(string noteText)

        {

            string note = (noteText ?? "").Trim();

            var lvi = new ListViewItem(new[] { "", "", note, "", "", "", "", "", "", "", "" });

            lvi.UseItemStyleForSubItems = false;

            lvi.SubItems[0].BackColor = SupeyTheme.SurfaceHeader;

            if (note.Length > 0)

            {

                lvi.SubItems[2].ForeColor = SupeyTheme.TextSecondary;

                lvi.Font = new Font(_fsTripsLv.Font, FontStyle.Italic);

            }

            lvi.Tag = new FsPreviewGapTag(note);

            _fsTripsLv.Items.Add(lvi);

        }



        private ListViewItem CreateFsTripListItem(MCDownloadedTrip trip)

        {

            var lvi = new ListViewItem(trip.TripNumber ?? "");

            lvi.Tag = trip;

            lvi.SubItems.Add(trip.Date ?? "");

            lvi.SubItems.Add(trip.ClientFullName ?? "");

            lvi.SubItems.Add(FormatTimeOnly(trip.PUTime));

            lvi.SubItems.Add(trip.PUStreet ?? "");

            lvi.SubItems.Add(trip.PUCity ?? "");

            lvi.SubItems.Add(FormatTimeOnly(trip.DOTime));

            lvi.SubItems.Add(trip.DOStreet ?? "");

            lvi.SubItems.Add(trip.DOCITY ?? "");

            lvi.SubItems.Add(trip.Miles ?? "");

            lvi.SubItems.Add(trip.Comments ?? "");

            try

            {

                lvi.BackColor = trip.GetColor();

            }

            catch

            {

                /* ignore */

            }

            return lvi;

        }

    }

}


