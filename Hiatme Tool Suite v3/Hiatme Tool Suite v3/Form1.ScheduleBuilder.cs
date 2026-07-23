using System;

using System.Collections.Generic;

using System.Drawing;

using System.IO;

using System.Linq;

using System.Threading;

using System.Threading.Tasks;

using System.Windows.Forms;



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

        private SupeyButton _fsTabReorderButton;

        private string _fsTabReorderSourceName;

        private Point _fsTabReorderStartScreen;

        private int _fsTabReorderFromIndex = -1;

        private int _fsTabReorderInsertIndex = -1;

        private bool _fsTabReorderDragging;

        private bool _fsDriverTabSuppressClick;

        private SupeyButton _fsTabReorderGhost;

        private Panel _fsTabReorderSpacer;

        private Panel _fsTabReorderDropIndicator;

        private Point _fsTabReorderGhostOffset;

        private int _fsTabReorderGhostAnchorScreenY;

        private int _fsTabReorderVisualGapIndex = -1;

        private bool _fsTabReorderDragLayoutReady;

        private bool _fsTabReorderGhostRaised;

        private Point _fsTabReorderGhostLastLocation = new Point(int.MinValue, int.MinValue);

        private string _fsActiveDriverTab;

        private SupeyListView _fsTripsLv;

        private ToolTip _fsTripsAlertTip;
        private string _fsTripsAlertTipLastText = "";
        private ScheduleBuilderTripAlertKind? _fsTripsAlertTipActiveKind;
        private System.Windows.Forms.Timer _fsTripsAlertTipDelayTimer;
        private ScheduleBuilderTripAlertKind? _fsTripsAlertTipPendingKind;
        private string _fsTripsAlertTipPendingText = "";
        private Rectangle _fsTripsAlertTipPendingIconBounds;

        private Panel _fsCutTripBar;
        private Label _fsCutTripBarLine1;
        private Label _fsCutTripBarLine2;
        private Panel _fsCutTripBarAccent;

        private const int FsCutTripBarHeight = 54;

        /// <summary>Shared trip-list column widths (pixels); same on every driver tab and in saved workbooks.</summary>
        private int[] _fsTripsColumnWidthsPx;

        private Label _fsToolbarStatusLbl;

        private SupeyButton _fsBuildBtn;

        private SupeyButton _fsLoadBtn;

        private SupeyButton _fsSaveBtn;

        private SupeyButton _fsSyncHistoryBtn;

        private string _fsPreferredSavePath;

        private bool _fsHasPreview;

        private bool _fsPreviewUiReady;

        private bool _fsDefaultSplitApplied;

        private bool _fsUserAdjustedMainSplit;

        private bool _applyingFsDefaultSplit;

        private int _fsSavedMapSplitterDistance;

        private int _fsMapRefreshGen;

        private bool _fsShowAllGroupsOnNextMapLoad;

        private bool _fsFocusFirstGroupAfterPreviewBind;

        private bool _fsCenterMaineAfterBuild;

        private Dictionary<string, GeoPoint> _fsMapPickupByTrip =
            new Dictionary<string, GeoPoint>(StringComparer.OrdinalIgnoreCase);

        private Dictionary<string, GeoPoint> _fsMapDropoffByTrip =
            new Dictionary<string, GeoPoint>(StringComparer.OrdinalIgnoreCase);

        private int _fsMileageHudGen;

        private CancellationTokenSource _fsMileageHudCts;

        private System.Windows.Forms.Timer _fsMileageHudDebounceTimer;
        private SupeyTripCluster _fsMileageHudPendingGroup;
        private MCDownloadedTrip _fsMileageHudPendingTrip;

        private int _fsSuppressMapSelectionUpdates;

        private double? _fsPreMoveGroupMeters;

        private MCDownloadedTrip _fsPreMoveTripRef;

        private bool _fsPreserveRouteChangeBaseline;



        private readonly Dictionary<string, List<ScheduleBuilderPreviewLine>> _fsLinesByTab =

            new Dictionary<string, List<ScheduleBuilderPreviewLine>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Cached WellRyde cancelled trip keys — re-applied after preview rebuilds.</summary>
        private HashSet<string> _fsWellRydeCancelledKeys;

        /// <summary>Cached Modivcare-rerouted trip keys — re-applied after preview rebuilds.</summary>
        private HashSet<string> _fsReroutedTripKeys;



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

                if (!tabPage6.Visible) return;

                EnsureFsSplitDistance();

            };

            RunWhenReady(EnsureFsSplitDistance);

            SupeyDarkScrollBars.Apply(tabPage6);



            _fsPreviewUiReady = true;

            SetScheduleBuilderStatus("Ready. Pick a service date and click BUILD.");

            RunWhenReady(async () =>
            {
                try { await SyncFsDriverEmailsAsync(reportOffline: false).ConfigureAwait(true); }
                catch { /* offline */ }
                try { await SyncFsGmailDefaultsAsync(reportOffline: false).ConfigureAwait(true); }
                catch { /* offline */ }
                try { await FsRefreshArchiveStatusAsync(reportOffline: false).ConfigureAwait(true); }
                catch { /* optional */ }
            });
        }



        private void StyleScheduleBuilderChrome()

        {

            tabPage6.BackColor = SupeyTheme.SurfaceBase;

            tabPage6.UseVisualStyleBackColor = false;



            if (materialCard15 != null)
            {
                materialCard15.Visible = true;
                materialCard15.BackColor = SupeyTheme.SurfaceHeader;
                materialCard15.ForeColor = SupeyTheme.TextPrimary;
                materialCard15.Padding = new Padding(0);

                var fill = materialCard15.Controls["sbStatusFillPanel"] as Panel;
                if (fill == null)
                {
                    fill = new Panel
                    {
                        Name = "sbStatusFillPanel",
                        Dock = DockStyle.Fill,
                        BackColor = SupeyTheme.SurfaceStatusBar,
                        Padding = new Padding(10, 0, 10, 0),
                    };
                    materialCard15.Controls.Add(fill);
                }
                else
                {
                    fill.BackColor = SupeyTheme.SurfaceStatusBar;
                }

                var divider = fill.Controls["sbStatusTopDivider"] as Panel;
                if (divider == null)
                {
                    divider = new Panel
                    {
                        Name = "sbStatusTopDivider",
                        Dock = DockStyle.Top,
                        Height = 1,
                        BackColor = SupeyTheme.Divider,
                    };
                    fill.Controls.Add(divider);
                    divider.BringToFront();
                }

                if (sbstatuslbl != null)
                {
                    if (!ReferenceEquals(sbstatuslbl.Parent, fill))
                        fill.Controls.Add(sbstatuslbl);
                    sbstatuslbl.AutoSize = false;
                    sbstatuslbl.ForeColor = SupeyTheme.TextSecondary;
                    sbstatuslbl.Font = SupeyTheme.BodyFont;
                    sbstatuslbl.TextAlign = ContentAlignment.MiddleLeft;
                    sbstatuslbl.BackColor = SupeyTheme.SurfaceStatusBar;
                    LayoutStatusLabelInCard(fill, sbstatuslbl);
                    fill.Resize += (_, __) => LayoutStatusLabelInCard(fill, sbstatuslbl);
                }
            }

            if (materialCard14 != null)
            {
                materialCard14.Dock = DockStyle.None;
                materialCard14.Anchor = AnchorStyles.None;
                materialCard14.Margin = new Padding(0);
                materialCard14.Padding = new Padding(0);
                materialCard14.BackColor = SupeyTheme.SurfaceBase;
            }

            tabPage6.Resize -= TabPage6_LayoutScheduleBuilderPanels;
            tabPage6.Resize += TabPage6_LayoutScheduleBuilderPanels;
            LayoutScheduleBuilderPanels();

        }

        private void TabPage6_LayoutScheduleBuilderPanels(object sender, EventArgs e)
        {
            LayoutScheduleBuilderPanels();
        }

        private void LayoutScheduleBuilderPanels()
        {
            if (tabPage6 == null || materialCard14 == null || materialCard15 == null)
                return;

            int left = 13;
            int right = 13;
            int top = 15;
            int bottom = 8;
            int gap = 6;
            int statusHeight = 41;
            int width = Math.Max(240, tabPage6.ClientSize.Width - left - right);

            materialCard15.SetBounds(
                left,
                Math.Max(top + 120, tabPage6.ClientSize.Height - bottom - statusHeight),
                width,
                statusHeight);

            int card14Height = Math.Max(180, materialCard15.Top - top - gap);
            materialCard14.SetBounds(left, top, width, card14Height);
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

            _fsSyncHistoryBtn = new SupeyButton

            {

                Text = "SYNC HISTORY",

                Kind = SupeyButton.Variant.Secondary,

                Size = new Size(132, 30),

                Margin = new Padding(6, 1, 0, 0),

                Enabled = true,

                Visible = false,

            };

            var saveTip = SupeyToolTip.Create(autoPopDelay: 12000, initialDelay: 400);

            _fsSaveBtn.Click += fsSaveBtn_Click;

            saveTip.SetToolTip(_fsSaveBtn,
                "Save the workbook using the service date (no file dialog). Overwrites the loaded .xlsx, or saves to Desktop\\SCHEDULES FOR {year}\\.");

            saveTip.SetToolTip(_fsLoadBtn,
                "Open a saved .xlsx workbook or driver .csv (Excel not required). Date is read from the file name or trip dates and sets the date picker.");
            _fsSyncHistoryBtn.Click += async (s, e) => await FsSyncHistoryNowAsync().ConfigureAwait(true);
            saveTip.SetToolTip(_fsSyncHistoryBtn,
                "Sync historical schedules from Desktop schedule folders into AI memory.");



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

            leftFlow.Controls.Add(_fsSyncHistoryBtn);



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

            string msg = text ?? "";
            _fsToolbarStatusLbl.Text = msg;
            if (sbstatuslbl != null && !sbstatuslbl.IsDisposed)
                sbstatuslbl.Text = msg;

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

            EnableFsControlDoubleBuffer(_fsDriverTabFlow);

            _fsDriverTabStrip.Controls.Add(_fsDriverTabFlow);

            _fsDriverTabStrip.Controls.Add(tabDivider);

            WireFsEmailSchedulesButton(_fsDriverTabStrip);

            WireFsSyncNewTripsButton(_fsDriverTabStrip);

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

                MultiSelect = true,

                Font = new Font("Segoe UI", 9.5f),

                OwnerDraw = true,

                HeaderStyle = ColumnHeaderStyle.Clickable,

                SuppressHoverRepaintFix = true,

            };

            _fsTripsLv.DrawColumnHeader += FsTripsLv_DrawColumnHeader;

            _fsTripsLv.DrawItem += FsTripsLv_DrawItem;

            _fsTripsLv.DrawSubItem += FsTripsLv_DrawSubItem;

            ConfigureFsTripsListViewColumns();

            ScheduleBuilderTripAlertsColumn.EnsureRowHeightFitsIcons(_fsTripsLv);

            host.Controls.Add(_fsTripsLv);

            BuildFsCutTripBar(host);

            BuildFsNewTripsBar(host);

            BuildFsMapModeToolbar(host);

            host.Controls.Add(_fsDriverTabStrip);

            ListViewMinWidthEnforcer.Attach(_fsTripsLv);

            ConfigureFsTripsColumnWidths();

            _fsTripsLv.ColumnWidthChanged += FsTripsLv_ColumnWidthChanged;

            ListViewHeaderEmptyAreaPainter.Attach(_fsTripsLv);

            BuildFsTripsContextMenu();

            _fsTripsLv.MouseUp += FsTripsLv_MouseUp_ShowContextMenu;

            _fsTripsLv.KeyDown += FsTripsLv_KeyDown_ScheduleShortcuts;

            WireFsTripsAlertIconToolTip();

            WireFsTripsListDragDrop();

            if (FsTripDragDropEnabled)
                _fsTripsLv.PostPaintItems = FsTripsPostPaintDragCursor;



            _fsTripsLv.SelectedIndexChanged += (s, e) => FsTripsLv_SelectionChangedUpdateMap();

        }



        private void RebuildFsDriverTabs(IReadOnlyList<string> tabNames = null)

        {

            if (_fsDriverTabFlow == null) return;

            _fsDriverTabFlow.SuspendLayout();

            _fsDriverTabFlow.Controls.Clear();

            _fsDriverTabButtons.Clear();

            if (tabNames != null)
                _fsDriverTabOrder = tabNames.ToList();



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
                btn.MouseDown += FsDriverTabButton_MouseDown_Reorder;

                _fsDriverTabButtons[name] = btn;

                _fsDriverTabFlow.Controls.Add(btn);

            }



            _fsDriverTabFlow.ResumeLayout(true);

        }



        private void FsDriverTabButton_Click(object sender, EventArgs e)

        {

            if (_fsDriverTabSuppressClick)
            {
                _fsDriverTabSuppressClick = false;
                return;
            }

            if (sender is SupeyButton btn && btn.Tag is string name)

                SelectFsDriverTab(name);

        }



        private void SelectFsDriverTab(string tabName, bool refreshMap = true)

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

            // Keep the user's map display mode (default: selected group only).
            _fsShowAllGroupsOnNextMapLoad = _fsMapDisplayMode == FsMapDisplayMode.AllDriverTrips;
            _fsFocusFirstGroupAfterPreviewBind = false;

            PushFsMapSelectionSuppress();
            try
            {
                ClearFsTripsListSelection();
                ShowFsTripsForTab(tabName, preserveScroll: false);
                TrySelectFirstTripOnActiveDriverTab();
            }
            finally
            {
                PopFsMapSelectionSuppress();
            }

            if (refreshMap && !_fsMapPreloadRunning)
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



        private bool FsTripsIsMergedBarRow(ListViewItem item, out FsPreviewNoteTag noteTag, out FsPreviewSectionHeaderTag sectionTag, out FsPreviewGapTag gapNoteTag)
        {
            noteTag = item?.Tag as FsPreviewNoteTag;
            sectionTag = item?.Tag as FsPreviewSectionHeaderTag;
            gapNoteTag = item?.Tag as FsPreviewGapTag;
            bool isReservesTab = _fsActiveDriverTab != null
                && _fsActiveDriverTab.Equals("Reserves", StringComparison.OrdinalIgnoreCase);
            if (noteTag?.Group != null)
                return true;
            if (gapNoteTag != null && ScheduleBuilderGapNotes.GapTagHasNoteBar(gapNoteTag))
                return true;
            return sectionTag != null && isReservesTab;
        }

        private bool FsTripsIsMergedBarRow(ListViewItem item, out FsPreviewNoteTag noteTag, out FsPreviewSectionHeaderTag sectionTag)
        {
            return FsTripsIsMergedBarRow(item, out noteTag, out sectionTag, out _);
        }

        private bool FsTripsTryGetEntireRowBounds(ListViewItem item, out Rectangle bounds)
        {
            bounds = Rectangle.Empty;
            if (_fsTripsLv == null || item == null)
                return false;
            try
            {
                bounds = _fsTripsLv.GetItemRect(item.Index, ItemBoundsPortion.Entire);
                int contentW = SupeyListViewHelpers.GetDetailsContentWidth(_fsTripsLv);
                if (contentW > bounds.Width)
                    bounds.Width = contentW;
            }
            catch (ArgumentException)
            {
                bounds = item.Bounds;
            }

            return bounds.Width > 0 && bounds.Height > 0;
        }

        private void FsTripsPaintMergedBarRow(Graphics g, ListViewItem item, bool selected)
        {
            if (g == null || item == null || !FsTripsIsMergedBarRow(item, out var noteTag, out var sectionTag, out var gapNoteTag))
                return;
            if (!FsTripsTryGetEntireRowBounds(item, out Rectangle rowBounds))
                return;

            int bump = FsTripsGetDragBumpPixels(item.Index);
            if (bump > 0)
                rowBounds = new Rectangle(rowBounds.X, rowBounds.Y + bump, rowBounds.Width, rowBounds.Height);

            FsTripsPaintDragDecorations(g, item, 0, rowBounds, bump);

            if (noteTag?.Group != null)
            {
                Color? barColor = ScheduleBuilderGroupNotes.ResolveNoteRowDisplayColor(
                    noteTag.NoteRowColor, noteTag.Group, FsShowGroupColorsEnabled);
                Color bg = selected
                    ? SupeyTheme.ListSelected
                    : (barColor ?? SupeyTheme.ListBody);
                Color fg = selected
                    ? SupeyTheme.ListSelectedText
                    : (noteTag.NoteTextColor
                        ?? (barColor.HasValue
                            ? ScheduleBuilderPreviewStyle.ContrastText(barColor.Value)
                            : SupeyTheme.ListText));
                string text = (noteTag.NoteText ?? "").Trim();
                SupeyListViewHelpers.PaintMergedDetailsRow(
                    g, rowBounds, bg, text, fg, _fsTripsLv.Font,
                    boldText: text.Length > 0,
                    centerText: noteTag.NoteTextCentered);
                return;
            }

            if (gapNoteTag != null && ScheduleBuilderGapNotes.GapTagHasNoteBar(gapNoteTag))
            {
                Color? barColor = gapNoteTag.NoteRowColor;
                Color bg = selected
                    ? SupeyTheme.ListSelected
                    : (barColor ?? SupeyTheme.ListBody);
                Color fg = selected
                    ? SupeyTheme.ListSelectedText
                    : (gapNoteTag.NoteTextColor
                        ?? (barColor.HasValue
                            ? ScheduleBuilderPreviewStyle.ContrastText(barColor.Value)
                            : SupeyTheme.ListText));
                string text = (gapNoteTag.NoteText ?? "").Trim();
                SupeyListViewHelpers.PaintMergedDetailsRow(
                    g, rowBounds, bg, text, fg, _fsTripsLv.Font,
                    boldText: text.Length > 0,
                    centerText: gapNoteTag.NoteTextCentered);
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
                SupeyListViewHelpers.DrawCellGridLines(e.Graphics, e.Bounds, _fsTripsLv);
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
            bool cancelled = !rerouted && tripTag?.CancelledOnWellRyde == true;

            Color rowBg = sel ? SupeyTheme.ListSelected : SupeyTheme.ListBody;

            if (!sel && rerouted)

                rowBg = ScheduleBuilderPreviewStyle.ReroutedTripBackColor;

            else if (!sel && cancelled)

                rowBg = ScheduleBuilderPreviewStyle.CancelledTripBackColor;

            else if (!sel && isSection)

                rowBg = isReservesTab
                    ? sectionTag.SectionColor
                    : SupeyTheme.SurfaceHeader;

            else if (!sel && isNote && noteTag != null)
            {
                Color? barColor = ScheduleBuilderGroupNotes.ResolveNoteRowDisplayColor(
                    noteTag.NoteRowColor, noteTag.Group, FsShowGroupColorsEnabled);
                if (barColor.HasValue)
                    rowBg = FsRouteHeaderBackColor(barColor.Value);
            }

            Color fill = rowBg;

            if (sel && rerouted)

                fill = ScheduleBuilderPreviewStyle.ReroutedTripSelectedBackColor;

            else if (sel && cancelled)

                fill = ScheduleBuilderPreviewStyle.CancelledTripSelectedBackColor;

            else if (!sel && !isGap && !isNote && !rerouted && !cancelled && e.ColumnIndex == 0 && FsShowGroupColorsEnabled
                && !isReservesTab

                && e.SubItem != null && e.SubItem.BackColor != Color.Empty

                && e.SubItem.BackColor != SupeyTheme.ListBody)

                fill = e.SubItem.BackColor;



            SupeyListViewHelpers.DrawSubItemCellBackground(e, fill, cellBounds);

            if (e.ColumnIndex == ScheduleBuilderTripAlertsColumn.ColumnIndex)
            {
                if (tripTag != null)
                {
                    var alerts = ScheduleBuilderTripAlertsColumn.ResolveAlerts(tripTag);
                    ScheduleBuilderTripAlertsColumn.PaintIcons(e.Graphics, cellBounds, alerts, fill);
                }

                SupeyListViewHelpers.DrawCellGridLines(e.Graphics, cellBounds, _fsTripsLv);
                return;
            }

            var bounds = new Rectangle(cellBounds.Left + 6, cellBounds.Top, cellBounds.Width - 6, cellBounds.Height);

            Color textColor = sel ? SupeyTheme.ListSelectedText : SupeyTheme.ListText;

            if (!sel && rerouted)

                textColor = Color.White;

            else if (!sel && cancelled)

                textColor = Color.White;

            var gapTag = e.Item?.Tag as FsPreviewGapTag;
            if (!sel && isGap && gapTag != null && e.ColumnIndex == ScheduleBuilderTripAlertsColumn.GapNoteColumnIndex && !string.IsNullOrWhiteSpace(gapTag.NoteText))
                textColor = SupeyTheme.TextSecondary;

            if (!sel && isSection && !isReservesTab && e.ColumnIndex == ScheduleBuilderTripAlertsColumn.SectionLabelColumnIndex)

                textColor = SupeyTheme.TextPrimary;

            Font drawFont = isSection && !isReservesTab && e.ColumnIndex == ScheduleBuilderTripAlertsColumn.SectionLabelColumnIndex

                ? new Font(_fsTripsLv.Font, FontStyle.Bold)

                : (isGap && gapTag != null && !string.IsNullOrWhiteSpace(gapTag.NoteText) && e.ColumnIndex == ScheduleBuilderTripAlertsColumn.GapNoteColumnIndex)
                    ? new Font(_fsTripsLv.Font, FontStyle.Italic)
                    : _fsTripsLv.Font;

            TextFormatFlags align = TextFormatFlags.Left;
            if (e.ColumnIndex >= 0 && e.ColumnIndex < _fsTripsLv.Columns.Count)
            {
                switch (_fsTripsLv.Columns[e.ColumnIndex].TextAlign)
                {
                    case HorizontalAlignment.Right:
                        align = TextFormatFlags.Right;
                        break;
                    case HorizontalAlignment.Center:
                        align = TextFormatFlags.HorizontalCenter;
                        break;
                }
            }

            TextRenderer.DrawText(e.Graphics,
                SupeyListViewHelpers.GetCellDisplayText(_fsTripsLv, e.ColumnIndex, e.SubItem?.Text ?? ""),
                drawFont, bounds, textColor,

                align | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter

                | TextFormatFlags.WordEllipsis | TextFormatFlags.GlyphOverhangPadding);



            SupeyListViewHelpers.DrawCellGridLines(e.Graphics, cellBounds, _fsTripsLv);

        }



        private void WireFsTripsAlertIconToolTip()
        {
            if (_fsTripsLv == null)
                return;

            _fsTripsAlertTip = SupeyToolTip.Create(initialDelay: 400, autoPopDelay: 12000, reshowDelay: 200);
            _fsTripsAlertTip.Tag = (Func<string>)(() => _fsTripsAlertTipLastText);
            _fsTripsLv.ShowItemToolTips = false;
            _fsTripsLv.MouseMove += FsTripsLv_MouseMove_AlertToolTip;
            _fsTripsLv.MouseLeave += FsTripsLv_MouseLeave_AlertToolTip;

            _fsTripsAlertTipDelayTimer = new System.Windows.Forms.Timer { Interval = 180 };
            _fsTripsAlertTipDelayTimer.Tick += FsTripsAlertTipDelayTimer_Tick;
        }

        private void FsTripsLv_MouseMove_AlertToolTip(object sender, MouseEventArgs e)
        {
            if (_fsTripsLv == null || _fsTripsAlertTip == null)
                return;

            ListViewHitTestInfo hit = _fsTripsLv.HitTest(e.Location);
            if (hit?.Item?.Tag is FsPreviewTripTag tripTag
                && ScheduleBuilderTripAlertsColumn.TryGetAlertsCellBounds(
                    _fsTripsLv,
                    hit.Item,
                    FsTripsGetDragBumpPixels(hit.Item.Index),
                    out Rectangle cellBounds)
                && cellBounds.Contains(e.Location))
            {
                var alerts = ScheduleBuilderTripAlertsColumn.ResolveAlerts(tripTag);
                if (ScheduleBuilderTripAlertsColumn.TryGetIconAtPoint(
                        cellBounds, alerts, e.Location, out var kind, out Rectangle iconBounds))
                {
                    string tip = ScheduleBuilderTripAlertsColumn.GetDisplayName(kind);
                    if (_fsTripsAlertTipActiveKind == kind)
                        return;

                    if (_fsTripsAlertTipPendingKind == kind)
                        return;

                    FsTripsQueueAlertToolTip(kind, tip, iconBounds);
                    return;
                }
            }

            FsTripsClearAlertToolTip();
        }

        private void FsTripsQueueAlertToolTip(
            ScheduleBuilderTripAlertKind kind,
            string tip,
            Rectangle iconBounds)
        {
            FsTripsCancelAlertToolTipDelay();

            if (_fsTripsAlertTipActiveKind != null)
            {
                FsTripsShowAlertToolTip(kind, tip, iconBounds);
                return;
            }

            _fsTripsAlertTipPendingKind = kind;
            _fsTripsAlertTipPendingText = tip ?? "";
            _fsTripsAlertTipPendingIconBounds = iconBounds;
            _fsTripsAlertTipDelayTimer?.Start();
        }

        private void FsTripsAlertTipDelayTimer_Tick(object sender, EventArgs e)
        {
            _fsTripsAlertTipDelayTimer?.Stop();
            if (_fsTripsAlertTipPendingKind == null)
                return;

            FsTripsShowAlertToolTip(
                _fsTripsAlertTipPendingKind.Value,
                _fsTripsAlertTipPendingText,
                _fsTripsAlertTipPendingIconBounds);
            _fsTripsAlertTipPendingKind = null;
            _fsTripsAlertTipPendingText = "";
        }

        private void FsTripsShowAlertToolTip(
            ScheduleBuilderTripAlertKind kind,
            string tip,
            Rectangle iconBounds)
        {
            tip = (tip ?? "").Trim();
            if (tip.Length == 0 || _fsTripsLv == null || _fsTripsAlertTip == null)
                return;

            if (_fsTripsAlertTipActiveKind == kind && tip == _fsTripsAlertTipLastText)
                return;

            if (_fsTripsAlertTipActiveKind != null)
                _fsTripsAlertTip.Hide(_fsTripsLv);

            _fsTripsAlertTipLastText = tip;
            _fsTripsAlertTipActiveKind = kind;

            Size tipSize = TextRenderer.MeasureText(
                tip,
                SupeyTheme.CaptionFont,
                new Size(440, int.MaxValue),
                TextFormatFlags.SingleLine | TextFormatFlags.Left);
            tipSize = new Size(tipSize.Width + 20, tipSize.Height + 10);

            Point anchor = ScheduleBuilderTripAlertsColumn.GetToolTipAnchor(iconBounds, tipSize);
            _fsTripsAlertTip.Show(tip, _fsTripsLv, anchor.X, anchor.Y, 12000);
        }

        private void FsTripsCancelAlertToolTipDelay()
        {
            _fsTripsAlertTipDelayTimer?.Stop();
            _fsTripsAlertTipPendingKind = null;
            _fsTripsAlertTipPendingText = "";
        }

        private void FsTripsClearAlertToolTip()
        {
            FsTripsCancelAlertToolTipDelay();
            if (_fsTripsAlertTipActiveKind == null && _fsTripsAlertTipLastText.Length == 0)
                return;

            _fsTripsAlertTipActiveKind = null;
            _fsTripsAlertTipLastText = "";
            _fsTripsAlertTip?.Hide(_fsTripsLv);
        }

        private void FsTripsLv_MouseLeave_AlertToolTip(object sender, EventArgs e)
        {
            FsTripsClearAlertToolTip();
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



            int total = _fsMainSplit.Height;

            if (total < 80) return;



            // Trips expands to fill the entire map area; the map collapses behind it.
            // The user can still drag the splitter down to reveal the map again.
            _fsMainSplit.Panel2MinSize = 72;

            _fsMainSplit.Panel1MinSize = 0;

            _fsMainSplit.FixedPanel = FixedPanel.Panel1;



            _applyingFsDefaultSplit = true;

            try

            {

                _fsMainSplit.SplitterDistance = 0;

            }

            finally { _applyingFsDefaultSplit = false; }



            // Keep the full-fill on later resizes instead of snapping back to a default split.
            _fsDefaultSplitApplied = true;

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

            _fsTripsLv.Columns.Add("Grp", 34);

            _fsTripsLv.Columns.Add("Alerts", ScheduleBuilderTripAlertsColumn.DefaultWidthPx);

            _fsTripsLv.Columns.Add("Trip #", 72);

            _fsTripsLv.Columns.Add("Date", 68);

            _fsTripsLv.Columns.Add("Client", 82);

            _fsTripsLv.Columns.Add("PU Time", 72);

            _fsTripsLv.Columns.Add("PU Street", 92);

            _fsTripsLv.Columns.Add("PU City", 58);

            _fsTripsLv.Columns.Add("DO Time", 72);

            _fsTripsLv.Columns.Add("DO Street", 92);

            _fsTripsLv.Columns.Add("DO City", 58);

            _fsTripsLv.Columns.Add("Miles", 42);

            _fsTripsLv.Columns.Add("Comments", 130);

            _fsTripsLv.Columns[ScheduleBuilderTripAlertsColumn.PuTimeColumnIndex].TextAlign = HorizontalAlignment.Right;  // PU Time
            _fsTripsLv.Columns[ScheduleBuilderTripAlertsColumn.DoTimeColumnIndex].TextAlign = HorizontalAlignment.Right;  // DO Time

        }



        /// <summary>
        /// Cap trip-list auto-fit so wide addresses/comments ellipsize instead of stretching the grid.
        /// Column widths are global across driver tabs (not recomputed per tab).
        /// </summary>
        private void ConfigureFsTripsColumnWidths()
        {
            if (_fsTripsLv == null) return;

            ListViewMinWidthEnforcer.SetContentAutoFit(_fsTripsLv, false);
            ApplyFsTripsColumnWidths();
        }

        private void ApplyFsTripsColumnWidths()
        {
            if (_fsTripsLv == null) return;

            int[] widths = _fsTripsColumnWidthsPx ?? ScheduleBuilderListViewColumnWidths.DefaultTripsListViewColumnWidthsPx;
            ScheduleBuilderListViewColumnWidths.PinTripsListViewWidths(_fsTripsLv, widths);
        }

        /// <summary>
        /// Size the Alerts column to the row with the most icons across every tab, then pin it so
        /// all tabs share the same width. Call after anything that changes alerts (analyzer,
        /// cancel sync, reroute sync).
        /// </summary>
        private void FsAutoSizeAlertsColumnToWidest()
        {
            if (_fsTripsLv == null || _fsTripsLv.IsDisposed || _fsLinesByTab == null)
                return;

            int maxIcons = 0;
            foreach (var kv in _fsLinesByTab)
            {
                var lines = kv.Value;
                if (lines == null)
                    continue;

                foreach (var line in lines)
                {
                    if (line?.Kind != ScheduleBuilderPreviewLine.LineKind.Trip || line.Trip == null)
                        continue;

                    int n = ScheduleBuilderTripAlertsColumn.CountAlerts(
                        line.Trip, line.CancelledOnWellRyde, line.ReroutedOnModivcare);
                    if (n > maxIcons)
                        maxIcons = n;
                }
            }

            int width = ScheduleBuilderTripAlertsColumn.WidthForIconCount(maxIcons);

            int[] widths = _fsTripsColumnWidthsPx
                ?? ScheduleBuilderListViewColumnWidths.CaptureListViewColumnPixels(_fsTripsLv)
                ?? (int[])ScheduleBuilderListViewColumnWidths.DefaultTripsListViewColumnWidthsPx.Clone();

            if (ScheduleBuilderTripAlertsColumn.ColumnIndex >= widths.Length
                || widths[ScheduleBuilderTripAlertsColumn.ColumnIndex] == width)
                return;

            widths[ScheduleBuilderTripAlertsColumn.ColumnIndex] = width;
            _fsTripsColumnWidthsPx = widths;
            ScheduleBuilderListViewColumnWidths.PinTripsListViewWidths(_fsTripsLv, widths);
        }

        private void FsTripsLv_ColumnWidthChanged(object sender, ColumnWidthChangedEventArgs e)
        {
            if (_fsTripsLv == null || ListViewMinWidthEnforcer.IsApplyingColumnWidths(_fsTripsLv))
                return;

            _fsTripsColumnWidthsPx = ScheduleBuilderListViewColumnWidths.CaptureListViewColumnPixels(_fsTripsLv);
            ScheduleBuilderListViewColumnWidths.PinTripsListViewWidths(_fsTripsLv, _fsTripsColumnWidthsPx);
        }



        private async void fsLoadBtn_Click(object sender, EventArgs e)

        {

            DateTime loadDay = fsbdatepicker?.Value.Date ?? DateTime.Today;
            string path = null;
            string ext = null;

            // Prefer Desktop / AI server workbook for the datepicker day (kitchen PCs).
            try
            {
                var resolved = await ScheduleWorkbookResolver.ResolveForReadAsync(
                    loadDay, HiatmeAiSettings.Load()).ConfigureAwait(true);
                if (resolved != null
                    && !string.IsNullOrWhiteSpace(resolved.FullPath)
                    && File.Exists(resolved.FullPath))
                {
                    path = resolved.FullPath;
                    ext = (Path.GetExtension(path) ?? "").ToLowerInvariant();
                    string origin = string.Equals(
                        resolved.Source, "server_cache", StringComparison.OrdinalIgnoreCase)
                        ? "server cache"
                        : "Desktop";
                    SetScheduleBuilderStatus(
                        "Loading " + (resolved.FileName ?? Path.GetFileName(path))
                        + " (" + origin + ")…");
                }
            }
            catch { }

            if (string.IsNullOrWhiteSpace(path))
            {
                using (var dlg = new OpenFileDialog())
                {
                    dlg.Title = "Load saved schedule";

                    int loadYear = loadDay.Year;
                    string desktopDir = ScheduleExportPaths.ResolveDesktopYearFolder(loadYear);
                    string cacheDir = ScheduleWorkbookResolver.LocalCacheYearFolder(loadYear);
                    dlg.InitialDirectory = Directory.Exists(desktopDir) && Directory.GetFiles(desktopDir).Length > 0
                        ? desktopDir
                        : (Directory.Exists(cacheDir) ? cacheDir : desktopDir);

                    dlg.Filter =

                        "Schedule|*.xlsx;*.xls;*.csv|Excel workbook|*.xlsx;*.xls|CSV (pick any file in the folder)|*.csv|All files|*.*";

                    dlg.CheckFileExists = true;

                    if (dlg.ShowDialog(this) != DialogResult.OK)

                        return;

                    path = dlg.FileName;
                    ext = (Path.GetExtension(path) ?? "").ToLowerInvariant();
                }
            }

            {

                _fsHasPreview = false;

                SetFsPreviewExportButtonsEnabled(false);

                if (fsbdatepicker != null) fsbdatepicker.Enabled = false;

                if (_fsBuildBtn != null) _fsBuildBtn.Enabled = false;

                if (_fsLoadBtn != null) _fsLoadBtn.Enabled = false;



                ShowTabLoadingOverlay(tabPage6, "Loading schedule…");

                try

                {

                    FsCancelRerouteProbe();
                    SetScheduleBuilderStatus("Loading schedule…");
                    UpdateTabLoadingOverlayMessage(tabPage6, "Loading schedule…");

                    ScheduleBuilderLoadResult load;

                    if (ext == ".xlsx" || ext == ".xls")
                    {
                        // Prefer saving back to Desktop when that file exists; cache loads stay cache.
                        ScheduleExportPaths.GetDefaultWorkbookSaveLocation(
                            loadDay, out _, out _, out string desktopSave);
                        _fsPreferredSavePath = File.Exists(desktopSave) ? desktopSave : path;
                        load = await ScheduleBuilderScheduleLoad.LoadFromWorkbookAsync(path)
                            .ConfigureAwait(true);
                    }
                    else if (ext == ".csv")
                    {
                        _fsPreferredSavePath = null;

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

                    UpdateTabLoadingOverlayMessage(tabPage6, "Checking rerouted-trips registry…");
                    SetScheduleBuilderStatus("Checking rerouted-trips registry…");

                    var rerouteMerge = await ScheduleBuilderReroutedTripsRegistry.MergeIntoBuilderAsync(
                        fsbuilder, serviceDate, HiatmeAiSettings.Load()).ConfigureAwait(true);

                    var reroutedKeys = ScheduleBuilderReroutedTripsRegistry.UnionReroutedTripNumbers(
                        load.ReroutedTripNumbers,
                        rerouteMerge.ReroutedTripNumbers);
                    fsbuilder.ReconcileReroutedTripsForPreview(reroutedKeys);
                    fsbuilder.DemoteUnconfirmedPartnerLegsForAllCancels(reroutedKeys);

                    var driverSync = await SyncFsDriversDuringBuildAsync(
                        fsbuilder.PreviewDriverLines.Keys,
                        text =>
                        {
                            SetScheduleBuilderStatus(text);
                            UpdateTabLoadingOverlayMessage(tabPage6, text);
                        }).ConfigureAwait(true);

                    BindScheduleBuilderPreview(fsbuilder, showInitialTab: false);
                    ScheduleBuilderReroutedTripsRegistry.MarkReroutedOnPreview(
                        _fsLinesByTab, reroutedKeys);
                    FsSetReroutedTripKeyCache(reroutedKeys);

                    _fsHasPreview = true;
                    SetFsPreviewExportButtonsEnabled(true);

                    UpdateTabLoadingOverlayMessage(tabPage6, "Verifying reroutes on Modivcare…");

                    string rerouteVerifyNote = await FsProbeReroutesAfterScheduleLoadAsync(
                            serviceDate,
                            refreshListView: false)
                        .ConfigureAwait(true);

                    UpdateTabLoadingOverlayMessage(tabPage6, "Checking cancelled trips on WellRyde…");

                    string cancelVerifyNote = await FsSyncCancelledTripsFromWellRydeAsync(
                            serviceDate,
                            refreshListView: false)
                        .ConfigureAwait(true);

                    UpdateTabLoadingOverlayMessage(tabPage6, "Checking Modivcare for new trips…");

                    var newTripsResult = await FsSyncNewModivcareTripsAsync(serviceDate)
                        .ConfigureAwait(true);
                    string newTripsNote = newTripsResult.StatusNote;

                    await FsApplyAnalyzerAlertsAsync(serviceDate).ConfigureAwait(true);

                    if (ScheduleBuilderPreviewUndo.LinesByTabContainsGap(_fsLinesByTab))
                        FsRevealGapsForManualInsert();

                    FsShowInitialTabAfterScheduleLoad();

                    _fsFocusFirstGroupAfterPreviewBind = true;
                    FsSyncMapToTripListSelectionAfterPreviewBind();

                    if (load.WorkbookColumnWidths != null)
                        _fsTripsColumnWidthsPx = ScheduleBuilderListViewColumnWidths.ApplyToTripsListView(_fsTripsLv, load.WorkbookColumnWidths);

                    FsAutoSizeAlertsColumnToWidest();

                    int drivers = fsbuilder.PreviewDriverLines.Count;

                    int trips = fsbuilder.PreviewDriverLines.Values.Sum(

                        l => l.Count(x => x?.Kind == ScheduleBuilderPreviewLine.LineKind.Trip));

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

                        + (rerouteMerge.GhostsAdded > 0
                            ? ", " + rerouteMerge.GhostsAdded + " rerouted ghost"
                              + (rerouteMerge.GhostsAdded == 1 ? "" : "s")
                            : "")

                        + ". Groups: " + groupingSummary + "."
                        + FormatFsDriverSyncNote(driverSync)
                        + " Undo history cleared."
                        + rerouteVerifyNote
                        + cancelVerifyNote
                        + newTripsNote);

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

                    HideTabLoadingOverlay(tabPage6, force: true);

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

            FsCancelRerouteProbe();
            SetScheduleBuilderStatus("Checking connections…");

            _fsHasPreview = false;
            _fsPreferredSavePath = null;

            SetFsPreviewExportButtonsEnabled(false);

            if (fsbdatepicker != null) fsbdatepicker.Enabled = false;

            if (_fsBuildBtn != null) _fsBuildBtn.Enabled = false;

            if (_fsLoadBtn != null) _fsLoadBtn.Enabled = false;



            string dayname = fsbdatepicker.Value.DayOfWeek.ToString();

            string day = fsbdatepicker.Value.Day.ToString();

            string nameofmonth = fsbdatepicker.Value.ToString("MMMM");

            string month = fsbdatepicker.Value.Month.ToString();

            string year = fsbdatepicker.Value.Year.ToString();



            void OnBuildStatus(string text)
            {
                SetScheduleBuilderStatus(text);
                if (!string.IsNullOrWhiteSpace(text))
                    UpdateTabLoadingOverlayMessage(tabPage6, text);
            }



            ShowTabLoadingOverlay(tabPage6, "Checking connections…");

            try

            {
                if (FsSafeBuildModeEnabled)
                    SetScheduleBuilderStatus("Safe Build Mode ON — running template-first build…");

                if (!await EnsureModivcareSessionAsync())

                {

                    SetScheduleBuilderStatus("Modivcare sign-in required.");

                    return;

                }



                fsbuilder = new FullScheduleBuilder(dayname, day, nameofmonth, month, year);

                fsbuilder.PreviewCsvExportOptions = MakeFsPreviewCsvExportOptions();
                fsbuilder.PreserveMultiRowGaps = FsMultiRowGapsEnabled;
                if (_fsDriverTabOrder != null && _fsDriverTabOrder.Count > 0)
                    fsbuilder.PreferredTabOrder = _fsDriverTabOrder.ToList();

                fsbuilder.UpdateLoadingScreen += OnBuildStatus;



                await fsbuilder.BuildPreviewAsync(fsbdatepicker.Value, mcLoginHandler).ConfigureAwait(true);

                var rerouteMerge = await ScheduleBuilderReroutedTripsRegistry.MergeIntoBuilderAsync(
                    fsbuilder, fsbdatepicker.Value.Date, HiatmeAiSettings.Load()).ConfigureAwait(true);

                fsbuilder.ReconcileReroutedTripsForPreview(rerouteMerge.ReroutedTripNumbers);
                fsbuilder.DemoteUnconfirmedPartnerLegsForAllCancels(rerouteMerge.ReroutedTripNumbers);

                var driverSync = await SyncFsDriversDuringBuildAsync(
                    fsbuilder.PreviewDriverLines.Keys,
                    OnBuildStatus).ConfigureAwait(true);

                BindScheduleBuilderPreview(fsbuilder);
                ScheduleBuilderReroutedTripsRegistry.MarkReroutedOnPreview(
                    _fsLinesByTab, rerouteMerge.ReroutedTripNumbers);
                FsSetReroutedTripKeyCache(rerouteMerge.ReroutedTripNumbers);

                string cancelVerifyNote = await FsSyncCancelledTripsFromWellRydeAsync(
                        fsbdatepicker.Value.Date,
                        refreshListView: false)
                    .ConfigureAwait(true);

                await FsApplyAnalyzerAlertsAsync(fsbdatepicker.Value.Date).ConfigureAwait(true);

                int rer = fsbuilder.PreviewReservesReroute?.Count ?? 0;
                if (rer > 0)
                    ShowFsTripsForTab("Reserves");
                else if (!string.IsNullOrWhiteSpace(_fsActiveDriverTab))
                    ShowFsTripsForTab(_fsActiveDriverTab);
                FsSyncReroutedHighlightsFromPreviewLines();

                int drivers = fsbuilder.PreviewDriverLines.Count;

                int trips = fsbuilder.PreviewDriverLines.Values.Sum(

                    l => l.Count(x => x?.Kind == ScheduleBuilderPreviewLine.LineKind.Trip));

                _fsHasPreview = true;

                SetFsPreviewExportButtonsEnabled(true);

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

                if (rerouteMerge.GhostsAdded > 0)
                    resMsg += ", " + rerouteMerge.GhostsAdded + " rerouted ghost"
                        + (rerouteMerge.GhostsAdded == 1 ? "" : "s");

                string buildSummary = "Built — " + drivers + " driver tab(s), "

                    + trips + " trip(s), " + resMsg

                    + "." + FormatFsDriverSyncNote(driverSync)
                    + " Undo history cleared."
                    + cancelVerifyNote;

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

                HideTabLoadingOverlay(tabPage6, force: true);

                EnableScheduleBuilderInputs(true);

            }

        }



        private async Task FsApplyAnalyzerAlertsAsync(DateTime serviceDate)
        {
            if (fsbuilder?.PreviewDriverLines == null)
                return;

            void AlertStatus(string text)
            {
                SetScheduleBuilderStatus(text);
                UpdateTabLoadingOverlayMessage(tabPage6, text);
            }

            try
            {
                AlertStatus("Connecting to WellRyde for trip alerts…");
                if (await EnsureWellRydePortalSessionForBillingAsync(
                        showMessageIfNoCredentials: false,
                        showMessageOnLoginFailure: false).ConfigureAwait(true))
                    analyzer.SetWellRydePortalSession(_wellRydeSession);
                else
                    analyzer.SetWellRydePortalSession(null);

                AlertStatus("Connecting to Modivcare for trip alerts…");
                if (!await EnsureModivcareSessionAsync().ConfigureAwait(true))
                    return;

                AlertStatus("Downloading Modivcare trips and checking alerts…");
                analyzer.IntializeAnalyzer(mcLoginHandler);
                await analyzer.ApplyAlertsToScheduleBuilderAsync(fsbuilder, serviceDate).ConfigureAwait(true);

                FsAutoSizeAlertsColumnToWidest();
                if (_fsTripsLv != null && !_fsTripsLv.IsDisposed)
                    _fsTripsLv.Invalidate();
            }
            catch (ScheduleAnalysisException ex)
            {
                SetScheduleBuilderStatus("Trip alert check skipped — " + ex.Message);
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

            if (_fsDriverTabOrder != null && _fsDriverTabOrder.Count > 0)
                fsbuilder.SetTabOrder(_fsDriverTabOrder);

            fsbuilder.PreviewCsvExportOptions = MakeFsPreviewCsvExportOptions();
            fsbuilder.WorkbookColumnWidths = ScheduleBuilderListViewColumnWidths.CaptureFromTripsListView(_fsTripsLv);
            fsbuilder.ExportPreviewCsvs(_fsLinesByTab);
        }



        private static List<ScheduleBuilderPreviewLine> BuildFsReservePreviewLines(FullScheduleBuilder builder)
        {
            if (builder == null)
                return new List<ScheduleBuilderPreviewLine>();

            if (builder.LoadedReserveSlots != null && builder.LoadedReserveSlots.Count > 0)
            {
                return ScheduleBuilderReserveBuckets.BuildReservePreviewLinesFromSlots(
                    builder.LoadedReserveSlots,
                    builder.PreviewReservesWillCalls,
                    builder.PreviewReserves,
                    builder.PreviewReservesReroute,
                    builder.PreviewReservesCancel);
            }

            return ScheduleBuilderReserveBuckets.BuildReservePreviewLines(
                builder.PreviewReserves,
                builder.PreviewReservesReroute,
                builder.PreviewReservesBanned,
                builder.PreviewReservesWillCalls,
                builder.WillCallsInDownloadCount,
                preserveTripOrder: builder.PreserveReserveTripOrder,
                cancels: builder.PreviewReservesCancel);
        }



        private void BindScheduleBuilderPreview(FullScheduleBuilder builder, bool showInitialTab = true)

        {

            if (builder == null || _fsDriverTabFlow == null || _fsTripsLv == null)

                return;



            _fsLinesByTab.Clear();
            ResetFsTabLinesRevisions();

            _fsWellRydeCancelledKeys = null;

            _fsReroutedTripKeys = null;

            _fsGroupsByTab.Clear();
            ClearFsTabMapCache();

            FsHideNewTripsBar();

            FsClearUndoHistory();

            _fsTripsLv.Items.Clear();



            var driverNames = ScheduleBuilderTabOrder.OrderDriverNames(
                builder.PreviewDriverLines.Keys,
                builder.TabOrder);

            foreach (var name in driverNames)

            {

                var lines = builder.PreviewDriverLines[name];

                if (lines == null) lines = new List<ScheduleBuilderPreviewLine>();
                else
                    lines = ScheduleBuilderGroupHeaderReconcile.Reconcile(lines);

                ScheduleBuilderTrailingRows.EnsureAtEnd(lines);

                SetFsLinesByTabEntry(name, lines);

                if (builder.PreviewDriverLines is Dictionary<string, List<ScheduleBuilderPreviewLine>> driverDict)
                    driverDict[name] = lines;

            }



            SetFsLinesByTabEntry("Reserves", BuildFsReservePreviewLines(builder));



            var tabNames = ScheduleBuilderTabOrder.NormalizeFullTabOrder(
                builder.TabOrder?.Count > 0 ? builder.TabOrder : null,
                _fsLinesByTab.Keys);

            RebuildFsDriverTabs(tabNames);

            _fsFocusFirstGroupAfterPreviewBind = true;
            _fsCenterMaineAfterBuild = true;

            if (!showInitialTab)
            {
                _fsActiveDriverTab = null;
                return;
            }

            // First driver tab so the map loads route groups, not an empty reserves view.
            if (ScheduleBuilderPreviewUndo.LinesByTabContainsGap(_fsLinesByTab))
                FsRevealGapsForManualInsert();

            var firstDriver = driverNames.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(firstDriver))

                SelectFsDriverTab(firstDriver, refreshMap: false);

            else if (tabNames.Count > 0)

                SelectFsDriverTab(tabNames[0], refreshMap: false);

            else

            {
                _fsActiveDriverTab = null;
                _fsMap?.CenterOnMaineHub();
                _fsCenterMaineAfterBuild = false;
            }

            if (showInitialTab && tabNames.Count > 0 && !string.IsNullOrWhiteSpace(_fsActiveDriverTab))
                StartFsMapPreloadAfterScheduleBind(tabNames);

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

            FsSyncMapToTripListSelectionAfterPreviewBind();
        }

        /// <summary>
        /// After BUILD/LOAD: pick the first group when requested, then always push list
        /// selection to the map (ShowDriverPlan resets overlays — filter must run after it).
        /// </summary>
        private void FsSyncMapToTripListSelectionAfterPreviewBind()
        {
            if (_fsMap == null || !_fsMap.Visible || !ScheduleOsrmGate.PreviewRoutingOk)
                return;

            if (_fsFocusFirstGroupAfterPreviewBind)
            {
                string tab = _fsActiveDriverTab;
                if (string.IsNullOrWhiteSpace(tab) || tab.Equals("Reserves", StringComparison.OrdinalIgnoreCase))
                    _fsFocusFirstGroupAfterPreviewBind = false;
                else if (_fsTripsLv != null && _fsTripsLv.Items.Count > 0)
                    TrySelectFirstTripOnActiveDriverTab();
            }

            if (_fsTripsLv != null && _fsTripsLv.SelectedItems.Count > 0)
            {
                _fsFocusFirstGroupAfterPreviewBind = false;
                _fsMap.SetMapDisplayFilterMode(_fsMapDisplayMode);
                FsTripsLv_SelectionChangedUpdateMap();
            }
            else if (!_fsFocusFirstGroupAfterPreviewBind)
            {
                ApplyFsMapDisplayFilter(autoFit: false);
            }
        }

        private bool TrySelectFirstTripOnActiveDriverTab()
        {
            if (_fsTripsLv == null || _fsTripsLv.Items.Count == 0)
                return false;

            foreach (ListViewItem item in _fsTripsLv.Items)
            {
                if (item.Tag is FsPreviewNoteTag || item.Tag is FsPreviewGapTag
                    || item.Tag is FsPreviewSectionHeaderTag)
                    continue;

                if (item.Tag is FsPreviewTripTag tripTag && tripTag.Trip != null)
                {
                    _fsTripsLv.SelectedItems.Clear();
                    item.Selected = true;
                    item.Focused = true;
                    item.EnsureVisible();
                    return true;
                }
            }

            return false;
        }

        private bool TrySelectFirstGroupOnActiveDriverTab()
        {
            if (_fsTripsLv == null || _fsTripsLv.Items.Count == 0)
                return false;

            string tab = _fsActiveDriverTab;
            if (_fsGroupsByTab.TryGetValue(tab ?? "", out var groups) && groups != null)
            {
                foreach (var g in groups)
                {
                    if (g == null || g.GroupNumber <= 0)
                        continue;

                    FsSelectGroupInListView(g.GroupNumber);
                    if (_fsTripsLv.SelectedItems.Count > 0)
                        return true;
                }
            }

            return TrySelectFirstTripOnActiveDriverTab();
        }

        private async Task RefreshFsMapForCurrentTabAsync()

        {

            if (_fsMap == null || string.IsNullOrWhiteSpace(_fsActiveDriverTab)) return;

            CancelFsMapPreloadIfRunning();

            string tabName = _fsActiveDriverTab;

            int gen = Interlocked.Increment(ref _fsMapRefreshGen);
            var token = ReplaceFsMapWorkToken();

            await RefreshFsMapCoreAsync(gen, tabName, token).ConfigureAwait(false);

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

            RequestFsMileageHudUpdate(group, trip);

        }



        private void EnsureFsMileageHudDebounceTimer()
        {
            if (_fsMileageHudDebounceTimer != null)
                return;

            _fsMileageHudDebounceTimer = new System.Windows.Forms.Timer { Interval = 150 };
            _fsMileageHudDebounceTimer.Tick += (s, e) =>
            {
                _fsMileageHudDebounceTimer.Stop();
                if (_fsMileageHudPendingGroup == null)
                    return;

                var group = _fsMileageHudPendingGroup;
                var trip = _fsMileageHudPendingTrip;
                _fsMileageHudPendingGroup = null;
                _fsMileageHudPendingTrip = null;
                _ = UpdateFsMapMileageHudAsync(group, trip);
            };
        }

        private void RequestFsMileageHudUpdate(SupeyTripCluster group, MCDownloadedTrip trip)
        {
            if (group == null)
                return;

            EnsureFsMileageHudDebounceTimer();
            _fsMileageHudPendingGroup = group;
            _fsMileageHudPendingTrip = trip;
            _fsMileageHudDebounceTimer.Stop();
            _fsMileageHudDebounceTimer.Start();
        }



        private async Task UpdateFsMapMileageHudAsync(SupeyTripCluster group, MCDownloadedTrip trip)

        {

            if (_fsMap == null || group == null) return;

            group = FsResolveLiveGroup(group);
            if (group == null) return;

            try { _fsMileageHudCts?.Cancel(); } catch { }
            _fsMileageHudCts?.Dispose();
            _fsMileageHudCts = new CancellationTokenSource();
            var token = _fsMileageHudCts.Token;

            int gen = Interlocked.Increment(ref _fsMileageHudGen);

            string activeTab = _fsActiveDriverTab;
            var pickup = _fsMapPickupByTrip;
            var dropoff = _fsMapDropoffByTrip;

            GeoPoint? pinPu = null;
            GeoPoint? pinDo = null;
            if (trip != null)
            {
                var pins = FsGetTripPinGeoPoints(trip);
                pinPu = pins.pu;
                pinDo = pins.dof;
            }

            FsMapBeginInvoke(() =>
            {
                if (gen != _fsMileageHudGen || _fsMap == null)
                    return;
                _fsMap.SetMileageHudBusy(group, trip);
            });

            try
            {
                MileageHudSnapshot snapshot = await Task.Run(async () =>
                    await ComputeFsMapMileageHudSnapshotAsync(
                        group, trip, activeTab, pickup, dropoff, pinPu, pinDo, gen, token).ConfigureAwait(false), token)
                    .ConfigureAwait(false);

                if (gen != _fsMileageHudGen)
                    return;

                if (snapshot == null)
                {
                    FsMapBeginInvoke(() =>
                    {
                        if (gen != _fsMileageHudGen)
                            return;
                        _fsMap?.ClearMileageHud();
                    });
                    return;
                }

                FsMapBeginInvoke(() =>
                {
                    if (gen != _fsMileageHudGen || _fsMap == null)
                        return;

                    _fsMap.SetMileageHud(
                        snapshot.Group,
                        snapshot.Trip,
                        snapshot.GroupMeters,
                        snapshot.TripMeters,
                        snapshot.TripApprox,
                        snapshot.ScorePercent,
                        snapshot.CurrentMeters,
                        snapshot.EfficiencyApprox,
                        snapshot.RouteChangeMeters);
                });
            }
            catch (OperationCanceledException)
            {
                if (gen == _fsMileageHudGen)
                {
                    FsMapBeginInvoke(() =>
                    {
                        if (gen != _fsMileageHudGen)
                            return;
                        _fsMap?.ClearMileageHud();
                    });
                }
            }

        }

        private sealed class MileageHudSnapshot
        {
            public SupeyTripCluster Group;
            public MCDownloadedTrip Trip;
            public double GroupMeters;
            public double? TripMeters;
            public bool TripApprox;
            public double? ScorePercent;
            public double CurrentMeters;
            public bool EfficiencyApprox;
            public double? RouteChangeMeters;
        }

        private (GeoPoint? pu, GeoPoint? dof) FsGetTripPinGeoPoints(MCDownloadedTrip trip)
        {
            if (trip == null || _fsMap == null)
                return (null, null);

            GeoPoint pu = default;
            GeoPoint dof = default;
            bool ok = false;

            void ReadPins()
            {
                ok = _fsMap.TryGetTripPinGeoPoints(trip, out pu, out dof);
            }

            if (InvokeRequired)
                Invoke((Action)ReadPins);
            else
                ReadPins();

            return ok ? ((GeoPoint?)pu, (GeoPoint?)dof) : (null, null);
        }

        private async Task<MileageHudSnapshot> ComputeFsMapMileageHudSnapshotAsync(
            SupeyTripCluster group,
            MCDownloadedTrip trip,
            string activeTab,
            Dictionary<string, GeoPoint> pickup,
            Dictionary<string, GeoPoint> dropoff,
            GeoPoint? pinPu,
            GeoPoint? pinDo,
            int gen,
            CancellationToken token)
        {
            if (gen != _fsMileageHudGen || token.IsCancellationRequested)
                return null;

            ScheduleBuilderMapMileage.HydrateGroupEndpointsFromLookup(group, pickup, dropoff);

            double? tripMeters = null;
            bool tripApprox = false;

            if (trip != null)
            {
                var tripLeg = await ScheduleBuilderMapMileage.ResolveTripPuDoMetersAsync(
                    group,
                    trip,
                    pickup,
                    dropoff,
                    pinPu,
                    pinDo,
                    token).ConfigureAwait(false);

                tripMeters = tripLeg.meters;
                tripApprox = tripLeg.approx;
            }

            if (gen != _fsMileageHudGen || token.IsCancellationRequested)
                return null;

            EnsureFsDriverRosterLoaded();
            var driverProfile = ScheduleBuilderDriverMapRouting.FindProfileForScheduleTab(
                _supeyRoster, activeTab);
            GeoPoint? homeGeo = null;
            var dayPosition = ScheduleBuilderDriverMapRouting.GroupDayPosition.Middle;
            if (driverProfile != null
                && _fsGroupsByTab.TryGetValue(activeTab, out var tabGroups)
                && tabGroups != null)
            {
                homeGeo = await ScheduleBuilderDriverMapRouting.ResolveHomeGeoAsync(
                    driverProfile, token).ConfigureAwait(false);
                int groupIndex = FindFsGroupIndex(tabGroups, group);
                dayPosition = ScheduleBuilderDriverMapRouting.ResolveDayPosition(
                    groupIndex, tabGroups.Count);
            }

            if (gen != _fsMileageHudGen || token.IsCancellationRequested)
                return null;

            double groupMeters = group.IntraClusterMeters > 0
                ? group.IntraClusterMeters
                : ScheduleBuilderMapMileage.GroupRouteMeters(group);

            var efficiency = await ScheduleBuilderMapMileage.ComputeGroupEfficiencyAsync(
                group,
                homeGeo,
                dayPosition,
                token,
                maxExactPermutationTrips: ScheduleBuilderMapMileage.MaxExactPermutationTripsDeskHud).ConfigureAwait(false);

            if (efficiency.currentMeters > 0)
                groupMeters = efficiency.currentMeters;

            double? routeChangeMeters = null;
            if (trip != null && _fsPreMoveGroupMeters.HasValue && _fsPreMoveTripRef != null
                && (ReferenceEquals(trip, _fsPreMoveTripRef)
                    || (!string.IsNullOrEmpty(trip.TripNumber)
                        && string.Equals(trip.TripNumber, _fsPreMoveTripRef.TripNumber,
                            StringComparison.OrdinalIgnoreCase))))
            {
                routeChangeMeters = groupMeters - _fsPreMoveGroupMeters.Value;
            }

            return new MileageHudSnapshot
            {
                Group = group,
                Trip = trip,
                GroupMeters = groupMeters,
                TripMeters = tripMeters,
                TripApprox = tripApprox,
                ScorePercent = efficiency.scorePercent,
                CurrentMeters = efficiency.currentMeters,
                EfficiencyApprox = efficiency.approx,
                RouteChangeMeters = routeChangeMeters,
            };
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



            void OnSaveStatus(string text)
            {
                SetScheduleBuilderStatus(text ?? "Saving…");
                if (!string.IsNullOrWhiteSpace(text))
                    UpdateTabLoadingOverlayMessage(tabPage6, text);
            }



            fsbuilder.UpdateLoadingScreen += OnSaveStatus;

            ShowTabLoadingOverlay(tabPage6, "Preparing export…");

            if (fsbdatepicker != null) fsbdatepicker.Enabled = false;

            if (_fsBuildBtn != null) _fsBuildBtn.Enabled = false;

            if (_fsLoadBtn != null) _fsLoadBtn.Enabled = false;

            SetFsPreviewExportButtonsEnabled(false);



            try

            {

                SetScheduleBuilderStatus("Preparing export…");

                if (fsbdatepicker != null)
                    fsbuilder.ApplyServiceDate(fsbdatepicker.Value);

                fsbuilder.PreferredExportPath = _fsPreferredSavePath;

                SyncFsPreviewCsvsForExport();

                await fsbuilder.CreateWorkbookAsync(promptForLocation: false).ConfigureAwait(true);



                if (!string.IsNullOrEmpty(fsbuilder.LastExportPath))

                {

                    _fsPreferredSavePath = fsbuilder.LastExportPath;

                    SetScheduleBuilderStatus(fsbuilder.LastExportWasCsv

                        ? "Saved CSV package — " + fsbuilder.LastExportPath

                        : "Saved workbook — " + fsbuilder.LastExportPath);

                }

                else

                    SetScheduleBuilderStatus("Save failed.");

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

                HideTabLoadingOverlay(tabPage6, force: true);

                EnableScheduleBuilderInputs(true);

                if (_fsHasPreview)
                    SetFsPreviewExportButtonsEnabled(true);

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

                if (!tabName.Equals("Reserves", StringComparison.OrdinalIgnoreCase))
                    ScheduleBuilderTrailingRows.EnsureAtEnd(lines);

                if (tabName.Equals("Reserves", StringComparison.OrdinalIgnoreCase))

                {

                    ShowFsReservesTab(lines);

                    _fsTripsLv.EndUpdate();

                    if (preserveScroll)
                        FsRestoreTripsListViewScroll(scrollAnchorLine, scrollAnchorItemIndex);

                    ApplyFsTripsColumnWidths();

                    FsSyncReroutedHighlightsFromPreviewLines();

                    return;

                }

                var groups = FsShowGroupColorsEnabled
                    ? ScheduleBuilderPreviewGroups.BuildFromPreviewLines(lines)
                    : ScheduleBuilderPreviewGroups.BuildTripFlatClustersFromPreviewLines(lines);

                _fsGroupsByTab[tabName] = groups;

                SupeyTripCluster lastHeaderGroup = null;
                bool sawTripRow = false;

                for (int li = 0; li < lines.Count; li++)

                {

                    var line = lines[li];

                    if (line == null) continue;

                    if (line.Kind == ScheduleBuilderPreviewLine.LineKind.Gap)

                    {

                        lastHeaderGroup = null;

                        if (ScheduleBuilderGapNotes.HasNoteContent(line))
                            AddFsPositionNoteRow(li, line.GapNoteText, line.GapNoteRowColor, line.GapNoteCenterText, line.GapNoteTextColor);
                        else if (line.TrailingPad || (FsShowGapsEnabled && sawTripRow))
                            AddFsTemplateGapRow(li, null, line.TrailingPad);

                        continue;

                    }

                    if (line.Kind == ScheduleBuilderPreviewLine.LineKind.GroupHeader)

                    {

                        if (ScheduleBuilderGroupNotes.ShouldShowNoteRow(line, FsShowGroupColorsEnabled))

                        {

                            var headerGroup = FindFsGroupByNumber(groups, line.GroupNumber);
                            if (headerGroup == null)
                                headerGroup = FindFsGroupForLineAfter(groups, lines, li);

                            if (headerGroup != null)

                            {

                                AddFsGroupNoteRow(headerGroup, line.GroupNoteText, li, line.GroupNoteRowColor, line.GroupNoteCenterText, line.GroupNoteTextColor);

                                lastHeaderGroup = headerGroup;

                            }

                        }
                        else if (FsShowGapsEnabled && sawTripRow)
                        {
                            AddFsTemplateGapRow(li, line.GroupNoteText);
                        }

                        continue;

                    }

                    if (line.Trip == null) continue;

                    var g = FindFsGroupForTrip(groups, line.Trip);

                    if (g == null) continue;

                    if (FsShowGroupColorsEnabled && !ReferenceEquals(g, lastHeaderGroup))

                    {

                        // Show the group-color header for every group, including the first.
                        // (Leading blank gap rows are still skipped above.)
                        AddFsGroupNoteRow(g, null, li, null);

                    }

                    lastHeaderGroup = g;

                    _fsTripsLv.Items.Add(CreateFsTripListItem(
                        g, line.Trip, li, line.ReroutedOnModivcare, line.CancelledOnWellRyde));
                    sawTripRow = true;

                }

            }

            _fsTripsLv.EndUpdate();

            if (preserveScroll)
                FsRestoreTripsListViewScroll(scrollAnchorLine, scrollAnchorItemIndex);

            ApplyFsTripsColumnWidths();

            FsSyncReroutedHighlightsFromPreviewLines();

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

                    AddFsReserveTripListItem(
                        line.Trip,
                        line.ReserveBandColor,
                        g,
                        li,
                        line.ReroutedOnModivcare,
                        line.CancelledOnWellRyde);

                }

            }

        }



        private void AddFsReservesSectionHeader(string title, Color? sectionColor, int previewLineIndex)

        {

            Color c = sectionColor ?? ScheduleBuilderReserveBuckets.SectionColorForTitle(title);

            var lvi = new ListViewItem("");

            lvi.UseItemStyleForSubItems = false;

            for (int i = 0; i < 12; i++)

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
            bool reroutedOnModivcare = false,
            bool cancelledOnWellRyde = false)

        {

            if (trip == null) return;

            Color band = bandColor ?? ScheduleBuilderReserveBuckets.ReserversBand;
            bool inReroutesSection = ScheduleBuilderReserveBuckets.IsRerouteBand(band);

            var lvi = new ListViewItem("—");

            lvi.UseItemStyleForSubItems = false;

            lvi.SubItems[0].BackColor = band;

            lvi.SubItems.Add("");

            lvi.SubItems.Add(trip.TripNumber ?? "");

            lvi.SubItems.Add(SupeyTripTimes.FormatDateForSchedule(trip.Date));

            lvi.SubItems.Add(trip.ClientFullName ?? "");

            lvi.SubItems.Add(SupeyTripTimes.FormatForSchedule(trip.PUTime));

            lvi.SubItems.Add(trip.PUStreet ?? "");

            lvi.SubItems.Add(trip.PUCity ?? "");

            lvi.SubItems.Add(SupeyTripTimes.FormatForSchedule(trip.DOTime));

            lvi.SubItems.Add(trip.DOStreet ?? "");

            lvi.SubItems.Add(trip.DOCITY ?? "");

            lvi.SubItems.Add(trip.Miles ?? "");

            lvi.SubItems.Add(trip.Comments ?? "");

            lvi.Tag = new FsPreviewTripTag(group, trip)
            {
                PreviewLineIndex = previewLineIndex,
                ReroutedOnModivcare = reroutedOnModivcare,
                CancelledOnWellRyde = cancelledOnWellRyde,
                InReservesReroutesSection = inReroutesSection,
                ReserveBandColor = band,
            };

            if (reroutedOnModivcare)
                ApplyFsReroutedTripRowStyle(lvi);
            else if (cancelledOnWellRyde)
                ApplyFsCancelledTripRowStyle(lvi);

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

        /// <summary>
        /// When a header's stored group # is stale after renumbering, bind it to the next trip's group.
        /// </summary>
        private static SupeyTripCluster FindFsGroupForLineAfter(
            List<SupeyTripCluster> groups,
            IList<ScheduleBuilderPreviewLine> lines,
            int headerLineIndex)
        {
            if (groups == null || lines == null)
                return null;

            for (int i = headerLineIndex + 1; i < lines.Count; i++)
            {
                var line = lines[i];
                if (line?.Kind == ScheduleBuilderPreviewLine.LineKind.Gap
                    || line?.Kind == ScheduleBuilderPreviewLine.LineKind.GroupHeader
                    || line?.Kind == ScheduleBuilderPreviewLine.LineKind.SectionHeader)
                {
                    break;
                }

                if (line?.Kind == ScheduleBuilderPreviewLine.LineKind.Trip && line.Trip != null)
                    return FindFsGroupForTrip(groups, line.Trip);
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



        private void AddFsTemplateGapRow(int previewLineIndex, string noteText = null, bool trailingPad = false)

        {

            string note = (noteText ?? "").Trim();
            var cells = new[] { "", "", "", "", note, "", "", "", "", "", "", "", "" };
            var lvi = new ListViewItem(cells);

            lvi.UseItemStyleForSubItems = false;
            lvi.Tag = new FsPreviewGapTag
            {
                PreviewLineIndex = previewLineIndex,
                NoteText = note,
                TrailingPad = trailingPad,
            };

            _fsTripsLv.Items.Add(lvi);

        }

        private void AddFsPositionNoteRow(int previewLineIndex, string noteText, Color? noteRowColor, bool centerText = false, Color? textColor = null)
        {
            string note = (noteText ?? "").Trim();
            var lvi = new ListViewItem("");
            lvi.UseItemStyleForSubItems = false;

            Color? barColor = noteRowColor;
            Color bar = barColor ?? SupeyTheme.ListBody;

            for (int c = 1; c <= 12; c++)
                lvi.SubItems.Add("");

            for (int c = 0; c < lvi.SubItems.Count; c++)
            {
                lvi.SubItems[c].Text = "";
                lvi.SubItems[c].BackColor = bar;
            }

            lvi.Tag = new FsPreviewGapTag
            {
                PreviewLineIndex = previewLineIndex,
                NoteText = note,
                NoteRowColor = noteRowColor,
                NoteTextCentered = centerText,
                NoteTextColor = textColor,
            };

            _fsTripsLv.Items.Add(lvi);
        }



        private void AddFsGroupNoteRow(SupeyTripCluster g, string noteText, int previewLineIndex, Color? noteRowColor, bool centerText = false, Color? textColor = null)

        {

            if (g == null) return;

            string note = (noteText ?? "").Trim();

            var lvi = new ListViewItem("");

            lvi.UseItemStyleForSubItems = false;

            Color? barColor = ScheduleBuilderGroupNotes.ResolveNoteRowDisplayColor(
                noteRowColor, g, FsShowGroupColorsEnabled);
            Color bar = barColor ?? SupeyTheme.ListBody;

            for (int c = 1; c <= 12; c++)

                lvi.SubItems.Add("");

            for (int c = 0; c < lvi.SubItems.Count; c++)
            {
                lvi.SubItems[c].Text = "";
                lvi.SubItems[c].BackColor = bar;
            }

            lvi.Tag = new FsPreviewNoteTag(g, note)
            {
                PreviewLineIndex = previewLineIndex,
                NoteRowColor = noteRowColor,
                NoteTextCentered = centerText,
                NoteTextColor = textColor,
            };

            _fsTripsLv.Items.Add(lvi);

        }



        private ListViewItem CreateFsTripListItem(
            SupeyTripCluster g,
            MCDownloadedTrip trip,
            int previewLineIndex,
            bool reroutedOnModivcare = false,
            bool cancelledOnWellRyde = false)

        {

            string grp = g != null ? g.GroupNumber.ToString() : "";

            var lvi = new ListViewItem(grp);

            lvi.UseItemStyleForSubItems = false;

            lvi.Tag = g != null
                ? (object)new FsPreviewTripTag(g, trip)
                {
                    PreviewLineIndex = previewLineIndex,
                    ReroutedOnModivcare = reroutedOnModivcare,
                    CancelledOnWellRyde = cancelledOnWellRyde,
                }
                : trip;

            if (g != null && !reroutedOnModivcare && !cancelledOnWellRyde && FsShowGroupColorsEnabled)
                lvi.SubItems[0].BackColor = g.DisplayColor;

            lvi.SubItems.Add("");

            lvi.SubItems.Add(trip.TripNumber ?? "");

            lvi.SubItems.Add(SupeyTripTimes.FormatDateForSchedule(trip.Date));

            lvi.SubItems.Add(trip.ClientFullName ?? "");

            lvi.SubItems.Add(SupeyTripTimes.FormatForSchedule(trip.PUTime));

            lvi.SubItems.Add(trip.PUStreet ?? "");

            lvi.SubItems.Add(trip.PUCity ?? "");

            lvi.SubItems.Add(SupeyTripTimes.FormatForSchedule(trip.DOTime));

            lvi.SubItems.Add(trip.DOStreet ?? "");

            lvi.SubItems.Add(trip.DOCITY ?? "");

            lvi.SubItems.Add(trip.Miles ?? "");

            lvi.SubItems.Add(trip.Comments ?? "");

            if (reroutedOnModivcare)
                ApplyFsReroutedTripRowStyle(lvi);
            else if (cancelledOnWellRyde)
                ApplyFsCancelledTripRowStyle(lvi);

            return lvi;

        }

        private static void ApplyFsCancelledTripRowStyle(ListViewItem lvi)
        {
            if (lvi == null)
                return;

            Color c = ScheduleBuilderPreviewStyle.CancelledTripBackColor;
            lvi.BackColor = c;
            for (int i = 0; i < lvi.SubItems.Count; i++)
                lvi.SubItems[i].BackColor = c;
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

        private static void ClearFsReroutedTripRowStyle(
            ListViewItem lvi,
            ScheduleBuilderPreviewLine line,
            bool isReservesTab,
            FsPreviewTripTag tag,
            bool showGroupColors)
        {
            if (lvi == null)
                return;

            Color body = SupeyTheme.ListBody;
            lvi.BackColor = body;
            for (int i = 0; i < lvi.SubItems.Count; i++)
                lvi.SubItems[i].BackColor = body;

            if (isReservesTab && lvi.SubItems.Count > 0)
            {
                Color band = line?.ReserveBandColor ?? ScheduleBuilderReserveBuckets.RerouteBand;
                lvi.SubItems[0].BackColor = band;
                return;
            }

            if (tag?.Group != null && showGroupColors && lvi.SubItems.Count > 0)
                lvi.SubItems[0].BackColor = tag.Group.DisplayColor;
        }

    }

}


