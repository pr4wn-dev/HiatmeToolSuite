using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MaterialSkin.Controls;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Partial class file for the "Supey Schedule" tab — an end-to-end schedule builder that
    /// pulls Modivcare trips, geocodes everything, clusters into ride-share groups, scores +
    /// greedy-assigns clusters to a roster of drivers, and previews the result on a list +
    /// map before saving as a Modivcare-format Excel workbook.
    /// </summary>
    /// <remarks>
    /// All UI is built programmatically in <see cref="InitializeSupeyTab"/> rather than via the
    /// designer to keep <c>Form1.Designer.cs</c> clean. The constructor in <c>Form1.cs</c>
    /// calls <see cref="InitializeSupeyTab"/> once, after <c>InitializeComponent</c> has placed
    /// the empty <c>tabPageSupey</c>.
    /// </remarks>
    public partial class Form1
    {
        // ---------- UI controls (all owned by tabPageSupey) ----------
        private Panel _supeyToolbar;
        private Panel _supeyStatusStrip;
        private Panel _supeyMainHost;
        private SplitContainer _supeyMainSplit;
        private bool _supeySplitDistanceInitialized;
        private SupeyCollapsiblePanel _supeyDriversCollapsible;
        private SupeyCollapsiblePanel _supeyTripsCollapsible;
        private SupeyCollapsiblePanel _supeyRightCollapsible;
        private MaterialLabel _supeyTemplateCompareLbl;

        private RJDatePicker _supeyDatePicker;
        private MaterialButton _supeyLoadBtn;
        private MaterialButton _supeyBuildBtn;
        private MaterialButton _supeySaveBtn;
        private MaterialButton _supeyCancelBtn;
        private MaterialCheckbox _supeyUseTemplatesChk;
        private MaterialLabel _supeyToolbarStatusLbl;
        private MaterialLabel _supeyOsrmStatusLbl;

        private MaterialProgressBar _supeyProgressBar;
        private MaterialLabel _supeyStatsLbl;
        private LinkLabel _supeyWarningsLink;

        private ListView _supeyDriversLv;
        private ColumnHeader _supeyDriversColCheck;
        private ColumnHeader _supeyDriversColName;
        private ColumnHeader _supeyDriversColCap;
        private ColumnHeader _supeyDriversColShift;
        private ColumnHeader _supeyDriversColRelease;
        private MaterialButton _supeyDriverAddBtn;
        private MaterialButton _supeyDriverEditBtn;
        private MaterialButton _supeyDriverRemoveBtn;
        private MaterialButton _supeyDriverSaveBtn;
        private MaterialButton _supeyDriverPullBtn;
        private MaterialLabel _supeyRosterFooter;
        private Label _supeyDriversEmptyHint;

        private ComboBox _supeyPreviewDriverCb;
        private ListView _supeyPreviewLv;
        private ColumnHeader _supeyPrevColGrp;
        private ColumnHeader _supeyPrevColTrip;
        private ColumnHeader _supeyPrevColClient;
        private ColumnHeader _supeyPrevColPUTime;
        private ColumnHeader _supeyPrevColPUStreet;
        private ColumnHeader _supeyPrevColPUCity;
        private ColumnHeader _supeyPrevColDOTime;
        private ColumnHeader _supeyPrevColDOStreet;
        private ColumnHeader _supeyPrevColDOCity;
        private ColumnHeader _supeyPrevColMiles;
        private MaterialLabel _supeyPreviewStatsLbl;
        private Label _supeyPreviewEmptyHint;

        private SupeyMapWorkspace _supeyMap;

        // ---------- runtime state ----------
        private List<SupeyDriverProfile> _supeyRoster = new List<SupeyDriverProfile>();
        private List<MCDownloadedTrip> _supeyLoadedTrips = new List<MCDownloadedTrip>();
        private SupeyScheduleResult _supeyResult;
        private CancellationTokenSource _supeyCts;

        /// <summary>Loaded pool = raw Modivcare rows; AiSchedule = BUILD/revise JSON mapped to drivers.</summary>
        private enum SupeyTripsPanelView { Empty, LoadedPool, AiSchedule }
        private SupeyTripsPanelView _supeyTripsPanelView = SupeyTripsPanelView.Empty;
        private DateTime _supeyRosterLastSaved;

        /// <summary>
        /// Builds the Supey Schedule tab UI on top of the designer-placed empty
        /// <see cref="tabPageSupey"/>. Idempotent — bailing out if controls have already been
        /// added prevents accidental double-initialization during designer experiments.
        /// </summary>
        private void InitializeSupeyTab()
        {
            if (tabPageSupey == null) return;
            if (tabPageSupey.Controls.Count > 0) return;

            tabPageSupey.BackColor = Color.FromArgb(33, 33, 33);
            tabPageSupey.UseVisualStyleBackColor = false;

            BuildSupeyToolbar();
            BuildSupeyStatusStrip();
            BuildSupeyWorkspace();

            tabPageSupey.Controls.Add(_supeyMainHost);
            tabPageSupey.Controls.Add(_supeyStatusStrip);
            tabPageSupey.Controls.Add(_supeyToolbar);

            LoadSupeyRosterFromDisk();
            UpdateSupeyButtonStates();
            SetSupeyStatus("Ready. Pick a service date and click Load Trips.");
            _ = RefreshSupeyOsrmStatusAsync();
        }

        private void BuildSupeyToolbar()
        {
            _supeyToolbar = new Panel
            {
                Dock = DockStyle.Top,
                Height = 64,
                BackColor = Color.FromArgb(45, 45, 45),
                Padding = new Padding(8, 6, 8, 6),
            };

            var flow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoSize = true,
                BackColor = Color.Transparent,
                Padding = new Padding(0),
            };

            var dateLabel = new Label
            {
                Text = "Service date:",
                AutoSize = true,
                ForeColor = Color.Gainsboro,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 9f),
                Margin = new Padding(4, 10, 4, 0),
            };
            _supeyDatePicker = new RJDatePicker
            {
                Size = new Size(200, 30),
                Margin = new Padding(0, 6, 12, 0),
                BorderColor = Color.Black,
                BorderSize = 1,
                Font = new Font("Microsoft Sans Serif", 9.75f),
                SkinColor = Color.FromArgb(64, 64, 64),
                TextColor = Color.White,
            };

            _supeyLoadBtn = MakeFlatButton("LOAD TRIPS", 0, 0, 110);
            _supeyLoadBtn.Margin = new Padding(0, 4, 8, 0);
            _supeyLoadBtn.Click += async (s, e) => await OnSupeyLoadClickedAsync();

            _supeyUseTemplatesChk = new MaterialCheckbox
            {
                Text = "Use templates as hints",
                AutoSize = true,
                Checked = true,
                Margin = new Padding(0, 8, 8, 0),
            };

            _supeyBuildBtn = MakeFlatButton("BUILD", 0, 0, 100);
            _supeyBuildBtn.Margin = new Padding(0, 4, 8, 0);
            _supeyBuildBtn.Click += async (s, e) => await OnSupeyBuildClickedAsync();

            _supeySaveBtn = MakeFlatButton("SAVE WORKBOOK", 0, 0, 150);
            _supeySaveBtn.Margin = new Padding(0, 4, 8, 0);
            _supeySaveBtn.Click += async (s, e) => await OnSupeySaveClickedAsync();

            _supeyCancelBtn = MakeFlatButton("CANCEL", 0, 0, 100);
            _supeyCancelBtn.Margin = new Padding(0, 4, 8, 0);
            _supeyCancelBtn.Type = MaterialButton.MaterialButtonType.Outlined;
            _supeyCancelBtn.UseAccentColor = false;
            _supeyCancelBtn.NoAccentTextColor = Color.Gainsboro;
            _supeyCancelBtn.Click += (s, e) => OnSupeyCancelClicked();

            _supeyToolbarStatusLbl = new MaterialLabel
            {
                Text = "Ready.",
                AutoSize = false,
                Width = 400,
                Height = 22,
                ForeColor = Color.Gainsboro,
                Margin = new Padding(8, 10, 0, 0),
            };

            _supeyOsrmStatusLbl = new MaterialLabel
            {
                Text = "OSRM: checking...",
                AutoSize = true,
                ForeColor = Color.Gray,
                Margin = new Padding(8, 10, 0, 0),
            };
            _supeyOsrmStatusLbl.MouseClick += async (s, e) => await RefreshSupeyOsrmStatusAsync();
            var osrmTip = "Local OSRM at " + OsrmSettings.LocalBaseUrl + "\r\n" +
                "Start: tools\\osrm\\scripts\\start-osrm.ps1\r\n" +
                "See: tools\\osrm\\README.md\r\nClick to refresh.";
            var osrmTipProvider = new ToolTip { AutoPopDelay = 12000, InitialDelay = 400 };
            osrmTipProvider.SetToolTip(_supeyOsrmStatusLbl, osrmTip);

            flow.Controls.Add(dateLabel);
            flow.Controls.Add(_supeyDatePicker);
            flow.Controls.Add(_supeyLoadBtn);
            flow.Controls.Add(_supeyUseTemplatesChk);
            flow.Controls.Add(_supeyBuildBtn);
            flow.Controls.Add(_supeySaveBtn);
            flow.Controls.Add(_supeyCancelBtn);
            flow.Controls.Add(_supeyOsrmStatusLbl);
            flow.Controls.Add(_supeyToolbarStatusLbl);
            _supeyToolbar.Controls.Add(flow);
        }

        private void BuildSupeyStatusStrip()
        {
            _supeyStatusStrip = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 38,
                BackColor = Color.FromArgb(35, 35, 35),
                Padding = new Padding(8, 4, 8, 4),
            };

            _supeyProgressBar = new MaterialProgressBar
            {
                Location = new Point(12, 12),
                Width = 280,
                Height = 12,
                Style = ProgressBarStyle.Marquee,
                Visible = false,
            };

            _supeyStatsLbl = new MaterialLabel
            {
                Text = "Fleet: -",
                Location = new Point(310, 8),
                Width = 700,
                Height = 22,
                ForeColor = Color.Gainsboro,
                AutoSize = false,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            };

            _supeyWarningsLink = new LinkLabel
            {
                Text = "0 warnings",
                // The previous Anchor=Top|Right setup was placed at x=1300 in design coordinates,
                // which left the link off-screen for any form narrower than ~1500. Switch to manual
                // right-edge positioning so the link sits at a predictable offset regardless of
                // the form's actual width.
                Location = new Point(0, 10),
                Width = 220,
                Height = 18,
                ForeColor = Color.Gold,
                LinkColor = Color.Gold,
                ActiveLinkColor = Color.Goldenrod,
                LinkBehavior = LinkBehavior.HoverUnderline,
                TextAlign = ContentAlignment.MiddleRight,
                Font = new Font("Segoe UI", 9f),
            };
            _supeyWarningsLink.Click += (s, e) => OnSupeyWarningsLinkClicked();

            _supeyStatusStrip.Controls.Add(_supeyProgressBar);
            _supeyStatusStrip.Controls.Add(_supeyStatsLbl);
            _supeyStatusStrip.Controls.Add(_supeyWarningsLink);

            // Manually pin the warnings link to the strip's right edge (12px gutter) on every
            // resize. Anchoring proved unreliable here because the design-time location placed the
            // link off-screen for narrower forms.
            void Reposition()
            {
                if (_supeyWarningsLink == null || _supeyStatusStrip == null) return;
                _supeyWarningsLink.Left = Math.Max(0, _supeyStatusStrip.ClientSize.Width - _supeyWarningsLink.Width - 12);
            }
            _supeyStatusStrip.Resize += (s, e) => Reposition();
            _supeyStatusStrip.HandleCreated += (s, e) => Reposition();
            Reposition();
        }

        /// <summary>
        /// Click handler for the bottom-right warnings link. Selects the dropdown's "Warnings"
        /// entry so the inline list renders — a much better UX than the giant text-modal we used
        /// to throw, especially for builds with hundreds of warnings.
        /// </summary>
        private void OnSupeyWarningsLinkClicked()
        {
            if (_supeyResult == null || _supeyResult.WarningCount == 0) return;
            if (_supeyPreviewDriverCb == null) return;
            for (int i = 0; i < _supeyPreviewDriverCb.Items.Count; i++)
            {
                if (_supeyPreviewDriverCb.Items[i] is SupeyPreviewItem itm &&
                    itm.Kind == SupeyPreviewItem.ItemKind.Warnings)
                {
                    _supeyPreviewDriverCb.SelectedIndex = i;
                    return;
                }
            }
            // Fallback: still hand them the modal if for any reason the dropdown entry didn't get
            // added (e.g. RebuildPreviewDropdown skipped it because WarningCount was 0).
            ShowSupeyWarningsModal();
        }

        private void BuildSupeyWorkspace()
        {
            _supeyMainHost = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(33, 33, 33),
                Padding = new Padding(0),
            };

            _supeyMainSplit = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                BackColor = Color.FromArgb(50, 50, 50),
                Panel1MinSize = 120,
                Panel2MinSize = 72,
                SplitterWidth = 8,
                FixedPanel = FixedPanel.None,
            };
            _supeyMainSplit.Panel1.BackColor = Color.FromArgb(33, 33, 33);
            _supeyMainSplit.Panel2.BackColor = Color.FromArgb(35, 35, 35);
            _supeyMainSplit.SizeChanged += (s, e) => EnsureSupeySplitDistance();
            _supeyMainSplit.SplitterMoved += (s, e) => { _supeySplitDistanceInitialized = true; };

            var workPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(33, 33, 33),
            };

            _supeyMap = new SupeyMapWorkspace { Dock = DockStyle.Fill };
            workPanel.Controls.Add(_supeyMap);

            _supeyDriversCollapsible = new SupeyCollapsiblePanel
            {
                Title = "Drivers",
                Dock = DockStyle.Left,
                ExpandedWidth = 300,
            };
            BuildSupeyDriversPanel(_supeyDriversCollapsible.ContentPanel);

            BuildSupeyAiPanel();

            _supeyRightCollapsible = new SupeyCollapsiblePanel
            {
                Title = "Info",
                Dock = DockStyle.Right,
                ExpandedWidth = 280,
            };
            BuildSupeyRightPanel(_supeyRightCollapsible.ContentPanel);

            workPanel.Controls.Add(_supeyAiCollapsible);
            workPanel.Controls.Add(_supeyRightCollapsible);
            workPanel.Controls.Add(_supeyDriversCollapsible);
            _supeyMainSplit.Panel1.Controls.Add(workPanel);

            _supeyTripsCollapsible = new SupeyCollapsiblePanel
            {
                Title = "Trips",
                Dock = DockStyle.Fill,
            };
            BuildSupeyTripsPanel(_supeyTripsCollapsible.ContentPanel);
            _supeyMainSplit.Panel2.Controls.Add(_supeyTripsCollapsible);

            _supeyMainHost.Controls.Add(_supeyMainSplit);
        }

        /// <summary>Default trip list to ~38% of workspace height; user drags the split bar after that.</summary>
        private void EnsureSupeySplitDistance()
        {
            if (_supeyMainSplit == null || _supeySplitDistanceInitialized) return;
            int total = _supeyMainSplit.Height;
            if (total < 200) return;

            int tripsH = Math.Max(_supeyMainSplit.Panel2MinSize,
                Math.Min(480, (int)(total * 0.38)));
            int mapH = total - tripsH - _supeyMainSplit.SplitterWidth;
            if (mapH < _supeyMainSplit.Panel1MinSize)
                mapH = _supeyMainSplit.Panel1MinSize;

            _supeyMainSplit.SplitterDistance = mapH;
            _supeySplitDistanceInitialized = true;
        }

        private void BuildSupeyRightPanel(Panel host)
        {
            host.Padding = new Padding(4);
            var warnHeader = new Label
            {
                Dock = DockStyle.Top,
                Height = 24,
                Text = "Warnings",
                ForeColor = Color.Gainsboro,
                Font = new Font("Segoe UI Semibold", 9f),
            };
            var warnLink = new LinkLabel
            {
                Dock = DockStyle.Top,
                Height = 22,
                Text = "View warnings…",
                LinkColor = Color.LightSkyBlue,
                ActiveLinkColor = Color.White,
                VisitedLinkColor = Color.LightSkyBlue,
            };
            warnLink.Click += (s, e) => ShowSupeyWarningsModal();

            _supeyTemplateCompareLbl = new MaterialLabel
            {
                Dock = DockStyle.Fill,
                Text = "Build a schedule to see template comparison.",
                ForeColor = Color.Silver,
                AutoSize = false,
                Padding = new Padding(4, 12, 4, 4),
            };

            var compareHeader = new Label
            {
                Dock = DockStyle.Top,
                Height = 24,
                Text = "Template compare",
                ForeColor = Color.Gainsboro,
                Font = new Font("Segoe UI Semibold", 9f),
            };

            host.Controls.Add(_supeyTemplateCompareLbl);
            host.Controls.Add(compareHeader);
            host.Controls.Add(warnLink);
            host.Controls.Add(warnHeader);
            _supeyTemplateCompareLbl.BringToFront();
        }

        private void BuildSupeyDriversPanel(Panel host)
        {
            var header = new Label
            {
                Dock = DockStyle.Top,
                Height = 28,
                Text = "Drivers",
                ForeColor = Color.Gainsboro,
                BackColor = Color.FromArgb(28, 28, 28),
                Padding = new Padding(10, 0, 0, 0),
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI Semibold", 10f),
            };

            // Three-row button area: PULL (primary) on top, the manual ADD / EDIT / REMOVE / SAVE
            // controls on row 2, and the roster footer on row 3.
            var btnRow = new Panel { Dock = DockStyle.Bottom, Height = 138, BackColor = Color.FromArgb(35, 35, 35), Padding = new Padding(8) };

            _supeyDriverPullBtn = new DarkOnAccentMaterialButton
            {
                Text = "PULL FROM WELLRYDE",
                Location = new Point(8, 8),
                AutoSize = false,
                Size = new Size(220, 36),
                Type = MaterialButton.MaterialButtonType.Contained,
                Density = MaterialButton.MaterialButtonDensity.Default,
                UseAccentColor = true,
                HighEmphasis = true,
            };
            _supeyDriverPullBtn.Click += async (s, e) => await OnSupeyPullFromWellRydeAsync();

            _supeyDriverAddBtn = MakeFlatButton("ADD", 8, 52, 76);
            _supeyDriverAddBtn.Click += (s, e) => OnSupeyDriverAdd();
            _supeyDriverEditBtn = MakeFlatButton("EDIT", 92, 52, 76);
            _supeyDriverEditBtn.Click += (s, e) => OnSupeyDriverEdit();
            _supeyDriverRemoveBtn = MakeFlatButton("REMOVE", 176, 52, 96);
            _supeyDriverRemoveBtn.Click += (s, e) => OnSupeyDriverRemove();
            _supeyDriverRemoveBtn.Type = MaterialButton.MaterialButtonType.Outlined;
            _supeyDriverRemoveBtn.UseAccentColor = false;
            _supeyDriverRemoveBtn.NoAccentTextColor = Color.Gainsboro;
            _supeyDriverSaveBtn = MakeFlatButton("SAVE ROSTER", 280, 52, 100);
            _supeyDriverSaveBtn.Click += (s, e) => SaveSupeyRosterToDisk(showOk: true);

            _supeyRosterFooter = new MaterialLabel
            {
                Text = "0 drivers",
                Location = new Point(8, 100),
                AutoSize = false,
                Width = 380,
                Height = 22,
                ForeColor = Color.Silver,
            };
            btnRow.Controls.Add(_supeyDriverPullBtn);
            btnRow.Controls.Add(_supeyDriverAddBtn);
            btnRow.Controls.Add(_supeyDriverEditBtn);
            btnRow.Controls.Add(_supeyDriverRemoveBtn);
            btnRow.Controls.Add(_supeyDriverSaveBtn);
            btnRow.Controls.Add(_supeyRosterFooter);

            _supeyDriversLv = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                BackColor = SupeyLvBg,
                ForeColor = Color.Gainsboro,
                FullRowSelect = true,
                // GridLines = true is purely declarative under owner-draw — the framework no
                // longer paints them — but we set it for accessibility tools and for parity with
                // the legacy listviews that report "GridLines: True" in the designer.
                GridLines = true,
                HideSelection = false,
                MultiSelect = false,
                CheckBoxes = true,
                Font = new Font("Archivo Medium", 10f),
                OwnerDraw = true,
                UseCompatibleStateImageBehavior = false,
            };
            // Match the trips preview's owner-draw dark theme (header 51/51/51, body 70/70/70,
            // RoyalBlue selection). Column 0 carries the standard checkbox glyph rendered via
            // DrawDefault — see SupeyDriversLv_DrawSubItem for the per-column dispatch.
            _supeyDriversLv.DrawColumnHeader += SupeyDriversLv_DrawColumnHeader;
            _supeyDriversLv.DrawItem += SupeyDriversLv_DrawItem;
            _supeyDriversLv.DrawSubItem += SupeyDriversLv_DrawSubItem;
            // CheckBoxes=true paints the box inside the first column; keep it narrow but with a
            // proper header so users see the on/off semantic.
            _supeyDriversColCheck = new ColumnHeader { Text = "Use", Width = 44 };
            _supeyDriversColName = new ColumnHeader { Text = "Driver", Width = 130 };
            _supeyDriversColCap = new ColumnHeader { Text = "Cap", Width = 44 };
            _supeyDriversColShift = new ColumnHeader { Text = "Shift", Width = 86 };
            _supeyDriversColRelease = new ColumnHeader { Text = "Release", Width = 76 };
            _supeyDriversLv.Columns.AddRange(new[]
            {
                _supeyDriversColCheck, _supeyDriversColName,
                _supeyDriversColCap, _supeyDriversColShift, _supeyDriversColRelease
            });
            _supeyDriversLv.DoubleClick += (s, e) => OnSupeyDriverEdit();
            // ItemChecked fires per-item during bulk Add() — the rebuild path uses
            // _supeySuppressItemChecked to mute it so we don't recompute button states N times
            // while items are still being constructed.
            _supeyDriversLv.ItemChecked += (s, e) =>
            {
                if (_supeySuppressItemChecked) return;
                UpdateSupeyButtonStates();
            };

            // Empty-state overlay — visible only while the roster is empty so the user knows
            // exactly where to start.
            _supeyDriversEmptyHint = new Label
            {
                Dock = DockStyle.Fill,
                Text = "No drivers yet.\nClick ADD to enter your first driver.",
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.Silver,
                BackColor = Color.FromArgb(70, 70, 70),
                Font = new Font("Segoe UI", 10f),
                Visible = true,
            };

            host.Controls.Add(_supeyDriversEmptyHint);
            host.Controls.Add(_supeyDriversLv);
            host.Controls.Add(btnRow);
            host.Controls.Add(header);
            _supeyDriversEmptyHint.BringToFront();

            // Apply the standard custom-listview behaviors: click-to-sort, content-driven min
            // column widths, dark header empty-area paint. Same treatment as the trips preview
            // ListView so the roster has consistent UX across the tab.
            try
            {
                ListViewSorter.Attach(_supeyDriversLv);
                ListViewMinWidthEnforcer.Attach(_supeyDriversLv);
                ListViewHeaderEmptyAreaPainter.Attach(_supeyDriversLv);
            }
            catch { }
        }

        private void BuildSupeyTripsPanel(Panel host)
        {
            var topPanel = new Panel { Dock = DockStyle.Top, Height = 40, BackColor = Color.FromArgb(45, 45, 45), Padding = new Padding(8, 6, 8, 6) };
            var lbl = new Label
            {
                Text = "Driver:",
                Location = new Point(10, 10),
                AutoSize = true,
                ForeColor = Color.Gainsboro,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 9f),
            };
            _supeyPreviewDriverCb = new ComboBox
            {
                Location = new Point(60, 8),
                Width = 360,
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.FromArgb(60, 60, 60),
                ForeColor = Color.Gainsboro,
                FlatStyle = FlatStyle.Flat,
            };
            _supeyPreviewDriverCb.SelectedIndexChanged += (s, e) => OnSupeyPreviewDriverChanged();
            topPanel.Controls.Add(lbl);
            topPanel.Controls.Add(_supeyPreviewDriverCb);

            _supeyPreviewStatsLbl = new MaterialLabel
            {
                Dock = DockStyle.Bottom,
                Height = 28,
                Text = "",
                ForeColor = Color.Silver,
                Padding = new Padding(10, 4, 10, 4),
                BackColor = Color.FromArgb(35, 35, 35),
            };

            _supeyPreviewLv = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                BackColor = Color.FromArgb(70, 70, 70),
                ForeColor = Color.Gainsboro,
                FullRowSelect = true,
                GridLines = true,
                HideSelection = false,
                MultiSelect = true,
                Font = new Font("Archivo Medium", 9.5f),
                OwnerDraw = true,
                HeaderStyle = ColumnHeaderStyle.Clickable,
            };
            _supeyPrevColGrp = new ColumnHeader { Text = "Grp", Width = 44 };
            _supeyPrevColTrip = new ColumnHeader { Text = "Trip #", Width = 88 };
            _supeyPrevColClient = new ColumnHeader { Text = "Client", Width = 140 };
            _supeyPrevColPUTime = new ColumnHeader { Text = "PU", Width = 58 };
            _supeyPrevColPUStreet = new ColumnHeader { Text = "Route", Width = -2 };
            _supeyPrevColPUCity = new ColumnHeader { Text = "PU→DO detail", Width = 0 };
            _supeyPrevColDOTime = new ColumnHeader { Text = "DO", Width = 58 };
            _supeyPrevColDOStreet = new ColumnHeader { Text = "PU St", Width = 0 };
            _supeyPrevColDOCity = new ColumnHeader { Text = "DO St", Width = 0 };
            _supeyPrevColMiles = new ColumnHeader { Text = "Mi", Width = 44 };
            _supeyPreviewLv.Columns.AddRange(new[]
            {
                _supeyPrevColGrp, _supeyPrevColTrip, _supeyPrevColClient,
                _supeyPrevColPUTime, _supeyPrevColPUStreet,
                _supeyPrevColDOTime, _supeyPrevColMiles,
            });
            _supeyPreviewLv.DrawColumnHeader += SupeyPreviewLv_DrawColumnHeader;
            _supeyPreviewLv.DrawItem += SupeyPreviewLv_DrawItem;
            _supeyPreviewLv.DrawSubItem += SupeyPreviewLv_DrawSubItem;

            // Empty-state hint over the trips area until a build runs. We toggle Visible from
            // RebuildPreviewDropdown / OnSupeyPreviewDriverChanged based on what's loaded.
            _supeyPreviewEmptyHint = new Label
            {
                Dock = DockStyle.Fill,
                Text = "No trips loaded yet.\n\n1. LOAD TRIPS (Modivcare)\n" +
                       "2. Add/check drivers\n" +
                       "3. BUILD — the AI schedule appears here per driver.\n\n" +
                       "Before BUILD you can open \"Loaded pool\" in the dropdown to verify downloads.",
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.Silver,
                BackColor = Color.FromArgb(70, 70, 70),
                Font = new Font("Segoe UI", 10.5f),
                Visible = true,
            };

            host.Controls.Add(_supeyPreviewEmptyHint);
            host.Controls.Add(_supeyPreviewLv);
            host.Controls.Add(_supeyPreviewStatsLbl);
            host.Controls.Add(topPanel);
            _supeyPreviewEmptyHint.BringToFront();

            try
            {
                ListViewSorter.Attach(_supeyPreviewLv);
                ListViewMinWidthEnforcer.Attach(_supeyPreviewLv);
                ListViewHeaderEmptyAreaPainter.Attach(_supeyPreviewLv);
            }
            catch { }

            BuildSupeyWarningsContextMenu();
            BuildSupeyTripsContextMenu();
        }

        // ----------------------------------------------------------------------
        // Warnings list right-click menu — Copy selected / Copy all / Clear all.
        // ----------------------------------------------------------------------

        private ContextMenuStrip _supeyWarningsCtxMenu;
        private ToolStripMenuItem _supeyWarningsCtxCopySelected;
        private ToolStripMenuItem _supeyWarningsCtxCopyAll;
        private ToolStripMenuItem _supeyWarningsCtxClear;

        /// <summary>
        /// Builds the dark-themed context menu shown when the user right-clicks the preview
        /// ListView while it's displaying warnings. The menu is gated by a MouseUp handler that
        /// checks the current dropdown selection — right-clicking inside a Driver or Reserves
        /// view never shows it, so the trips view stays uncluttered.
        /// </summary>
        private void BuildSupeyWarningsContextMenu()
        {
            _supeyWarningsCtxMenu = new ContextMenuStrip
            {
                Renderer = new DarkContextMenuRenderer(),
                BackColor = DarkContextMenuRenderer.Background,
                ForeColor = DarkContextMenuRenderer.ForeColor,
                ShowImageMargin = true,
            };

            _supeyWarningsCtxCopySelected = new ToolStripMenuItem("Copy selected warning")
            {
                BackColor = DarkContextMenuRenderer.Background,
                ForeColor = DarkContextMenuRenderer.ForeColor,
                ShortcutKeys = Keys.Control | Keys.C,
                ShowShortcutKeys = true,
                Image = MenuIconFactory.GetCopyIcon(),
            };
            _supeyWarningsCtxCopySelected.Click += (s, e) => CopyWarningsToClipboard(selectedOnly: true);

            _supeyWarningsCtxCopyAll = new ToolStripMenuItem("Copy all warnings")
            {
                BackColor = DarkContextMenuRenderer.Background,
                ForeColor = DarkContextMenuRenderer.ForeColor,
                ShortcutKeys = Keys.Control | Keys.Shift | Keys.C,
                ShowShortcutKeys = true,
                Image = MenuIconFactory.GetCopyAllIcon(),
            };
            _supeyWarningsCtxCopyAll.Click += (s, e) => CopyWarningsToClipboard(selectedOnly: false);

            _supeyWarningsCtxClear = new ToolStripMenuItem("Clear all warnings")
            {
                BackColor = DarkContextMenuRenderer.Background,
                ForeColor = DarkContextMenuRenderer.ForeColor,
                Image = MenuIconFactory.GetClearIcon(),
            };
            _supeyWarningsCtxClear.Click += (s, e) => ClearAllWarnings();

            _supeyWarningsCtxMenu.Items.Add(_supeyWarningsCtxCopySelected);
            _supeyWarningsCtxMenu.Items.Add(_supeyWarningsCtxCopyAll);
            _supeyWarningsCtxMenu.Items.Add(new ToolStripSeparator());
            _supeyWarningsCtxMenu.Items.Add(_supeyWarningsCtxClear);

            // Gate visibility on dropdown selection so the menu only appears in Warnings mode.
            // Hooking MouseUp instead of ContextMenuStrip lets us inspect the click at runtime;
            // the ListView's ContextMenuStrip property would show it unconditionally.
            _supeyPreviewLv.MouseUp += SupeyPreviewLv_MouseUp_HandleWarningsContext;
        }

        private void SupeyPreviewLv_MouseUp_HandleWarningsContext(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right) return;
            if (_supeyResult == null) return;
            if (!(_supeyPreviewDriverCb?.SelectedItem is SupeyPreviewItem itm)) return;

            if (itm.Kind == SupeyPreviewItem.ItemKind.Warnings)
            {
                int selected = _supeyPreviewLv.SelectedItems.Count;
                _supeyWarningsCtxCopySelected.Enabled = selected > 0;
                _supeyWarningsCtxCopySelected.Text = selected > 1
                    ? "Copy " + selected + " selected warnings"
                    : "Copy selected warning";
                _supeyWarningsCtxCopyAll.Enabled = _supeyPreviewLv.Items.Count > 0;
                _supeyWarningsCtxClear.Enabled = _supeyResult.WarningCount > 0;
                _supeyWarningsCtxMenu.Show(_supeyPreviewLv, e.Location);
                return;
            }

            // Driver / Reserves view → show the trips context menu, with menu labels rewritten
            // to reflect what the user is actually copying.
            ShowSupeyTripsContextMenu(itm, e.Location);
        }

        /// <summary>
        /// Serializes warnings to a tab-separated table on the clipboard so they paste cleanly into
        /// Excel / a ticket / a chat. Header row included; <paramref name="selectedOnly"/> is true
        /// for the "Copy selected" menu item, false for "Copy all".
        /// </summary>
        private void CopyWarningsToClipboard(bool selectedOnly)
        {
            if (_supeyPreviewLv == null || _supeyPreviewLv.Items.Count == 0) return;

            IEnumerable<ListViewItem> rows;
            if (selectedOnly)
            {
                if (_supeyPreviewLv.SelectedItems.Count == 0) return;
                var list = new List<ListViewItem>(_supeyPreviewLv.SelectedItems.Count);
                foreach (ListViewItem r in _supeyPreviewLv.SelectedItems) list.Add(r);
                rows = list;
            }
            else
            {
                var list = new List<ListViewItem>(_supeyPreviewLv.Items.Count);
                foreach (ListViewItem r in _supeyPreviewLv.Items) list.Add(r);
                rows = list;
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Kind\tTrip #\tDriver / Scope\tDetail");
            foreach (var r in rows)
            {
                if (r == null) continue;
                var w = r.Tag as SupeyWarning;
                string kind = r.SubItems.Count > 0 ? (r.SubItems[0].Text ?? "") : "";
                string trip = r.SubItems.Count > 1 ? (r.SubItems[1].Text ?? "") : "";
                string scope = r.SubItems.Count > 2 ? (r.SubItems[2].Text ?? "") : "";
                string detail = w?.Detail ?? (r.SubItems.Count > 4 ? (r.SubItems[4].Text ?? "") : "");
                // Strip tabs/newlines from any field so the TSV survives a paste into Excel.
                sb.Append(Sanitize(kind)).Append('\t')
                  .Append(Sanitize(trip)).Append('\t')
                  .Append(Sanitize(scope)).Append('\t')
                  .Append(Sanitize(detail))
                  .AppendLine();
            }
            try
            {
                Clipboard.SetText(sb.ToString());
                int count = (rows is ICollection<ListViewItem> coll) ? coll.Count : 0;
                if (count == 0) foreach (var _ in rows) count++;
                SetSupeyStatus("Copied " + count + " warning" + (count == 1 ? "" : "s") + " to the clipboard.");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not copy to clipboard:\n\n" + ex.Message, "Supey Schedule",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private static string Sanitize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
        }

        // ----------------------------------------------------------------------
        // Trips list right-click menu — Copy this driver / Copy all drivers.
        // Used so the user can grab the schedule, paste it back into the chat,
        // and we can A/B against the historical 2026 schedules to figure out
        // where the auto-builder differs from the dispatcher's real-world calls.
        // ----------------------------------------------------------------------

        private ContextMenuStrip _supeyTripsCtxMenu;
        private ToolStripMenuItem _supeyTripsCtxCopyThis;
        private ToolStripMenuItem _supeyTripsCtxCopyAll;
        private ToolStripMenuItem _supeyTripsCtxCopyCompare;
        private SupeyTemplateCompare _supeyLastTemplateCompare;

        private void BuildSupeyTripsContextMenu()
        {
            _supeyTripsCtxMenu = new ContextMenuStrip
            {
                Renderer = new DarkContextMenuRenderer(),
                BackColor = DarkContextMenuRenderer.Background,
                ForeColor = DarkContextMenuRenderer.ForeColor,
                ShowImageMargin = true,
            };

            _supeyTripsCtxCopyThis = new ToolStripMenuItem("Copy this driver's schedule")
            {
                BackColor = DarkContextMenuRenderer.Background,
                ForeColor = DarkContextMenuRenderer.ForeColor,
                ShortcutKeys = Keys.Control | Keys.C,
                ShowShortcutKeys = true,
                Image = MenuIconFactory.GetCopyIcon(),
            };
            _supeyTripsCtxCopyThis.Click += (s, e) => CopyCurrentScheduleToClipboard();

            _supeyTripsCtxCopyAll = new ToolStripMenuItem("Copy schedule for all drivers")
            {
                BackColor = DarkContextMenuRenderer.Background,
                ForeColor = DarkContextMenuRenderer.ForeColor,
                ShortcutKeys = Keys.Control | Keys.Shift | Keys.C,
                ShowShortcutKeys = true,
                Image = MenuIconFactory.GetCopyAllIcon(),
            };
            _supeyTripsCtxCopyAll.Click += (s, e) => CopyAllSchedulesToClipboard();

            _supeyTripsCtxCopyCompare = new ToolStripMenuItem("Copy template compare (TSV)")
            {
                BackColor = DarkContextMenuRenderer.Background,
                ForeColor = DarkContextMenuRenderer.ForeColor,
                Image = MenuIconFactory.GetCopyIcon(),
            };
            _supeyTripsCtxCopyCompare.Click += (s, e) =>
            {
                if (_supeyLastTemplateCompare == null) return;
                try
                {
                    Clipboard.SetText(_supeyLastTemplateCompare.ToTabSeparatedSummary());
                    SetSupeyStatus("Template compare copied to clipboard.");
                }
                catch (Exception ex)
                {
                    SetSupeyStatus("Could not copy: " + ex.Message);
                }
            };

            _supeyTripsCtxMenu.Items.Add(_supeyTripsCtxCopyThis);
            _supeyTripsCtxMenu.Items.Add(_supeyTripsCtxCopyAll);
            _supeyTripsCtxMenu.Items.Add(new ToolStripSeparator());
            _supeyTripsCtxMenu.Items.Add(_supeyTripsCtxCopyCompare);
        }

        private void ShowSupeyTripsContextMenu(SupeyPreviewItem itm, System.Drawing.Point location)
        {
            // Tailor the "Copy this..." label to whatever the user is currently looking at.
            // Reserves view has no driver name, so we frame it as "Copy reserves list" — same
            // gesture, different scope. Disable when there's nothing to copy so the menu
            // can't paste an empty TSV onto the clipboard.
            if (itm.Kind == SupeyPreviewItem.ItemKind.Reserves)
            {
                _supeyTripsCtxCopyThis.Text = "Copy reserves list";
                _supeyTripsCtxCopyThis.Enabled = _supeyResult.Reserves.Count > 0;
            }
            else
            {
                string driverName = itm.Plan?.Driver?.Name;
                _supeyTripsCtxCopyThis.Text = string.IsNullOrEmpty(driverName)
                    ? "Copy this driver's schedule"
                    : "Copy " + driverName + "'s schedule";
                _supeyTripsCtxCopyThis.Enabled = itm.Plan != null && itm.Plan.Groups.Count > 0;
            }
            _supeyTripsCtxCopyAll.Enabled = _supeyResult.DriverPlans.Count > 0
                || _supeyResult.Reserves.Count > 0;
            _supeyTripsCtxCopyCompare.Enabled = _supeyLastTemplateCompare != null && _supeyLastTemplateCompare.HadTemplates;

            _supeyTripsCtxMenu.Show(_supeyPreviewLv, location);
        }

        /// <summary>
        /// Copies just the currently-selected driver's schedule (or the Reserves list, if that's
        /// what's on screen) to the clipboard as TSV. Header row + "=== driver name ===" banner
        /// so the user can paste several copies into one chat message and the boundaries stay
        /// readable. Falls through silently when nothing is selected.
        /// </summary>
        private void CopyCurrentScheduleToClipboard()
        {
            if (_supeyResult == null) return;
            if (!(_supeyPreviewDriverCb?.SelectedItem is SupeyPreviewItem itm)) return;

            var sb = new System.Text.StringBuilder();
            sb.Append("Service date: ").AppendLine(_supeyResult.ServiceDate.ToString("yyyy-MM-dd"));
            sb.AppendLine();

            string descriptor;
            if (itm.Kind == SupeyPreviewItem.ItemKind.Reserves)
            {
                AppendReservesToClipboard(sb);
                descriptor = "reserves list";
            }
            else if (itm.Plan != null)
            {
                AppendDriverScheduleToClipboard(sb, itm.Plan);
                descriptor = "schedule for " + (itm.Plan.Driver?.Name ?? "driver");
            }
            else
            {
                return;
            }

            try
            {
                Clipboard.SetText(sb.ToString());
                SetSupeyStatus("Copied " + descriptor + " to the clipboard.");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not copy to clipboard:\n\n" + ex.Message, "Supey Schedule",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        /// <summary>
        /// Copies every driver's schedule (and Reserves) into one big TSV blob — a section per
        /// driver, separated by "=== name ===" banners. This is the form the user pastes back
        /// into chat so we can compare the auto-built day against the historical 2026 schedules
        /// without making them flip through the dropdown.
        /// </summary>
        private void CopyAllSchedulesToClipboard()
        {
            if (_supeyResult == null) return;

            var sb = new System.Text.StringBuilder();
            sb.Append("Service date: ").AppendLine(_supeyResult.ServiceDate.ToString("yyyy-MM-dd"));
            sb.Append("Drivers: ").Append(_supeyResult.DriverPlans.Count)
              .Append(", reserves: ").Append(_supeyResult.Reserves.Count).AppendLine();
            if (_supeyResult.FleetActiveSeconds > 0)
            {
                sb.Append("Fleet active: ")
                  .Append(SupeyTripTimes.FormatHoursMinutesFromSeconds(_supeyResult.FleetActiveSeconds))
                  .Append(" · ").Append(SupeyTripTimes.FormatMiles(_supeyResult.FleetMeters)).AppendLine();
            }
            sb.AppendLine();

            int driversWithTrips = 0;
            foreach (var plan in _supeyResult.DriverPlans)
            {
                AppendDriverScheduleToClipboard(sb, plan);
                if (plan.Groups.Count > 0) driversWithTrips++;
            }
            if (_supeyResult.Reserves.Count > 0)
                AppendReservesToClipboard(sb);

            try
            {
                Clipboard.SetText(sb.ToString());
                SetSupeyStatus("Copied " + driversWithTrips + " driver schedule(s)" +
                    (_supeyResult.Reserves.Count > 0
                        ? " + " + _supeyResult.Reserves.Count + " reserves"
                        : "") +
                    " to the clipboard.");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not copy to clipboard:\n\n" + ex.Message, "Supey Schedule",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        /// <summary>
        /// Renders one driver's schedule as a markdown-friendly TSV block: a banner with the
        /// driver name + per-day stats, then a header row, then one row per trip in group +
        /// pickup-time order. Drivers with no trips assigned still get a banner so it's obvious
        /// in the dump that they participated in the build but weren't given anything.
        /// </summary>
        private static void AppendDriverScheduleToClipboard(System.Text.StringBuilder sb, SupeyDriverPlan plan)
        {
            string driverName = plan.Driver?.Name ?? "(driver)";
            if (plan.Groups.Count == 0)
            {
                sb.Append("=== ").Append(driverName).AppendLine(" === — no trips assigned");
                sb.AppendLine();
                return;
            }

            int riders = plan.RiderCount;
            int groups = plan.Groups.Count;
            sb.Append("=== ").Append(driverName).Append(" === (")
              .Append(riders).Append(" trip").Append(riders == 1 ? "" : "s")
              .Append(", ").Append(groups).Append(" group").Append(groups == 1 ? "" : "s");
            if (plan.FirstPickup.HasValue)
                sb.Append(", first PU ").Append(SupeyTripTimes.FormatTimeOfDay(plan.FirstPickup.Value));
            if (plan.LastDropoff.HasValue)
                sb.Append(", last DO ").Append(SupeyTripTimes.FormatTimeOfDay(plan.LastDropoff.Value));
            if (plan.ReleaseTimeOfDay.HasValue)
                sb.Append(", release ").Append(SupeyTripTimes.FormatTimeOfDay(plan.ReleaseTimeOfDay.Value));
            sb.Append(", ").Append(SupeyTripTimes.FormatHoursMinutesFromSeconds(plan.TotalDriveSeconds))
              .Append(" / ").Append(SupeyTripTimes.FormatMiles(plan.TotalMeters));
            sb.AppendLine(")");

            sb.AppendLine("Grp\tTrip #\tClient\tPU Time\tPU Street\tPU City\tDO Time\tDO Street\tDO City\tMiles");
            foreach (var g in plan.Groups)
            {
                foreach (var t in g.Trips)
                {
                    sb.Append(g.GroupNumber).Append('\t')
                      .Append(Sanitize(t.TripNumber)).Append('\t')
                      .Append(Sanitize(t.ClientFullName)).Append('\t')
                      .Append(Sanitize(t.PUTime)).Append('\t')
                      .Append(Sanitize(t.PUStreet)).Append('\t')
                      .Append(Sanitize(t.PUCity)).Append('\t')
                      .Append(Sanitize(t.DOTime)).Append('\t')
                      .Append(Sanitize(t.DOStreet)).Append('\t')
                      .Append(Sanitize(t.DOCITY)).Append('\t')
                      .Append(Sanitize(t.Miles))
                      .AppendLine();
                }
            }
            sb.AppendLine();
        }

        private void AppendReservesToClipboard(System.Text.StringBuilder sb)
        {
            sb.Append("=== RESERVES === (")
              .Append(_supeyResult.Reserves.Count)
              .Append(" trip").Append(_supeyResult.Reserves.Count == 1 ? "" : "s")
              .AppendLine(" left unassigned)");
            sb.AppendLine("Trip #\tClient\tPU Time\tPU Street\tPU City\tDO Time\tDO Street\tDO City\tMiles");
            foreach (var t in _supeyResult.Reserves)
            {
                sb.Append(Sanitize(t.TripNumber)).Append('\t')
                  .Append(Sanitize(t.ClientFullName)).Append('\t')
                  .Append(Sanitize(t.PUTime)).Append('\t')
                  .Append(Sanitize(t.PUStreet)).Append('\t')
                  .Append(Sanitize(t.PUCity)).Append('\t')
                  .Append(Sanitize(t.DOTime)).Append('\t')
                  .Append(Sanitize(t.DOStreet)).Append('\t')
                  .Append(Sanitize(t.DOCITY)).Append('\t')
                  .Append(Sanitize(t.Miles))
                  .AppendLine();
            }
            sb.AppendLine();
        }

        /// <summary>
        /// Wipes every warning out of the current build result (both the build-level list and each
        /// driver plan's list) and refreshes the preview UI. The schedule itself is unchanged — only
        /// the diagnostic messages disappear, useful when the user has reviewed them and wants the
        /// view to stop nagging.
        /// </summary>
        private void ClearAllWarnings()
        {
            if (_supeyResult == null || _supeyResult.WarningCount == 0) return;

            var confirm = MessageBox.Show(this,
                "Clear all " + _supeyResult.WarningCount + " warnings from this build?\n\n" +
                "This only removes the diagnostic messages — your schedule (drivers, groups, and reserves) is not affected.",
                "Clear warnings", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
            if (confirm != DialogResult.Yes) return;

            int removed = _supeyResult.WarningCount;
            _supeyResult.BuildWarnings.Clear();
            foreach (var p in _supeyResult.DriverPlans) p.Warnings.Clear();

            // The Warnings dropdown entry no longer makes sense; rebuild the preview so it
            // disappears, then BindSupeyPreview will drop the user back onto the first available
            // view (driver 0 or reserves).
            BindSupeyPreview();
            // Refresh the bottom-right status link too.
            if (_supeyWarningsLink != null) _supeyWarningsLink.Text = "0 warnings";
            SetSupeyStatus("Cleared " + removed + " warning" + (removed == 1 ? "" : "s") + ".");
        }

        private void BuildSupeyMapPanel(Panel host)
        {
            _supeyMap = new SupeyMapWorkspace { Dock = DockStyle.Fill };
            host.Controls.Add(_supeyMap);
        }

        private static MaterialButton MakeFlatButton(string text, int x, int y, int width)
        {
            var b = new MaterialButton
            {
                Text = text,
                Location = new Point(x, y),
                AutoSize = false,
                Size = new Size(width, 32),
                Type = MaterialButton.MaterialButtonType.Contained,
                Density = MaterialButton.MaterialButtonDensity.Default,
                UseAccentColor = false,
                HighEmphasis = true,
            };
            return b;
        }

        // ---------- Roster ----------

        private void LoadSupeyRosterFromDisk()
        {
            string warning;
            _supeyRoster = SupeyDriverRosterStore.Load(out warning);
            if (!string.IsNullOrEmpty(warning))
            {
                SetSupeyStatus(warning);
            }
            RebuildSupeyDriversList();
        }

        private void SaveSupeyRosterToDisk(bool showOk)
        {
            // Persist whatever the ListView currently knows about (caller should already have
            // mutated _supeyRoster for any add/edit/remove before calling).
            var saved = SupeyDriverRosterStore.Save(_supeyRoster);
            if (saved.Ok)
            {
                _supeyRosterLastSaved = saved.SavedAtLocal;
                _supeyRosterFooter.Text = _supeyRoster.Count + " drivers · saved " + saved.SavedAtLocal.ToString("HH:mm");
                if (showOk) SetSupeyStatus("Roster saved.");
            }
            else
            {
                SetSupeyStatus(saved.ErrorMessage);
            }
        }

        private bool _supeySuppressItemChecked;

        private void RebuildSupeyDriversList()
        {
            if (_supeyDriversLv == null) return;
            _supeySuppressItemChecked = true;
            try
            {
                _supeyDriversLv.BeginUpdate();
                _supeyDriversLv.Items.Clear();
                foreach (var d in _supeyRoster)
                {
                    string shift = (d.ShiftStart ?? "—") + "-" + (d.ShiftEnd ?? "—");
                    var item = new ListViewItem(new[] { "", d.Name ?? "", d.CapacityPassengers.ToString(), shift, "" })
                    {
                        Tag = d,
                        Checked = true,
                    };
                    _supeyDriversLv.Items.Add(item);
                }
                _supeyDriversLv.EndUpdate();
            }
            finally
            {
                _supeySuppressItemChecked = false;
            }
            // Auto-fit each column to the widest header / cell after binding so long driver names
            // and shift strings aren't clipped. Same pattern used by every other listview on Form1.
            ListViewMinWidthEnforcer.ScheduleRecompute(_supeyDriversLv);
            _supeyRosterFooter.Text = _supeyRoster.Count + " drivers" +
                (_supeyRosterLastSaved == DateTime.MinValue ? "" : " · saved " + _supeyRosterLastSaved.ToString("HH:mm"));
            if (_supeyDriversEmptyHint != null)
                _supeyDriversEmptyHint.Visible = _supeyRoster.Count == 0;
            UpdateSupeyButtonStates();
        }

        private void OnSupeyDriverAdd()
        {
            using (var ed = new SupeyDriverEditorForm(null))
            {
                if (ed.ShowDialog(this) != DialogResult.OK || ed.Result == null) return;
                _supeyRoster.Add(ed.Result);
                RebuildSupeyDriversList();
                SaveSupeyRosterToDisk(showOk: false);
            }
        }

        private void OnSupeyDriverEdit()
        {
            if (_supeyDriversLv.SelectedItems.Count == 0) return;
            var item = _supeyDriversLv.SelectedItems[0];
            var existing = item.Tag as SupeyDriverProfile;
            if (existing == null) return;

            using (var ed = new SupeyDriverEditorForm(existing))
            {
                if (ed.ShowDialog(this) != DialogResult.OK || ed.Result == null) return;
                int idx = _supeyRoster.IndexOf(existing);
                if (idx >= 0) _supeyRoster[idx] = ed.Result;
                RebuildSupeyDriversList();
                SaveSupeyRosterToDisk(showOk: false);
            }
        }

        private void OnSupeyDriverRemove()
        {
            if (_supeyDriversLv.SelectedItems.Count == 0) return;
            var item = _supeyDriversLv.SelectedItems[0];
            var existing = item.Tag as SupeyDriverProfile;
            if (existing == null) return;

            var dr = MessageBox.Show(this,
                "Remove " + existing.Name + " from the roster?",
                "Remove driver", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
            if (dr != DialogResult.Yes) return;

            _supeyRoster.Remove(existing);
            RebuildSupeyDriversList();
            SaveSupeyRosterToDisk(showOk: false);
        }

        /// <summary>
        /// Opens the WellRyde driver-import picker, lets the user pick which active drivers to
        /// add to the roster, and merges the selected detail records into <see cref="_supeyRoster"/>
        /// (matching by <see cref="SupeyDriverProfile.WellRydeSecId"/> so re-pulls update existing
        /// rows rather than duplicating them). Falls back to "match by name" for any drivers added
        /// manually before this feature shipped — a courtesy to existing rosters.
        /// </summary>
        /// <remarks>
        /// Login is gated by <see cref="EnsureWellRydePortalSessionForBillingAsync"/>, the same
        /// flow Trip Scout / billing use, so the user gets a familiar prompt if they're not
        /// signed in. The persisted JSON is rewritten in one shot at the end so a crash midway
        /// never leaves a half-merged roster on disk.
        /// </remarks>
        private async Task OnSupeyPullFromWellRydeAsync()
        {
            // Reuse the existing WellRyde gate; it handles "session expired" → re-prompt /
            // "no creds saved" → MessageBox automatically. Returns false on user-visible failure.
            bool ok;
            try
            {
                ok = await EnsureWellRydePortalSessionForBillingAsync();
            }
            catch (Exception ex)
            {
                SetSupeyStatus("WellRyde sign-in failed: " + (ex.Message ?? "unknown error"));
                return;
            }
            if (!ok || _wellRydeSession == null)
            {
                SetSupeyStatus("WellRyde sign-in cancelled or failed.");
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

                int added = 0;
                int updated = 0;
                foreach (var detail in picks)
                {
                    if (detail == null || string.IsNullOrEmpty(detail.SecId)) continue;
                    var existing = FindRosterDriverBySecIdOrName(detail.SecId, detail.FullName);
                    if (existing != null)
                    {
                        ApplyWellRydeDetailToProfile(detail, existing);
                        updated++;
                    }
                    else
                    {
                        var profile = new SupeyDriverProfile();
                        ApplyWellRydeDetailToProfile(detail, profile);
                        _supeyRoster.Add(profile);
                        added++;
                    }
                }

                RebuildSupeyDriversList();
                SaveSupeyRosterToDisk(showOk: false);

                string msg = "Imported from WellRyde: " + added + " new" +
                    (updated > 0 ? ", " + updated + " refreshed" : "") + ".";
                SetSupeyStatus(msg);
            }
        }

        /// <summary>
        /// Match an incoming WellRyde detail back to a roster row. Prefer SEC id (stable),
        /// fall back to a case-insensitive name match so manually-entered drivers get linked up
        /// to their WellRyde record on first import.
        /// </summary>
        private SupeyDriverProfile FindRosterDriverBySecIdOrName(string secId, string fullName)
        {
            if (!string.IsNullOrEmpty(secId))
            {
                foreach (var d in _supeyRoster)
                {
                    if (d != null && string.Equals(d.WellRydeSecId, secId, StringComparison.OrdinalIgnoreCase))
                        return d;
                }
            }
            string name = (fullName ?? "").Trim();
            if (name.Length > 0)
            {
                foreach (var d in _supeyRoster)
                {
                    if (d != null && string.Equals((d.Name ?? "").Trim(), name, StringComparison.OrdinalIgnoreCase))
                        return d;
                }
            }
            return null;
        }

        /// <summary>
        /// Copies fields from a WellRyde <see cref="WellRydeUserDetail"/> into a roster profile.
        /// Address / name / vehicle label come straight from WellRyde; capacity and shift are
        /// preserved from the existing profile when re-syncing (or seeded with defaults on first
        /// import) since WellRyde doesn't expose those.
        /// </summary>
        private static void ApplyWellRydeDetailToProfile(WellRydeUserDetail detail, SupeyDriverProfile profile)
        {
            if (detail == null || profile == null) return;

            profile.WellRydeSecId = detail.SecId ?? "";
            profile.WellRydeUsername = detail.Username ?? "";
            profile.WellRydeSyncedAtUtc = DateTime.UtcNow;
            profile.Name = !string.IsNullOrWhiteSpace(detail.FullName) ? detail.FullName.Trim() : profile.Name;

            // Address: take WellRyde's value if present, otherwise leave the existing local value
            // alone — protects manual edits the user made before the import.
            string street = (detail.FullStreet ?? "").Trim();
            if (street.Length > 0) profile.HomeStreet = street;
            string city = (detail.City ?? "").Trim();
            if (city.Length > 0) profile.HomeCity = city;
            string state = (detail.State ?? "").Trim();
            if (state.Length > 0) profile.HomeState = state;
            string zip = (detail.Zip ?? "").Trim();
            if (zip.Length > 0) profile.HomeZip = zip;

            string vehicleLabel = (detail.VehicleLabel ?? "").Trim();
            if (vehicleLabel.Length > 0 && string.IsNullOrWhiteSpace(profile.VehicleLabel))
                profile.VehicleLabel = vehicleLabel;

            // Capacity and shift defaults only on first creation — the test for "is this a fresh
            // profile" is that capacity is still the constructor default 0 (would be 4 from the
            // SupeyDriverProfile default initializer if any other code touched it).
            if (profile.CapacityPassengers <= 0) profile.CapacityPassengers = 4;
            if (string.IsNullOrWhiteSpace(profile.ShiftStart)) profile.ShiftStart = "06:00";
            if (string.IsNullOrWhiteSpace(profile.ShiftEnd)) profile.ShiftEnd = "18:00";
        }

        // ---------- Load / Build / Save / Cancel ----------

        private async Task OnSupeyLoadClickedAsync()
        {
            if (_supeyCts != null) return;
            _supeyCts = new CancellationTokenSource();
            try
            {
                SetSupeyToolbarBusy(true, "Loading Modivcare trips...");
                if (!await EnsureModivcareSessionAsync())
                {
                    SetSupeyStatus("Modivcare sign-in required.");
                    return;
                }
                var date = _supeyDatePicker.Value;
                _supeyLoadedTrips = await SupeyScheduleBuilder.DownloadTripsAsync(date, mcLoginHandler);
                _supeyResult = null;
                _supeyTripsPanelView = SupeyTripsPanelView.LoadedPool;
                BindSupeyLoadedTripsList();
                SetSupeyStatus(BuildPostLoadStatus(_supeyLoadedTrips.Count, date)
                    + " Click BUILD for AI schedule in this list.");
            }
            catch (ScheduleBuilderException ex)
            {
                MessageBox.Show(this, ex.Message, "Supey Schedule", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                SetSupeyStatus("Trip load failed.");
            }
            catch (OperationCanceledException)
            {
                SetSupeyStatus("Load canceled.");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Unexpected error loading trips:\n\n" + ex.Message, "Supey Schedule",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                SetSupeyStatus("Trip load failed.");
            }
            finally
            {
                // Order matters: clear _supeyCts BEFORE refreshing the toolbar. UpdateSupeyButtonStates
                // gates BUILD on `_supeyCts == null`, so refreshing first leaves the button disabled
                // even after a successful load. (Lost ~10 minutes to this — leaving a comment.)
                try { _supeyCts?.Dispose(); } catch { }
                _supeyCts = null;
                SetSupeyToolbarBusy(false, null);
            }
        }

        private async Task OnSupeyBuildClickedAsync()
        {
            if (_supeyCts != null) return;
            if (_supeyLoadedTrips == null || _supeyLoadedTrips.Count == 0)
            {
                SetSupeyStatus("No trips loaded — click Load Trips first.");
                return;
            }
            var selected = GetCheckedSupeyDrivers();
            if (selected.Count == 0)
            {
                SetSupeyStatus("Check at least one driver in the roster.");
                return;
            }

            _supeyCts = new CancellationTokenSource();
            var token = _supeyCts.Token;

            try
            {
                SetSupeyToolbarBusy(true, "Asking AIagent for schedule...");
                AppendSupeyAiTranscriptIfPresent("AI Build · thinking", "…");
                var date = _supeyDatePicker.Value;

                if (_supeyAiSettings == null)
                    _supeyAiSettings = HiatmeAiSettings.Load();

                var ctx = HiatmeScheduleContextBuilder.Build(
                    date, _supeyRoster, _supeyLoadedTrips, null, false, selected);

                var aiResp = await HiatmeAiClient.ScheduleBuildAsync(
                    _supeyAiSettings, ctx, token).ConfigureAwait(true);

                if (aiResp?.Schedule == null)
                    throw new InvalidOperationException("AI server returned no schedule.");

                ApplySupeyAiSchedule(aiResp, "AI Build");
                SetSupeyStatus("AI build complete. " + _supeyResult.DriverPlans.Count + " driver(s), " +
                    _supeyResult.Reserves.Count + " reserve(s), " + _supeyResult.WarningCount + " warning(s).");
            }
            catch (OperationCanceledException)
            {
                SetSupeyStatus("Build canceled.");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "AI build failed:\n\n" + ex.Message, "Supey Schedule",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                SetSupeyStatus("AI build failed — fix server/Ollama and try again.");
            }
            finally
            {
                // Same ordering rule as OnSupeyLoadClickedAsync: dispose + null the CTS first
                // so the toolbar refresh sees `_supeyCts == null` and re-enables BUILD/SAVE.
                try { _supeyCts?.Dispose(); } catch { }
                _supeyCts = null;
                SetSupeyToolbarBusy(false, null);
            }
        }

        private async Task OnSupeySaveClickedAsync()
        {
            if (_supeyResult == null)
            {
                SetSupeyStatus("Nothing to save — click Build first.");
                return;
            }
            try
            {
                SetSupeyToolbarBusy(true, "Saving workbook...");
                await SupeyScheduleBuilder.SaveWorkbookAsync(_supeyResult, this);
                if (_supeyAiSettings == null)
                    _supeyAiSettings = HiatmeAiSettings.Load();
                try
                {
                    var ctx = HiatmeScheduleContextBuilder.Build(
                        _supeyDatePicker.Value,
                        _supeyRoster,
                        _supeyLoadedTrips,
                        _supeyResult,
                        true,
                        GetCheckedSupeyDrivers());
                    await HiatmeAiClient.SyncScheduleAsync(
                        _supeyAiSettings, ctx, "save").ConfigureAwait(true);
                }
                catch
                {
                    // non-fatal
                }
                if (_supeyAiSettings.RememberOnSave)
                {
                    try
                    {
                        var summary = HiatmeScheduleSummary.ForMemory(_supeyResult);
                        await HiatmeAiClient.AddMemoryAsync(_supeyAiSettings, summary).ConfigureAwait(true);
                    }
                    catch
                    {
                        // non-fatal
                    }
                }
                SetSupeyStatus("Workbook saved.");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not save workbook:\n\n" + ex.Message, "Supey Schedule",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                SetSupeyStatus("Save failed.");
            }
            finally
            {
                SetSupeyToolbarBusy(false, null);
            }
        }

        private void OnSupeyCancelClicked()
        {
            try { _supeyCts?.Cancel(); } catch { }
        }

        // ---------- Preview wiring ----------

        private void BindSupeyPreview()
        {
            _supeyPreviewDriverCb.Items.Clear();
            if (_supeyResult == null)
            {
                _supeyMap?.Clear();
                _supeyStatsLbl.Text = "Fleet: -";
                _supeyWarningsLink.Text = "0 warnings";
                if (_supeyTripsPanelView == SupeyTripsPanelView.LoadedPool
                    && (_supeyLoadedTrips?.Count ?? 0) > 0)
                {
                    BindSupeyLoadedTripsList();
                    return;
                }
                _supeyPreviewLv.Items.Clear();
                _supeyPreviewStatsLbl.Text = "";
                if (_supeyPreviewEmptyHint != null) _supeyPreviewEmptyHint.Visible = true;
                return;
            }

            _supeyTripsPanelView = SupeyTripsPanelView.AiSchedule;
            if (_supeyPreviewEmptyHint != null) _supeyPreviewEmptyHint.Visible = false;

            // Refresh per-driver release time in the roster ListView.
            foreach (ListViewItem item in _supeyDriversLv.Items)
            {
                if (item == null) continue;
                var d = item.Tag as SupeyDriverProfile;
                if (d == null) continue;
                var plan = _supeyResult.DriverPlans.FirstOrDefault(p => ReferenceEquals(p.Driver, d) ||
                    string.Equals(p.Driver?.Name, d.Name, StringComparison.OrdinalIgnoreCase));
                item.SubItems[4].Text = plan?.ReleaseTimeOfDay.HasValue == true
                    ? SupeyTripTimes.FormatTimeOfDay(plan.ReleaseTimeOfDay.Value) : "";
            }

            foreach (var plan in _supeyResult.DriverPlans)
            {
                string label = plan.Driver?.Name ?? "(driver)";
                if (plan.ReleaseTimeOfDay.HasValue)
                    label += " · done " + SupeyTripTimes.FormatTimeOfDay(plan.ReleaseTimeOfDay.Value);
                else if (plan.Groups.Count == 0)
                    label += " · no trips";
                _supeyPreviewDriverCb.Items.Add(new SupeyPreviewItem(plan, label));
            }
            if (_supeyResult.Reserves.Count > 0)
                _supeyPreviewDriverCb.Items.Add(new SupeyPreviewItem(null, "Reserves · " + _supeyResult.Reserves.Count + " trip(s)"));
            if (_supeyResult.WarningCount > 0)
                _supeyPreviewDriverCb.Items.Add(new SupeyPreviewItem(
                    SupeyPreviewItem.ItemKind.Warnings,
                    "Warnings · " + _supeyResult.WarningCount));

            string fleet = "Fleet: " + SupeyTripTimes.FormatHoursMinutesFromSeconds(_supeyResult.FleetActiveSeconds) +
                " · " + SupeyTripTimes.FormatMiles(_supeyResult.FleetMeters) +
                (_supeyResult.EarliestRelease.HasValue
                    ? " · earliest release " + SupeyTripTimes.FormatTimeOfDay(_supeyResult.EarliestRelease.Value)
                    : "");
            _supeyStatsLbl.Text = fleet;
            _supeyWarningsLink.Text = _supeyResult.WarningCount + " warning" + (_supeyResult.WarningCount == 1 ? "" : "s");

            SelectSupeyPreviewDriverWithTrips();
        }

        private void SelectSupeyPreviewDriverWithTrips()
        {
            if (_supeyPreviewDriverCb == null || _supeyPreviewDriverCb.Items.Count == 0) return;
            int pick = 0;
            for (int i = 0; i < _supeyPreviewDriverCb.Items.Count; i++)
            {
                var pi = _supeyPreviewDriverCb.Items[i] as SupeyPreviewItem;
                if (pi?.Plan == null) continue;
                int n = pi.Plan.Groups?.Sum(g => g?.Trips?.Count ?? 0) ?? 0;
                if (n > 0)
                {
                    pick = i;
                    break;
                }
            }
            _supeyPreviewDriverCb.SelectedIndex = pick;
        }

        private void OnSupeyPreviewDriverChanged()
        {
            var item = _supeyPreviewDriverCb.SelectedItem as SupeyPreviewItem;
            _supeyPreviewLv.BeginUpdate();
            _supeyPreviewLv.Items.Clear();

            if (item == null)
            {
                _supeyPreviewLv.EndUpdate();
                _supeyMap.Clear();
                _supeyPreviewStatsLbl.Text = "";
                return;
            }

            if (item.Kind == SupeyPreviewItem.ItemKind.Warnings)
            {
                BindWarningsPreview();
                _supeyPreviewLv.EndUpdate();
                ListViewMinWidthEnforcer.ScheduleRecompute(_supeyPreviewLv);
                return;
            }

            if (item.Kind == SupeyPreviewItem.ItemKind.LoadedTrips)
            {
                BindLoadedTripsPreview();
                _supeyPreviewLv.EndUpdate();
                ListViewMinWidthEnforcer.ScheduleRecompute(_supeyPreviewLv);
                return;
            }

            if (item.Plan != null)
            {
                int rowIdx = 0;
                foreach (var g in item.Plan.Groups)
                {
                    foreach (var t in g.Trips)
                    {
                        string route = ((t.PUCity ?? "").Trim() + " → " + (t.DOCITY ?? "").Trim()).Trim();
                        var lvi = new ListViewItem(new[]
                        {
                            g.GroupNumber.ToString(),
                            t.TripNumber ?? "",
                            t.ClientFullName ?? "",
                            t.PUTime ?? "",
                            route,
                            t.DOTime ?? "",
                            t.Miles ?? "",
                        });
                        lvi.UseItemStyleForSubItems = false;
                        lvi.Tag = new SupeyPreviewRowTag(g, t, item.Plan);
                        lvi.SubItems[0].BackColor = g.GroupColor;
                        lvi.SubItems[0].ForeColor = Color.Black;
                        _supeyPreviewLv.Items.Add(lvi);
                        rowIdx++;
                    }
                }
                _supeyMap.ShowDriverPlan(item.Plan);
                _supeyPreviewStatsLbl.Text = "Trips: " + item.Plan.RiderCount + " · groups: " + item.Plan.Groups.Count +
                    " · drive " + SupeyTripTimes.FormatHoursMinutesFromSeconds(item.Plan.TotalDriveSeconds) +
                    " · " + SupeyTripTimes.FormatMiles(item.Plan.TotalMeters) +
                    (item.Plan.LastDropoff.HasValue ? " · last DO " + SupeyTripTimes.FormatTimeOfDay(item.Plan.LastDropoff.Value) : "") +
                    (item.Plan.ReleaseTimeOfDay.HasValue ? " · release " + SupeyTripTimes.FormatTimeOfDay(item.Plan.ReleaseTimeOfDay.Value) : "") +
                    (item.Plan.Warnings.Count > 0 ? " · " + item.Plan.Warnings.Count + " warning(s)" : "");
            }
            else
            {
                // Reserves
                foreach (var t in _supeyResult.Reserves)
                {
                    string route = ((t.PUCity ?? "").Trim() + " → " + (t.DOCITY ?? "").Trim()).Trim();
                    var lvi = new ListViewItem(new[]
                    {
                        "—",
                        t.TripNumber ?? "",
                        t.ClientFullName ?? "",
                        t.PUTime ?? "",
                        route,
                        t.DOTime ?? "",
                        t.Miles ?? "",
                    });
                    lvi.UseItemStyleForSubItems = false;
                    lvi.SubItems[0].BackColor = Color.DimGray;
                    lvi.SubItems[0].ForeColor = Color.White;
                    _supeyPreviewLv.Items.Add(lvi);
                }
                _supeyMap.Clear();
                _supeyPreviewStatsLbl.Text = "Reserves: " + _supeyResult.Reserves.Count + " trip(s) — drag onto a driver above (manual reassign in a follow-up build).";
            }

            _supeyPreviewLv.EndUpdate();
            ListViewMinWidthEnforcer.ScheduleRecompute(_supeyPreviewLv);
        }

        /// <summary>Raw Modivcare download (not the AI schedule) — only before BUILD.</summary>
        private void BindSupeyLoadedTripsList()
        {
            if (_supeyPreviewDriverCb == null) return;
            int n = _supeyLoadedTrips?.Count ?? 0;
            _supeyPreviewDriverCb.Items.Clear();
            if (n == 0)
            {
                _supeyPreviewLv.Items.Clear();
                _supeyPreviewStatsLbl.Text = "";
                if (_supeyPreviewEmptyHint != null) _supeyPreviewEmptyHint.Visible = true;
                return;
            }

            _supeyPreviewDriverCb.Items.Add(new SupeyPreviewItem(
                SupeyPreviewItem.ItemKind.LoadedTrips,
                "Loaded pool (not scheduled) · " + n));
            if (_supeyPreviewEmptyHint != null) _supeyPreviewEmptyHint.Visible = false;
            _supeyPreviewDriverCb.SelectedIndex = 0;
        }

        private void BindLoadedTripsPreview()
        {
            _supeyPreviewLv.Items.Clear();
            if (_supeyLoadedTrips == null || _supeyLoadedTrips.Count == 0) return;

            var sorted = new List<MCDownloadedTrip>(_supeyLoadedTrips);
            sorted.Sort((a, b) => string.Compare(a?.PUTime, b?.PUTime, StringComparison.OrdinalIgnoreCase));

            foreach (var t in sorted)
            {
                if (t == null) continue;
                string route = ((t.PUStreet ?? "").Trim() + ", " + (t.PUCity ?? "").Trim()).Trim(',', ' ');
                if (!string.IsNullOrWhiteSpace(t.DOStreet) || !string.IsNullOrWhiteSpace(t.DOCITY))
                    route += " → " + ((t.DOStreet ?? "").Trim() + ", " + (t.DOCITY ?? "").Trim()).Trim(',', ' ');
                var lvi = new ListViewItem(new[]
                {
                    "—",
                    t.TripNumber ?? "",
                    t.ClientFullName ?? "",
                    t.PUTime ?? "",
                    route,
                    t.DOTime ?? t.SchedDOTime ?? "",
                    t.Miles ?? "",
                });
                lvi.Tag = t;
                _supeyPreviewLv.Items.Add(lvi);
            }

            _supeyMap?.Clear();
            _supeyPreviewStatsLbl.Text = sorted.Count
                + " trips downloaded · click BUILD — this list will switch to the AI schedule per driver.";
        }

        private void BindWarningsPreview()
        {
            int total = 0;
            // Build-level warnings appear under a "Build" pseudo-driver so the user can see at a
            // glance whether the issue is roster-wide (e.g. a driver home that won't geocode) or
            // specific to one trip cluster.
            foreach (var w in _supeyResult.BuildWarnings)
            {
                AddWarningRow(w, "Build");
                total++;
            }
            foreach (var p in _supeyResult.DriverPlans)
            {
                foreach (var w in p.Warnings)
                {
                    AddWarningRow(w, p.Driver?.Name ?? "(driver)");
                    total++;
                }
            }
            _supeyMap.Clear();
            _supeyPreviewStatsLbl.Text = "Warnings: " + total +
                " — sort the Grp column to group by kind, then dig into specific issues.";
        }

        private void AddWarningRow(SupeyWarning w, string driverName)
        {
            string kindLabel = FormatWarningKind(w.Kind);
            var lvi = new ListViewItem(new[]
            {
                kindLabel,
                string.IsNullOrEmpty(w.TripNumber) ? "—" : w.TripNumber,
                driverName ?? "",
                "",
                w.Detail ?? "",
                "",
                "",
            });
            lvi.UseItemStyleForSubItems = false;
            lvi.SubItems[0].BackColor = ColorForWarningKind(w.Kind);
            lvi.SubItems[0].ForeColor = Color.Black;
            // Stash the warning on the row so a future double-click can jump to the offending
            // trip / driver in the preview.
            lvi.Tag = w;
            _supeyPreviewLv.Items.Add(lvi);
        }

        private static string FormatWarningKind(SupeyWarningKind k)
        {
            switch (k)
            {
                case SupeyWarningKind.MissingGeo: return "Geo";
                case SupeyWarningKind.LateArrival: return "Late DO";
                case SupeyWarningKind.TightArrival: return "Tight";
                case SupeyWarningKind.LateNextPickup: return "Late PU";
                case SupeyWarningKind.StraightLineFallback: return "OSRM";
                default: return k.ToString();
            }
        }

        private static Color ColorForWarningKind(SupeyWarningKind k)
        {
            switch (k)
            {
                case SupeyWarningKind.MissingGeo: return Color.FromArgb(232, 168, 96);    // amber — data quality
                case SupeyWarningKind.LateArrival: return Color.FromArgb(232, 96, 96);    // red   — hard miss
                case SupeyWarningKind.LateNextPickup: return Color.FromArgb(232, 96, 96); // red
                case SupeyWarningKind.TightArrival: return Color.FromArgb(232, 220, 96);  // yellow — within margin
                case SupeyWarningKind.StraightLineFallback: return Color.FromArgb(160, 160, 160); // grey — informational
                default: return Color.FromArgb(200, 200, 200);
            }
        }

        // ---------- Owner-draw for the preview ListView ----------

        private static readonly Color SupeyLvBg = Color.FromArgb(70, 70, 70);
        private static readonly Color SupeyLvSel = Color.RoyalBlue;
        private static readonly Color SupeyLvText = Color.White;
        // Owner-draw mode shortcuts the framework's GridLines rendering, so we paint them by
        // hand. Slightly darker than the body so the boundary registers without competing with
        // the row content. Matches the visual weight of the legacy ListViews on Form1 that have
        // GridLines = true with the system default theme.
        private static readonly Color SupeyLvGrid = Color.FromArgb(56, 56, 56);

        private void SupeyPreviewLv_DrawColumnHeader(object sender, DrawListViewColumnHeaderEventArgs e)
        {
            // Mimic the existing trip listviews' header.
            using (var brush = new SolidBrush(Color.FromArgb(51, 51, 51)))
                e.Graphics.FillRectangle(brush, e.Bounds);
            using (var brush = new SolidBrush(Color.Gainsboro))
            using (var fnt = new Font("Archivo Medium", 11f))
            {
                var rect = new Rectangle(e.Bounds.Left + 6, e.Bounds.Top, e.Bounds.Width - 6, e.Bounds.Height);
                TextRenderer.DrawText(e.Graphics, e.Header.Text ?? "", fnt, rect, Color.Gainsboro,
                    TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter);
            }
        }

        private void SupeyPreviewLv_DrawItem(object sender, DrawListViewItemEventArgs e)
        {
            bool sel = e.Item != null && e.Item.Selected;
            using (var bg = new SolidBrush(sel ? SupeyLvSel : SupeyLvBg))
                e.Graphics.FillRectangle(bg, e.Bounds);
        }

        private void SupeyPreviewLv_DrawSubItem(object sender, DrawListViewSubItemEventArgs e)
        {
            bool sel = e.Item != null && e.Item.Selected;
            // Group swatch column gets the cluster's color even on selection so legend / list correlate.
            Color fill = sel ? SupeyLvSel : SupeyLvBg;
            if (e.ColumnIndex == 0 && !sel && e.SubItem.BackColor != Color.Empty &&
                e.SubItem.BackColor != SupeyLvBg)
            {
                fill = e.SubItem.BackColor;
            }
            using (var bg = new SolidBrush(fill))
                e.Graphics.FillRectangle(bg, e.Bounds);

            var bounds = new Rectangle(e.Bounds.Left + 6, e.Bounds.Top, e.Bounds.Width - 6, e.Bounds.Height);
            Color textColor = (e.ColumnIndex == 0 && !sel && e.SubItem.ForeColor != Color.Empty)
                ? e.SubItem.ForeColor : SupeyLvText;
            TextRenderer.DrawText(e.Graphics, e.SubItem.Text ?? "", _supeyPreviewLv.Font, bounds, textColor,
                TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.WordEllipsis | TextFormatFlags.GlyphOverhangPadding);

            DrawSupeyGridLines(e.Graphics, e.Bounds);
        }

        // ---------- Owner-draw for the roster ListView (mirrors preview's look) ----------

        private void SupeyDriversLv_DrawColumnHeader(object sender, DrawListViewColumnHeaderEventArgs e)
        {
            using (var brush = new SolidBrush(Color.FromArgb(51, 51, 51)))
                e.Graphics.FillRectangle(brush, e.Bounds);
            using (var fnt = new Font("Archivo Medium", 11f))
            {
                var rect = new Rectangle(e.Bounds.Left + 6, e.Bounds.Top, e.Bounds.Width - 6, e.Bounds.Height);
                TextRenderer.DrawText(e.Graphics, e.Header.Text ?? "", fnt, rect, Color.Gainsboro,
                    TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter);
            }
        }

        private void SupeyDriversLv_DrawItem(object sender, DrawListViewItemEventArgs e)
        {
            bool sel = e.Item != null && e.Item.Selected;
            using (var bg = new SolidBrush(sel ? SupeyLvSel : SupeyLvBg))
                e.Graphics.FillRectangle(bg, e.Bounds);
        }

        private void SupeyDriversLv_DrawSubItem(object sender, DrawListViewSubItemEventArgs e)
        {
            // Column 0 is the checkbox column; let the system render the box, but background-fill
            // it ourselves so selection highlight is consistent edge-to-edge.
            bool sel = e.Item != null && e.Item.Selected;
            using (var bg = new SolidBrush(sel ? SupeyLvSel : SupeyLvBg))
                e.Graphics.FillRectangle(bg, e.Bounds);

            if (e.ColumnIndex == 0)
            {
                // DrawDefault paints the standard checkbox glyph on top of our fill. We still get
                // the gridline pass *after* the default — the framework runs DrawDefault inline.
                e.DrawDefault = true;
                DrawSupeyGridLines(e.Graphics, e.Bounds);
                return;
            }

            var bounds = new Rectangle(e.Bounds.Left + 6, e.Bounds.Top, e.Bounds.Width - 6, e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, e.SubItem.Text ?? "", _supeyDriversLv.Font, bounds, SupeyLvText,
                TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.WordEllipsis | TextFormatFlags.GlyphOverhangPadding);

            DrawSupeyGridLines(e.Graphics, e.Bounds);
        }

        /// <summary>
        /// Paints a 1px right + bottom border on a sub-item cell to emulate <c>GridLines = true</c>
        /// when the ListView is in owner-draw mode. Single source of truth so the roster and the
        /// trips preview always agree on grid color / weight.
        /// </summary>
        private static void DrawSupeyGridLines(System.Drawing.Graphics g, Rectangle bounds)
        {
            using (var pen = new Pen(SupeyLvGrid, 1f))
            {
                // Right border (cell separator); use Bottom-1 so the line sits inside the row
                // rather than bleeding into the next.
                g.DrawLine(pen, bounds.Right - 1, bounds.Top, bounds.Right - 1, bounds.Bottom - 1);
                // Bottom border (row separator).
                g.DrawLine(pen, bounds.Left, bounds.Bottom - 1, bounds.Right - 1, bounds.Bottom - 1);
            }
        }

        // ---------- Helpers ----------

        private List<SupeyDriverProfile> GetCheckedSupeyDrivers()
        {
            var list = new List<SupeyDriverProfile>();
            if (_supeyDriversLv == null) return list;
            // Items can transiently expose null entries during a BeginUpdate / Add cycle when the
            // ItemChecked event handler re-enters this method mid-populate. Null-guard rather than
            // forbid the re-entry — the bulk-rebuild path already wraps in BeginUpdate and the
            // event will fire again with the final state when EndUpdate completes.
            foreach (ListViewItem item in _supeyDriversLv.Items)
            {
                if (item == null) continue;
                if (!item.Checked) continue;
                if (item.Tag is SupeyDriverProfile d) list.Add(d);
            }
            return list;
        }

        private void UpdateSupeyButtonStates()
        {
            int loaded = _supeyLoadedTrips?.Count ?? 0;
            int checkedDrivers = GetCheckedSupeyDrivers().Count;
            bool buildOk = loaded > 0 && checkedDrivers > 0 && _supeyCts == null;
            if (_supeyBuildBtn != null) _supeyBuildBtn.Enabled = buildOk;
            if (_supeySaveBtn != null) _supeySaveBtn.Enabled = _supeyResult != null && _supeyCts == null;

            // Tooltip explains the disabled state — without this, the user just sees a greyed-out
            // button after Load Trips and has no idea what's missing.
            EnsureSupeyToolTip();
            if (_supeyBuildBtn != null && _supeyToolTip != null)
            {
                string tip;
                if (_supeyCts != null) tip = "A build is in progress. Click Cancel to stop it.";
                else if (loaded == 0) tip = "Load trips for a service date first.";
                else if (_supeyRoster.Count == 0) tip = "Add drivers to the roster first (use ADD on the left).";
                else if (checkedDrivers == 0) tip = "Check at least one driver in the roster to include them.";
                else tip = "Build the schedule for the selected drivers.";
                _supeyToolTip.SetToolTip(_supeyBuildBtn, tip);
            }
        }

        private ToolTip _supeyToolTip;
        private void EnsureSupeyToolTip()
        {
            if (_supeyToolTip != null) return;
            _supeyToolTip = new ToolTip
            {
                AutoPopDelay = 12000,
                InitialDelay = 350,
                ReshowDelay = 200,
                ShowAlways = true,
            };
        }

        /// <summary>
        /// Composes the status string after Load Trips finishes. When the roster is empty or no
        /// driver is checked we tack on the next-step hint so the user knows BUILD is blocked
        /// for a recoverable reason — not because Load itself failed.
        /// </summary>
        private string BuildPostLoadStatus(int tripCount, DateTime date)
        {
            string lead = "Loaded " + tripCount + " trips for " + date.ToString("MMM d, yyyy") + ".";
            if (_supeyRoster.Count == 0)
                return lead + " Add drivers (ADD on the left), check the ones you want, then click BUILD.";
            int checkedCount = GetCheckedSupeyDrivers().Count;
            if (checkedCount == 0)
                return lead + " Check at least one driver in the roster, then click BUILD.";
            return lead + " Click BUILD to assemble the schedule.";
        }

        private void SetSupeyToolbarBusy(bool busy, string msg)
        {
            if (_supeyToolbar == null) return;
            _supeyLoadBtn.Enabled = !busy;
            _supeyDatePicker.Enabled = !busy;
            _supeyCancelBtn.Visible = busy;
            _supeyProgressBar.Visible = busy;
            if (msg != null) SetSupeyStatus(msg);
            UpdateSupeyButtonStates();
        }

        private void SetSupeyStatus(string text)
        {
            if (_supeyToolbarStatusLbl == null) return;
            if (InvokeRequired) { try { BeginInvoke((Action)(() => SetSupeyStatus(text))); } catch { } return; }
            _supeyToolbarStatusLbl.Text = text ?? "";
        }

        private async Task RefreshSupeyOsrmStatusAsync()
        {
            if (_supeyOsrmStatusLbl == null) return;
            HiatmeGeoSettings.Refresh();
            if (_supeyAiSettings == null)
                _supeyAiSettings = HiatmeAiSettings.Load();

            HiatmeGeoClient.GeoStatus serverGeo = null;
            if (HiatmeGeoSettings.UseServer)
            {
                serverGeo = await HiatmeGeoClient.GetStatusAsync(_supeyAiSettings).ConfigureAwait(true);
            }

            OsrmSettings.InvalidateHealthCache();
            bool localOk = serverGeo?.OsrmLocalOk == true
                || await Task.Run(() => OsrmSettings.TryHealthCheckAsync()).ConfigureAwait(true);

            void Apply()
            {
                if (_supeyOsrmStatusLbl == null || _supeyOsrmStatusLbl.IsDisposed) return;
                if (HiatmeGeoSettings.UseServer && serverGeo != null)
                {
                    if (serverGeo.OsrmLocalOk)
                    {
                        _supeyOsrmStatusLbl.Text = "Geo: server OSRM OK";
                        _supeyOsrmStatusLbl.ForeColor = Color.LightGreen;
                    }
                    else
                    {
                        _supeyOsrmStatusLbl.Text = "Geo: server OSRM public fallback";
                        _supeyOsrmStatusLbl.ForeColor = Color.Orange;
                    }
                }
                else if (localOk)
                {
                    _supeyOsrmStatusLbl.Text = "OSRM: local OK";
                    _supeyOsrmStatusLbl.ForeColor = Color.LightGreen;
                }
                else if (OsrmSettings.PreferLocal)
                {
                    _supeyOsrmStatusLbl.Text = "OSRM: offline (public fallback)";
                    _supeyOsrmStatusLbl.ForeColor = Color.Orange;
                }
                else
                {
                    _supeyOsrmStatusLbl.Text = "OSRM: public demo";
                    _supeyOsrmStatusLbl.ForeColor = Color.Gainsboro;
                }
            }
            if (InvokeRequired)
                BeginInvoke((Action)Apply);
            else
                Apply();
        }

        private void ShowSupeyWarningsModal()
        {
            if (_supeyResult == null) return;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Build warnings:");
            sb.AppendLine();
            if (_supeyResult.BuildWarnings.Count > 0)
            {
                sb.AppendLine("[Build]");
                foreach (var w in _supeyResult.BuildWarnings) sb.AppendLine(" - " + w.Detail);
                sb.AppendLine();
            }
            foreach (var p in _supeyResult.DriverPlans)
            {
                if (p.Warnings.Count == 0) continue;
                sb.AppendLine("[" + (p.Driver?.Name ?? "Driver") + "]");
                foreach (var w in p.Warnings) sb.AppendLine(" - " + w.Detail);
                sb.AppendLine();
            }
            if (sb.Length == 0) sb.AppendLine("No warnings.");
            MessageBox.Show(this, sb.ToString(), "Supey Schedule warnings",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // ---------- Combo / row tag wrappers ----------

        private sealed class SupeyPreviewItem
        {
            public enum ItemKind { Driver, Reserves, Warnings, LoadedTrips }
            public ItemKind Kind { get; }
            public SupeyDriverPlan Plan { get; }
            public string Display { get; }
            public SupeyPreviewItem(SupeyDriverPlan plan, string display)
            {
                Plan = plan;
                Kind = plan != null ? ItemKind.Driver : ItemKind.Reserves; // legacy ctor
                Display = display;
            }
            public SupeyPreviewItem(ItemKind kind, string display)
            {
                Kind = kind;
                Plan = null;
                Display = display;
            }
            public override string ToString() => Display ?? "";
        }

        private sealed class SupeyPreviewRowTag
        {
            public SupeyTripCluster Group { get; }
            public MCDownloadedTrip Trip { get; }
            public SupeyDriverPlan Plan { get; }
            public SupeyPreviewRowTag(SupeyTripCluster g, MCDownloadedTrip t, SupeyDriverPlan p)
            { Group = g; Trip = t; Plan = p; }
        }
    }
}
