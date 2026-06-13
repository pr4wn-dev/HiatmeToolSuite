using System;

using System.Collections.Generic;

using System.Drawing;

using System.IO;

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

        private Panel _fsMapOfflineOverlay;

        private Label _fsMapOfflineLbl;

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

        private SupeyButton _fsLoadBtn;

        private SupeyButton _fsSaveBtn;

        private bool _fsHasPreview;

        private bool _fsPreviewUiReady;

        private bool _fsDefaultSplitApplied;

        private bool _fsUserAdjustedMainSplit;

        private bool _applyingFsDefaultSplit;

        private int _fsSavedMapSplitterDistance;

        private int _fsMapRefreshGen;

        private bool _fsShowAllGroupsOnNextMapLoad;

        private bool _fsCenterMaineAfterBuild;

        private Dictionary<string, GeoPoint> _fsMapPickupByTrip =
            new Dictionary<string, GeoPoint>(StringComparer.OrdinalIgnoreCase);

        private Dictionary<string, GeoPoint> _fsMapDropoffByTrip =
            new Dictionary<string, GeoPoint>(StringComparer.OrdinalIgnoreCase);

        private int _fsMileageHudGen;

        private int _fsSuppressMapSelectionUpdates;

        private double? _fsPreMoveGroupMeters;

        private MCDownloadedTrip _fsPreMoveTripRef;

        private bool _fsPreserveRouteChangeBaseline;



        private readonly Dictionary<string, List<ScheduleBuilderPreviewLine>> _fsLinesByTab =

            new Dictionary<string, List<ScheduleBuilderPreviewLine>>(StringComparer.OrdinalIgnoreCase);



        private readonly Dictionary<string, List<SupeyTripCluster>> _fsGroupsByTab =

            new Dictionary<string, List<SupeyTripCluster>>(StringComparer.OrdinalIgnoreCase);



        private void InitializeScheduleBuilderTab()

        {

            LoadFsScheduleBuilderSettings();

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

                AutoSizeMode = AutoSizeMode.GrowAndShrink,

                BackColor = SupeyTheme.SurfaceHeader,

                Padding = new Padding(12, 12, 0, 0),

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



            _fsLoadBtn = new SupeyButton

            {

                Text = "LOAD",

                Kind = SupeyButton.Variant.Secondary,

                Size = new Size(96, 30),

                Margin = new Padding(0, 1, 6, 0),

            };

            _fsLoadBtn.Click += fsLoadBtn_Click;



            _fsSaveBtn = new SupeyButton

            {

                Text = "SAVE SCHEDULE",

                Kind = SupeyButton.Variant.Secondary,

                Size = new Size(160, 30),

                Margin = new Padding(0, 1, 0, 0),

                Enabled = false,

            };

            var saveTip = SupeyToolTip.Create(autoPopDelay: 12000, initialDelay: 400);

            _fsSaveBtn.Click += fsSaveBtn_Click;

            saveTip.SetToolTip(_fsSaveBtn,
                "Export Excel workbook, or a folder of driver CSVs if Excel is not installed. Does not require the office AI server.");

            saveTip.SetToolTip(_fsLoadBtn,
                "Open a saved .xlsx workbook or driver .csv (Excel not required). Date is read from the file name or trip dates and sets the date picker.");



            _fsToolbarStatusLbl = new Label

            {

                Text = "Ready",

                Dock = DockStyle.Fill,

                AutoEllipsis = true,

                ForeColor = SupeyTheme.TextSecondary,

                BackColor = SupeyTheme.SurfaceHeader,

                TextAlign = ContentAlignment.MiddleRight,

                Font = SupeyTheme.BodyFont,

                Padding = new Padding(8, 14, 12, 0),

            };



            leftFlow.Controls.Add(dateLabel);

            if (fsbdatepicker != null)

                leftFlow.Controls.Add(fsbdatepicker);

            leftFlow.Controls.Add(MakeFsToolbarSeparator());

            leftFlow.Controls.Add(_fsBuildBtn);

            leftFlow.Controls.Add(_fsLoadBtn);

            leftFlow.Controls.Add(_fsSaveBtn);



            _fsToolbarPanel.Controls.Add(divider);

            _fsToolbarPanel.Controls.Add(_fsToolbarStatusLbl);

            _fsToolbarPanel.Controls.Add(leftFlow);

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

            _fsMap.UseGroupRouteColors = _fsShowGroupColors;

            _fsMap.CenterOnMaineHub();

            _fsMap.SetSupeyStatusOnHost = null;

            BuildFsRulesWorkspaceDock();

            _fsMainSplit.Panel1.Controls.Add(_fsMapWorkPanel);



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

            SupeyListViewHelpers.WireSplitContainerSmoothResize(_fsMainSplit);

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

                GridLines = false,

                HideSelection = false,

                MultiSelect = true,

                Font = new Font("Segoe UI", 9.5f),

                OwnerDraw = true,

                HeaderStyle = ColumnHeaderStyle.Clickable,

            };

            _fsTripsLv.DrawColumnHeader += FsTripsLv_DrawColumnHeader;

            _fsTripsLv.DrawItem += FsTripsLv_DrawItem;

            _fsTripsLv.DrawSubItem += FsTripsLv_DrawSubItem;

            ConfigureFsTripsListViewColumns();



            host.Controls.Add(_fsTripsLv);

            BuildFsMapModeToolbar(host);

            host.Controls.Add(_fsDriverTabStrip);

            ListViewMinWidthEnforcer.Attach(_fsTripsLv);

            ListViewHeaderEmptyAreaPainter.Attach(_fsTripsLv);

            BuildFsTripsContextMenu();

            _fsTripsLv.MouseUp += FsTripsLv_MouseUp_ShowContextMenu;

            _fsTripsLv.KeyDown += FsTripsLv_KeyDown_ScheduleShortcuts;

            WireFsTripsListDragDrop();

            if (FsTripDragDropEnabled)
                _fsTripsLv.PostPaintItems = FsTripsPostPaintDragCursor;



            _fsTripsLv.SelectedIndexChanged += (s, e) => FsTripsLv_SelectionChangedUpdateMap();

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

            if (_fsMap != null && !string.IsNullOrWhiteSpace(_fsActiveDriverTab))
                _fsMap.SaveLegendSnapshotForTab(_fsActiveDriverTab);

            _fsActiveDriverTab = tabName;

            foreach (var kv in _fsDriverTabButtons)

            {

                kv.Value.Kind = string.Equals(kv.Key, tabName, StringComparison.OrdinalIgnoreCase)

                    ? SupeyButton.Variant.Primary

                    : SupeyButton.Variant.Secondary;

            }

            SetFsMapDisplayMode(FsMapDisplayMode.AllDriverTrips, applyFilter: false);

            _fsShowAllGroupsOnNextMapLoad = true;

            PushFsMapSelectionSuppress();
            try
            {
                ClearFsTripsListSelection();
                ShowFsTripsForTab(tabName, preserveScroll: false);
            }
            finally
            {
                PopFsMapSelectionSuppress();
            }

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



        private bool FsTripsIsMergedBarRow(ListViewItem item, out FsPreviewNoteTag noteTag, out FsPreviewSectionHeaderTag sectionTag)
        {
            noteTag = item?.Tag as FsPreviewNoteTag;
            sectionTag = item?.Tag as FsPreviewSectionHeaderTag;
            bool isReservesTab = _fsActiveDriverTab != null
                && _fsActiveDriverTab.Equals("Reserves", StringComparison.OrdinalIgnoreCase);
            if (noteTag?.Group != null)
                return true;
            return sectionTag != null && isReservesTab;
        }

        private bool FsTripsTryGetEntireRowBounds(ListViewItem item, out Rectangle bounds)
        {
            bounds = Rectangle.Empty;
            if (_fsTripsLv == null || item == null)
                return false;
            try
            {
                bounds = _fsTripsLv.GetItemRect(item.Index, ItemBoundsPortion.Entire);
            }
            catch (ArgumentException)
            {
                bounds = item.Bounds;
            }

            return bounds.Width > 0 && bounds.Height > 0;
        }

        private void FsTripsPaintMergedBarRow(Graphics g, ListViewItem item, bool selected)
        {
            if (g == null || item == null || !FsTripsIsMergedBarRow(item, out var noteTag, out var sectionTag))
                return;
            if (!FsTripsTryGetEntireRowBounds(item, out Rectangle rowBounds))
                return;

            int bump = FsTripsGetDragBumpPixels(item.Index);
            if (bump > 0)
                rowBounds = new Rectangle(rowBounds.X, rowBounds.Y + bump, rowBounds.Width, rowBounds.Height);

            FsTripsPaintDragDecorations(g, item, 0, rowBounds, bump);

            if (noteTag?.Group != null)
            {
                Color bg = selected ? SupeyTheme.ListSelected : noteTag.Group.DisplayColor;
                Color fg = selected
                    ? SupeyTheme.ListSelectedText
                    : ScheduleBuilderPreviewStyle.ContrastText(noteTag.Group.DisplayColor);
                string text = (noteTag.NoteText ?? "").Trim();
                SupeyListViewHelpers.PaintMergedDetailsRow(
                    g, rowBounds, bg, text, fg, _fsTripsLv.Font, boldText: text.Length > 0);
                return;
            }

            if (sectionTag != null)
            {
                string text = (sectionTag.Title ?? "").Trim();
                SupeyListViewHelpers.PaintMergedDetailsRow(
                    g,
                    rowBounds,
                    sectionTag.SectionColor,
                    text,
                    ScheduleBuilderPreviewStyle.ReserveSectionHeaderText,
                    _fsTripsLv.Font,
                    boldText: true);
            }
        }



        private void FsTripsLv_DrawItem(object sender, DrawListViewItemEventArgs e)

        {

            if (FsTripsIsMergedBarRow(e.Item, out _, out _))
            {
                e.DrawDefault = false;
                FsTripsPaintMergedBarRow(e.Graphics, e.Item, e.Item != null && e.Item.Selected);
                return;
            }

            SupeyListViewHelpers.SuppressDefaultDrawItem(e);

        }



        private void FsTripsLv_DrawSubItem(object sender, DrawListViewSubItemEventArgs e)

        {

            if (FsTripsIsDragSourceRow(e.Item))
            {
                var faint = Color.FromArgb(120, SupeyTheme.ListBody);
                SupeyListViewHelpers.DrawSubItemCellBackground(e, faint);
                SupeyListViewHelpers.DrawCellGridLines(e.Graphics, e.Bounds);
                return;
            }

            if (FsTripsIsDragMergeTargetRow(e.Item))
            {
                FsTripsPaintDragMergeCell(e);
                return;
            }

            if (FsTripsIsMergedBarRow(e.Item, out _, out _))
            {
                e.DrawDefault = false;
                return;
            }

            var noteTag = e.Item?.Tag as FsPreviewNoteTag;
            var tripTag = e.Item?.Tag as FsPreviewTripTag;
            var sectionTag = e.Item?.Tag as FsPreviewSectionHeaderTag;
            bool isReservesTab = _fsActiveDriverTab != null
                && _fsActiveDriverTab.Equals("Reserves", StringComparison.OrdinalIgnoreCase);

            int bump = FsTripsGetDragBumpPixels(e.Item?.Index ?? -1);

            var cellBounds = bump > 0

                ? new Rectangle(e.Bounds.X, e.Bounds.Y + bump, e.Bounds.Width, e.Bounds.Height)

                : e.Bounds;

            FsTripsPaintDragDecorations(e.Graphics, e.Item, e.ColumnIndex, cellBounds, bump);



            bool sel = e.Item != null && e.Item.Selected;

            bool isGap = e.Item?.Tag is FsPreviewGapTag;

            bool isSection = sectionTag != null;

            bool isNote = noteTag != null;
            bool rerouted = tripTag?.ReroutedOnModivcare == true;

            Color rowBg = sel ? SupeyTheme.ListSelected : SupeyTheme.ListBody;

            if (!sel && rerouted)

                rowBg = ScheduleBuilderPreviewStyle.ReroutedTripBackColor;

            else if (!sel && isSection)

                rowBg = isReservesTab
                    ? sectionTag.SectionColor
                    : SupeyTheme.SurfaceHeader;

            else if (!sel && isGap)

                rowBg = SupeyTheme.ListBody;

            else if (!sel && isNote && noteTag?.Group != null && FsShowGroupColorsEnabled)

                rowBg = FsRouteHeaderBackColor(noteTag.Group.DisplayColor);

            Color fill = rowBg;

            if (sel && rerouted)

                fill = ScheduleBuilderPreviewStyle.ReroutedTripSelectedBackColor;

            else if (!sel && !isGap && !isNote && !rerouted && e.ColumnIndex == 0 && FsShowGroupColorsEnabled
                && !isReservesTab

                && e.SubItem != null && e.SubItem.BackColor != Color.Empty

                && e.SubItem.BackColor != SupeyTheme.ListBody)

                fill = e.SubItem.BackColor;



            SupeyListViewHelpers.DrawSubItemCellBackground(e, fill, cellBounds);



            var bounds = new Rectangle(cellBounds.Left + 6, cellBounds.Top, cellBounds.Width - 6, cellBounds.Height);

            Color textColor = sel ? SupeyTheme.ListSelectedText : SupeyTheme.ListText;

            if (!sel && rerouted)

                textColor = Color.White;

            if (!sel && isSection && !isReservesTab && e.ColumnIndex == 2)

                textColor = SupeyTheme.TextPrimary;

            Font drawFont = isSection && !isReservesTab && e.ColumnIndex == 2

                ? new Font(_fsTripsLv.Font, FontStyle.Bold)

                : _fsTripsLv.Font;

            TextRenderer.DrawText(e.Graphics, e.SubItem?.Text ?? "", drawFont, bounds, textColor,

                TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter

                | TextFormatFlags.WordEllipsis | TextFormatFlags.GlyphOverhangPadding);



            SupeyListViewHelpers.DrawCellGridLines(e.Graphics, cellBounds);

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

            _fsTripsLv.Columns.Add("Grp", 40);

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



        private async void fsLoadBtn_Click(object sender, EventArgs e)

        {

            using (var dlg = new OpenFileDialog())

            {

                dlg.Title = "Load saved schedule";

                dlg.Filter =

                    "Schedule|*.xlsx;*.xls;*.csv|Excel workbook|*.xlsx;*.xls|CSV (pick any file in the folder)|*.csv|All files|*.*";

                dlg.CheckFileExists = true;

                if (dlg.ShowDialog(this) != DialogResult.OK)

                    return;



                string path = dlg.FileName;

                string ext = (Path.GetExtension(path) ?? "").ToLowerInvariant();



                _fsHasPreview = false;

                if (_fsSaveBtn != null) _fsSaveBtn.Enabled = false;

                if (fsbdatepicker != null) fsbdatepicker.Enabled = false;

                if (_fsBuildBtn != null) _fsBuildBtn.Enabled = false;

                if (_fsLoadBtn != null) _fsLoadBtn.Enabled = false;



                try

                {

                    SetScheduleBuilderStatus("Loading schedule…");

                    ScheduleBuilderLoadResult load;

                    if (ext == ".xlsx" || ext == ".xls")

                        load = await ScheduleBuilderScheduleLoad.LoadFromWorkbookAsync(path)

                            .ConfigureAwait(true);

                    else if (ext == ".csv")

                    {

                        string folder = Path.GetDirectoryName(path);

                        load = ScheduleBuilderScheduleLoad.LoadFromFolder(folder, path);

                    }

                    else

                    {

                        MessageBox.Show(this,

                            "Choose an Excel workbook (.xlsx) or a driver .csv file.",

                            "Schedule Builder",

                            MessageBoxButtons.OK,

                            MessageBoxIcon.Information);

                        return;

                    }



                    if (load == null || load.DriverLines.Count == 0)

                    {

                        MessageBox.Show(this,

                            "No driver tabs found in that file or folder.\n\n"

                            + "Expected one .csv per driver (same names as templates), or an Excel workbook with driver sheets.",

                            "Schedule Builder",

                            MessageBoxButtons.OK,

                            MessageBoxIcon.Warning);

                        SetScheduleBuilderStatus("Load found no driver tabs.");

                        return;

                    }



                    DateTime serviceDate = load.ServiceDate ?? DateTime.Today;

                    if (load.ServiceDate.HasValue && fsbdatepicker != null)

                        fsbdatepicker.Value = load.ServiceDate.Value;

                    fsbuilder = FullScheduleBuilder.FromServiceDate(serviceDate);

                    fsbuilder.ApplyLoadedSchedule(load);

                    var driverSync = await SyncFsDriversDuringBuildAsync(
                        fsbuilder.PreviewDriverLines.Keys,
                        SetScheduleBuilderStatus).ConfigureAwait(true);

                    BindScheduleBuilderPreview(fsbuilder);

                    if (ScheduleBuilderPreviewUndo.LinesByTabContainsGap(_fsLinesByTab))
                    {
                        FsRevealGapsForManualInsert();
                        if (!string.IsNullOrWhiteSpace(_fsActiveDriverTab))
                            ShowFsTripsForTab(_fsActiveDriverTab);
                    }

                    int drivers = fsbuilder.PreviewDriverLines.Count;

                    int trips = fsbuilder.PreviewDriverLines.Values.Sum(

                        l => l.Count(x => x?.Kind == ScheduleBuilderPreviewLine.LineKind.Trip));

                    _fsHasPreview = true;

                    if (_fsSaveBtn != null) _fsSaveBtn.Enabled = true;



                    string groupingSummary = SummarizeLoadGroupingNotes(load.DriverGroupingNotes);

                    int res = fsbuilder.PreviewReserves?.Count ?? 0;

                    int rer = fsbuilder.PreviewReservesReroute?.Count ?? 0;

                    int wc = fsbuilder.PreviewReservesWillCalls?.Count ?? 0;



                    string dateMsg = load.ServiceDate.HasValue

                        ? serviceDate.ToString("dddd, MMMM d, yyyy")

                          + " (from " + load.ServiceDateSource + ")"

                        : serviceDate.ToString("dddd, MMMM d, yyyy")

                          + " (date not in file — using today for templates)";



                    SetScheduleBuilderStatus(

                        "Loaded " + dateMsg + " — " + drivers + " driver tab(s), " + trips + " trip(s)"

                        + (res + rer + wc > 0

                            ? ", reserves " + res + (wc > 0 ? ", " + wc + " will call(s)" : "")

                              + (rer > 0 ? ", " + rer + " reroute(s)" : "")

                            : "")

                        + ". Groups: " + groupingSummary + "."
                        + FormatFsDriverSyncNote(driverSync)
                        + " Undo history cleared.");

                }

                catch (InvalidOperationException ex)

                {

                    MessageBox.Show(this,

                        ex.Message,

                        "Schedule Builder",

                        MessageBoxButtons.OK,

                        MessageBoxIcon.Warning);

                    SetScheduleBuilderStatus("Load failed.");

                }

                catch (Exception ex)

                {

                    MessageBox.Show(this,

                        "Could not load the schedule.\n\n" + ex.Message,

                        "Schedule Builder",

                        MessageBoxButtons.OK,

                        MessageBoxIcon.Error);

                    SetScheduleBuilderStatus("Load failed.");

                }

                finally

                {

                    EnableScheduleBuilderInputs(true);

                }

            }

        }



        private static string SummarizeLoadGroupingNotes(

            IReadOnlyDictionary<string, string> notesByTab)

        {

            if (notesByTab == null || notesByTab.Count == 0)

                return "unknown";

            var groups = notesByTab.Values

                .Where(n => !string.IsNullOrWhiteSpace(n))

                .GroupBy(n => n.Trim(), StringComparer.OrdinalIgnoreCase)

                .OrderByDescending(g => g.Count())

                .Select(g => g.Count() + "× " + g.Key)

                .ToList();

            return groups.Count > 0 ? string.Join("; ", groups) : "unknown";

        }



        private async void fsBuildBtn_Click(object sender, EventArgs e)

        {

            SetScheduleBuilderStatus("Checking connections…");

            _fsHasPreview = false;

            if (_fsSaveBtn != null) _fsSaveBtn.Enabled = false;

            if (fsbdatepicker != null) fsbdatepicker.Enabled = false;

            if (_fsBuildBtn != null) _fsBuildBtn.Enabled = false;

            if (_fsLoadBtn != null) _fsLoadBtn.Enabled = false;

            if (_fsSaveBtn != null) _fsSaveBtn.Enabled = false;



            string dayname = fsbdatepicker.Value.DayOfWeek.ToString();

            string day = fsbdatepicker.Value.Day.ToString();

            string nameofmonth = fsbdatepicker.Value.ToString("MMMM");

            string month = fsbdatepicker.Value.Month.ToString();

            string year = fsbdatepicker.Value.Year.ToString();



            void OnBuildStatus(string text) => SetScheduleBuilderStatus(text);



            try

            {

                if (!await EnsureModivcareSessionAsync())

                {

                    SetScheduleBuilderStatus("Modivcare sign-in required.");

                    return;

                }



                fsbuilder = new FullScheduleBuilder(dayname, day, nameofmonth, month, year);

                fsbuilder.PreviewCsvExportOptions = MakeFsPreviewCsvExportOptions();

                fsbuilder.UpdateLoadingScreen += OnBuildStatus;



                await fsbuilder.BuildPreviewAsync(fsbdatepicker.Value, mcLoginHandler).ConfigureAwait(true);

                var driverSync = await SyncFsDriversDuringBuildAsync(
                    fsbuilder.PreviewDriverLines.Keys,
                    OnBuildStatus).ConfigureAwait(true);

                BindScheduleBuilderPreview(fsbuilder);

                int drivers = fsbuilder.PreviewDriverLines.Count;

                int trips = fsbuilder.PreviewDriverLines.Values.Sum(

                    l => l.Count(x => x?.Kind == ScheduleBuilderPreviewLine.LineKind.Trip));

                _fsHasPreview = true;

                if (_fsSaveBtn != null) _fsSaveBtn.Enabled = true;

                int rer = fsbuilder.PreviewReservesReroute?.Count ?? 0;

                int res = fsbuilder.PreviewReserves?.Count ?? 0;

                int wc = fsbuilder.PreviewReservesWillCalls?.Count ?? 0;

                int wcDl = fsbuilder.WillCallsInDownloadCount;
                int wcCmt = fsbuilder.WillCallsCommentInDownloadCount;

                string resMsg = res + " reserve" + (res == 1 ? "" : "s");

                if (wc > 0)

                    resMsg += ", " + wc + " will call" + (wc == 1 ? "" : "s");

                else if (wcDl > 0)

                    resMsg += " (0 will calls placed — " + wcDl + " in download";

                if (wcDl > 0 && wc == 0)
                {
                    if (wcCmt > 0)
                        resMsg += "; " + wcCmt + " have WILL CALL comment (need 00:00/12AM PU)";
                    resMsg += " — check banned / driver tabs)";
                }

                if (rer > 0)

                    resMsg += ", " + rer + " reroute" + (rer == 1 ? "" : "s");

                string buildSummary = "Built — " + drivers + " driver tab(s), "

                    + trips + " trip(s), " + resMsg

                    + "." + FormatFsDriverSyncNote(driverSync)
                    + " Undo history cleared.";

                SetScheduleBuilderStatus(buildSummary + " Saving workbook…");

                SyncFsPreviewCsvsForExport();

                await fsbuilder.CreateWorkbookAsync().ConfigureAwait(true);

                if (!string.IsNullOrEmpty(fsbuilder.LastExportPath))

                    SetScheduleBuilderStatus(buildSummary + " Saved workbook — " + fsbuilder.LastExportPath);

                else

                    SetScheduleBuilderStatus(buildSummary + " Save cancelled — preview ready; click SAVE SCHEDULE to try again.");

            }

            catch (ScheduleBuilderException ex)

            {

                MessageBox.Show(this,

                    "Schedule build stopped.\n\n" + ex.Message,

                    "Schedule Builder",

                    MessageBoxButtons.OK,

                    MessageBoxIcon.Warning);

                SetScheduleBuilderStatus("Build failed — see message.");

            }

            catch (Exception ex)

            {

                MessageBox.Show(this,

                    "Unexpected error while building schedule.\n\n" + ex.Message,

                    "Schedule Builder",

                    MessageBoxButtons.OK,

                    MessageBoxIcon.Error);

                SetScheduleBuilderStatus("Build failed.");

            }

            finally

            {

                if (fsbuilder != null)

                    fsbuilder.UpdateLoadingScreen -= OnBuildStatus;

                EnableScheduleBuilderInputs(true);

            }

        }



        private void EnableScheduleBuilderInputs(bool enabled)

        {

            if (fsbdatepicker != null) fsbdatepicker.Enabled = enabled;

            if (_fsBuildBtn != null) _fsBuildBtn.Enabled = enabled;

            if (_fsLoadBtn != null) _fsLoadBtn.Enabled = enabled;

        }



        private void SyncFsPreviewCsvsForExport()
        {
            if (fsbuilder == null)
                return;

            fsbuilder.PreviewCsvExportOptions = MakeFsPreviewCsvExportOptions();
            fsbuilder.ExportPreviewCsvs(_fsLinesByTab);
        }



        private void BindScheduleBuilderPreview(FullScheduleBuilder builder)

        {

            if (builder == null || _fsDriverTabFlow == null || _fsTripsLv == null)

                return;



            _fsLinesByTab.Clear();

            _fsGroupsByTab.Clear();

            FsClearUndoHistory();

            _fsTripsLv.Items.Clear();



            var driverNames = builder.PreviewDriverLines.Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();

            foreach (var name in driverNames)

            {

                var lines = builder.PreviewDriverLines[name];

                if (lines == null) lines = new List<ScheduleBuilderPreviewLine>();
                else
                    lines = ScheduleBuilderGroupHeaderReconcile.Reconcile(lines);

                _fsLinesByTab[name] = lines;

            }



            var reserves = builder.PreviewReserves ?? new List<MCDownloadedTrip>();

            var reroutes = builder.PreviewReservesReroute ?? new List<MCDownloadedTrip>();

            var banned = builder.PreviewReservesBanned ?? new List<MCDownloadedTrip>();

            var willCalls = builder.PreviewReservesWillCalls ?? new List<MCDownloadedTrip>();

            _fsLinesByTab["Reserves"] = ScheduleBuilderReserveBuckets.BuildReservePreviewLines(

                reserves, reroutes, banned, willCalls, builder.WillCallsInDownloadCount);



            var tabNames = new[] { "Reserves" }.Concat(driverNames).ToList();

            RebuildFsDriverTabs(tabNames);

            _fsCenterMaineAfterBuild = true;

            // First driver tab (right of Reserves) so the map loads route groups, not an empty reserves view.
            if (driverNames.Count > 0)

                SelectFsDriverTab(driverNames[0]);

            else if (tabNames.Count > 0)

                SelectFsDriverTab("Reserves");

            else

            {
                _fsActiveDriverTab = null;
                _fsMap?.CenterOnMaineHub();
                _fsCenterMaineAfterBuild = false;
            }

        }



        private void PushFsMapSelectionSuppress() =>
            Interlocked.Increment(ref _fsSuppressMapSelectionUpdates);

        private void PopFsMapSelectionSuppress()
        {
            if (_fsSuppressMapSelectionUpdates > 0)
                Interlocked.Decrement(ref _fsSuppressMapSelectionUpdates);
        }

        private bool FsMapSelectionUpdatesSuppressed =>
            _fsSuppressMapSelectionUpdates > 0;

        private void ClearFsTripsListSelection()
        {
            if (_fsTripsLv == null || _fsTripsLv.SelectedItems.Count == 0)
                return;

            _fsTripsLv.SelectedItems.Clear();
        }

        private void FinalizeFsMapAfterRefresh()
        {
            if (_fsMap == null || !_fsMap.Visible || !ScheduleOsrmGate.PreviewRoutingOk)
                return;

            if (_fsTripsLv == null || _fsTripsLv.SelectedItems.Count == 0)
                _fsMap.ClearTripSelectionHighlight();

            if (_fsShowAllGroupsOnNextMapLoad)
            {
                _fsShowAllGroupsOnNextMapLoad = false;
                if (_fsMapDisplayMode == FsMapDisplayMode.AllDriverTrips)
                    _fsMap.ShowAllGroups();
            }

            ApplyFsMapDisplayFilter(autoFit: false);
        }

        private async Task RefreshFsMapForCurrentTabAsync()

        {

            if (_fsMap == null || string.IsNullOrWhiteSpace(_fsActiveDriverTab)) return;

            string tabName = _fsActiveDriverTab;

            int gen = Interlocked.Increment(ref _fsMapRefreshGen);
            Interlocked.Increment(ref _fsMileageHudGen);

            PushFsMapSelectionSuppress();
            try
            {

            _fsMap.SaveLegendSnapshotForTabIfLoaded(tabName);
            _fsMap.Clear();
            _fsMap.ClearMileageHud();
            _fsMapPickupByTrip.Clear();
            _fsMapDropoffByTrip.Clear();

            bool isReservesTab = tabName.Equals("Reserves", StringComparison.OrdinalIgnoreCase);

            if (string.IsNullOrEmpty(tabName) || !_fsLinesByTab.TryGetValue(tabName, out var lines))

            {

                SetFsMapPreviewAvailable(ScheduleOsrmGate.PreviewRoutingOk);
                _fsMap.CenterOnMaineHub();

                return;

            }



            var trips = CollectFsMapTrips(lines);

            if (trips.Count == 0)
            {
                SetFsMapPreviewAvailable(ScheduleOsrmGate.PreviewRoutingOk);
                _fsMap.CenterOnMaineHub();
                return;
            }

            bool mapLoadingActive = false;
            try
            {
                _fsMap.PushMapLoading("Loading map…");
                mapLoadingActive = true;
                await Task.Yield();

                if (gen != _fsMapRefreshGen) return;

                try
                {
                    await HiatmeGeoSettings.RefreshConnectivityAsync(HiatmeAiSettings.Load(), CancellationToken.None)
                        .ConfigureAwait(true);
                }
                catch (OperationCanceledException) { }
                catch { }

                _fsMap.SetMapLoadingMessage("Checking routing…");
                var (routingOk, routingDetail) = await ScheduleOsrmGate.ProbePreviewRoutingAsync(
                    HiatmeAiSettings.Load(), CancellationToken.None).ConfigureAwait(true);

                if (gen != _fsMapRefreshGen) return;

                if (!routingOk)
                {
                    SetFsMapPreviewAvailable(false, routingDetail);
                    SetScheduleBuilderStatus(tabName
                        + " · map hidden (road routing offline — trip list still works).");
                    return;
                }

                SetFsMapPreviewAvailable(true, showGroupKey: !isReservesTab && FsShowGroupColorsEnabled);
                _fsMap.SetMapLoadingMessage("Geocoding trips…");

            var pickup = new Dictionary<string, GeoPoint>(StringComparer.OrdinalIgnoreCase);

            var dropoff = new Dictionary<string, GeoPoint>(StringComparer.OrdinalIgnoreCase);

            await ScheduleBuilderMapGeocode.ResolveTripsForMapAsync(trips, pickup, dropoff, CancellationToken.None)
                .ConfigureAwait(true);

            _fsMapPickupByTrip = pickup;
            _fsMapDropoffByTrip = dropoff;



            if (gen != _fsMapRefreshGen) return;



            if (!_fsGroupsByTab.TryGetValue(tabName, out var groups) || groups == null)

                groups = new List<SupeyTripCluster>();

            if (isReservesTab && groups.Count == 0 && lines != null && lines.Count > 0)

            {

                groups = ScheduleBuilderPreviewGroups.BuildTripFlatClustersFromPreviewLines(lines);

                _fsGroupsByTab[tabName] = groups;

            }



            foreach (var g in groups)

            {

                if (g == null) continue;

                ScheduleBuilderPreviewGroups.ApplyGeocodes(g, pickup, dropoff);

            }

            if (gen != _fsMapRefreshGen) return;

            EnsureFsDriverRosterLoaded();
            SupeyDriverProfile driverProfile = null;
            GeoPoint? homeGeo = null;
            if (!isReservesTab)
            {
                driverProfile = ScheduleBuilderDriverMapRouting.FindProfileForScheduleTab(_supeyRoster, tabName);
                if (driverProfile != null && string.IsNullOrWhiteSpace(driverProfile.ScheduleTabKey))
                    driverProfile.ScheduleTabKey = tabName;

                _fsMap.SetMapLoadingMessage("Resolving driver home…");
                if (driverProfile != null)
                {
                    homeGeo = await ScheduleBuilderDriverMapRouting.ResolveHomeGeoAsync(
                        driverProfile, CancellationToken.None).ConfigureAwait(true);
                }
            }
            if (gen != _fsMapRefreshGen) return;

            if (!string.Equals(tabName, _fsActiveDriverTab, StringComparison.OrdinalIgnoreCase))
                return;

            _fsMap.SetMapLoadingMessage("Loading road routes…");
            SetScheduleBuilderStatus(tabName + " map · loading road routes…");
            _fsMap.TripFlatMapMode = isReservesTab || !FsShowGroupColorsEnabled;
            _fsMap.UseGroupRouteColors = !isReservesTab && _fsShowGroupColors;
            var routeHome = (!isReservesTab && FsShowGroupColorsEnabled) ? homeGeo : (GeoPoint?)null;
            var routeProgress = new Progress<(int Done, int Total)>(p =>
            {
                if (gen != _fsMapRefreshGen) return;
                string msg = p.Total > 1
                    ? "Loading road routes… " + p.Done + "/" + p.Total
                    : "Loading road routes…";
                _fsMap.SetMapLoadingMessage(msg);
            });
            var routeCounts = await ScheduleBuilderPreviewGroups.BuildOsrmRoutePolylinesAsync(
                groups, routeHome, CancellationToken.None, routeProgress).ConfigureAwait(true);

            if (!isReservesTab && FsShowGroupColorsEnabled)
            {
                await ScheduleBuilderPreviewGroups.BuildTripLegPolylinesAsync(
                    groups, CancellationToken.None).ConfigureAwait(true);
            }

            if (gen != _fsMapRefreshGen) return;

            var plan = new SupeyDriverPlan
            {
                Driver = driverProfile ?? new SupeyDriverProfile { Name = tabName, ScheduleTabKey = tabName },
            };
            plan.Groups.AddRange(groups);
            if (!isReservesTab && homeGeo.HasValue && SupeyMapWorkspace.IsValidGeoPoint(homeGeo.Value))
                plan.HomeGeo = homeGeo;

            if (gen != _fsMapRefreshGen) return;

            bool centerMaineAfterBuild = _fsCenterMaineAfterBuild;
            if (centerMaineAfterBuild)
                _fsCenterMaineAfterBuild = false;

            if (gen != _fsMapRefreshGen) return;

            _fsMap.UseGroupRouteColors = !isReservesTab && _fsShowGroupColors;
            _fsMap.TripFlatMapMode = isReservesTab || !FsShowGroupColorsEnabled;

            _fsMap.ShowDriverPlan(plan, autoFitViewport: !centerMaineAfterBuild, restoreSavedLegend: !_fsShowAllGroupsOnNextMapLoad);

            if (centerMaineAfterBuild || !SupeyMapWorkspace.HasValidMapPins(plan))
                _fsMap.CenterOnMaineHub();

            FinalizeFsMapAfterRefresh();

            int pinCount = pickup.Count + dropoff.Count;

            int grpCount = groups.Count;

            if (pinCount > 0)
            {
                string routes = routeCounts.roadGroups > 0 && routeCounts.straightGroups > 0
                    ? routeCounts.roadGroups + " road, " + routeCounts.straightGroups + " straight fallback"
                    : routeCounts.straightGroups > 0
                        ? routeCounts.straightGroups + " straight (OSRM unavailable)"
                        : "road routes";
                string countLabel = _fsMap.TripFlatMapMode
                    ? grpCount + " trip(s), "
                    : grpCount + " group(s), ";
                SetScheduleBuilderStatus(tabName + " map · " + countLabel
                    + pinCount + " pin(s), " + routes
                    + (plan.HomeGeo.HasValue ? ", home pin" : FormatFsHomePinHint(driverProfile, tabName))
                    + ".");
            }

            else if (!HiatmeGeoSettings.UseServer && HiatmeGeoSettings.ServerOnly)

                SetScheduleBuilderStatus(tabName + " map · no pins (office server offline — BUILD/SAVE still work).");

            else

                SetScheduleBuilderStatus(tabName + " map · no pins (geocode cache empty — BUILD/SAVE still work).");
            }
            catch (OperationCanceledException)
            {
                // Geocode / OSRM timeouts — map refresh continues with fallbacks.
            }
            finally
            {
                if (mapLoadingActive)
                    _fsMap.PopMapLoading();
            }
            }
            finally
            {
                PopFsMapSelectionSuppress();
            }

        }



        private void BuildFsMapOfflineOverlay()
        {
            _fsMapOfflineOverlay = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = SupeyTheme.SurfaceBase,
                Visible = false,
            };
            _fsMapOfflineLbl = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = SupeyTheme.TextSecondary,
                Font = new Font("Segoe UI", 10f),
            };
            _fsMapOfflineOverlay.Controls.Add(_fsMapOfflineLbl);
        }

        private void SetFsMapPreviewAvailable(bool available, string routingDetail = null, bool? showGroupKey = null)
        {
            if (_fsMap == null) return;

            bool showKey = showGroupKey ?? FsShowGroupColorsEnabled;
            _fsMap.Visible = available;
            _fsSideTabPanel?.SetPageEnabled(FsSidePageGroupKey, available && showKey);

            if (_fsMapOfflineOverlay == null) return;

            _fsMapOfflineOverlay.Visible = !available;
            if (!available)
            {
                _fsMap.Clear();
                _fsMap.ClearMileageHud();
                _fsSideTabPanel?.SetPageEnabled(FsSidePageGroupKey, false);
                string detail = string.IsNullOrWhiteSpace(routingDetail)
                    ? ScheduleOsrmGate.PreviewRoutingDetail
                    : routingDetail;
                _fsMapOfflineLbl.Text =
                    "Map preview requires road routing (OSRM).\r\n\r\n"
                    + "Start the office AI server and Maine OSRM, then switch tabs to refresh.\r\n\r\n"
                    + detail;
            }
        }

        private void FsTripsLv_SelectionChangedUpdateMap()

        {

            if (FsMapSelectionUpdatesSuppressed)
                return;

            if (!_fsPreserveRouteChangeBaseline)

            {

                _fsPreMoveGroupMeters = null;

                _fsPreMoveTripRef = null;

            }

            _fsPreserveRouteChangeBaseline = false;

            if (_fsMap == null || !_fsMap.Visible || !ScheduleOsrmGate.PreviewRoutingOk) return;

            ApplyFsMapDisplayFilter();

            if (_fsTripsLv == null || _fsTripsLv.SelectedItems.Count == 0

                || string.IsNullOrWhiteSpace(_fsActiveDriverTab))

            {

                _fsMap.ClearMileageHud();

                return;

            }

            var item = _fsTripsLv.SelectedItems[0];

            if (item.Tag is FsPreviewGapTag || item.Tag is FsPreviewSectionHeaderTag)

            {

                _fsMap.ClearMileageHud();

                return;

            }

            SupeyTripCluster group = null;

            MCDownloadedTrip trip = null;

            if (item.Tag is FsPreviewNoteTag noteTag)

                group = noteTag.Group;

            else if (item.Tag is FsPreviewTripTag rowTag)

            {

                trip = rowTag.Trip;

                group = rowTag.Group;

            }

            else

            {

                trip = item.Tag as MCDownloadedTrip;

            }

            if (group == null && trip != null

                && _fsGroupsByTab.TryGetValue(_fsActiveDriverTab, out var groups))

                group = FindFsGroupForTrip(groups, trip);

            if (group == null)

            {

                _fsMap.ClearMileageHud();

                return;

            }

            if (trip != null)

            {

                if (_fsMapDisplayMode == FsMapDisplayMode.SelectedTrips && _fsTripsLv.SelectedItems.Count == 1)

                    _fsMap.FocusTrip(trip);

            }

            _ = UpdateFsMapMileageHudAsync(group, trip);

        }



        private async Task UpdateFsMapMileageHudAsync(SupeyTripCluster group, MCDownloadedTrip trip)

        {

            if (_fsMap == null || group == null) return;

            group = FsResolveLiveGroup(group);
            if (group == null) return;

            int gen = Interlocked.Increment(ref _fsMileageHudGen);

            _fsMap.PushMapLoading("Loading mileage…");
            try
            {
            await Task.Yield();
            if (gen != _fsMileageHudGen) return;

            double? tripMeters = null;

            bool tripApprox = false;

            if (trip != null)

            {

                GeoPoint? pinPu = null, pinDo = null;

                if (_fsMap.TryGetTripPinGeoPoints(trip, out var puPin, out var doPin))

                {

                    pinPu = puPin;

                    pinDo = doPin;

                }

                var tripLeg = await ScheduleBuilderMapMileage.ResolveTripPuDoMetersAsync(

                    group,

                    trip,

                    _fsMapPickupByTrip,

                    _fsMapDropoffByTrip,

                    pinPu,

                    pinDo,

                    CancellationToken.None).ConfigureAwait(true);

                tripMeters = tripLeg.meters;

                tripApprox = tripLeg.approx;

            }

            EnsureFsDriverRosterLoaded();
            var driverProfile = ScheduleBuilderDriverMapRouting.FindProfileForScheduleTab(
                _supeyRoster, _fsActiveDriverTab);
            GeoPoint? homeGeo = null;
            var dayPosition = ScheduleBuilderDriverMapRouting.GroupDayPosition.Middle;
            if (driverProfile != null
                && _fsGroupsByTab.TryGetValue(_fsActiveDriverTab, out var tabGroups)
                && tabGroups != null)
            {
                homeGeo = await ScheduleBuilderDriverMapRouting.ResolveHomeGeoAsync(
                    driverProfile, CancellationToken.None).ConfigureAwait(true);
                int groupIndex = FindFsGroupIndex(tabGroups, group);
                dayPosition = ScheduleBuilderDriverMapRouting.ResolveDayPosition(
                    groupIndex, tabGroups.Count);
            }

            _fsMap.SetMapLoadingMessage("Calculating group efficiency…");
            var efficiency = await ScheduleBuilderMapMileage.ComputeGroupEfficiencyAsync(
                group,
                homeGeo,
                dayPosition,
                CancellationToken.None).ConfigureAwait(true);

            double groupMeters = efficiency.currentMeters > 0
                ? efficiency.currentMeters
                : ScheduleBuilderMapMileage.GroupRouteMeters(group);

            double? routeChangeMeters = null;

            if (trip != null && _fsPreMoveGroupMeters.HasValue && _fsPreMoveTripRef != null

                && (ReferenceEquals(trip, _fsPreMoveTripRef)

                    || (!string.IsNullOrEmpty(trip.TripNumber)

                        && string.Equals(trip.TripNumber, _fsPreMoveTripRef.TripNumber,

                            StringComparison.OrdinalIgnoreCase))))

            {

                routeChangeMeters = groupMeters - _fsPreMoveGroupMeters.Value;

            }

            if (gen != _fsMileageHudGen) return;

            _fsMap.SetMileageHud(
                group,
                trip,
                groupMeters,
                tripMeters,
                tripApprox,
                efficiency.scorePercent,
                efficiency.currentMeters,
                efficiency.approx,
                routeChangeMeters);
            }
            catch (OperationCanceledException)
            {
                // OSRM / geocode timeout while computing mileage HUD.
            }
            finally
            {
                _fsMap.PopMapLoading();
            }

        }

        /// <summary>Map/list rebuild creates new cluster objects — always use the live row from the current tab.</summary>
        private SupeyTripCluster FsResolveLiveGroup(SupeyTripCluster group)
        {
            if (group == null || string.IsNullOrWhiteSpace(_fsActiveDriverTab))
                return group;
            if (!_fsGroupsByTab.TryGetValue(_fsActiveDriverTab, out var groups) || groups == null)
                return group;
            int idx = FindFsGroupIndex(groups, group);
            if (idx >= 0 && idx < groups.Count)
                return groups[idx];
            return group;
        }



        private void FsSnapshotPreMoveGroupMeters(string tab, MCDownloadedTrip trip, bool merge, MCDownloadedTrip mergeTargetTrip)

        {

            _fsPreMoveTripRef = trip;

            _fsPreMoveGroupMeters = null;

            if (!_fsGroupsByTab.TryGetValue(tab, out var groups))

                return;

            SupeyTripCluster baselineGroup = merge && mergeTargetTrip != null

                ? FindFsGroupForTrip(groups, mergeTargetTrip)

                : FindFsGroupForTrip(groups, trip);

            if (baselineGroup != null)

                _fsPreMoveGroupMeters = ScheduleBuilderMapMileage.GroupRouteMeters(baselineGroup);

        }



        private async void fsSaveBtn_Click(object sender, EventArgs e)

        {

            if (fsbuilder == null || !_fsHasPreview)

            {

                SetScheduleBuilderStatus("Build the schedule first, then click SAVE SCHEDULE.");

                return;

            }



            void OnSaveStatus(string text) => SetScheduleBuilderStatus(text ?? "Saving…");



            fsbuilder.UpdateLoadingScreen += OnSaveStatus;

            if (fsbdatepicker != null) fsbdatepicker.Enabled = false;

            if (_fsBuildBtn != null) _fsBuildBtn.Enabled = false;

            if (_fsLoadBtn != null) _fsLoadBtn.Enabled = false;

            if (_fsSaveBtn != null) _fsSaveBtn.Enabled = false;



            try

            {

                SetScheduleBuilderStatus("Preparing export…");

                SyncFsPreviewCsvsForExport();

                await fsbuilder.CreateWorkbookAsync().ConfigureAwait(true);



                if (!string.IsNullOrEmpty(fsbuilder.LastExportPath))

                {

                    SetScheduleBuilderStatus(fsbuilder.LastExportWasCsv

                        ? "Saved CSV package — " + fsbuilder.LastExportPath

                        : "Saved workbook — " + fsbuilder.LastExportPath);

                }

                else

                    SetScheduleBuilderStatus("Save cancelled.");

            }

            catch (ScheduleBuilderException ex)

            {

                MessageBox.Show(this,

                    "Could not save the schedule.\n\n" + ex.Message,

                    "Schedule Builder",

                    MessageBoxButtons.OK,

                    MessageBoxIcon.Warning);

                SetScheduleBuilderStatus("Save failed — see message.");

            }

            catch (Exception ex)

            {

                MessageBox.Show(this,

                    "Unexpected error while saving.\n\n" + ex.Message,

                    "Schedule Builder",

                    MessageBoxButtons.OK,

                    MessageBoxIcon.Error);

                SetScheduleBuilderStatus("Save failed.");

            }

            finally

            {

                fsbuilder.UpdateLoadingScreen -= OnSaveStatus;

                EnableScheduleBuilderInputs(true);

                if (_fsHasPreview && _fsSaveBtn != null)

                    _fsSaveBtn.Enabled = true;

            }

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



        private void ShowFsTripsForTab(string tabName, bool preserveScroll = true)

        {

            if (_fsTripsLv == null || string.IsNullOrEmpty(tabName))

                return;

            int scrollAnchorLine = -1;
            int scrollAnchorItemIndex = -1;
            if (preserveScroll && _fsTripsLv.Items.Count > 0 && _fsTripsLv.TopItem != null)
            {
                scrollAnchorLine = FsPreviewLineRef.GetLineIndex(_fsTripsLv.TopItem.Tag);
                scrollAnchorItemIndex = _fsTripsLv.TopItem.Index;
            }

            _fsTripsLv.ListViewItemSorter = null;

            _fsTripsLv.Sorting = SortOrder.None;

            _fsTripsLv.BeginUpdate();

            _fsTripsLv.Items.Clear();

            if (_fsLinesByTab.TryGetValue(tabName, out var lines) && lines != null)

            {

                if (tabName.Equals("Reserves", StringComparison.OrdinalIgnoreCase))

                {

                    ShowFsReservesTab(lines);

                    _fsTripsLv.EndUpdate();

                    if (preserveScroll)
                        FsRestoreTripsListViewScroll(scrollAnchorLine, scrollAnchorItemIndex);

                    ListViewMinWidthEnforcer.ScheduleRecompute(_fsTripsLv);

                    return;

                }

                var groups = FsShowGroupColorsEnabled
                    ? ScheduleBuilderPreviewGroups.BuildFromPreviewLines(lines)
                    : ScheduleBuilderPreviewGroups.BuildTripFlatClustersFromPreviewLines(lines);

                _fsGroupsByTab[tabName] = groups;

                SupeyTripCluster lastHeaderGroup = null;

                for (int li = 0; li < lines.Count; li++)

                {

                    var line = lines[li];

                    if (line == null) continue;

                    if (line.Kind == ScheduleBuilderPreviewLine.LineKind.Gap)

                    {

                        lastHeaderGroup = null;

                        if (FsShowGapsEnabled)
                            AddFsTemplateGapRow(li);

                        continue;

                    }

                    if (line.Kind == ScheduleBuilderPreviewLine.LineKind.GroupHeader)

                    {

                        if (FsShowGroupColorsEnabled)

                        {

                            var headerGroup = FindFsGroupByNumber(groups, line.GroupNumber);

                            if (headerGroup != null)

                            {

                                AddFsGroupNoteRow(headerGroup, line.GroupNoteText, li);

                                lastHeaderGroup = headerGroup;

                            }

                        }

                        continue;

                    }

                    if (line.Trip == null) continue;

                    var g = FindFsGroupForTrip(groups, line.Trip);

                    if (g == null) continue;

                    if (FsShowGroupColorsEnabled && !ReferenceEquals(g, lastHeaderGroup))

                    {

                        AddFsGroupNoteRow(g, null, li);

                    }

                    lastHeaderGroup = g;

                    _fsTripsLv.Items.Add(CreateFsTripListItem(g, line.Trip, li, line.ReroutedOnModivcare));

                }

            }

            _fsTripsLv.EndUpdate();

            if (preserveScroll)
                FsRestoreTripsListViewScroll(scrollAnchorLine, scrollAnchorItemIndex);

            ListViewMinWidthEnforcer.ScheduleRecompute(_fsTripsLv);

        }

        private void FsRestoreTripsListViewScroll(int previewLineIndex, int fallbackItemIndex)
        {
            if (_fsTripsLv == null || _fsTripsLv.Items.Count == 0)
                return;

            ListViewItem top = null;
            if (previewLineIndex >= 0)
            {
                int bestDist = int.MaxValue;
                foreach (ListViewItem item in _fsTripsLv.Items)
                {
                    int line = FsPreviewLineRef.GetLineIndex(item.Tag);
                    if (line < 0)
                        continue;
                    int dist = Math.Abs(line - previewLineIndex);
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        top = item;
                    }
                }
            }

            if (top == null && fallbackItemIndex >= 0)
            {
                int idx = Math.Min(fallbackItemIndex, _fsTripsLv.Items.Count - 1);
                top = _fsTripsLv.Items[idx];
            }

            if (top == null)
                return;

            try
            {
                _fsTripsLv.TopItem = top;
            }
            catch
            {
                // ListView can reject TopItem during layout; ignore.
            }
        }



        private void ShowFsReservesTab(List<ScheduleBuilderPreviewLine> lines)

        {

            var groups = ScheduleBuilderPreviewGroups.BuildTripFlatClustersFromPreviewLines(lines);

            _fsGroupsByTab["Reserves"] = groups;

            for (int li = 0; li < (lines?.Count ?? 0); li++)

            {

                var line = lines[li];

                if (line == null) continue;

                if (line.Kind == ScheduleBuilderPreviewLine.LineKind.SectionHeader)

                {

                    AddFsReservesSectionHeader(line.SectionTitle, line.ReserveBandColor, li);

                    continue;

                }

                if (line.Kind == ScheduleBuilderPreviewLine.LineKind.Trip && line.Trip != null)

                {

                    var g = FindFsGroupForTrip(groups, line.Trip);

                    AddFsReserveTripListItem(line.Trip, line.ReserveBandColor, g, li, line.ReroutedOnModivcare);

                }

            }

        }



        private void AddFsReservesSectionHeader(string title, Color? sectionColor, int previewLineIndex)

        {

            Color c = sectionColor ?? ScheduleBuilderReserveBuckets.SectionColorForTitle(title);

            var lvi = new ListViewItem("");

            lvi.UseItemStyleForSubItems = false;

            for (int i = 0; i < 11; i++)

                lvi.SubItems.Add("");

            for (int i = 0; i < lvi.SubItems.Count; i++)
            {
                lvi.SubItems[i].Text = "";
                lvi.SubItems[i].BackColor = c;
            }

            lvi.Tag = new FsPreviewSectionHeaderTag(title, c) { PreviewLineIndex = previewLineIndex };

            _fsTripsLv.Items.Add(lvi);

        }



        private void AddFsReserveTripListItem(
            MCDownloadedTrip trip,
            Color? bandColor,
            SupeyTripCluster group,
            int previewLineIndex,
            bool reroutedOnModivcare = false)

        {

            if (trip == null) return;

            Color band = bandColor ?? ScheduleBuilderReserveBuckets.ReserversBand;

            var lvi = new ListViewItem("—");

            lvi.UseItemStyleForSubItems = false;

            lvi.SubItems[0].BackColor = band;

            lvi.SubItems.Add(trip.TripNumber ?? "");

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

            lvi.Tag = new FsPreviewTripTag(group, trip)
            {
                PreviewLineIndex = previewLineIndex,
                ReroutedOnModivcare = reroutedOnModivcare,
            };

            if (reroutedOnModivcare)
                ApplyFsReroutedTripRowStyle(lvi);

            _fsTripsLv.Items.Add(lvi);

        }



        private static SupeyTripCluster FindFsGroupByNumber(List<SupeyTripCluster> groups, int groupNumber)
        {
            if (groups == null || groupNumber <= 0) return null;
            for (int i = 0; i < groups.Count; i++)
            {
                if (groups[i] != null && groups[i].GroupNumber == groupNumber)
                    return groups[i];
            }
            return null;
        }

        private static int FindFsGroupIndex(List<SupeyTripCluster> groups, SupeyTripCluster group)
        {
            if (groups == null || group == null) return -1;
            for (int i = 0; i < groups.Count; i++)
            {
                if (ReferenceEquals(groups[i], group)) return i;
                if (groups[i] != null && groups[i].GroupNumber == group.GroupNumber) return i;
            }
            return -1;
        }

        private static SupeyTripCluster FindFsGroupForTrip(List<SupeyTripCluster> groups, MCDownloadedTrip trip)

        {

            if (groups == null || trip == null) return null;

            string tn = (trip.TripNumber ?? "").Trim();

            foreach (var g in groups)

            {

                if (g?.Trips == null) continue;

                foreach (var t in g.Trips)

                {

                    if (ReferenceEquals(t, trip)

                        || (!string.IsNullOrEmpty(tn)

                            && string.Equals(t?.TripNumber, tn, StringComparison.OrdinalIgnoreCase)))

                        return g;

                }

            }

            return null;

        }



        private static Color FsRouteHeaderBackColor(Color groupColor)
        {
            return ScheduleBuilderPreviewStyle.RouteHeaderBackColor(groupColor);
        }



        private void AddFsTemplateGapRow(int previewLineIndex)

        {

            var lvi = new ListViewItem(new[] { "", "", "", "", "", "", "", "", "", "", "", "" });

            lvi.UseItemStyleForSubItems = false;

            lvi.BackColor = SupeyTheme.ListBody;

            for (int c = 0; c < lvi.SubItems.Count; c++)

                lvi.SubItems[c].BackColor = SupeyTheme.ListBody;

            lvi.Tag = new FsPreviewGapTag { PreviewLineIndex = previewLineIndex };

            _fsTripsLv.Items.Add(lvi);

        }



        private void AddFsGroupNoteRow(SupeyTripCluster g, string noteText, int previewLineIndex)

        {

            if (g == null) return;

            string note = (noteText ?? "").Trim();

            var lvi = new ListViewItem("");

            lvi.UseItemStyleForSubItems = false;

            Color bar = g.DisplayColor;

            for (int c = 1; c <= 11; c++)

                lvi.SubItems.Add("");

            for (int c = 0; c < lvi.SubItems.Count; c++)
            {
                lvi.SubItems[c].Text = "";
                lvi.SubItems[c].BackColor = bar;
            }

            lvi.Tag = new FsPreviewNoteTag(g, note) { PreviewLineIndex = previewLineIndex };

            _fsTripsLv.Items.Add(lvi);

        }



        private ListViewItem CreateFsTripListItem(
            SupeyTripCluster g,
            MCDownloadedTrip trip,
            int previewLineIndex,
            bool reroutedOnModivcare = false)

        {

            string grp = g != null ? g.GroupNumber.ToString() : "";

            var lvi = new ListViewItem(grp);

            lvi.UseItemStyleForSubItems = false;

            lvi.Tag = g != null
                ? (object)new FsPreviewTripTag(g, trip)
                {
                    PreviewLineIndex = previewLineIndex,
                    ReroutedOnModivcare = reroutedOnModivcare,
                }
                : trip;

            if (reroutedOnModivcare)

                ApplyFsReroutedTripRowStyle(lvi);

            else if (g != null && FsShowGroupColorsEnabled)

            {

                lvi.SubItems[0].BackColor = g.DisplayColor;

            }

            lvi.SubItems.Add(trip.TripNumber ?? "");

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

            return lvi;

        }

        private static void ApplyFsReroutedTripRowStyle(ListViewItem lvi)
        {
            if (lvi == null)
                return;

            Color c = ScheduleBuilderPreviewStyle.ReroutedTripBackColor;
            lvi.BackColor = c;
            for (int i = 0; i < lvi.SubItems.Count; i++)
                lvi.SubItems[i].BackColor = c;
        }

    }

}


