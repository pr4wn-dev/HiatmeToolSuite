using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>Driver Habits — Live today poll + Day/Week/Month/Year scorecard.</summary>
    partial class Form1
    {
        // tabPageLateDrivers is declared in Form1.Designer.cs (always under Trip Scout).
        private SupeyCard ldMainCard;
        private SupeyCard ldStatusCard;
        private SupeyLabel ldStatusLbl;
        private Panel ldToolbar;
        private SupeyCard ldToolbarCard;
        private Panel ldToolbarInner;
        private RJDatePicker ldDatePicker;
        private FlowLayoutPanel ldPeriodStrip;
        private Panel ldWeekStrip;
        private SupeyMaterialButton ldWeekPrevBtn;
        private SupeyMaterialButton ldWeekNextBtn;
        private Label ldWeekRangeLbl;
        private SupeyComboBox ldMonthCombo;
        private SupeyComboBox ldYearCombo;
        private SupeyMaterialButton ldRefreshBtn;
        private Panel ldSearchHost;
        private SupeyCard ldSearchCard;
        private Panel ldSearchInner;
        private SupeyTextBox ldSearchBox;
        private bool _ldSuppressSearch;
        private Label ldDateHintLbl;
        private Label ldRangeCaptionLbl;
        private Label ldDriverCaptionLbl;
        private Label ldTripCaptionLbl;
        private Panel ldDriverStripHost;
        private Panel ldDriverStripHeader;
        private Panel ldDriverStripRow;
        private FlowLayoutPanel ldDriverStrip;
        private SupeyMaterialButton ldDriverPrevBtn;
        private SupeyMaterialButton ldDriverNextBtn;
        private Panel ldTripHeader;
        private Panel ldStageHost;
        private Panel ldHeroHost;
        private Panel ldHeroInner;
        private SupeyCard ldHeroCard;
        private Panel ldOtpHost;
        private MarketMeterControl ldOtpMeter;
        private HiatmeAiClient.LateDriversDayPerformance _ldDayPerf;
        private SupeyListView ldTripLv;

        private const int LateDriversToolbarInnerH = 42;
        private const int LateDriversDriverStripH = 114;
        private const int LateDriversHeroH = 118;
        private const int LateDriversOtpMeterW = 148;
        private const int LateDriversDriverTileW = 148;
        private const int LateDriversDriverTileH = 78;
        private const int LateDriversDriverTileGap = 8;
        private const int LateDriversDriverNavBtnW = 28;
        private string _ldLastHash;
        private string _ldHabitsHash;
        private bool _ldLoadInFlight;
        private bool _ldBuilt;
        private bool _ldFirstLoadDone;
        private bool _ldSuppressDateChanged;
        private bool _ldSuppressPeriodPickerChanged;
        private string _ldSelectedPeriod = "day";
        /// <summary>Day-mode date (date picker). Not overwritten when Week/Month/Year snap their unit.</summary>
        private DateTime _ldDayDate = DateTime.Today;
        /// <summary>Week/Month/Year focus date (unit start for API via LateDriversApiAnchorDate).</summary>
        private DateTime _ldAnchorDate = DateTime.Today;
        private string _ldSelectedDriver; // null = All drivers; LateDriversReservedKey = Reserved
        /// <summary>Pinned strip tile for WellRyde trips with Reserved status.</summary>
        private const string LateDriversReservedKey = "__reserved__";
        /// <summary>Legacy Other-tile key — still treated as Reserved if selected.</summary>
        private const string LateDriversOtherKeyLegacy = "__not_on_schedule__";
        private const string LateDriversOtherKey = LateDriversReservedKey; // compat for SelectLateDriversDriver call sites
        private string _ldHabitChip = "all";
        private string _ldRangeLabel = "";
        private List<HiatmeAiClient.LateDriversEventRow> _ldEventRows;
        private List<HiatmeAiClient.LateDriversDriverSummary> _ldDriverRows;
        private int _ldMcTripCount;
        private List<HiatmeAiClient.LateDriversDriverSummary> _ldStripDrivers =
            new List<HiatmeAiClient.LateDriversDriverSummary>();
        private int _ldDriverScrollOffset;
        private bool _ldDriverStripRendering;
        private readonly List<SupeyMaterialButton> _ldPeriodButtons = new List<SupeyMaterialButton>();
        private TableLayoutPanel ldScorecardHost;
        private readonly Dictionary<string, Label> _ldScoreValues =
            new Dictionary<string, Label>(StringComparer.OrdinalIgnoreCase);
        private readonly List<SupeyCard> _ldScoreCards = new List<SupeyCard>();
        private readonly List<SupeyCard> _ldDriverTiles = new List<SupeyCard>();
        private System.Windows.Forms.Timer _ldDriverAlertBlinkTimer;
        private bool _ldDriverAlertBlinkOn;
        /// <summary>Blink/chirp window after the Suite first notices a late/early.</summary>
        private const int LateDriversAlertWindowSeconds = 75;
        private const string LateDriversAlertSoundFileName = "law-and-order-alert.mp3";
        private const string LateDriversBellSoundFileName = "will-call-bell.wav";
        /// <summary>
        /// Event keys already announced today (persisted). Survives restart; not pruned mid-day
        /// when the habits merge briefly misses an early row.
        /// </summary>
        private readonly HashSet<string> _ldAlertChirpKeys =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>
        /// Local blink deadline per event key — starts when the Suite first sees the alert,
        /// not when the panel stamped detected_at (avoids missing ding when poll lags).
        /// </summary>
        private readonly Dictionary<string, DateTime> _ldAlertHotUntil =
            new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private bool _ldAlertChirpKeysLoaded;
        private string _ldAlertChirpKeysLoadedForDate;
        /// <summary>True after a habits/live payload is applied — do not prune seen-keys against an empty boot list.</summary>
        private bool _ldAlertSnapshotReady;
        private HashSet<string> _ldAlertFreshChirpKeys;

        /// <summary>Cached SCHEDULES FOR {year} workbook for Day/Live schedule ListView merge.</summary>
        private string _ldScheduleCacheDateIso;
        private string _ldScheduleCachePath;
        private string _ldScheduleCacheFileName;
        private string _ldScheduleCacheSource;
        private string _ldScheduleCacheEtag;
        /// <summary>LastWriteTimeUtc ticks + length — invalidate cache when Desktop/cache file changes.</summary>
        private string _ldScheduleCacheFileStamp;
        private ScheduleBuilderLoadResult _ldScheduleCache;
        private string _ldScheduleCacheError;

        /// <summary>WellRyde / Trip Scout rows for the day — fills Actual (and missing Sched) times.</summary>
        private string _ldWrTripsDateIso;
        private readonly Dictionary<string, HiatmeAiClient.TripScoutServerTripRow> _ldWrTripsByTripNo =
            new Dictionary<string, HiatmeAiClient.TripScoutServerTripRow>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Cached Reserved-tile rows (WR status Reserved).</summary>
        private string _ldOffScheduleCacheDateIso;
        private List<LateDriversTripRowTag> _ldOffScheduleCache;
        private int _ldOffScheduleCount;

        /// <summary>ListView Tag: schedule row + optional habit event (blink uses HabitEvent).</summary>
        private sealed class LateDriversTripRowTag
        {
            public MCDownloadedTrip ScheduleTrip;
            public HiatmeAiClient.LateDriversEventRow HabitEvent;
            public bool FromSchedule;
            public bool HabitOnly;
            public bool IsGroupHeader;
            public bool IsGap;
            public int GroupNumber;
            public string GroupLabel;
            public Color? GroupColor;
            public string DriverDisplay;
            public string TripNo;
            public string ServiceDate;
            public string Client;
            public string SchedDisplay;
            public string SchedPuDisplay;
            public string ActualPuDisplay;
            public string SchedDoDisplay;
            public string ActualDoDisplay;
            public string StatusDisplay;
            public string StateDisplay;
            public DateTime SortTime;
            /// <summary>
            /// Exact habit keys on this trip (strict trip #). Used for score-tile highlight
            /// so paint never fuzzy-matches a different trip's habits.
            /// </summary>
            public HashSet<string> HabitChipKeys;
            public bool HabitChipOpen;
            /// <summary>
            /// Workbook still lists this trip under the selected driver, but WellRyde
            /// shows a different driver — not their ding; habits follow WR.
            /// </summary>
            public bool ReassignedAway;
            public string ReassignedToDriver;
            /// <summary>
            /// Habit-only row on the WR driver while the workbook still has the trip
            /// under someone else.
            /// </summary>
            public string ReceivedFromDriver;
            /// <summary>
            /// Reserved-tile marker (non-empty when row is from the Reserved list).
            /// </summary>
            public string OffScheduleKind;
            /// <summary>Inserted expand row under a schedule trip (WR time/address/driver change).</summary>
            public bool IsChangeDetail;
            public HiatmeAiClient.TripScoutChangeRow ChangeEvent;
        }

        private static bool LateDriversIsReservedSelected(string driverKey) =>
            string.Equals(driverKey, LateDriversReservedKey, StringComparison.Ordinal)
            || string.Equals(driverKey, LateDriversOtherKeyLegacy, StringComparison.Ordinal);

        private bool LateDriversReservedSelected => LateDriversIsReservedSelected(_ldSelectedDriver);

        // Compat aliases used by locate/bell paths during rename.
        private static bool LateDriversIsOtherSelected(string driverKey) =>
            LateDriversIsReservedSelected(driverKey);

        private bool LateDriversOtherSelected => LateDriversReservedSelected;

        private void InitializeLateDriversTab()
        {
            if (_ldBuilt || hiatmeTabControl == null || tabPageLateDrivers == null)
                return;

            try
            {
                // Clear a prior failed partial build so retry is clean.
                if (tabPageLateDrivers.Controls.Count > 0
                    && (ldMainCard == null || !tabPageLateDrivers.Controls.Contains(ldMainCard)))
                {
                    tabPageLateDrivers.Controls.Clear();
                }
                else if (!_ldBuilt && ldMainCard != null)
                {
                    try { tabPageLateDrivers.Controls.Clear(); } catch { }
                    ldMainCard = null;
                    ldStatusCard = null;
                    ldToolbar = null;
                    ldToolbarCard = null;
                    ldToolbarInner = null;
                    ldStageHost = null;
                    ldTripLv = null;
                    ldPeriodStrip = null;
                    ldDatePicker = null;
                    ldWeekStrip = null;
                    ldWeekPrevBtn = null;
                    ldWeekNextBtn = null;
                    ldWeekRangeLbl = null;
                    ldMonthCombo = null;
                    ldYearCombo = null;
                    ldDateHintLbl = null;
                    ldRangeCaptionLbl = null;
                    ldDriverCaptionLbl = null;
                    ldTripCaptionLbl = null;
                    ldDriverStripHost = null;
                    ldDriverStripHeader = null;
                    ldDriverStripRow = null;
                    ldDriverStrip = null;
                    ldDriverPrevBtn = null;
                    ldDriverNextBtn = null;
                    ldTripHeader = null;
                    _ldStripDrivers.Clear();
                    _ldDriverScrollOffset = 0;
                    ldHeroHost = null;
                    ldHeroInner = null;
                    ldHeroCard = null;
                    ldOtpHost = null;
                    ldOtpMeter = null;
                    ldScorecardHost = null;
                    ldRefreshBtn = null;
                    ldSearchHost = null;
                    ldSearchCard = null;
                    ldSearchInner = null;
                    ldSearchBox = null;
                    ldLiveSwitch = null;
                    ldColorsSwitch = null;
                    _ldLiveChromeHost = null;
                    _ldLiveTimerCard = null;
                    _ldLiveScanCard = null;
                    _ldLiveScan = null;
                    _ldLiveCountdown = null;
                    _ldLiveDivider = null;
                    ClearLateDriversBellUiRefs();
                    ResetLateDriversBellState();
                    _ldDayDate = DateTime.Today;
                    _ldAnchorDate = DateTime.Today;
                    ClearLateDriversScheduleCache();
                    _ldPeriodButtons.Clear();
                    _ldScoreValues.Clear();
                    _ldScoreCards.Clear();
                    _ldDriverTiles.Clear();
                }
                if (tabImageList != null && tabImageList.Images.ContainsKey("driver-habits.png"))
                    tabPageLateDrivers.ImageKey = "driver-habits.png";
                else if (tabImageList != null && tabImageList.Images.ContainsKey("late-drivers.png"))
                    tabPageLateDrivers.ImageKey = "late-drivers.png";
                else if (tabImageList != null && tabImageList.Images.ContainsKey("magnify.png"))
                    tabPageLateDrivers.ImageKey = "magnify.png";

                int tripScoutAt = hiatmeTabControl.TabPages.IndexOf(tabPage9);
                int lateAt = hiatmeTabControl.TabPages.IndexOf(tabPageLateDrivers);
                if (tripScoutAt >= 0)
                {
                    int want = tripScoutAt + 1;
                    if (lateAt < 0)
                        hiatmeTabControl.TabPages.Insert(want, tabPageLateDrivers);
                    else if (lateAt != want)
                    {
                        hiatmeTabControl.TabPages.Remove(tabPageLateDrivers);
                        tripScoutAt = hiatmeTabControl.TabPages.IndexOf(tabPage9);
                        want = tripScoutAt >= 0 ? tripScoutAt + 1 : hiatmeTabControl.TabPages.Count;
                        hiatmeTabControl.TabPages.Insert(want, tabPageLateDrivers);
                    }
                }
                else if (lateAt < 0)
                {
                    hiatmeTabControl.TabPages.Add(tabPageLateDrivers);
                }

                ldMainCard = new SupeyCard
                {
                    Name = "ldMainCard",
                    SurfaceLevel = SupeyCard.Surface.Standard,
                    ShowBorder = true,
                };
                ldStatusCard = new SupeyCard
                {
                    Name = "ldStatusCard",
                    SurfaceLevel = SupeyCard.Surface.Elevated,
                    ShowBorder = true,
                };
                ldStatusLbl = new SupeyLabel
                {
                    Name = "ldStatusLbl",
                    Text = "Status: Driver Habits — idle",
                    AutoSize = false,
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleLeft,
                    Padding = new Padding(10, 0, 10, 0),
                };
                ldStatusCard.Controls.Add(ldStatusLbl);

                _ldDayDate = DateTime.Today;
                _ldAnchorDate = DateTime.Today;
                ldDatePicker = new RJDatePicker
                {
                    Name = "ldDatePicker",
                    Size = new Size(214, 35),
                    BorderSize = 2,
                    BorderColor = Color.Black,
                    SkinColor = Color.FromArgb(64, 64, 64),
                    TextColor = Color.White,
                    Font = new Font("Microsoft Sans Serif", 9.75F),
                };
                try { ldDatePicker.Value = _ldDayDate; } catch { }
                ldDatePicker.ValueChanged += (_, __) =>
                {
                    if (_ldSuppressDateChanged || !_ldBuilt || LateDriversLiveEnabled)
                        return;
                    _ldDayDate = ldDatePicker.Value.Date;
                    // Keep period focus aligned so Week/Month/Year open on the same neighborhood.
                    _ldAnchorDate = _ldDayDate;
                    SyncLateDriversPeriodPickersFromAnchor();
                    UpdateLateDriversToolbarHints();
                    _ = LateDriversRefreshAsync(force: true);
                };

                ldDateHintLbl = new Label
                {
                    Name = "ldDateHintLbl",
                    AutoSize = false,
                    TextAlign = ContentAlignment.MiddleLeft,
                    BackColor = Color.Transparent,
                    Text = "Day:",
                };
                ldRangeCaptionLbl = new Label
                {
                    Name = "ldRangeCaptionLbl",
                    AutoSize = false,
                    TextAlign = ContentAlignment.MiddleLeft,
                    BackColor = Color.Transparent,
                    Text = "Pick a range, then Refresh to load driver habits",
                };

                ldPeriodStrip = new FlowLayoutPanel
                {
                    Name = "ldPeriodStrip",
                    AutoSize = false,
                    WrapContents = false,
                    FlowDirection = FlowDirection.LeftToRight,
                    BackColor = Color.Transparent,
                    Margin = Padding.Empty,
                    Padding = Padding.Empty,
                };
                foreach (string label in new[] { "Day", "Week", "Month", "Year" })
                {
                    var btn = new SupeyMaterialButton
                    {
                        Name = "ldPeriod_" + label,
                        Text = label,
                        Type = SupeyMaterialButton.MaterialButtonType.Outlined,
                        Size = new Size(68, 30),
                        Margin = new Padding(0, 0, 6, 0),
                        Tag = label.ToLowerInvariant(),
                    };
                    btn.Click += LdPeriodButton_Click;
                    _ldPeriodButtons.Add(btn);
                    ldPeriodStrip.Controls.Add(btn);
                }
                StyleLateDriversPeriodButtons();

                BuildLateDriversWeekStrip();
                BuildLateDriversMonthYearCombos();

                ldRefreshBtn = new SupeyMaterialButton
                {
                    Name = "ldRefreshBtn",
                    Text = "Refresh",
                    Type = SupeyMaterialButton.MaterialButtonType.Contained,
                    UseAccentColor = true,
                    Size = new Size(88, 30),
                    Margin = Padding.Empty,
                };
                ldRefreshBtn.Click += (_, __) => _ = LateDriversRefreshAsync(force: true);

                BuildLateDriversLiveSwitch();
                EnsureLateDriversLiveChrome();

                BuildLateDriversBodyChrome();
                EnsureLateDriversBellAlertHost();

                // Dock: Fill first, then Tops (last Top = topmost).
                // Top→bottom: Toolbar, Search, Will-call strip, Range, Driver strip, Hero, Stage.
                ldMainCard.Controls.Add(ldStageHost);
                ldMainCard.Controls.Add(ldHeroHost);
                ldMainCard.Controls.Add(ldDriverStripHost);
                ldMainCard.Controls.Add(ldRangeCaptionLbl);
                if (ldBellAlertHost != null)
                    ldMainCard.Controls.Add(ldBellAlertHost);
                ldMainCard.Controls.Add(ldSearchHost);
                ldMainCard.Controls.Add(ldToolbar);

                tabPageLateDrivers.Controls.Add(ldStatusCard);
                tabPageLateDrivers.Controls.Add(ldMainCard);
                tabPageLateDrivers.Resize += (_, __) => LayoutLateDriversTabPanels();

                tabPageLateDrivers.Text = "Driver Habits";
                UpdateLateDriversPeriodPickerChrome();
                UpdateLateDriversToolbarHints();
                ApplyLateDriversVisualTheme(layout: true);
                _ldBuilt = true;
            }
            catch (Exception ex)
            {
                // Do NOT set _ldBuilt — allow a retry on next tab select.
                try
                {
                    tabPageLateDrivers.Text = "Driver Habits";
                    System.Diagnostics.Debug.WriteLine("Driver Habits UI failed: " + ex);
                    if (ldStatusLbl != null && !ldStatusLbl.IsDisposed)
                        ldStatusLbl.Text = "Status: Driver Habits UI failed — " + ex.Message;
                    else if (tabPageLateDrivers.Controls.Count == 0)
                    {
                        tabPageLateDrivers.Controls.Add(new Label
                        {
                            Dock = DockStyle.Fill,
                            TextAlign = ContentAlignment.MiddleCenter,
                            Text = "Driver Habits failed to build:\r\n" + ex.Message,
                            ForeColor = Color.OrangeRed,
                        });
                    }
                }
                catch { }
            }
        }

        private void BuildLateDriversBodyChrome()
        {
            // ── Top toolbar card (period + date + Live + Refresh) ─────────
            // Host padding matches hero / driver strip so the elevated card is full-width inset.
            ldToolbar = new Panel
            {
                Name = "ldToolbar",
                Dock = DockStyle.Top,
                Height = LateDriversToolbarInnerH + 16 + 8, // inner pad + host pad
                Padding = new Padding(10, 4, 10, 4),
                BackColor = Color.Transparent,
            };
            ldToolbarCard = new SupeyCard
            {
                Name = "ldToolbarCard",
                Dock = DockStyle.Fill,
                SurfaceLevel = SupeyCard.Surface.Elevated,
                ShowBorder = true,
                CornerRadius = 8,
                Padding = Padding.Empty,
            };
            ldToolbarInner = new Panel
            {
                Name = "ldToolbarInner",
                Dock = DockStyle.Fill,
                Padding = new Padding(12, 8, 12, 8),
                BackColor = Color.Transparent,
            };
            ldToolbarInner.Resize += (_, __) => LayoutLateDriversToolbar();

            if (ldPeriodStrip != null)
                ldToolbarInner.Controls.Add(ldPeriodStrip);
            if (ldDateHintLbl != null)
                ldToolbarInner.Controls.Add(ldDateHintLbl);
            if (ldDatePicker != null)
                ldToolbarInner.Controls.Add(ldDatePicker);
            if (ldWeekStrip != null)
                ldToolbarInner.Controls.Add(ldWeekStrip);
            if (ldMonthCombo != null)
                ldToolbarInner.Controls.Add(ldMonthCombo);
            if (ldYearCombo != null)
                ldToolbarInner.Controls.Add(ldYearCombo);
            if (ldColorsSwitch != null)
                ldToolbarInner.Controls.Add(ldColorsSwitch);
            if (ldLiveSwitch != null)
                ldToolbarInner.Controls.Add(ldLiveSwitch);
            if (_ldLiveChromeHost != null)
                ldToolbarInner.Controls.Add(_ldLiveChromeHost);
            if (ldRefreshBtn != null)
                ldToolbarInner.Controls.Add(ldRefreshBtn);

            ldToolbarCard.Controls.Add(ldToolbarInner);
            ldToolbar.Controls.Add(ldToolbarCard);

            // ── Search row (Trip Scout–style full-width card) ─────────────
            ldSearchHost = new Panel
            {
                Name = "ldSearchHost",
                Dock = DockStyle.Top,
                Height = 54,
                Padding = new Padding(10, 0, 10, 4),
                BackColor = Color.Transparent,
            };
            ldSearchCard = new SupeyCard
            {
                Name = "ldSearchCard",
                Dock = DockStyle.Fill,
                SurfaceLevel = SupeyCard.Surface.Elevated,
                ShowBorder = true,
                CornerRadius = 8,
                Padding = Padding.Empty,
            };
            ldSearchInner = new Panel
            {
                Name = "ldSearchInner",
                Dock = DockStyle.Fill,
                Padding = new Padding(12, 8, 12, 8),
                BackColor = Color.Transparent,
            };
            ldSearchInner.Resize += (_, __) => LayoutLateDriversSearchBox();
            ldSearchBox = new SupeyTextBox
            {
                Name = "ldSearchBox",
                Hint = "Search trips by ID, client, driver, address, status…",
                LeadingIcon = Properties.Resources.magnify,
                UseTallSize = false,
                UseToolbarSize = true,
                MaxLength = 200,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            };
            try { ldSearchBox.Height = 30; } catch { }
            ldSearchBox.TextChanged += LdSearchBox_TextChanged;
            ldSearchBox.KeyDown += LdSearchBox_KeyDown;
            ldSearchInner.Controls.Add(ldSearchBox);
            ldSearchCard.Controls.Add(ldSearchInner);
            ldSearchHost.Controls.Add(ldSearchCard);

            ldRangeCaptionLbl.Dock = DockStyle.Top;
            ldRangeCaptionLbl.Height = 22;
            ldRangeCaptionLbl.Padding = new Padding(22, 0, 22, 0);
            ldRangeCaptionLbl.BackColor = Color.Transparent;

            // ── Driver card strip (fixed height; ◀ ▶ pages when overflow) ─
            ldDriverStripHost = new Panel
            {
                Name = "ldDriverStripHost",
                Dock = DockStyle.Top,
                Height = LateDriversDriverStripH,
                Padding = new Padding(10, 2, 10, 2),
                BackColor = SupeyTheme.Surface,
            };
            ldDriverStripHeader = new Panel
            {
                Name = "ldDriverStripHeader",
                Dock = DockStyle.Top,
                Height = 28,
                BackColor = Color.Transparent,
            };
            ldDriverCaptionLbl = new Label
            {
                Name = "ldDriverCaptionLbl",
                AutoSize = false,
                Text = "Drivers",
                TextAlign = ContentAlignment.MiddleLeft,
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
            };
            ldDriverStripHeader.Controls.Add(ldDriverCaptionLbl);

            ldDriverStripRow = new Panel
            {
                Name = "ldDriverStripRow",
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
            };
            ldDriverStripRow.Resize += (_, __) => LayoutLateDriversDriverStripRow();
            ldDriverPrevBtn = new SupeyMaterialButton
            {
                Name = "ldDriverPrevBtn",
                Text = "◀",
                Type = SupeyMaterialButton.MaterialButtonType.Outlined,
                Margin = Padding.Empty,
                Size = new Size(LateDriversDriverNavBtnW, LateDriversDriverTileH),
                Enabled = false,
                Visible = false,
            };
            ldDriverNextBtn = new SupeyMaterialButton
            {
                Name = "ldDriverNextBtn",
                Text = "▶",
                Type = SupeyMaterialButton.MaterialButtonType.Outlined,
                Margin = Padding.Empty,
                Size = new Size(LateDriversDriverNavBtnW, LateDriversDriverTileH),
                Enabled = false,
                Visible = false,
            };
            ldDriverPrevBtn.Click += (_, __) => LateDriversShiftDriverStrip(-1);
            ldDriverNextBtn.Click += (_, __) => LateDriversShiftDriverStrip(1);
            ldDriverStrip = new FlowLayoutPanel
            {
                Name = "ldDriverStrip",
                WrapContents = false,
                FlowDirection = FlowDirection.LeftToRight,
                AutoScroll = false,
                Padding = new Padding(4, 0, 4, 0),
                BackColor = Color.Transparent,
            };
            ldDriverStrip.Resize += (_, __) => RenderLateDriversDriverStripPage();
            ldDriverStripRow.Controls.Add(ldDriverStrip);
            ldDriverStripRow.Controls.Add(ldDriverPrevBtn);
            ldDriverStripRow.Controls.Add(ldDriverNextBtn);
            ldDriverStripHost.Controls.Add(ldDriverStripRow);
            ldDriverStripHost.Controls.Add(ldDriverStripHeader);

            // ── Scorecard hero (host adds side padding; docked Margin is ignored) ─
            ldHeroHost = new Panel
            {
                Name = "ldHeroHost",
                Dock = DockStyle.Top,
                Height = LateDriversHeroH + 6,
                Padding = new Padding(10, 2, 10, 4),
                BackColor = Color.Transparent,
            };
            ldHeroCard = new SupeyCard
            {
                Name = "ldHeroCard",
                Dock = DockStyle.Fill,
                SurfaceLevel = SupeyCard.Surface.Elevated,
                ShowBorder = true,
                CornerRadius = 8,
                // StyleToolTabCard clears SupeyCard.Padding — keep inset on an inner panel.
                Padding = Padding.Empty,
            };
            ldHeroInner = new Panel
            {
                Name = "ldHeroInner",
                Dock = DockStyle.Fill,
                Padding = new Padding(8, 6, 8, 6),
                BackColor = Color.Transparent,
            };
            ldScorecardHost = new TableLayoutPanel
            {
                Name = "ldScorecardHost",
                Dock = DockStyle.Fill,
                ColumnCount = 8,
                RowCount = 1,
                Padding = Padding.Empty,
                Margin = Padding.Empty,
                BackColor = Color.Transparent,
            };
            for (int i = 0; i < 8; i++)
                ldScorecardHost.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 8f));
            ldScorecardHost.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            BuildLateDriversScorecardWidgets();

            // Live day-performance meter — ModivCare + WellRyde scored trips (not Market OTP).
            ldOtpHost = new Panel
            {
                Name = "ldOtpHost",
                Dock = DockStyle.Right,
                Width = LateDriversOtpMeterW,
                Padding = new Padding(8, 0, 0, 0),
                BackColor = Color.Transparent,
            };
            ldOtpMeter = new MarketMeterControl
            {
                Name = "ldOtpMeter",
                Dock = DockStyle.Fill,
                Compact = true,
                Caption = "Today",
                InvertGood = false,
                DetailText = "day performance",
            };
            ldOtpMeter.SetValue(null, "—", "waiting for WR trips", animate: false);
            ldOtpHost.Controls.Add(ldOtpMeter);

            ldHeroInner.Controls.Add(ldScorecardHost);
            ldHeroInner.Controls.Add(ldOtpHost);
            ldHeroCard.Controls.Add(ldHeroInner);
            ldHeroHost.Controls.Add(ldHeroCard);

            // ── Stage: trip list — explicit bounds below chrome (not Dock.Fill).
            // Dock.Fill + BringToFront on search/bell leaves the list full-height under the search bar.
            ldStageHost = new Panel
            {
                Name = "ldStageHost",
                Dock = DockStyle.None,
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                Padding = new Padding(10, 4, 10, 8),
                BackColor = SupeyTheme.Surface,
            };

            ldTripHeader = new Panel
            {
                Name = "ldTripHeader",
                Dock = DockStyle.Top,
                Height = 30,
                Padding = new Padding(0, 2, 0, 2),
                BackColor = Color.Transparent,
            };
            ldTripCaptionLbl = new Label
            {
                Name = "ldTripCaptionLbl",
                AutoSize = false,
                Text = "Trip habits",
                TextAlign = ContentAlignment.MiddleLeft,
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
            };
            ldTripHeader.Controls.Add(ldTripCaptionLbl);

            ldTripLv = CreateLateDriversListView("ldTripLv");
            EnsureLateDriversTripColumns(showDriver: true);
            ldTripLv.DoubleClick += LdTripLv_DoubleClick;

            ldStageHost.Controls.Add(ldTripLv);
            ldStageHost.Controls.Add(ldTripHeader);
        }

        private void BuildLateDriversScorecardWidgets()
        {
            if (ldScorecardHost == null)
                return;
            ldScorecardHost.Controls.Clear();
            _ldScoreValues.Clear();
            _ldScoreCards.Clear();
            // Clickable filters (same keys as the old chip strip).
            var metrics = new[]
            {
                ("all", "All"),
                ("late_pu", "Late PU"),
                ("late_do", "Late DO"),
                ("early_pu", "Early PU"),
                ("early_do", "Early DO"),
                ("unfinished_ticket", "Unfinished"),
                ("billed_unfinished", "Billed skip"),
                ("open", "Open now"),
            };
            for (int i = 0; i < metrics.Length; i++)
            {
                var pair = metrics[i];
                var card = new SupeyCard
                {
                    Name = "ldScore_" + pair.Item1,
                    SurfaceLevel = SupeyCard.Surface.Standard,
                    ShowBorder = true,
                    CornerRadius = 8,
                    Dock = DockStyle.Fill,
                    Margin = new Padding(i == 0 ? 0 : 3, 0, i == metrics.Length - 1 ? 0 : 3, 0),
                    Padding = new Padding(4, 4, 4, 4),
                    Tag = pair.Item1,
                    Cursor = Cursors.Hand,
                };

                // Vertically center caption+value as one block (Dock Top/Fill left empty bottom).
                var frame = new TableLayoutPanel
                {
                    Name = "ldScoreFrame_" + pair.Item1,
                    Dock = DockStyle.Fill,
                    ColumnCount = 1,
                    RowCount = 3,
                    Margin = Padding.Empty,
                    Padding = Padding.Empty,
                    BackColor = Color.Transparent,
                };
                frame.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
                frame.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
                frame.RowStyles.Add(new RowStyle(SizeType.Absolute, 48f));
                frame.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));

                var stack = new Panel
                {
                    Name = "ldScoreStack_" + pair.Item1,
                    Dock = DockStyle.Fill,
                    Margin = Padding.Empty,
                    Padding = Padding.Empty,
                    BackColor = Color.Transparent,
                    Cursor = Cursors.Hand,
                };
                var caption = new Label
                {
                    Name = "ldScoreCap_" + pair.Item1,
                    Text = pair.Item2,
                    AutoSize = false,
                    Dock = DockStyle.Top,
                    Height = 18,
                    TextAlign = ContentAlignment.MiddleCenter,
                    ForeColor = SupeyTheme.TextSecondary,
                    BackColor = Color.Transparent,
                    Font = SupeyTheme.CaptionFont,
                    Cursor = Cursors.Hand,
                };
                var value = new Label
                {
                    Name = "ldScoreVal_" + pair.Item1,
                    Text = "0",
                    AutoSize = false,
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleCenter,
                    ForeColor = SupeyTheme.TextPrimary,
                    BackColor = Color.Transparent,
                    Font = new Font("Segoe UI Semibold", 16f, FontStyle.Bold),
                    Cursor = Cursors.Hand,
                };
                stack.Controls.Add(value);
                stack.Controls.Add(caption);
                var spacerTop = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent, Cursor = Cursors.Hand };
                var spacerBot = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent, Cursor = Cursors.Hand };
                frame.Controls.Add(spacerTop, 0, 0);
                frame.Controls.Add(stack, 0, 1);
                frame.Controls.Add(spacerBot, 0, 2);

                card.Controls.Add(frame);
                WireLateDriversScorecardClick(card, pair.Item1);
                _ldScoreValues[pair.Item1] = value;
                _ldScoreCards.Add(card);
                ldScorecardHost.Controls.Add(card, i, 0);
            }
            StyleLateDriversScoreFilters();
        }

        private void WireLateDriversScorecardClick(Control root, string key)
        {
            if (root == null) return;
            string filterKey = (key ?? "all").Trim().ToLowerInvariant();
            EventHandler pick = (_, __) => SelectLateDriversHabitFilter(filterKey);
            root.Click += pick;
            foreach (Control child in root.Controls)
                WireLateDriversScorecardClick(child, filterKey);
        }

        private void SelectLateDriversHabitFilter(string key)
        {
            string next = (key ?? "all").Trim().ToLowerInvariant();
            // Clicking the active tile again clears the highlight.
            if (next == _ldHabitChip && next != "all")
                next = "all";
            else if (next == _ldHabitChip)
                return;
            _ldHabitChip = next;
            StyleLateDriversScoreFilters();
            // Score tiles highlight trips only — never filter/deselect the driver strip.
            BindLateDriversTripPane();
            FocusLateDriversFirstChipMatch();
            try { ldTripLv?.Invalidate(true); } catch { }
        }

        private void StyleLateDriversScoreFilters()
        {
            string chip = (_ldHabitChip ?? "all").Trim().ToLowerInvariant();
            foreach (var card in _ldScoreCards)
            {
                if (card == null || card.IsDisposed) continue;
                string key = (card.Tag as string ?? "").Trim().ToLowerInvariant();
                bool on = key == chip;
                card.Accent = on ? SupeyCard.AccentEdge.Top : SupeyCard.AccentEdge.None;
                card.SurfaceLevel = on ? SupeyCard.Surface.Elevated : SupeyCard.Surface.Standard;
                card.ShowBorder = true;
                card.Invalidate(true);
            }
        }

        private void BuildLateDriversWeekStrip()
        {
            ldWeekStrip = new Panel
            {
                Name = "ldWeekStrip",
                Size = new Size(280, 30),
                BackColor = Color.Transparent,
                Margin = Padding.Empty,
                Visible = false,
            };
            ldWeekPrevBtn = new SupeyMaterialButton
            {
                Name = "ldWeekPrevBtn",
                Text = "◀",
                Type = SupeyMaterialButton.MaterialButtonType.Outlined,
                Size = new Size(30, 30),
                Margin = Padding.Empty,
                Location = new Point(0, 0),
            };
            ldWeekPrevBtn.Click += (_, __) =>
            {
                if (!_ldBuilt) return;
                SetLateDriversAnchorDate(LateDriversWeekStartMonday(_ldAnchorDate).AddDays(-7), refresh: true);
            };
            ldWeekNextBtn = new SupeyMaterialButton
            {
                Name = "ldWeekNextBtn",
                Text = "▶",
                Type = SupeyMaterialButton.MaterialButtonType.Outlined,
                Size = new Size(30, 30),
                Margin = Padding.Empty,
                Location = new Point(250, 0),
            };
            ldWeekNextBtn.Click += (_, __) =>
            {
                if (!_ldBuilt) return;
                SetLateDriversAnchorDate(LateDriversWeekStartMonday(_ldAnchorDate).AddDays(7), refresh: true);
            };
            ldWeekRangeLbl = new Label
            {
                Name = "ldWeekRangeLbl",
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.Transparent,
                Location = new Point(34, 0),
                Size = new Size(212, 30),
                Text = "",
            };
            ldWeekStrip.Controls.Add(ldWeekPrevBtn);
            ldWeekStrip.Controls.Add(ldWeekRangeLbl);
            ldWeekStrip.Controls.Add(ldWeekNextBtn);
            UpdateLateDriversWeekRangeLabel();
        }

        private void BuildLateDriversMonthYearCombos()
        {
            ldMonthCombo = new SupeyComboBox { Name = "ldMonthCombo", Visible = false };
            ConfigureToolbarSupeyCombo(ldMonthCombo, 120);
            for (int m = 1; m <= 12; m++)
                ldMonthCombo.Items.Add(CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(m));
            ldMonthCombo.SelectedIndexChanged += (_, __) =>
            {
                if (_ldSuppressPeriodPickerChanged || !_ldBuilt) return;
                ApplyLateDriversMonthYearFromCombos(refresh: true);
            };

            ldYearCombo = new SupeyComboBox { Name = "ldYearCombo", Visible = false };
            ConfigureToolbarSupeyCombo(ldYearCombo, 88);
            int yNow = DateTime.Today.Year;
            for (int y = yNow - 5; y <= yNow + 1; y++)
                ldYearCombo.Items.Add(y.ToString(CultureInfo.InvariantCulture));
            ldYearCombo.SelectedIndexChanged += (_, __) =>
            {
                if (_ldSuppressPeriodPickerChanged || !_ldBuilt) return;
                ApplyLateDriversMonthYearFromCombos(refresh: true);
            };

            SyncLateDriversPeriodPickersFromAnchor();
        }

        private static DateTime LateDriversWeekStartMonday(DateTime d)
        {
            d = d.Date;
            int offset = ((int)d.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
            return d.AddDays(-offset);
        }

        private DateTime LateDriversApiAnchorDate()
        {
            string mode = LateDriversSelectedMode();
            if (mode == "live")
                return DateTime.Today;
            if (mode == "day")
                return _ldDayDate.Date;
            DateTime d = _ldAnchorDate.Date;
            if (mode == "week")
                return LateDriversWeekStartMonday(d);
            if (mode == "month")
                return new DateTime(d.Year, d.Month, 1);
            if (mode == "year")
                return new DateTime(d.Year, 1, 1);
            return d;
        }

        /// <summary>
        /// Modivcare Trip Download only covers roughly current/next month and ~8 days back.
        /// Calling the downloader outside that window pops a MessageBox — skip instead.
        /// </summary>
        private static bool LateDriversModivcareDownloadWindowOk(DateTime day)
        {
            day = day.Date;
            DateTime today = DateTime.Today;
            if (day < today.AddDays(-8) || day > today.AddDays(30))
                return false;
            DateTime monthStart = new DateTime(today.Year, today.Month, 1);
            DateTime nextMonthEnd = monthStart.AddMonths(2).AddDays(-1);
            return day >= monthStart && day <= nextMonthEnd;
        }

        private void SetLateDriversAnchorDate(DateTime date, bool refresh)
        {
            if (LateDriversLiveEnabled)
                return;
            _ldAnchorDate = date.Date;
            SyncLateDriversPeriodPickersFromAnchor();
            UpdateLateDriversToolbarHints();
            if (refresh && _ldBuilt)
                _ = LateDriversRefreshAsync(force: true);
        }

        private void ApplyLateDriversMonthYearFromCombos(bool refresh)
        {
            int year = _ldAnchorDate.Year;
            int month = _ldAnchorDate.Month;
            if (ldYearCombo?.SelectedItem != null
                && int.TryParse(
                    Convert.ToString(ldYearCombo.SelectedItem),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int y))
                year = y;
            if (string.Equals(_ldSelectedPeriod, "year", StringComparison.OrdinalIgnoreCase))
                month = 1;
            else if (ldMonthCombo != null && ldMonthCombo.SelectedIndex >= 0)
                month = ldMonthCombo.SelectedIndex + 1;
            month = Math.Max(1, Math.Min(12, month));
            SetLateDriversAnchorDate(new DateTime(year, month, 1), refresh);
        }

        private void SyncLateDriversPeriodPickersFromAnchor()
        {
            _ldSuppressDateChanged = true;
            _ldSuppressPeriodPickerChanged = true;
            try
            {
                if (ldDatePicker != null && !ldDatePicker.IsDisposed)
                {
                    try { ldDatePicker.Value = _ldDayDate; } catch { }
                }
                DateTime periodFocus = _ldAnchorDate.Date;
                if (ldMonthCombo != null && !ldMonthCombo.IsDisposed && ldMonthCombo.Items.Count >= 12)
                {
                    int mi = Math.Max(0, Math.Min(11, periodFocus.Month - 1));
                    if (ldMonthCombo.SelectedIndex != mi)
                        ldMonthCombo.SelectedIndex = mi;
                }
                if (ldYearCombo != null && !ldYearCombo.IsDisposed && ldYearCombo.Items.Count > 0)
                {
                    string ys = periodFocus.Year.ToString(CultureInfo.InvariantCulture);
                    int yi = ldYearCombo.Items.IndexOf(ys);
                    if (yi < 0)
                    {
                        ldYearCombo.Items.Add(ys);
                        yi = ldYearCombo.Items.IndexOf(ys);
                    }
                    if (yi >= 0 && ldYearCombo.SelectedIndex != yi)
                        ldYearCombo.SelectedIndex = yi;
                }
                UpdateLateDriversWeekRangeLabel();
            }
            finally
            {
                _ldSuppressDateChanged = false;
                _ldSuppressPeriodPickerChanged = false;
            }
        }

        private void UpdateLateDriversWeekRangeLabel()
        {
            if (ldWeekRangeLbl == null || ldWeekRangeLbl.IsDisposed)
                return;
            DateTime start = LateDriversWeekStartMonday(_ldAnchorDate);
            DateTime end = start.AddDays(6);
            if (start.Year == end.Year)
                ldWeekRangeLbl.Text = string.Format(
                    CultureInfo.CurrentCulture,
                    "{0:MMM d} – {1:MMM d, yyyy}",
                    start,
                    end);
            else
                ldWeekRangeLbl.Text = string.Format(
                    CultureInfo.CurrentCulture,
                    "{0:MMM d, yyyy} – {1:MMM d, yyyy}",
                    start,
                    end);
        }

        private void UpdateLateDriversPeriodPickerChrome()
        {
            string p = LateDriversSelectedMode();
            bool live = p == "live";
            bool day = p == "day";
            bool week = p == "week";
            bool month = p == "month";
            bool year = p == "year";

            if (ldDatePicker != null && !ldDatePicker.IsDisposed)
                ldDatePicker.Visible = day && !live;
            if (ldWeekStrip != null && !ldWeekStrip.IsDisposed)
                ldWeekStrip.Visible = week && !live;
            if (ldMonthCombo != null && !ldMonthCombo.IsDisposed)
                ldMonthCombo.Visible = month && !live;
            if (ldYearCombo != null && !ldYearCombo.IsDisposed)
                ldYearCombo.Visible = (month || year) && !live;

            if (ldDateHintLbl != null && !ldDateHintLbl.IsDisposed)
            {
                if (live) ldDateHintLbl.Text = "Today:";
                else if (day) ldDateHintLbl.Text = "Day:";
                else if (week) ldDateHintLbl.Text = "Week:";
                else if (month) ldDateHintLbl.Text = "Month:";
                else if (year) ldDateHintLbl.Text = "Year:";
                else ldDateHintLbl.Text = "Period:";
            }

            SetLateDriversPeriodControlsEnabled(!live);
            SyncLateDriversPeriodPickersFromAnchor();
            LayoutLateDriversToolbar();
        }

        private void LayoutLateDriversToolbar()
        {
            var box = ldToolbarInner;
            if (box == null || box.IsDisposed)
                return;
            int padL = box.Padding.Left;
            int padR = box.Padding.Right;
            int y = box.Padding.Top;
            int innerH = Math.Max(LateDriversToolbarInnerH, box.ClientSize.Height - box.Padding.Vertical);
            int x = padL;
            string mode = LateDriversSelectedMode();
            bool live = mode == "live";

            if (ldPeriodStrip != null && !ldPeriodStrip.IsDisposed)
            {
                ldPeriodStrip.SetBounds(x, y + Math.Max(0, (innerH - 30) / 2), 300, 30);
                x = ldPeriodStrip.Right + 12;
            }

            if (ldDateHintLbl != null && !ldDateHintLbl.IsDisposed)
            {
                int hintW = 52;
                switch (mode)
                {
                    case "live": hintW = 52; break;
                    case "week": hintW = 52; break;
                    case "month": hintW = 58; break;
                    case "year": hintW = 48; break;
                    default: hintW = 40; break;
                }
                ldDateHintLbl.SetBounds(x, y + Math.Max(0, (innerH - 20) / 2), hintW, 20);
                x = ldDateHintLbl.Right + 6;
            }

            if (!live && mode == "day" && ldDatePicker != null && !ldDatePicker.IsDisposed)
            {
                const int dateH = 34;
                ldDatePicker.SetBounds(x, y + Math.Max(0, (innerH - dateH) / 2), 214, dateH);
                x = ldDatePicker.Right + 12;
            }
            else if (!live && mode == "week" && ldWeekStrip != null && !ldWeekStrip.IsDisposed)
            {
                const int stripW = 280, stripH = 30;
                ldWeekStrip.SetBounds(x, y + Math.Max(0, (innerH - stripH) / 2), stripW, stripH);
                if (ldWeekPrevBtn != null) ldWeekPrevBtn.SetBounds(0, 0, 30, 30);
                if (ldWeekRangeLbl != null) ldWeekRangeLbl.SetBounds(34, 0, 212, 30);
                if (ldWeekNextBtn != null) ldWeekNextBtn.SetBounds(stripW - 30, 0, 30, 30);
                x = ldWeekStrip.Right + 12;
            }
            else if (!live && mode == "month")
            {
                if (ldMonthCombo != null && !ldMonthCombo.IsDisposed)
                {
                    const int h = 30;
                    ldMonthCombo.SetBounds(x, y + Math.Max(0, (innerH - h) / 2), 120, h);
                    x = ldMonthCombo.Right + 8;
                }
                if (ldYearCombo != null && !ldYearCombo.IsDisposed)
                {
                    const int h = 30;
                    ldYearCombo.SetBounds(x, y + Math.Max(0, (innerH - h) / 2), 88, h);
                    x = ldYearCombo.Right + 12;
                }
            }
            else if (!live && mode == "year" && ldYearCombo != null && !ldYearCombo.IsDisposed)
            {
                const int h = 30;
                ldYearCombo.SetBounds(x, y + Math.Max(0, (innerH - h) / 2), 88, h);
                x = ldYearCombo.Right + 12;
            }

            // Right cluster: Live switch + loady/timer + Refresh
            const int btnW = 88, btnH = 30;
            int right = box.ClientSize.Width - padR;
            if (ldRefreshBtn != null && !ldRefreshBtn.IsDisposed)
            {
                right -= btnW;
                ldRefreshBtn.SetBounds(right, y + Math.Max(0, (innerH - btnH) / 2), btnW, btnH);
                ldRefreshBtn.BringToFront();
                right -= 10;
            }

            EnsureLateDriversLiveChrome();
            if (live && _ldLiveChromeHost != null && !_ldLiveChromeHost.IsDisposed)
            {
                int chromeW = MeasureLateDriversLiveChromeWidth();
                int chromeH = TripScoutLiveCardHeight;
                right -= chromeW;
                _ldLiveChromeHost.Visible = true;
                _ldLiveChromeHost.SetBounds(
                    right,
                    y + Math.Max(0, (innerH - chromeH) / 2),
                    chromeW,
                    chromeH);
                LayoutLateDriversLiveChromeHost();
                _ldLiveChromeHost.BringToFront();
                right -= 10;
            }
            else if (_ldLiveChromeHost != null && !_ldLiveChromeHost.IsDisposed)
            {
                _ldLiveChromeHost.Visible = false;
            }

            if (ldLiveSwitch != null && !ldLiveSwitch.IsDisposed)
            {
                StyleLateDriversLiveSwitch();
                Size sw = ldLiveSwitch.GetPreferredSize(Size.Empty);
                right -= sw.Width;
                ldLiveSwitch.SetBounds(
                    Math.Max(x + 8, right),
                    y + Math.Max(0, (innerH - sw.Height) / 2),
                    sw.Width,
                    sw.Height);
                ldLiveSwitch.BringToFront();
                right -= 10;
            }

            if (ldColorsSwitch != null && !ldColorsSwitch.IsDisposed)
            {
                StyleLateDriversColorsSwitch();
                Size cw = ldColorsSwitch.GetPreferredSize(Size.Empty);
                right -= cw.Width;
                ldColorsSwitch.SetBounds(
                    Math.Max(x + 8, right),
                    y + Math.Max(0, (innerH - cw.Height) / 2),
                    cw.Width,
                    cw.Height);
                ldColorsSwitch.BringToFront();
            }
        }

        private void LayoutLateDriversSearchBox()
        {
            if (ldSearchInner == null || ldSearchInner.IsDisposed
                || ldSearchBox == null || ldSearchBox.IsDisposed)
                return;
            int padL = ldSearchInner.Padding.Left;
            int padR = ldSearchInner.Padding.Right;
            int padT = ldSearchInner.Padding.Top;
            int h = 30;
            int w = Math.Max(120, ldSearchInner.ClientSize.Width - padL - padR);
            ldSearchBox.UseToolbarSize = true;
            ldSearchBox.SetBounds(padL, padT, w, h);
        }

        private void LdSearchBox_TextChanged(object sender, EventArgs e)
        {
            if (_ldSuppressSearch || !_ldBuilt || IsDisposed)
                return;
            ApplyLateDriversSearchLive();
        }

        private void LdSearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                if (ldSearchBox != null && !string.IsNullOrEmpty(ldSearchBox.Text))
                {
                    _ldSuppressSearch = true;
                    try { ldSearchBox.Text = ""; }
                    finally { _ldSuppressSearch = false; }
                    ApplyLateDriversSearchLive();
                }
                return;
            }
            if (e.KeyCode != Keys.Enter)
                return;
            e.Handled = true;
            e.SuppressKeyPress = true;
            // Jump to owning driver / Other with the trip focused; clear search so that tile's
            // full list shows (Trip Scout keeps filtering — Habits needs the driver context).
            string jumpQ = ldSearchBox?.Text;
            if (string.IsNullOrWhiteSpace(jumpQ))
                return;
            _ldSuppressSearch = true;
            try { ldSearchBox.Text = ""; }
            finally { _ldSuppressSearch = false; }
            GoToLateDriversTripSearch(jumpQ);
        }

        private string LateDriversSearchQuery => (ldSearchBox?.Text ?? "").Trim();

        /// <summary>
        /// Trip Scout–style live filter: non-empty query shows matching trips across the day
        /// (driver sheets + Other). Empty query restores the selected tile view.
        /// </summary>
        private void ApplyLateDriversSearchLive()
        {
            if (!_ldBuilt || ldTripLv == null || ldTripLv.IsDisposed)
                return;
            BindLateDriversTripPane();
        }

        private SupeyListView CreateLateDriversListView(string name)
        {
            var lv = new SupeyListView
            {
                Name = name,
                View = View.Details,
                FullRowSelect = true,
                HideSelection = false,
                HeaderStyle = ColumnHeaderStyle.Clickable,
                BorderStyle = BorderStyle.None,
                MultiSelect = false,
                Dock = DockStyle.Fill,
            };
            try { lv.Font = ListViewOwnerDrawFonts.Cell; } catch { }
            lv.DrawColumnHeader += listView_DrawColumnHeader;
            lv.DrawItem += listView_DrawItem;
            lv.DrawSubItem += listView_DrawSubItem;
            ListViewSorter.Attach(lv);
            ListViewMinWidthEnforcer.Attach(lv);
            ListViewHeaderEmptyAreaPainter.Attach(lv);
            SupeyListViewHelpers.EnableDoubleBufferRecursively(lv);
            return lv;
        }

        private void LdPeriodButton_Click(object sender, EventArgs e)
        {
            if (LateDriversLiveEnabled)
                return;
            var btn = sender as SupeyMaterialButton;
            string period = (btn?.Tag as string ?? "day").Trim().ToLowerInvariant();
            if (period == _ldSelectedPeriod)
                return;
            string prev = _ldSelectedPeriod;
            _ldSelectedPeriod = period;
            // Seed Week/Month/Year from the day the user was viewing — do not overwrite _ldDayDate
            // (snapping Day→Month to the 1st used to make Day→Modivcare download Jan 1 / month start).
            if (string.Equals(prev, "day", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(period, "day", StringComparison.OrdinalIgnoreCase))
            {
                _ldAnchorDate = _ldDayDate.Date;
            }
            StyleLateDriversPeriodButtons();
            _ldSelectedDriver = null;
            UpdateLateDriversPeriodPickerChrome();
            UpdateLateDriversToolbarHints();
            _ = LateDriversRefreshAsync(force: true);
        }

        private void UpdateLateDriversToolbarHints()
        {
            string mode = LateDriversSelectedMode();
            DateTime anchor = LateDriversApiAnchorDate();

            if (ldDateHintLbl != null && !ldDateHintLbl.IsDisposed)
            {
                switch (mode)
                {
                    case "week":
                        ldDateHintLbl.Text = "Week:";
                        break;
                    case "month":
                        ldDateHintLbl.Text = "Month:";
                        break;
                    case "year":
                        ldDateHintLbl.Text = "Year:";
                        break;
                    default:
                        ldDateHintLbl.Text = "Day:";
                        break;
                }
            }

            if (ldRangeCaptionLbl != null && !ldRangeCaptionLbl.IsDisposed)
            {
                if (mode == "live")
                {
                    ldRangeCaptionLbl.Text = "Live = today only, refreshes every 60s"
                        + (string.IsNullOrWhiteSpace(_ldRangeLabel) ? "" : (" · " + _ldRangeLabel));
                }
                else if (!string.IsNullOrWhiteSpace(_ldRangeLabel))
                {
                    ldRangeCaptionLbl.Text = "Loaded: " + CultureInfo.CurrentCulture.TextInfo.ToTitleCase(mode)
                        + "  " + _ldRangeLabel
                        + "  — change period or Refresh to reload";
                }
                else
                {
                    switch (mode)
                    {
                        case "week":
                            {
                                var start = LateDriversWeekStartMonday(anchor);
                                var end = start.AddDays(6);
                                ldRangeCaptionLbl.Text = "Week "
                                    + start.ToString("MMM d", CultureInfo.CurrentCulture)
                                    + " – " + end.ToString("MMM d, yyyy", CultureInfo.CurrentCulture)
                                    + "  — Refresh to load";
                                break;
                            }
                        case "month":
                            ldRangeCaptionLbl.Text = "Month "
                                + anchor.ToString("MMMM yyyy", CultureInfo.CurrentCulture)
                                + "  — Refresh to load";
                            break;
                        case "year":
                            ldRangeCaptionLbl.Text = "Year " + anchor.Year + "  — Refresh to load";
                            break;
                        default:
                            ldRangeCaptionLbl.Text = "Day "
                                + anchor.ToString("ddd MMM d, yyyy", CultureInfo.CurrentCulture)
                                + "  — Refresh to load";
                            break;
                    }
                }
            }

            UpdateLateDriversTripCaption();
        }

        private void UpdateLateDriversTripCaption()
        {
            if (ldTripCaptionLbl == null || ldTripCaptionLbl.IsDisposed)
                return;
            if (string.IsNullOrEmpty(_ldSelectedDriver))
                ldTripCaptionLbl.Text = "Trip habits — all drivers";
            else if (LateDriversReservedSelected)
                ldTripCaptionLbl.Text = "Reserved — WellRyde trips with Reserved status";
            else
                ldTripCaptionLbl.Text = "Trip habits — " + _ldSelectedDriver;
        }

        private void StyleLateDriversPeriodButtons()
        {
            foreach (var btn in _ldPeriodButtons)
            {
                if (btn == null || btn.IsDisposed)
                    continue;
                string key = (btn.Tag as string ?? "").Trim().ToLowerInvariant();
                bool on = key == _ldSelectedPeriod;
                btn.Type = on
                    ? SupeyMaterialButton.MaterialButtonType.Contained
                    : SupeyMaterialButton.MaterialButtonType.Outlined;
                btn.UseAccentColor = on;
            }
        }

        private void ApplyLateDriversVisualTheme(bool layout = true)
        {
            if (tabPageLateDrivers == null)
                return;
            tabPageLateDrivers.BackColor = SupeyTheme.SurfaceBase;
            tabPageLateDrivers.ForeColor = SupeyTheme.TextPrimary;
            if (ldMainCard != null)
                StyleToolTabCard(ldMainCard, SupeyCard.Surface.Standard);
            if (ldStatusCard != null)
            {
                ldStatusCard.Visible = true;
                StyleToolTabStatusBar(ldStatusCard);
                var fill = EnsureToolTabStatusFill(ldStatusCard, "ldStatusFillPanel");
                fill.Resize -= LdStatusFill_Resize;
                fill.Resize += LdStatusFill_Resize;
                ldStatusCard.Resize -= LdStatusCard_Resize;
                ldStatusCard.Resize += LdStatusCard_Resize;
            }
            if (ldStatusLbl != null)
            {
                var fill = ldStatusCard?.Controls["ldStatusFillPanel"] as System.Windows.Forms.Panel;
                if (fill != null && !ReferenceEquals(ldStatusLbl.Parent, fill))
                    fill.Controls.Add(ldStatusLbl);
                ldStatusLbl.AutoSize = false;
                ldStatusLbl.ForeColor = SupeyTheme.TextSecondary;
                ldStatusLbl.Font = SupeyTheme.BodyFont;
                ldStatusLbl.TextAlign = ContentAlignment.MiddleLeft;
                ldStatusLbl.BackColor = SupeyTheme.SurfaceStatusBar;
                LayoutStatusLabelInCard(fill ?? ldStatusCard, ldStatusLbl);
            }
            if (ldDatePicker != null)
            {
                ldDatePicker.Anchor = AnchorStyles.Top | AnchorStyles.Left;
                ldDatePicker.SkinColor = SupeyTheme.SurfaceElevated;
                ldDatePicker.TextColor = SupeyTheme.TextPrimary;
                ldDatePicker.BorderColor = SupeyTheme.BorderSubtle;
                ldDatePicker.BorderSize = 1;
                ldDatePicker.Font = SupeyTheme.BodyFont;
            }
            if (ldWeekPrevBtn != null && !ldWeekPrevBtn.IsDisposed)
            {
                ldWeekPrevBtn.Type = SupeyMaterialButton.MaterialButtonType.Outlined;
                ldWeekPrevBtn.UseAccentColor = false;
            }
            if (ldWeekNextBtn != null && !ldWeekNextBtn.IsDisposed)
            {
                ldWeekNextBtn.Type = SupeyMaterialButton.MaterialButtonType.Outlined;
                ldWeekNextBtn.UseAccentColor = false;
            }
            StyleLateDriversCaption(ldWeekRangeLbl, secondary: false);
            if (ldMonthCombo != null && !ldMonthCombo.IsDisposed)
                ConfigureToolbarSupeyCombo(ldMonthCombo, 120);
            if (ldYearCombo != null && !ldYearCombo.IsDisposed)
                ConfigureToolbarSupeyCombo(ldYearCombo, 88);
            StyleLateDriversPeriodButtons();
            StyleLateDriversCaption(ldDateHintLbl, secondary: true);
            StyleLateDriversCaption(ldRangeCaptionLbl, secondary: true);
            StyleLateDriversCaption(ldDriverCaptionLbl, secondary: false);
            StyleLateDriversCaption(ldTripCaptionLbl, secondary: false);
            if (ldToolbar != null && !ldToolbar.IsDisposed)
                ldToolbar.BackColor = Color.Transparent;
            if (ldToolbarCard != null && !ldToolbarCard.IsDisposed)
                StyleToolTabCard(ldToolbarCard, SupeyCard.Surface.Elevated);
            if (ldToolbarInner != null && !ldToolbarInner.IsDisposed)
            {
                ldToolbarInner.Padding = new Padding(12, 8, 12, 8);
                ldToolbarInner.BackColor = Color.Transparent;
            }
            if (ldDriverStripHost != null && !ldDriverStripHost.IsDisposed)
                ldDriverStripHost.BackColor = Color.Transparent;
            if (ldDriverStripRow != null && !ldDriverStripRow.IsDisposed)
                ldDriverStripRow.BackColor = Color.Transparent;
            if (ldDriverStrip != null && !ldDriverStrip.IsDisposed)
                ldDriverStrip.BackColor = Color.Transparent;
            if (ldDriverPrevBtn != null && !ldDriverPrevBtn.IsDisposed)
                ldDriverPrevBtn.Type = SupeyMaterialButton.MaterialButtonType.Outlined;
            if (ldDriverNextBtn != null && !ldDriverNextBtn.IsDisposed)
                ldDriverNextBtn.Type = SupeyMaterialButton.MaterialButtonType.Outlined;
            if (ldTripHeader != null && !ldTripHeader.IsDisposed)
                ldTripHeader.BackColor = Color.Transparent;
            if (ldScorecardHost != null && !ldScorecardHost.IsDisposed)
                ldScorecardHost.BackColor = Color.Transparent;
            if (ldStageHost != null && !ldStageHost.IsDisposed)
                ldStageHost.BackColor = Color.Transparent;
            if (ldHeroCard != null && !ldHeroCard.IsDisposed)
            {
                StyleToolTabCard(ldHeroCard, SupeyCard.Surface.Elevated);
                // StyleToolTabCard zeros Padding; inset lives on ldHeroInner.
            }
            if (ldHeroInner != null && !ldHeroInner.IsDisposed)
            {
                ldHeroInner.Padding = new Padding(8, 6, 8, 6);
                ldHeroInner.BackColor = Color.Transparent;
            }
            if (ldOtpHost != null && !ldOtpHost.IsDisposed)
            {
                ldOtpHost.BackColor = Color.Transparent;
                ldOtpHost.Width = LateDriversOtpMeterW;
            }
            if (ldOtpMeter != null && !ldOtpMeter.IsDisposed)
            {
                ldOtpMeter.Invalidate();
            }
            if (ldRangeCaptionLbl != null && !ldRangeCaptionLbl.IsDisposed)
                ldRangeCaptionLbl.BackColor = Color.Transparent;
            if (ldRefreshBtn != null && !ldRefreshBtn.IsDisposed)
            {
                ldRefreshBtn.Type = SupeyMaterialButton.MaterialButtonType.Contained;
                ldRefreshBtn.UseAccentColor = true;
            }
            StyleLateDriversLiveChromeTheme();
            StyleLateDriversScoreFilters();
            StyleLateDriversDriverTiles();
            StyleLateDriversList(ldTripLv);
            ApplyLateDriversTripListColorMode();
            SupeyDarkScrollBars.Apply(tabPageLateDrivers);
            if (layout)
                LayoutLateDriversTabPanels();
        }

        private static void StyleLateDriversCaption(Label lbl, bool secondary = true)
        {
            if (lbl == null || lbl.IsDisposed)
                return;
            lbl.ForeColor = secondary ? SupeyTheme.TextSecondary : SupeyTheme.TextPrimary;
            lbl.BackColor = Color.Transparent;
            try
            {
                lbl.Font = secondary ? SupeyTheme.CaptionFont : SupeyTheme.SubHeaderFont;
            }
            catch { }
        }

        private static void StyleLateDriversList(SupeyListView lv)
        {
            if (lv == null || lv.IsDisposed)
                return;
            lv.BackColor = SupeyTheme.ListBody;
            lv.ForeColor = SupeyTheme.ListText;
            try { lv.Font = ListViewOwnerDrawFonts.Cell; } catch { }
            lv.BorderStyle = BorderStyle.None;
            lv.FullRowSelect = true;
            lv.HideSelection = false;
            lv.HeaderStyle = ColumnHeaderStyle.Clickable;
            lv.View = View.Details;
            lv.OwnerDraw = true;
        }

        private void LdStatusFill_Resize(object sender, EventArgs e)
        {
            var fill = sender as System.Windows.Forms.Panel;
            LayoutStatusLabelInCard(fill ?? ldStatusCard, ldStatusLbl);
        }

        private void LdStatusCard_Resize(object sender, EventArgs e)
        {
            var fill = ldStatusCard?.Controls["ldStatusFillPanel"] as System.Windows.Forms.Panel;
            LayoutStatusLabelInCard(fill ?? ldStatusCard, ldStatusLbl);
        }

        private void LayoutLateDriversTabPanels()
        {
            if (tabPageLateDrivers == null || ldMainCard == null || ldStatusCard == null)
                return;
            int tabW = tabPageLateDrivers.ClientSize.Width;
            int tabH = tabPageLateDrivers.ClientSize.Height;
            int statusTop = Math.Max(ToolTabInset, tabH - ToolTabInset - ToolTabStatusH);
            ldStatusCard.SetBounds(ToolTabInset, statusTop, Math.Max(200, tabW - (ToolTabInset * 2)), ToolTabStatusH);
            ldMainCard.SetBounds(ToolTabInset, ToolTabInset, Math.Max(200, tabW - (ToolTabInset * 2)),
                Math.Max(160, statusTop - ToolTabInset - ToolTabGap));
            LayoutStatusLabelInCard(ldStatusCard, ldStatusLbl);

            // Inner chrome: toolbar dock Top; stage sized explicitly below search/hero.
            LayoutLateDriversToolbar();
            LayoutLateDriversStageList();
        }

        /// <summary>
        /// Size the trip list under toolbar / search / bell / strip / scorecard
        /// (Trip Scout–style explicit bounds so the search bar never covers the list).
        /// </summary>
        private void LayoutLateDriversStageList()
        {
            if (ldMainCard == null || ldMainCard.IsDisposed
                || ldStageHost == null || ldStageHost.IsDisposed)
                return;

            int top = 0;
            top += LateDriversVisibleHostHeight(ldToolbar);
            top += LateDriversVisibleHostHeight(ldSearchHost);
            top += LateDriversVisibleHostHeight(ldBellAlertHost);
            top += LateDriversVisibleHostHeight(ldRangeCaptionLbl);
            top += LateDriversVisibleHostHeight(ldDriverStripHost);
            top += LateDriversVisibleHostHeight(ldHeroHost);

            int w = ldMainCard.ClientSize.Width;
            int h = Math.Max(80, ldMainCard.ClientSize.Height - top);
            ldStageHost.Dock = DockStyle.None;
            ldStageHost.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            ldStageHost.SetBounds(0, top, Math.Max(120, w), h);
            try { ldTripLv?.Invalidate(true); } catch { }
        }

        private static int LateDriversVisibleHostHeight(Control c)
        {
            if (c == null || c.IsDisposed || !c.Visible)
                return 0;
            return Math.Max(0, c.Height);
        }

        private string LateDriversSelectedMode()
        {
            if (LateDriversLiveEnabled)
                return "live";
            string s = (_ldSelectedPeriod ?? "day").Trim().ToLowerInvariant();
            if (s == "week" || s == "month" || s == "year")
                return s;
            return "day";
        }

        private string LateDriversSelectedServiceDateIso()
        {
            try
            {
                return LateDriversApiAnchorDate()
                    .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }
            catch { }
            return DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        private HiatmeAiSettings LateDriversAiSettings()
        {
            if (_supeyAiSettings == null)
                _supeyAiSettings = HiatmeAiSettings.Load();
            return _supeyAiSettings;
        }

        /// <summary>
        /// Push printed-schedule trip→driver ownership so the AI panel attributes
        /// late/early to the workbook owner after WellRyde reassigns.
        /// </summary>
        private async Task UploadLateDriversScheduleAssignAsync(
            HiatmeAiSettings settings,
            string serviceDateIso)
        {
            if (settings == null || string.IsNullOrWhiteSpace(settings.BaseUrl))
                return;
            if (string.IsNullOrWhiteSpace(serviceDateIso))
                return;

            await EnsureLateDriversScheduleCacheAsync(serviceDateIso.Trim(), forceReload: false)
                .ConfigureAwait(true);
            if (_ldScheduleCache == null)
                return;

            var rows = new List<HiatmeAiClient.ScheduleAssignTripRow>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void addTrip(string tripNo, string driver)
            {
                if (string.IsNullOrWhiteSpace(tripNo) || string.IsNullOrWhiteSpace(driver))
                    return;
                string d = driver.Trim();
                if (d.Equals("Reserves", StringComparison.OrdinalIgnoreCase)
                    || d.Equals("Reserve", StringComparison.OrdinalIgnoreCase)
                    || d.IndexOf("unassign", StringComparison.OrdinalIgnoreCase) >= 0)
                    return;
                string tn = tripNo.Trim();
                string key = ScheduleBuilderModivcareTripMatch.NormalizeTripNumber(tn);
                if (string.IsNullOrEmpty(key) || !seen.Add(key))
                    return;
                rows.Add(new HiatmeAiClient.ScheduleAssignTripRow
                {
                    TripNumber = tn,
                    Driver = d,
                });
            }

            if (_ldScheduleCache.DriverTrips != null)
            {
                foreach (var kv in _ldScheduleCache.DriverTrips)
                {
                    if (string.IsNullOrWhiteSpace(kv.Key) || kv.Value == null)
                        continue;
                    foreach (var t in kv.Value)
                    {
                        if (t == null) continue;
                        addTrip(t.TripNumber, kv.Key);
                    }
                }
            }
            if (_ldScheduleCache.DriverLines != null)
            {
                foreach (var kv in _ldScheduleCache.DriverLines)
                {
                    if (string.IsNullOrWhiteSpace(kv.Key) || kv.Value == null)
                        continue;
                    foreach (var line in kv.Value)
                    {
                        if (line?.Trip == null) continue;
                        addTrip(line.Trip.TripNumber, kv.Key);
                    }
                }
            }

            if (rows.Count == 0)
                return;

            try
            {
                await HiatmeAiClient.PutScheduleAssignDayAsync(
                        settings, serviceDateIso.Trim(), rows, source: "late-drivers-workbook")
                    .ConfigureAwait(true);
            }
            catch
            {
                // Non-fatal — habits still load; blame falls back to WR driver.
            }
        }

        private async Task<(bool Ok, string Message, bool Downloaded)> EnsureModivcareDaySnapshotAsync(
            HiatmeAiSettings settings,
            string serviceDateIso)
        {
            if (settings == null || string.IsNullOrWhiteSpace(settings.BaseUrl))
                return (false, "AI server not configured", false);
            if (string.IsNullOrWhiteSpace(serviceDateIso))
                return (false, "service date missing", false);

            DateTime day;
            if (!DateTime.TryParseExact(
                    serviceDateIso.Trim(),
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out day))
            {
                return (false, "bad service date", false);
            }

            var st = await HiatmeAiClient.GetModivcareDayStatusAsync(settings, serviceDateIso)
                .ConfigureAwait(true);
            if (st != null && st.Ok && st.Exists && st.TripCount > 0)
                return (true, "Modivcare schedule on file (" + st.TripCount + " trips)", false);

            // Avoid MCTripDownloader MessageBox ("Only current and next month…") for out-of-window days.
            if (!LateDriversModivcareDownloadWindowOk(day))
            {
                return (false,
                    "Need Modivcare schedule for " + serviceDateIso
                    + " (outside download window — pick a recent day or Refresh after it is on file)",
                    false);
            }

            SetLateDriversStatus("Status: Downloading Modivcare schedule for " + serviceDateIso + "…");
            if (!await EnsureModivcareSessionAsync().ConfigureAwait(true))
                return (false, "Need Modivcare login to load schedule for " + serviceDateIso, false);

            List<MCDownloadedTrip> downloaded = null;
            try
            {
                var dler = new MCTripDownloader { SuppressUiDialogs = true };
                downloaded = await dler.DownloadTripRecords(day, mcLoginHandler).ConfigureAwait(true);
                if (dler.InvalidDate)
                {
                    return (false,
                        "No Modivcare schedule for " + serviceDateIso + " (date not available in portal)",
                        false);
                }
            }
            catch (Exception ex)
            {
                return (false, "Modivcare download failed: " + ex.Message, false);
            }

            if (downloaded == null || downloaded.Count == 0)
                return (false, "No Modivcare trips for " + serviceDateIso + " (day off / empty)", false);

            var rows = HiatmeAiClient.ModivcareDayTripsFromDownloaded(downloaded);
            var put = await HiatmeAiClient.PutModivcareDayAsync(
                    settings, serviceDateIso, rows, source: "late-drivers")
                .ConfigureAwait(true);
            if (put == null || !put.Ok)
                return (false, "Failed to store Modivcare schedule: " + (put?.Error ?? "unknown"), false);

            return (true, "Stored Modivcare schedule (" + put.TripCount + " trips)", true);
        }

        private void EnsureLateDriversFirstUseLoad()
        {
            if (!_ldBuilt || _ldFirstLoadDone)
                return;
            _ldFirstLoadDone = true;
            // Defer until after the tab finishes laying out (strip width / list bounds).
            try
            {
                BeginInvoke(new Action(() =>
                {
                    _ = EnsureMarketScorecardCachedAsync();
                    _ = LateDriversRefreshAsync(force: true);
                }));
            }
            catch
            {
                _ = EnsureMarketScorecardCachedAsync();
                _ = LateDriversRefreshAsync(force: true);
            }
        }

        /// <summary>Warm Market OTP cache so the Habits On-time meter has a period baseline.</summary>
        private async Task EnsureMarketScorecardCachedAsync()
        {
            if (_mpLastScorecard != null && _mpLastScorecard.HasData)
            {
                PushMarketOtpToDriverHabits();
                return;
            }
            try
            {
                var settings = HiatmeAiSettings.Load();
                var card = await HiatmeAiClient.GetModivcareMarketScorecardAsync(settings)
                    .ConfigureAwait(true);
                if (card != null && card.Ok && card.HasData)
                {
                    _mpLastScorecard = card;
                    PushMarketOtpToDriverHabits();
                }
            }
            catch { /* optional baseline */ }
        }

        private async Task LateDriversRefreshAsync(bool force)
        {
            if (!_ldBuilt || _ldLoadInFlight || IsDisposed)
                return;
            _ldLoadInFlight = true;
            bool liveMode = LateDriversLiveEnabled;
            if (ldRefreshBtn != null && !ldRefreshBtn.IsDisposed)
                ldRefreshBtn.Enabled = false;
            try
            {
                if (liveMode)
                    StartLateDriversLiveScan();

                var settings = LateDriversAiSettings();
                if (settings == null || string.IsNullOrWhiteSpace(settings.BaseUrl))
                {
                    SetLateDriversStatus("Status: AI server not configured — set URL in AI Assistant.");
                    return;
                }

                string mode = LateDriversSelectedMode();
                string sd = LateDriversSelectedServiceDateIso();
                // Do not wipe the schedule workbook cache on Refresh — re-parsing the .xlsx is the
                // slow part. Cache invalidates when the service date / path / etag / file stamp change.
                // WR trips still re-fetch below via forceRefresh: true.

                if (mode == "live" || mode == "day")
                {
                    if (mode == "live")
                    {
                        // Live may pull Modivcare for today. Day mode must NEVER call the
                        // Modivcare portal — its HttpClient has no timeout and freezes the app
                        // on off-days / empty calendar dates.
                        var ensured = await EnsureModivcareDaySnapshotAsync(settings, sd)
                            .ConfigureAwait(true);
                        if (!ensured.Ok)
                        {
                            SetLateDriversStatus("Status: " + ensured.Message);
                            return;
                        }
                        await UploadLateDriversScheduleAssignAsync(settings, sd)
                            .ConfigureAwait(true);
                        // Upload already warmed the workbook; this is a cheap cache hit unless stale.
                        await EnsureLateDriversScheduleCacheAsync(sd, forceReload: false)
                            .ConfigureAwait(true);
                    }
                    else
                    {
                        // Day: use whatever is already on the AI server / Desktop only.
                        SetLateDriversStatus("Status: Loading " + sd + "…");
                        var st = await HiatmeAiClient.GetModivcareDayStatusAsync(settings, sd)
                            .ConfigureAwait(true);
                        if (st != null && st.Ok && st.Exists && st.TripCount > 0)
                        {
                            // Upload → EnsureScheduleCacheAsync (off-thread parse). No second parse.
                            await UploadLateDriversScheduleAssignAsync(settings, sd)
                                .ConfigureAwait(true);
                        }
                        else
                        {
                            // Off-day / no MC: Desktop or local cache only — don't wait on server download.
                            EnsureLateDriversScheduleCache(sd, forceReload: false);
                        }
                    }
                }

                if (!force && mode == "live" && !string.IsNullOrEmpty(_ldLastHash))
                {
                    var st = await HiatmeAiClient.GetLateDriversStatusAsync(settings, sd)
                        .ConfigureAwait(true);
                    var habitsPeek = await HiatmeAiClient.GetLateDriversHabitsAsync(
                            settings, "day", sd)
                        .ConfigureAwait(true);
                    string habitsHash = habitsPeek != null && habitsPeek.Ok
                        ? (habitsPeek.ContentHash ?? "")
                        : "";
                    if (st != null && st.Ok
                        && string.Equals(st.ContentHash, _ldLastHash, StringComparison.Ordinal)
                        && string.Equals(habitsHash, _ldHabitsHash ?? "", StringComparison.Ordinal))
                    {
                        // Habits unchanged — still refresh WR so status/actuals keep moving.
                        await EnsureLateDriversWrTripsAsync(settings, sd, forceRefresh: true)
                            .ConfigureAwait(true);
                        await RefreshLateDriversScheduleChangesAsync(settings, sd)
                            .ConfigureAwait(true);
                        await RefreshLateDriversBellAsync(settings, autoShowIfNew: true)
                            .ConfigureAwait(true);
                        if (!string.IsNullOrWhiteSpace(_ldSelectedDriver))
                            BindLateDriversTripPane();
                        string bellNote = LateDriversPeekBellStatusNote();
                        SetLateDriversStatus(
                            "Status: Live — " + st.EventCount + " habit signals today ("
                            + st.OpenCount + " still open) · unchanged · "
                            + DateTime.Now.ToString("h:mm:ss tt", CultureInfo.CurrentCulture)
                            + (string.IsNullOrEmpty(bellNote) ? "" : " · " + bellNote));
                        return;
                    }
                }

                SetLateDriversStatus("Status: Loading " + mode + "…");

                string habitPeriod = mode == "live" ? "day" : mode;
                var habitsTask = HiatmeAiClient.GetLateDriversHabitsAsync(settings, habitPeriod, sd);
                // WR actuals/status for every trip (habits alone only cover alert sides).
                var wrTask = EnsureLateDriversWrTripsAsync(settings, sd, forceRefresh: true);
                var changesTask = (mode == "live" || mode == "day")
                    ? RefreshLateDriversScheduleChangesAsync(settings, sd)
                    : Task.CompletedTask;
                var bellTask = (mode == "live" || mode == "day")
                    ? RefreshLateDriversBellAsync(settings, autoShowIfNew: mode == "live")
                    : Task.CompletedTask;

                if (mode == "live" || mode == "day")
                {
                    if (mode == "live")
                    {
                        var doc = await HiatmeAiClient.GetLateDriversLiveAsync(settings, sd)
                            .ConfigureAwait(true);
                        if (doc == null || !doc.Ok)
                        {
                            SetLateDriversStatus("Status: " + (doc?.Error ?? "live load failed"));
                            return;
                        }
                        if (!doc.ModivcareExists)
                        {
                            SetLateDriversStatus(
                                "Status: Need Modivcare schedule for " + sd
                                + " — scoring paused until schedule is stored.");
                            return;
                        }
                        var habits = await habitsTask.ConfigureAwait(true);
                        await wrTask.ConfigureAwait(true);
                        await changesTask.ConfigureAwait(true);
                        await bellTask.ConfigureAwait(true);
                        _ldDayPerf = doc.DayPerformance;
                        ApplyLateDriversEventPayload(
                            doc.Events ?? new List<HiatmeAiClient.LateDriversEventRow>(),
                            doc.ContentHash,
                            sd,
                            sd,
                            doc.ModivcareTripCount,
                            habits: habits);
                        string bellNote = LateDriversPeekBellStatusNote();
                        SetLateDriversStatus(
                            "Status: Live " + sd + " — "
                            + (_ldEventRows?.Count ?? 0) + " events · "
                            + DateTime.Now.ToString("h:mm:ss tt", CultureInfo.CurrentCulture)
                            + (string.IsNullOrEmpty(bellNote) ? "" : " · " + bellNote));
                    }
                    else
                    {
                        // Habits is the scorecard source of truth; day late-events are merged in.
                        var dayTask = HiatmeAiClient.GetLateDriversDayAsync(settings, sd);
                        var habits = await habitsTask.ConfigureAwait(true);
                        var doc = await dayTask.ConfigureAwait(true);
                        await wrTask.ConfigureAwait(true);
                        await changesTask.ConfigureAwait(true);
                        await bellTask.ConfigureAwait(true);

                        if ((doc == null || !doc.Ok) && (habits == null || !habits.Ok))
                        {
                            // Still paint an empty day rather than leaving the prior day on screen.
                            ApplyLateDriversEventPayload(
                                new List<HiatmeAiClient.LateDriversEventRow>(),
                                "",
                                sd,
                                sd,
                                0,
                                habits: null);
                            SetLateDriversStatus(
                                "Status: " + (doc?.Error ?? habits?.Error ?? "No data for " + sd));
                            return;
                        }

                        var lateEvents = (doc != null && doc.Ok)
                            ? (doc.Events ?? new List<HiatmeAiClient.LateDriversEventRow>())
                            : new List<HiatmeAiClient.LateDriversEventRow>();
                        int mcTrips = (doc != null && doc.Ok) ? doc.ModivcareTripCount : 0;
                        string hash = (habits != null && habits.Ok && !string.IsNullOrEmpty(habits.ContentHash))
                            ? habits.ContentHash
                            : (doc?.ContentHash ?? "");
                        if (doc != null && doc.Ok)
                            _ldDayPerf = doc.DayPerformance;
                        ApplyLateDriversEventPayload(lateEvents, hash, sd, sd, mcTrips, habits: habits);
                        if (doc != null && doc.Ok && !doc.ModivcareExists
                            && (habits == null || habits.EventCount <= 0)
                            && lateEvents.Count == 0)
                        {
                            SetLateDriversStatus(
                                "Status: No schedule/habits for " + sd + " (day off or not pulled)");
                        }
                    }
                }
                else
                {
                    await EnsureLateDriversScheduleCacheAsync(sd, forceReload: false)
                        .ConfigureAwait(true);
                    var doc = await HiatmeAiClient.GetLateDriversPeriodAsync(
                            settings, mode, sd)
                        .ConfigureAwait(true);
                    if (doc == null || !doc.Ok)
                    {
                        SetLateDriversStatus("Status: " + (doc?.Error ?? mode + " load failed"));
                        return;
                    }
                    _ldLastHash = doc.ContentHash ?? "";
                    _ldEventRows = FilterLateDriversCountingEvents(
                        doc.Events ?? new List<HiatmeAiClient.LateDriversEventRow>());
                    _ldDriverRows = doc.Drivers ?? new List<HiatmeAiClient.LateDriversDriverSummary>();
                    if (_ldDriverRows.Count == 0 && _ldEventRows.Count > 0)
                        _ldDriverRows = BuildLateDriversRollup(_ldEventRows);
                    else
                        SortLateDriversByMinutes(_ldDriverRows);
                    _ldRangeLabel = (doc.FromDate ?? "") + " → " + (doc.ToDate ?? "");
                    var habits = await habitsTask.ConfigureAwait(true);
                    await wrTask.ConfigureAwait(true);
                    MergeLateDriversHabits(habits);
                    _ldAlertSnapshotReady = true;
                    PresentLateDriversLoadedData();
                    int earlyN = (_ldDriverRows ?? new List<HiatmeAiClient.LateDriversDriverSummary>())
                        .Sum(d => d?.EarlyCount ?? 0);
                    int unfinN = (_ldDriverRows ?? new List<HiatmeAiClient.LateDriversDriverSummary>())
                        .Sum(d => d?.Unfinished ?? 0);
                    SetLateDriversStatus(
                        "Status: " + CultureInfo.CurrentCulture.TextInfo.ToTitleCase(mode)
                        + " " + _ldRangeLabel
                        + " — " + (_ldDriverRows?.Count ?? 0) + " drivers · "
                        + (_ldEventRows?.Count ?? 0) + " events"
                        + (earlyN > 0 ? " · " + earlyN + " early" : "")
                        + (unfinN > 0 ? " · " + unfinN + " unfinished" : ""));
                }
            }
            catch (Exception ex)
            {
                SetLateDriversStatus("Status: " + ex.Message);
            }
            finally
            {
                _ldLoadInFlight = false;
                if (liveMode)
                {
                    await StopLateDriversLiveScanAfterMinimumAsync().ConfigureAwait(true);
                    LateDriversScheduleNextLivePoll();
                }
                if (ldRefreshBtn != null && !ldRefreshBtn.IsDisposed)
                    ldRefreshBtn.Enabled = true;
                LateDriversUpdateLivePollCountdownLabel();
            }
        }

        /// <summary>
        /// B/C DO (early or late) does not count — return ride, no appointment deadline.
        /// Only habits that count against us are shown in Driver Habits.
        /// </summary>
        private static bool LateDriversEventCountsAgainstUs(HiatmeAiClient.LateDriversEventRow e)
        {
            if (e == null) return false;
            string habit = (e.Habit ?? e.Kind ?? "").Trim().ToLowerInvariant();
            // Live late rows often only have Side until Habit is filled in below.
            if (string.IsNullOrEmpty(habit)
                && string.Equals(e.Side, "do", StringComparison.OrdinalIgnoreCase))
                habit = "late_do";
            if ((habit == "early_do" || habit == "late_do")
                && !McTripTimingRules.IsALeg(e.TripNo))
                return false;
            return true;
        }

        private static List<HiatmeAiClient.LateDriversEventRow> FilterLateDriversCountingEvents(
            List<HiatmeAiClient.LateDriversEventRow> events)
        {
            return (events ?? new List<HiatmeAiClient.LateDriversEventRow>())
                .Where(LateDriversEventCountsAgainstUs)
                .ToList();
        }

        private void ApplyLateDriversEventPayload(
            List<HiatmeAiClient.LateDriversEventRow> events,
            string contentHash,
            string fromDate,
            string toDate,
            int mcTripCount,
            HiatmeAiClient.LateDriversHabitsDoc habits = null)
        {
            _ldLastHash = contentHash ?? "";
            _ldMcTripCount = Math.Max(0, mcTripCount);
            _ldEventRows = FilterLateDriversCountingEvents(events);
            foreach (var e in _ldEventRows)
            {
                if (e == null || !string.IsNullOrWhiteSpace(e.Habit))
                    continue;
                e.Habit = string.Equals(e.Side, "do", StringComparison.OrdinalIgnoreCase)
                    ? "late_do"
                    : "late_pu";
            }
            // Drop B/C late_do again after habit labels are assigned from Side.
            _ldEventRows = FilterLateDriversCountingEvents(_ldEventRows);
            _ldDriverRows = BuildLateDriversRollup(_ldEventRows);
            _ldRangeLabel = fromDate == toDate ? fromDate : (fromDate + " → " + toDate);
            MergeLateDriversHabits(habits);
            _ldAlertSnapshotReady = true;
            PresentLateDriversLoadedData();
            int openN = (_ldEventRows ?? new List<HiatmeAiClient.LateDriversEventRow>())
                .Count(e => e != null && e.Open);
            int earlyN = (_ldDriverRows ?? new List<HiatmeAiClient.LateDriversDriverSummary>())
                .Sum(d => d?.EarlyCount ?? 0);
            int unfinOpen = (_ldDriverRows ?? new List<HiatmeAiClient.LateDriversDriverSummary>())
                .Sum(d => d?.UnfinishedOpen ?? 0);
            SetLateDriversStatus(
                "Status: Day " + fromDate + " — "
                + (_ldEventRows?.Count ?? 0) + " events ("
                + openN + " open"
                + (earlyN > 0 ? ", " + earlyN + " early" : "")
                + (unfinOpen > 0 ? ", " + unfinOpen + " unfinished" : "")
                + ") · "
                + (_ldDriverRows?.Count ?? 0) + " drivers"
                + (mcTripCount > 0 ? " · MC " + mcTripCount : ""));
        }

        private void PresentLateDriversLoadedData()
        {
            // Day/Live: pull schedule roster so clean drivers still appear in the strip.
            MergeLateDriversScheduleRosterIntoDriverRows();
            // Always show workbook tab names; fold WR spellings onto those tiles.
            RemapLateDriversRowsToScheduleNames();
            RefreshLateDriversOffScheduleCache();
            LayoutLateDriversTabPanels();
            LayoutLateDriversDriverStripRow();
            BindLateDriversDriverStrip();
            RefreshLateDriversScorecard();
            RefreshLateDriversOtpMeter();
            BindLateDriversTripPane();
            UpdateLateDriversToolbarHints();
            // Always re-evaluate blink/chirp after habit payload changes (strip render can early-return).
            SyncLateDriversDriverAlertBlink();
            try
            {
                ldTripLv?.Invalidate(true);
                ldDriverStrip?.Invalidate(true);
                ldScorecardHost?.Invalidate(true);
                ldOtpMeter?.Invalidate();
            }
            catch { }
        }

        /// <summary>
        /// Add every workbook driver to the strip (0 habit counts) so Day/Live shows the full roster.
        /// </summary>
        private void MergeLateDriversScheduleRosterIntoDriverRows()
        {
            string mode = LateDriversSelectedMode();
            if (mode != "day" && mode != "live")
                return;

            string sd = LateDriversSelectedServiceDateIso();
            EnsureLateDriversScheduleCache(sd, forceReload: false);
            if (_ldScheduleCache == null)
                return;

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void addKey(string k)
            {
                if (string.IsNullOrWhiteSpace(k)) return;
                string n = k.Trim();
                if (n.Equals("Reserves", StringComparison.OrdinalIgnoreCase))
                    return;
                if (n.StartsWith("Reserve", StringComparison.OrdinalIgnoreCase)
                    && n.IndexOf(" ", StringComparison.Ordinal) < 0)
                    return;
                names.Add(n);
            }

            if (_ldScheduleCache.DriverTrips != null)
            {
                foreach (var k in _ldScheduleCache.DriverTrips.Keys)
                    addKey(k);
            }
            if (_ldScheduleCache.DriverLines != null)
            {
                foreach (var k in _ldScheduleCache.DriverLines.Keys)
                    addKey(k);
            }
            if (names.Count == 0)
                return;

            if (_ldDriverRows == null)
                _ldDriverRows = new List<HiatmeAiClient.LateDriversDriverSummary>();

            // Habits vs workbook often disagree on spelling ("Jeffrey Brown" vs "Jeffrey B").
            // Skip schedule names that already fuzzy-match a habits tile.
            foreach (string scheduleName in names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
            {
                bool exists = _ldDriverRows.Any(d =>
                    d != null && LateDriversDriverNamesMatch(d.Driver, scheduleName));
                if (exists)
                    continue;
                _ldDriverRows.Add(new HiatmeAiClient.LateDriversDriverSummary
                {
                    Driver = scheduleName,
                    Trips = new List<HiatmeAiClient.LateDriversEventRow>(),
                });
            }

            CollapseLateDriversDuplicateDriverRows();
            SortLateDriversByMinutes(_ldDriverRows);
        }

        /// <summary>
        /// Day performance from panel (ModivCare + WellRyde scored trips).
        /// Big number is clean/scored; ring fills with on-time ratio and flashes on drops.
        /// </summary>
        private void RefreshLateDriversOtpMeter()
        {
            if (ldOtpMeter == null || ldOtpMeter.IsDisposed)
                return;

            var perf = _ldDayPerf;
            if (perf == null || perf.Scored <= 0)
            {
                int pending = perf?.Pending ?? 0;
                ldOtpMeter.SetValue(
                    null,
                    "—",
                    pending > 0
                        ? pending + " trips waiting on WR"
                        : "waiting for WR trips");
                return;
            }

            double pct = perf.Pct ?? (perf.OnTime * 100.0 / Math.Max(1, perf.Scored));
            string value = pct.ToString("0.0", CultureInfo.InvariantCulture) + "%";
            string detail = perf.OnTime.ToString(CultureInfo.InvariantCulture)
                + "/" + perf.Scored.ToString(CultureInfo.InvariantCulture)
                + " clean"
                + (perf.Late > 0 ? " · " + perf.Late + " late" : "")
                + (perf.Pending > 0 ? " · " + perf.Pending + " left" : "");
            ldOtpMeter.SetValue(Math.Min(100, Math.Max(0, pct)), value, detail);
        }

        /// <summary>Market scorecard refresh no longer drives the day meter — keep hook for theme/load.</summary>
        private void PushMarketOtpToDriverHabits()
        {
            // Intentionally empty: Habits meter is day performance from live/day APIs.
        }

        private void MergeLateDriversHabits(HiatmeAiClient.LateDriversHabitsDoc habits)
        {
            if (habits == null || !habits.Ok)
            {
                _ldHabitsHash = "";
                return;
            }
            _ldHabitsHash = habits.ContentHash ?? "";

            var habitDrivers = habits.Drivers
                ?? new List<HiatmeAiClient.LateDriversHabitDriverSummary>();
            var byName = new Dictionary<string, HiatmeAiClient.LateDriversDriverSummary>(
                StringComparer.OrdinalIgnoreCase);
            foreach (var d in _ldDriverRows ?? new List<HiatmeAiClient.LateDriversDriverSummary>())
            {
                if (d == null || string.IsNullOrWhiteSpace(d.Driver))
                    continue;
                byName[d.Driver.Trim()] = d;
            }

            foreach (var h in habitDrivers)
            {
                if (h == null || string.IsNullOrWhiteSpace(h.Driver))
                    continue;
                string key = h.Driver.Trim();
                if (!byName.TryGetValue(key, out var row))
                {
                    row = new HiatmeAiClient.LateDriversDriverSummary
                    {
                        Driver = key,
                        Trips = new List<HiatmeAiClient.LateDriversEventRow>(),
                    };
                    byName[key] = row;
                }
                row.EarlyPu = h.EarlyPu;
                row.EarlyDo = h.EarlyDo;
                row.EarlyCount = h.EarlyCount > 0 ? h.EarlyCount : (h.EarlyPu + h.EarlyDo);
                row.Unfinished = h.Unfinished;
                row.UnfinishedOpen = h.UnfinishedOpen;
                // Billed-skip counts are included in h.Unfinished; keep open separate.
                // Prefer habit late minutes when present (includes closed lates from habits store)
                if (h.LateMinutes > row.TotalMinutes)
                    row.TotalMinutes = h.LateMinutes;
                if (h.LateCount > row.LateCount)
                {
                    row.LateCount = h.LateCount;
                    row.PuCount = Math.Max(row.PuCount, h.LatePu);
                    row.DoCount = Math.Max(row.DoCount, h.LateDo);
                }
            }

            var lateKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in _ldEventRows ?? new List<HiatmeAiClient.LateDriversEventRow>())
            {
                if (e == null) continue;
                lateKeys.Add(LateDriversTripKey(e.ServiceDate, e.TripNo, e.Side));
            }

            var merged = new List<HiatmeAiClient.LateDriversEventRow>(
                _ldEventRows ?? new List<HiatmeAiClient.LateDriversEventRow>());
            foreach (var he in habits.Events ?? new List<HiatmeAiClient.LateDriversHabitEventRow>())
            {
                if (he == null) continue;
                string kind = (he.Kind ?? he.Habit ?? "").Trim().ToLowerInvariant();
                if (string.IsNullOrEmpty(kind))
                    continue;
                // B/C DO (early or late) does not count against us — skip merge.
                if ((kind == "early_do" || kind == "late_do")
                    && !McTripTimingRules.IsALeg(he.TripNo))
                    continue;
                string side = (he.Side ?? "").Trim().ToLowerInvariant();
                if (kind == "late_pu" || kind == "late_do")
                {
                    if (string.IsNullOrEmpty(side))
                        side = kind.EndsWith("do") ? "do" : "pu";
                    if (lateKeys.Contains(LateDriversTripKey(he.ServiceDate, he.TripNo, side)))
                        continue;
                }
                merged.Add(HabitEventToLateRow(he, kind, side));
            }
            _ldEventRows = FilterLateDriversCountingEvents(merged);

            foreach (var row in byName.Values)
            {
                string driver = row.Driver ?? "";
                row.Trips = _ldEventRows
                    .Where(e =>
                    {
                        if (e == null) return false;
                        string ed = string.IsNullOrWhiteSpace(e.Driver)
                            ? "(unassigned)"
                            : e.Driver.Trim();
                        return LateDriversDriverNamesMatch(ed, driver);
                    })
                    .ToList();
                row.OpenCount = row.Trips.Count(t => t != null && t.Open);
                // Recompute late/early from filtered trips so B/C DO never inflates the scorecard.
                int pu = 0, doN = 0, earlyPu = 0, earlyDo = 0;
                double lateMins = 0;
                foreach (var t in row.Trips)
                {
                    if (t == null) continue;
                    string k = (t.Habit ?? t.Kind ?? "").Trim().ToLowerInvariant();
                    string side = (t.Side ?? "").Trim().ToLowerInvariant();
                    bool latePu = k == "late_pu" || (string.IsNullOrEmpty(k) && side == "pu");
                    bool lateDo = k == "late_do" || (string.IsNullOrEmpty(k) && side == "do");
                    if (latePu) { pu++; lateMins += LateDriversDisplayHabitMinutes(t); }
                    else if (lateDo) { doN++; lateMins += LateDriversDisplayHabitMinutes(t); }
                    else if (k == "early_pu") earlyPu++;
                    else if (k == "early_do") earlyDo++;
                }
                row.PuCount = pu;
                row.DoCount = doN;
                row.LateCount = pu + doN;
                row.TotalMinutes = lateMins;
                row.EarlyPu = earlyPu;
                row.EarlyDo = earlyDo;
                row.EarlyCount = earlyPu + earlyDo;
            }

            _ldDriverRows = byName.Values.ToList();
            CollapseLateDriversDuplicateDriverRows();
            SortLateDriversByMinutes(_ldDriverRows);
        }

        private static string LateDriversTripKey(string serviceDate, string tripNo, string side)
        {
            return (serviceDate ?? "").Trim() + "|"
                + (tripNo ?? "").Trim() + "|"
                + (side ?? "").Trim().ToLowerInvariant();
        }

        private static HiatmeAiClient.LateDriversEventRow HabitEventToLateRow(
            HiatmeAiClient.LateDriversHabitEventRow he,
            string kind,
            string side)
        {
            if (string.IsNullOrEmpty(side))
            {
                if (kind.Contains("do")) side = "do";
                else if (kind.Contains("pu")) side = "pu";
                else side = "ticket";
            }
            return new HiatmeAiClient.LateDriversEventRow
            {
                EventId = he.EventId,
                ServiceDate = he.ServiceDate,
                TripNo = he.TripNo,
                Driver = he.Driver,
                Client = he.Client,
                Side = side,
                SchedIso = he.SchedIso,
                ActualIso = he.ActualIso,
                MinutesLate = he.Minutes,
                Open = he.Open,
                StatusLatest = he.Status,
                DetectedAt = he.DetectedAt,
                ResolvedAt = he.ResolvedAt,
                Habit = kind,
                Kind = kind,
            };
        }

        private static List<HiatmeAiClient.LateDriversDriverSummary> BuildLateDriversRollup(
            List<HiatmeAiClient.LateDriversEventRow> events)
        {
            var map = new Dictionary<string, HiatmeAiClient.LateDriversDriverSummary>(
                StringComparer.OrdinalIgnoreCase);
            if (events == null)
                return new List<HiatmeAiClient.LateDriversDriverSummary>();

            foreach (var e in events)
            {
                if (e == null)
                    continue;
                string key = string.IsNullOrWhiteSpace(e.Driver) ? "(unassigned)" : e.Driver.Trim();
                if (!map.TryGetValue(key, out var row))
                {
                    row = new HiatmeAiClient.LateDriversDriverSummary
                    {
                        Driver = key,
                        Trips = new List<HiatmeAiClient.LateDriversEventRow>(),
                    };
                    map[key] = row;
                }
                row.LateCount++;
                if (string.Equals(e.Side, "do", StringComparison.OrdinalIgnoreCase))
                    row.DoCount++;
                else
                    row.PuCount++;
                if (e.Open)
                    row.OpenCount++;
                row.TotalMinutes += LateDriversDisplayHabitMinutes(e);
                row.Trips.Add(e);
            }

            var list = map.Values.ToList();
            SortLateDriversByMinutes(list);
            return list;
        }

        private static void SortLateDriversByMinutes(List<HiatmeAiClient.LateDriversDriverSummary> list)
        {
            if (list == null || list.Count < 2)
                return;
            // Worst-first: unfinished → late count → early count → late minutes (tiebreak).
            // Minutes alone used to bury multi-hit drivers under one long late.
            list.Sort((a, b) =>
            {
                if (a == null && b == null) return 0;
                if (a == null) return 1;
                if (b == null) return -1;
                int cmp = b.UnfinishedOpen.CompareTo(a.UnfinishedOpen);
                if (cmp != 0) return cmp;
                cmp = b.LateCount.CompareTo(a.LateCount);
                if (cmp != 0) return cmp;
                cmp = b.EarlyCount.CompareTo(a.EarlyCount);
                if (cmp != 0) return cmp;
                return b.TotalMinutes.CompareTo(a.TotalMinutes);
            });
        }

        private void BindLateDriversDriverStrip()
        {
            if (ldDriverStrip == null || ldDriverStrip.IsDisposed)
                return;

            // Full roster always — habit score tiles highlight trips, not drivers.
            var rows = (_ldDriverRows ?? new List<HiatmeAiClient.LateDriversDriverSummary>())
                .Where(d => d != null)
                .ToList();
            string keep = _ldSelectedDriver;
            if (!string.IsNullOrEmpty(keep)
                && !LateDriversIsOtherSelected(keep)
                && !rows.Any(d => string.Equals(d.Driver, keep, StringComparison.OrdinalIgnoreCase)))
                keep = null;

            _ldStripDrivers = rows;
            _ldSelectedDriver = keep;
            _ldDriverScrollOffset = 0;

            // If a specific driver is selected, page so that driver is in the visible window.
            if (!string.IsNullOrEmpty(keep))
            {
                int idx = _ldStripDrivers.FindIndex(d =>
                    d != null && string.Equals(d.Driver, keep, StringComparison.OrdinalIgnoreCase));
                if (idx >= 0)
                {
                    int page = LateDriversDriverStripPageSize();
                    if (page > 0 && idx >= page)
                        _ldDriverScrollOffset = idx - page + 1;
                }
            }

            RenderLateDriversDriverStripPage();
        }

        private void LayoutLateDriversDriverStripRow()
        {
            if (ldDriverStripRow == null || ldDriverStripRow.IsDisposed)
                return;
            int w = ldDriverStripRow.ClientSize.Width;
            int h = ldDriverStripRow.ClientSize.Height;
            bool showNav = (ldDriverPrevBtn != null && !ldDriverPrevBtn.IsDisposed && ldDriverPrevBtn.Visible)
                || (ldDriverNextBtn != null && !ldDriverNextBtn.IsDisposed && ldDriverNextBtn.Visible);
            int nav = showNav ? LateDriversDriverNavBtnW : 0;
            int gap = showNav ? 4 : 0;
            int tileTop = Math.Max(0, (h - LateDriversDriverTileH) / 2);
            if (ldDriverPrevBtn != null && !ldDriverPrevBtn.IsDisposed)
                ldDriverPrevBtn.SetBounds(0, tileTop, nav, LateDriversDriverTileH);
            if (ldDriverNextBtn != null && !ldDriverNextBtn.IsDisposed)
                ldDriverNextBtn.SetBounds(Math.Max(0, w - nav), tileTop, nav, LateDriversDriverTileH);
            if (ldDriverStrip != null && !ldDriverStrip.IsDisposed)
            {
                int left = nav + gap;
                int stripW = Math.Max(0, w - left - nav - gap);
                ldDriverStrip.SetBounds(left, 0, stripW, h);
            }
        }

        private int LateDriversDriverStripPageSize()
        {
            if (ldDriverStrip == null || ldDriverStrip.IsDisposed)
                return 1;
            int slot = LateDriversDriverTileW + LateDriversDriverTileGap;
            int inner = Math.Max(0, ldDriverStrip.ClientSize.Width - ldDriverStrip.Padding.Horizontal);
            // Reserve slots for pinned "All drivers" + "Reserved" tiles.
            int totalSlots = Math.Max(1, inner / Math.Max(1, slot));
            return Math.Max(1, totalSlots - 2);
        }

        private void LateDriversShiftDriverStrip(int delta)
        {
            if (delta == 0)
                return;
            int page = LateDriversDriverStripPageSize();
            int maxOff = Math.Max(0, (_ldStripDrivers?.Count ?? 0) - page);
            int next = Math.Max(0, Math.Min(maxOff, _ldDriverScrollOffset + delta));
            if (next == _ldDriverScrollOffset)
                return;
            _ldDriverScrollOffset = next;
            RenderLateDriversDriverStripPage();
        }

        private void RenderLateDriversDriverStripPage()
        {
            if (ldDriverStrip == null || ldDriverStrip.IsDisposed || _ldDriverStripRendering)
                return;
            _ldDriverStripRendering = true;
            ldDriverStrip.SuspendLayout();
            try
            {
                var rows = _ldStripDrivers ?? new List<HiatmeAiClient.LateDriversDriverSummary>();
                int slot = LateDriversDriverTileW + LateDriversDriverTileGap;
                int rowW = ldDriverStripRow != null && !ldDriverStripRow.IsDisposed
                    ? ldDriverStripRow.ClientSize.Width
                    : 0;
                // Decide nav from full row width first, then lay out so strip width is correct.
                int slotsNoNav = Math.Max(1, rowW / Math.Max(1, slot));
                bool canPage = rows.Count > Math.Max(1, slotsNoNav - 2);
                if (ldDriverPrevBtn != null && !ldDriverPrevBtn.IsDisposed)
                    ldDriverPrevBtn.Visible = canPage;
                if (ldDriverNextBtn != null && !ldDriverNextBtn.IsDisposed)
                    ldDriverNextBtn.Visible = canPage;
                LayoutLateDriversDriverStripRow();

                int page = LateDriversDriverStripPageSize();
                int maxOff = Math.Max(0, rows.Count - page);
                if (_ldDriverScrollOffset > maxOff)
                    _ldDriverScrollOffset = maxOff;
                if (_ldDriverScrollOffset < 0)
                    _ldDriverScrollOffset = 0;
                if (ldDriverPrevBtn != null && !ldDriverPrevBtn.IsDisposed)
                    ldDriverPrevBtn.Enabled = canPage && _ldDriverScrollOffset > 0;
                if (ldDriverNextBtn != null && !ldDriverNextBtn.IsDisposed)
                    ldDriverNextBtn.Enabled = canPage && _ldDriverScrollOffset < maxOff;

                ldDriverStrip.Controls.Clear();
                _ldDriverTiles.Clear();

                int allLates = rows.Sum(d => d.LateCount);
                int allEarly = rows.Sum(d => d.EarlyCount);
                int allUnfin = rows.Sum(d => d.Unfinished);
                double allMins = rows.Sum(d => d.TotalMinutes);
                var allTile = CreateLateDriversDriverTile(
                    "All drivers",
                    allLates,
                    allEarly,
                    allUnfin,
                    allMins,
                    summary: null);
                ldDriverStrip.Controls.Add(allTile);
                _ldDriverTiles.Add(allTile);

                // Pinned: WellRyde trips currently in Reserved status.
                // Use cached count only — never rebuild/parse here (strip Resize would hang).
                int reservedN = Math.Max(0, _ldOffScheduleCount);
                var reservedSummary = new HiatmeAiClient.LateDriversDriverSummary
                {
                    Driver = LateDriversReservedKey,
                    LateCount = reservedN,
                };
                var reservedTile = CreateLateDriversDriverTile(
                    "Reserved",
                    reservedN,
                    0,
                    0,
                    0,
                    summary: reservedSummary);
                var reservedStats = reservedTile.Controls["ldTileStats"] as Label;
                if (reservedStats != null)
                    reservedStats.Text = reservedN == 1 ? "1 reserved" : (reservedN + " reserved");
                var reservedMins = reservedTile.Controls["ldTileMins"] as Label;
                if (reservedMins != null)
                    reservedMins.Text = "WR status · Reserved";
                ldDriverStrip.Controls.Add(reservedTile);
                _ldDriverTiles.Add(reservedTile);

                int end = Math.Min(rows.Count, _ldDriverScrollOffset + page);
                for (int i = _ldDriverScrollOffset; i < end; i++)
                {
                    var d = rows[i];
                    if (d == null)
                        continue;
                    var tile = CreateLateDriversDriverTile(
                        d.Driver ?? "",
                        d.LateCount,
                        d.EarlyCount,
                        d.Unfinished,
                        d.TotalMinutes,
                        summary: d);
                    ldDriverStrip.Controls.Add(tile);
                    _ldDriverTiles.Add(tile);
                }

                StyleLateDriversDriverTiles();
                SyncLateDriversDriverAlertBlink();
            }
            finally
            {
                ldDriverStrip.ResumeLayout(true);
                _ldDriverStripRendering = false;
            }
        }

        private static bool LateDriversIsTimingHabitKey(string hk)
        {
            return hk == "late_pu" || hk == "late_do" || hk == "early_pu" || hk == "early_do";
        }

        /// <summary>
        /// True when this driver should flash: late just opened, or early PU/DO
        /// just landed — both only for <see cref="LateDriversAlertWindowSeconds"/>.
        /// </summary>
        private static double LateDriversUnixNow()
        {
            return (DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc))
                .TotalSeconds;
        }

        private static bool LateDriversTsStillHot(double? unixTs)
        {
            if (!unixTs.HasValue || unixTs.Value <= 0)
                return false;
            double age = LateDriversUnixNow() - unixTs.Value;
            return age >= 0 && age <= LateDriversAlertWindowSeconds;
        }

        /// <summary>
        /// When the early PU/DO actually happened. Prefer ActualIso — habits rebuild
        /// used to rewrite detected_at every poll.
        /// </summary>
        private static double? LateDriversEarlyEventTs(HiatmeAiClient.LateDriversEventRow e)
        {
            if (e == null) return null;
            if (!string.IsNullOrWhiteSpace(e.ActualIso)
                && (DateTime.TryParse(
                        e.ActualIso.Trim(),
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind,
                        out var dt)
                    || DateTime.TryParse(e.ActualIso.Trim(), out dt)))
            {
                if (dt.Kind == DateTimeKind.Unspecified)
                    dt = DateTime.SpecifyKind(dt, DateTimeKind.Local);
                return (dt.ToUniversalTime()
                    - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
            }
            if (e.DetectedAt.HasValue && e.DetectedAt.Value > 0)
                return e.DetectedAt.Value;
            if (e.ResolvedAt.HasValue && e.ResolvedAt.Value > 0)
                return e.ResolvedAt.Value;
            return null;
        }

        private static bool LateDriversEventIsServiceToday(HiatmeAiClient.LateDriversEventRow e)
        {
            string sd = (e?.ServiceDate ?? "").Trim();
            if (string.IsNullOrEmpty(sd))
                return false;
            return string.Equals(
                sd,
                DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                StringComparison.Ordinal);
        }

        /// <summary>
        /// Pure eligibility for blink/chirp consideration (today + timing habit).
        /// Open lates qualify regardless of detected_at age; closed lates qualify briefly
        /// by wall-clock actual/resolve so a quick complete still flashes once.
        /// </summary>
        private static bool LateDriversEventIsTimingAlertCandidate(HiatmeAiClient.LateDriversEventRow e)
        {
            if (e == null || e.Excluded)
                return false;
            if (!LateDriversEventIsServiceToday(e))
                return false;
            string hk = HabitKeyOf(e);
            if (!LateDriversIsTimingHabitKey(hk))
                return false;
            if (hk.StartsWith("early", StringComparison.Ordinal))
                return true;
            if (e.Open)
                return true;
            // Closed late: brief post-complete flash by panel timestamps.
            if (!string.IsNullOrWhiteSpace(e.ActualIso)
                && LateDriversTsStillHot(LateDriversEarlyEventTs(e)))
                return true;
            return LateDriversTsStillHot(e.ResolvedAt);
        }

        private static string LateDriversAlertEventKey(HiatmeAiClient.LateDriversEventRow e)
        {
            if (e == null) return "";
            string sd = (e.ServiceDate ?? "").Trim();
            if (string.IsNullOrEmpty(sd))
                sd = LateDriversAlertSeenStore.TodayIso();

            string trip = (e.TripNo ?? "").Trim().TrimStart('+');
            string leg = ScheduleBuilderPreviewDrag.TripLegKey(trip);
            if (string.IsNullOrEmpty(leg))
                leg = WellRydeFilterDataParser.FormatTripIdForScheduleMatch(trip);
            if (string.IsNullOrEmpty(leg))
                leg = ScheduleBuilderModivcareTripMatch.NormalizeTripNumber(trip);
            if (string.IsNullOrEmpty(leg))
                leg = trip;

            string hk = HabitKeyOf(e);
            if (string.IsNullOrEmpty(hk))
            {
                hk = string.Equals(e.Side, "do", StringComparison.OrdinalIgnoreCase)
                    ? "late_do"
                    : "late_pu";
            }

            // Stable across live EventId (…|pu) vs habits EventId (…|late_pu).
            return sd + "|" + leg + "|" + hk;
        }

        /// <summary>
        /// True while this event is inside the Suite-local first-seen blink window.
        /// Hot map is maintained by <see cref="CollectLateDriversActiveAlertKeys"/>.
        /// </summary>
        private bool LateDriversEventNeedsCallAlert(HiatmeAiClient.LateDriversEventRow e)
        {
            if (!LateDriversEventIsTimingAlertCandidate(e))
                return false;
            string key = LateDriversAlertEventKey(e);
            if (string.IsNullOrEmpty(key))
                return false;
            DateTime until;
            return _ldAlertHotUntil.TryGetValue(key, out until) && DateTime.UtcNow <= until;
        }

        private bool LateDriversDriverNeedsCallAlert(
            HiatmeAiClient.LateDriversDriverSummary summary)
        {
            if (summary?.Trips == null || summary.Trips.Count == 0)
                return false;
            return summary.Trips.Any(LateDriversEventNeedsCallAlert);
        }

        private void EnsureLateDriversAlertChirpKeysLoaded()
        {
            string today = LateDriversAlertSeenStore.TodayIso();
            if (_ldAlertChirpKeysLoaded
                && string.Equals(_ldAlertChirpKeysLoadedForDate, today, StringComparison.Ordinal))
                return;

            _ldAlertChirpKeys.Clear();
            foreach (string k in LateDriversAlertSeenStore.LoadForToday())
                _ldAlertChirpKeys.Add(k);
            _ldAlertChirpKeysLoaded = true;
            _ldAlertChirpKeysLoadedForDate = today;
        }

        private void PersistLateDriversAlertChirpKeys()
        {
            try
            {
                LateDriversAlertSeenStore.SaveForToday(_ldAlertChirpKeys);
                _ldAlertChirpKeysLoaded = true;
                _ldAlertChirpKeysLoadedForDate = LateDriversAlertSeenStore.TodayIso();
            }
            catch { }
        }

        private static string LateDriversAlertFingerprint(string tripNo, string habitOrSide)
        {
            string trip = (tripNo ?? "").Trim().TrimStart('+');
            string leg = ScheduleBuilderPreviewDrag.TripLegKey(trip);
            if (string.IsNullOrEmpty(leg))
                leg = WellRydeFilterDataParser.FormatTripIdForScheduleMatch(trip);
            if (string.IsNullOrEmpty(leg))
                leg = ScheduleBuilderModivcareTripMatch.NormalizeTripNumber(trip);
            if (string.IsNullOrEmpty(leg))
                leg = trip;

            string hk = (habitOrSide ?? "").Trim().ToLowerInvariant();
            if (hk == "pu") hk = "late_pu";
            else if (hk == "do") hk = "late_do";
            return leg + "|" + hk;
        }

        private static string LateDriversAlertFingerprintFromKey(string storedKey)
        {
            if (string.IsNullOrWhiteSpace(storedKey))
                return "";
            string[] parts = storedKey.Split('|');
            if (parts.Length < 2)
                return "";
            string habitOrSide = parts[parts.Length - 1];
            // date|trip|habit  OR  date|trip|side  OR  date|trip|side|habit
            string trip = parts.Length >= 3 ? parts[1] : parts[0];
            if (parts.Length >= 4)
                trip = parts[1];
            return LateDriversAlertFingerprint(trip, habitOrSide);
        }

        private bool LateDriversAlertAlreadySeen(
            string stableKey,
            HiatmeAiClient.LateDriversEventRow e)
        {
            if (!string.IsNullOrEmpty(stableKey) && _ldAlertChirpKeys.Contains(stableKey))
                return true;
            if (e != null && !string.IsNullOrWhiteSpace(e.EventId)
                && _ldAlertChirpKeys.Contains(e.EventId.Trim()))
                return true;

            string wantHabit = HabitKeyOf(e);
            if (string.IsNullOrEmpty(wantHabit))
                wantHabit = (e?.Side ?? "").Trim().ToLowerInvariant();
            if (wantHabit == "pu") wantHabit = "late_pu";
            else if (wantHabit == "do") wantHabit = "late_do";

            string wantTrip = (e?.TripNo ?? "").Trim();
            if (string.IsNullOrEmpty(wantHabit) || string.IsNullOrEmpty(wantTrip))
                return false;

            foreach (string k in _ldAlertChirpKeys)
            {
                if (string.IsNullOrEmpty(k))
                    continue;
                string[] parts = k.Split('|');
                if (parts.Length < 2)
                    continue;
                string storedHabit = parts[parts.Length - 1].Trim();
                if (storedHabit == "pu") storedHabit = "late_pu";
                else if (storedHabit == "do") storedHabit = "late_do";
                if (!string.Equals(storedHabit, wantHabit, StringComparison.OrdinalIgnoreCase))
                    continue;

                // date|trip|habit — trip may contain dashes but not pipes.
                string storedTrip = parts.Length >= 3 ? parts[1] : parts[0];

                if (TripScoutTripNosMatch(storedTrip, wantTrip)
                    || ScheduleBuilderPreviewDrag.TripLegKeysMatch(storedTrip, wantTrip))
                    return true;

                // Fingerprint fallback (leg-normalized).
                if (string.Equals(
                        LateDriversAlertFingerprintFromKey(k),
                        LateDriversAlertFingerprint(wantTrip, wantHabit),
                        StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private void LateDriversRememberAlertKey(string stableKey, HiatmeAiClient.LateDriversEventRow e)
        {
            bool added = false;
            if (!string.IsNullOrEmpty(stableKey) && _ldAlertChirpKeys.Add(stableKey))
                added = true;
            if (e != null && !string.IsNullOrWhiteSpace(e.EventId)
                && _ldAlertChirpKeys.Add(e.EventId.Trim()))
                added = true;
            if (added)
                PersistLateDriversAlertChirpKeys();
        }

        /// <summary>
        /// Build the set of keys that should blink now. Starts a ~75s local window the first
        /// time the Suite sees each open late / fresh early episode.
        /// </summary>
        private HashSet<string> CollectLateDriversActiveAlertKeys()
        {
            EnsureLateDriversAlertChirpKeysLoaded();

            var activeKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var freshKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            DateTime now = DateTime.UtcNow;
            _ldAlertFreshChirpKeys = null;

            // Boot / pre-load: keep disk keys intact (empty episode used to wipe the file).
            if (!_ldAlertSnapshotReady || _ldEventRows == null)
                return activeKeys;

            foreach (var e in _ldEventRows)
            {
                if (!LateDriversEventIsTimingAlertCandidate(e))
                    continue;
                string key = LateDriversAlertEventKey(e);
                if (string.IsNullOrEmpty(key))
                    continue;

                DateTime until;
                if (_ldAlertHotUntil.TryGetValue(key, out until) && now <= until)
                {
                    activeKeys.Add(key);
                    continue;
                }

                if (LateDriversAlertAlreadySeen(key, e))
                {
                    LateDriversRememberAlertKey(key, e);
                    _ldAlertHotUntil.Remove(key);
                    continue;
                }

                // First Suite observation of a late OR early — same ~75s blink+chirp window.
                _ldAlertHotUntil[key] = now.AddSeconds(LateDriversAlertWindowSeconds);
                LateDriversRememberAlertKey(key, e);
                activeKeys.Add(key);
                freshKeys.Add(key);
            }

            // Drop hot windows for trips no longer in the board — but NEVER prune the
            // day-long seen store (brief habits-miss / empty merge used to wipe disk and
            // re-alert the same early every launch).
            var liveKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in _ldEventRows)
            {
                if (!LateDriversEventIsTimingAlertCandidate(e))
                    continue;
                string k = LateDriversAlertEventKey(e);
                if (!string.IsNullOrEmpty(k))
                    liveKeys.Add(k);
            }
            foreach (string stale in _ldAlertHotUntil.Keys.Where(k => !liveKeys.Contains(k)).ToList())
                _ldAlertHotUntil.Remove(stale);

            if (freshKeys.Count > 0)
                PersistLateDriversAlertChirpKeys();

            _ldAlertFreshChirpKeys = freshKeys;
            return activeKeys;
        }

        private void MaybePlayLateDriversAlertChirp(HashSet<string> activeKeys)
        {
            var fresh = _ldAlertFreshChirpKeys;
            _ldAlertFreshChirpKeys = null;
            if (fresh == null || fresh.Count == 0)
                return;
            if (activeKeys == null || activeKeys.Count == 0)
                return;
            TryPlayLateDriversSoundOnce(LateDriversAlertSoundFileName, "LateDriversAlertSound");
        }

        private static void TryPlayLateDriversAlertSoundOnce() =>
            TryPlayLateDriversSoundOnce(LateDriversAlertSoundFileName, "LateDriversAlertSound");

        private static void TryPlayLateDriversBellSoundOnce() =>
            TryPlayLateDriversSoundOnce(LateDriversBellSoundFileName, "LateDriversBellSound");

        private static void TryPlayLateDriversSoundOnce(string fileName, string threadName)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(fileName))
                    return;
                string baseDir = AppDomain.CurrentDomain.BaseDirectory ?? "";
                if (string.IsNullOrEmpty(baseDir))
                    return;
                string path = Path.Combine(baseDir, "Resources", fileName);
                if (!File.Exists(path))
                    path = Path.Combine(baseDir, fileName);
                if (!File.Exists(path))
                    return;

                string fullPath = Path.GetFullPath(path);
                var playThread = new Thread(() =>
                {
                    try
                    {
                        using (var reader = new MediaFoundationReader(fullPath))
                        using (var output = new WasapiOut(AudioClientShareMode.Shared, 200))
                        using (var done = new ManualResetEvent(false))
                        {
                            // WasapiOut can leave Playing early; wait for stop + clip length
                            // so the bell isn't chopped when the using-dispose tears down.
                            output.PlaybackStopped += (_, __) =>
                            {
                                try { done.Set(); } catch { }
                            };
                            output.Init(reader);
                            output.Play();
                            int waitMs = (int)Math.Ceiling(reader.TotalTime.TotalMilliseconds) + 500;
                            if (waitMs < 800)
                                waitMs = 800;
                            if (waitMs > 15000)
                                waitMs = 15000;
                            done.WaitOne(waitMs);
                            try
                            {
                                if (output.PlaybackState == PlaybackState.Playing)
                                    output.Stop();
                            }
                            catch { }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine("Driver Habits sound (" + fileName + "): " + ex);
                    }
                })
                {
                    IsBackground = true,
                    Name = string.IsNullOrEmpty(threadName) ? "LateDriversSound" : threadName,
                };
                playThread.SetApartmentState(ApartmentState.STA);
                playThread.Start();
            }
            catch
            {
                /* optional audio */
            }
        }

        private void EnsureLateDriversDriverAlertBlinkTimer()
        {
            if (_ldDriverAlertBlinkTimer != null)
                return;
            _ldDriverAlertBlinkTimer = new System.Windows.Forms.Timer { Interval = 550 };
            _ldDriverAlertBlinkTimer.Tick += (_, __) =>
            {
                _ldDriverAlertBlinkOn = !_ldDriverAlertBlinkOn;
                ApplyLateDriversDriverAlertBlinkPhase();
                ApplyLateDriversTripAlertBlinkPhase();
            };
        }

        private void SyncLateDriversDriverAlertBlink()
        {
            var activeKeys = CollectLateDriversActiveAlertKeys();
            bool any = activeKeys.Count > 0;
            MaybePlayLateDriversAlertChirp(activeKeys);

            if (any)
            {
                EnsureLateDriversDriverAlertBlinkTimer();
                if (!_ldDriverAlertBlinkTimer.Enabled)
                {
                    _ldDriverAlertBlinkOn = true;
                    _ldDriverAlertBlinkTimer.Start();
                }
                ApplyLateDriversDriverAlertBlinkPhase();
                ApplyLateDriversTripAlertBlinkPhase();
            }
            else
            {
                _ldDriverAlertBlinkTimer?.Stop();
                _ldDriverAlertBlinkOn = false;
                ApplyLateDriversDriverAlertBlinkPhase();
                ApplyLateDriversTripAlertBlinkPhase();
            }
        }

        private static Color LateDriversAlertFlashColor()
        {
            // Shared late/early attention color (orange-red).
            return Color.FromArgb(220, 70, 55);
        }

        private void ApplyLateDriversDriverAlertBlinkPhase()
        {
            foreach (var tile in _ldDriverTiles)
            {
                if (tile == null || tile.IsDisposed)
                    continue;
                var summary = tile.Tag as HiatmeAiClient.LateDriversDriverSummary;
                bool alert = summary == null
                    ? (_ldDriverRows ?? new List<HiatmeAiClient.LateDriversDriverSummary>())
                        .Any(LateDriversDriverNeedsCallAlert)
                    : (!LateDriversIsOtherSelected(summary.Driver)
                        && LateDriversDriverNeedsCallAlert(summary));

                var nameLbl = tile.Controls["ldTileName"] as Label;
                bool selected = summary == null
                    ? string.IsNullOrEmpty(_ldSelectedDriver)
                    : string.Equals(summary.Driver, _ldSelectedDriver, StringComparison.OrdinalIgnoreCase);

                if (!alert || !_ldDriverAlertBlinkOn)
                {
                    tile.BorderColorOverride = null;
                    tile.AccentColorOverride = null;
                    if (!selected)
                        tile.Accent = SupeyCard.AccentEdge.None;
                    if (nameLbl != null && !nameLbl.IsDisposed)
                        nameLbl.ForeColor = SupeyTheme.TextPrimary;
                    continue;
                }

                Color flash = LateDriversAlertFlashColor();
                tile.BorderColorOverride = flash;
                tile.AccentColorOverride = flash;
                tile.Accent = SupeyCard.AccentEdge.Top;
                tile.ShowBorder = true;
                if (nameLbl != null && !nameLbl.IsDisposed)
                    nameLbl.ForeColor = flash;
            }
        }

        /// <summary>
        /// Flash hot late/early rows in the trip grid (same window as driver tiles).
        /// Uses item BackColor — Supey owner-draw honors that for the row fill.
        /// </summary>
        private void ApplyLateDriversTripAlertBlinkPhase()
        {
            if (ldTripLv == null || ldTripLv.IsDisposed)
                return;

            bool anyHot = false;
            foreach (ListViewItem item in ldTripLv.Items)
            {
                if (item == null)
                    continue;
                if (item.Tag is LateDriversTripRowTag sep
                    && (sep.IsGroupHeader || sep.IsGap || sep.IsChangeDetail))
                    continue;

                string tripNo = null;
                if (item.Tag is LateDriversTripRowTag wrap)
                    tripNo = wrap.TripNo;
                else
                {
                    var habitRow = LateDriversHabitFromTag(item.Tag);
                    tripNo = habitRow?.TripNo;
                }

                var row = LateDriversHabitFromTag(item.Tag);
                bool alert = LateDriversEventNeedsCallAlert(row);
                if (alert && _ldDriverAlertBlinkOn)
                {
                    anyHot = true;
                    Color flash = LateDriversAlertFlashColor();
                    if (item.BackColor != flash)
                        item.BackColor = flash;
                    continue;
                }

                // Will-call ready tint (Trip Scout blue) when not in habit flash.
                if (LateDriversIsWillCallTrip(tripNo))
                {
                    if (item.BackColor != LateDriversWillCallRowColor)
                        item.BackColor = LateDriversWillCallRowColor;
                    continue;
                }

                if (item.BackColor != Color.Empty)
                    item.BackColor = Color.Empty;
            }

            if (anyHot || !_ldDriverAlertBlinkOn)
            {
                try { ldTripLv.Invalidate(true); }
                catch { }
            }
        }

        private static HiatmeAiClient.LateDriversEventRow LateDriversHabitFromTag(object tag)
        {
            if (tag is HiatmeAiClient.LateDriversEventRow direct)
                return direct;
            if (tag is LateDriversTripRowTag wrap)
                return wrap.HabitEvent;
            return null;
        }

        private SupeyCard CreateLateDriversDriverTile(
            string title,
            int lates,
            int early,
            int unfin,
            double minutes,
            HiatmeAiClient.LateDriversDriverSummary summary)
        {
            string tileKey = (summary?.Driver ?? "all").Trim();
            foreach (char c in Path.GetInvalidFileNameChars())
                tileKey = tileKey.Replace(c, '_');
            tileKey = tileKey.Replace(' ', '_');
            if (string.IsNullOrEmpty(tileKey))
                tileKey = "all";
            var card = new SupeyCard
            {
                Name = "ldDriverTile_" + tileKey,
                SurfaceLevel = SupeyCard.Surface.Elevated,
                ShowBorder = true,
                CornerRadius = 8,
                Margin = new Padding(0, 0, LateDriversDriverTileGap, 0),
                Padding = new Padding(10, 8, 10, 8),
                Size = new Size(LateDriversDriverTileW, LateDriversDriverTileH),
                MaximumSize = new Size(LateDriversDriverTileW, LateDriversDriverTileH),
                MinimumSize = new Size(LateDriversDriverTileW, LateDriversDriverTileH),
                Cursor = Cursors.Hand,
                Tag = summary, // null = All drivers
            };
            var nameRow = new Panel
            {
                Name = "ldTileNameRow",
                Dock = DockStyle.Top,
                Height = 22,
                BackColor = Color.Transparent,
            };
            var nameLbl = new Label
            {
                Name = "ldTileName",
                AutoSize = false,
                Dock = DockStyle.Fill,
                Text = title ?? "",
                TextAlign = ContentAlignment.MiddleLeft,
                BackColor = Color.Transparent,
                ForeColor = SupeyTheme.TextPrimary,
                Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold),
            };
            nameRow.Controls.Add(nameLbl);
            if (summary != null
                && !string.IsNullOrWhiteSpace(summary.Driver)
                && !LateDriversIsOtherSelected(summary.Driver))
            {
                var reviewBtn = new Label
                {
                    Name = "ldTileReview",
                    AutoSize = false,
                    Dock = DockStyle.Right,
                    Width = 22,
                    Text = "i",
                    TextAlign = ContentAlignment.MiddleCenter,
                    BackColor = Color.Transparent,
                    ForeColor = SupeyTheme.AccentPrimary,
                    Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold),
                    Cursor = Cursors.Hand,
                    Tag = summary.Driver.Trim(),
                };
                try
                {
                    var tip = new ToolTip();
                    tip.SetToolTip(reviewBtn, "Performance review");
                }
                catch { }
                string driverForReview = summary.Driver.Trim();
                reviewBtn.Click += (_, __) =>
                    _ = ShowLateDriversPerformanceReviewAsync(driverForReview);
                nameRow.Controls.Add(reviewBtn);
            }
            var statsLbl = new Label
            {
                Name = "ldTileStats",
                AutoSize = false,
                Dock = DockStyle.Top,
                Height = 18,
                Text = "L " + lates + "   E " + early + "   U " + unfin,
                TextAlign = ContentAlignment.MiddleLeft,
                BackColor = Color.Transparent,
                ForeColor = SupeyTheme.TextSecondary,
                Font = SupeyTheme.CaptionFont,
            };
            var minsLbl = new Label
            {
                Name = "ldTileMins",
                AutoSize = false,
                Dock = DockStyle.Fill,
                Text = minutes.ToString("0", CultureInfo.InvariantCulture) + "m late",
                TextAlign = ContentAlignment.MiddleLeft,
                BackColor = Color.Transparent,
                ForeColor = SupeyTheme.TextPrimary,
                Font = SupeyTheme.BodyFont,
            };
            card.Controls.Add(minsLbl);
            card.Controls.Add(statsLbl);
            card.Controls.Add(nameRow);

            void pick(object s, EventArgs e)
            {
                var chosen = card.Tag as HiatmeAiClient.LateDriversDriverSummary;
                SelectLateDriversDriver(chosen?.Driver, focusTripNo: null);
            }
            card.Click += pick;
            nameLbl.Click += pick;
            statsLbl.Click += pick;
            minsLbl.Click += pick;
            nameRow.Click += pick;

            return card;
        }

        private async Task ShowLateDriversPerformanceReviewAsync(string driverName)
        {
            if (string.IsNullOrWhiteSpace(driverName) || IsDisposed)
                return;
            var settings = LateDriversAiSettings();
            if (settings == null || string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                SupeyMessageDialog.ShowWarning(
                    this,
                    "Performance review",
                    "AI server not configured",
                    "Set the AI server URL in AI Assistant settings.");
                return;
            }

            string sd = LateDriversSelectedServiceDateIso();
            SetLateDriversStatus("Status: Loading performance review for " + driverName.Trim() + "…");
            try
            {
                var review = await HiatmeAiClient.GetDriverHabitsReviewAsync(
                        settings, sd, driverName.Trim())
                    .ConfigureAwait(true);
                if (review == null || !review.Ok)
                {
                    SupeyMessageDialog.ShowWarning(
                        this,
                        "Performance review",
                        "Could not load review",
                        review?.Error ?? "unknown error");
                    return;
                }

                using (var form = new DriverHabitsReviewForm(review))
                    form.ShowDialog(this);
            }
            catch (Exception ex)
            {
                SupeyMessageDialog.ShowWarning(
                    this,
                    "Performance review",
                    "Review failed",
                    ex.Message);
            }
            finally
            {
                UpdateLateDriversToolbarHints();
            }
        }

        /// <summary>
        /// Select All drivers (null) or a specific driver tile, rebind the trip list,
        /// and optionally scroll/select a trip by number.
        /// </summary>
        private void SelectLateDriversDriver(string driverName, string focusTripNo)
        {
            string next = string.IsNullOrWhiteSpace(driverName) ? null : driverName.Trim();
            bool changed = !string.Equals(
                _ldSelectedDriver ?? "",
                next ?? "",
                StringComparison.OrdinalIgnoreCase);

            // Reserved remaps score-tile captions — reset filter when crossing that boundary.
            bool wasOther = LateDriversIsReservedSelected(_ldSelectedDriver);
            bool nowOther = LateDriversIsReservedSelected(next);
            _ldSelectedDriver = next;
            if (wasOther != nowOther)
                _ldHabitChip = "all";

            if (changed && !string.IsNullOrEmpty(next) && !nowOther)
            {
                // Page the strip so the chosen driver tile is visible.
                var rows = _ldStripDrivers ?? new List<HiatmeAiClient.LateDriversDriverSummary>();
                int idx = rows.FindIndex(d =>
                    d != null && string.Equals(d.Driver, next, StringComparison.OrdinalIgnoreCase));
                if (idx >= 0)
                {
                    int page = LateDriversDriverStripPageSize();
                    if (page > 0)
                    {
                        if (idx < _ldDriverScrollOffset)
                            _ldDriverScrollOffset = idx;
                        else if (idx >= _ldDriverScrollOffset + page)
                            _ldDriverScrollOffset = Math.Max(0, idx - page + 1);
                        RenderLateDriversDriverStripPage();
                    }
                }
            }

            StyleLateDriversDriverTiles();
            UpdateLateDriversTripCaption();
            RefreshLateDriversScorecard();
            BindLateDriversTripPane();
            SyncLateDriversDriverAlertBlink();

            if (!string.IsNullOrWhiteSpace(focusTripNo))
                FocusLateDriversTripInList(focusTripNo);
        }

        private void FocusLateDriversTripInList(string tripNo)
        {
            if (ldTripLv == null || ldTripLv.IsDisposed || string.IsNullOrWhiteSpace(tripNo))
                return;

            ListViewItem match = null;
            foreach (ListViewItem item in ldTripLv.Items)
            {
                if (item?.Tag is LateDriversTripRowTag wrap)
                {
                    if (wrap.IsGroupHeader || wrap.IsGap || wrap.IsChangeDetail)
                        continue;
                    if (LateDriversTripQueryMatches(wrap.TripNo, tripNo))
                    {
                        match = item;
                        break;
                    }
                }
                else
                {
                    var habit = LateDriversHabitFromTag(item?.Tag);
                    if (LateDriversTripQueryMatches(habit?.TripNo, tripNo))
                    {
                        match = item;
                        break;
                    }
                }
            }

            if (match == null)
                return;

            try
            {
                ldTripLv.SelectedItems.Clear();
                match.Selected = true;
                match.Focused = true;
                match.EnsureVisible();
                ldTripLv.Focus();
            }
            catch { }
        }

        private static bool LateDriversTripQueryMatches(string tripNo, string query)
        {
            if (string.IsNullOrWhiteSpace(tripNo) || string.IsNullOrWhiteSpace(query))
                return false;
            string t = tripNo.Trim().TrimStart('+');
            string q = query.Trim().TrimStart('+');
            if (t.Length == 0 || q.Length == 0)
                return false;
            // WR portal ids (1-20260727-69081-B) vs schedule (1-69081-B).
            if (TripScoutTripNosMatch(t, q))
                return true;
            if (LateDriversTripNosEqualForChip(t, q))
                return true;
            if (t.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            string nt = ScheduleBuilderModivcareTripMatch.NormalizeTripNumber(t);
            string nq = ScheduleBuilderModivcareTripMatch.NormalizeTripNumber(q);
            if (!string.IsNullOrEmpty(nq) && !string.IsNullOrEmpty(nt)
                && nt.IndexOf(nq, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            string ft = WellRydeFilterDataParser.FormatTripIdForScheduleMatch(t);
            string fq = WellRydeFilterDataParser.FormatTripIdForScheduleMatch(q);
            if (!string.IsNullOrEmpty(ft) && !string.IsNullOrEmpty(fq)
                && (string.Equals(ft, fq, StringComparison.OrdinalIgnoreCase)
                    || LateDriversTripNosEqualForChip(ft, fq)))
                return true;
            string legT = ScheduleBuilderPreviewDrag.TripLegKey(t);
            string legQ = ScheduleBuilderPreviewDrag.TripLegKey(q);
            if (!string.IsNullOrEmpty(legQ) && !string.IsNullOrEmpty(legT)
                && string.Equals(legT, legQ, StringComparison.OrdinalIgnoreCase))
                return true;
            return false;
        }

        /// <summary>
        /// Find a trip by # (full or partial) and jump to its driver tile, Other, or All.
        /// </summary>
        private void GoToLateDriversTripSearch(string query)
        {
            query = (query ?? "").Trim();
            if (string.IsNullOrEmpty(query))
                return;

            string mode = LateDriversSelectedMode();
            bool singleDay = mode == "day" || mode == "live";
            string sd = LateDriversSelectedServiceDateIso();
            if (singleDay)
                EnsureLateDriversScheduleCache(sd, forceReload: false);

            // Prefer exact workbook trip # when several partial matches exist.
            string focusTrip = null;

            if (singleDay)
            {
                string owner;
                string ownedTrip;
                if (TryFindLateDriversWorkbookTrip(query, out owner, out ownedTrip)
                    && !string.IsNullOrWhiteSpace(owner))
                {
                    focusTrip = ownedTrip ?? query;
                    if (LateDriversIsReservesTabName(owner)
                        || LateDriversIsReservedStatus(FindLateDriversWrTrip(focusTrip)?.Status))
                    {
                        SelectLateDriversDriver(LateDriversReservedKey, focusTrip);
                        SetLateDriversStatus("Status: Found " + focusTrip + " — Reserved");
                        return;
                    }

                    string tile = MatchLateDriversStripDriver(owner);
                    SelectLateDriversDriver(tile ?? owner, focusTrip);
                    SetLateDriversStatus(
                        "Status: Found " + focusTrip + " — " + (tile ?? owner));
                    return;
                }

                if (TryFindLateDriversOffScheduleTrip(query, sd, out focusTrip))
                {
                    SelectLateDriversDriver(LateDriversReservedKey, focusTrip);
                    SetLateDriversStatus("Status: Found " + focusTrip + " — Reserved");
                    return;
                }
            }

            // Habit / period list
            var habit = (_ldEventRows ?? new List<HiatmeAiClient.LateDriversEventRow>())
                .FirstOrDefault(e => e != null && LateDriversTripQueryMatches(e.TripNo, query));
            if (habit != null)
            {
                focusTrip = habit.TripNo?.Trim() ?? query;
                string blame = string.IsNullOrWhiteSpace(habit.Driver)
                    ? null
                    : habit.Driver.Trim();
                if (!string.IsNullOrEmpty(blame)
                    && blame.IndexOf("unassign", StringComparison.OrdinalIgnoreCase) < 0
                    && !blame.Equals("Reserves", StringComparison.OrdinalIgnoreCase))
                {
                    string tile = MatchLateDriversStripDriver(blame);
                    if (!string.IsNullOrEmpty(tile) && singleDay)
                    {
                        SelectLateDriversDriver(tile, focusTrip);
                        SetLateDriversStatus("Status: Found " + focusTrip + " — " + tile);
                        return;
                    }
                }
                SelectLateDriversDriver(null, focusTrip);
                SetLateDriversStatus("Status: Found " + focusTrip + " — all drivers");
                return;
            }

            // WR day trips (may not have habits yet)
            if (singleDay)
            {
                var wr = FindLateDriversWrTripByQuery(query);
                if (wr != null && !string.IsNullOrWhiteSpace(wr.TripNo))
                {
                    focusTrip = wr.TripNo.Trim();
                    if (LateDriversIsReservedStatus(wr.Status))
                    {
                        SelectLateDriversDriver(LateDriversReservedKey, focusTrip);
                        SetLateDriversStatus("Status: Found " + focusTrip + " — Reserved");
                        return;
                    }
                    string owner2 = FindLateDriversWorkbookOwnerForTrip(focusTrip);
                    if (!string.IsNullOrWhiteSpace(owner2)
                        && !LateDriversIsReservesTabName(owner2)
                        && owner2.IndexOf("unassign", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        string tile = MatchLateDriversStripDriver(owner2);
                        SelectLateDriversDriver(tile ?? owner2, focusTrip);
                        SetLateDriversStatus(
                            "Status: Found " + focusTrip + " — " + (tile ?? owner2));
                        return;
                    }
                    // Unassigned / no sheet — show under All so search still lands somewhere.
                    SelectLateDriversDriver(null, focusTrip);
                    SetLateDriversStatus("Status: Found " + focusTrip + " — all drivers");
                    return;
                }
            }

            SetLateDriversStatus("Status: No trip matching \"" + query + "\"");
        }

        private string MatchLateDriversStripDriver(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;
            string want = CanonicalizeLateDriversDriverLabel(name.Trim());
            var hit = (_ldDriverRows ?? new List<HiatmeAiClient.LateDriversDriverSummary>())
                .FirstOrDefault(d => d != null && LateDriversDriverNamesMatch(d.Driver, want));
            return hit?.Driver;
        }

        private bool TryFindLateDriversWorkbookTrip(
            string query,
            out string owner,
            out string tripNo)
        {
            owner = null;
            tripNo = null;
            if (_ldScheduleCache == null || string.IsNullOrWhiteSpace(query))
                return false;

            string foundOwner = null;
            string foundTrip = null;

            void consider(string tab, string tn)
            {
                if (string.IsNullOrWhiteSpace(tab) || string.IsNullOrWhiteSpace(tn))
                    return;
                if (!LateDriversTripQueryMatches(tn, query))
                    return;
                // Prefer exact / longer match when upgrading.
                if (foundTrip == null
                    || tn.Length > foundTrip.Length
                    || string.Equals(
                        ScheduleBuilderModivcareTripMatch.NormalizeTripNumber(tn),
                        ScheduleBuilderModivcareTripMatch.NormalizeTripNumber(query),
                        StringComparison.OrdinalIgnoreCase))
                {
                    foundOwner = tab.Trim();
                    foundTrip = tn.Trim();
                }
            }

            if (_ldScheduleCache.DriverTrips != null)
            {
                foreach (var kv in _ldScheduleCache.DriverTrips)
                {
                    if (kv.Value == null) continue;
                    foreach (var t in kv.Value)
                        consider(kv.Key, t?.TripNumber);
                }
            }
            if (_ldScheduleCache.DriverLines != null)
            {
                foreach (var kv in _ldScheduleCache.DriverLines)
                {
                    if (kv.Value == null) continue;
                    foreach (var line in kv.Value)
                        consider(kv.Key, line?.Trip?.TripNumber);
                }
            }
            if (_ldScheduleCache.ReserveFileTrips != null)
            {
                foreach (var t in _ldScheduleCache.ReserveFileTrips)
                    consider("Reserves", t?.TripNumber);
            }

            owner = foundOwner;
            tripNo = foundTrip;
            return !string.IsNullOrEmpty(tripNo);
        }

        private bool TryFindLateDriversOffScheduleTrip(
            string query,
            string serviceDateIso,
            out string tripNo)
        {
            tripNo = null;
            var rows = BuildLateDriversOffScheduleRows(serviceDateIso);
            var hit = (rows ?? new List<LateDriversTripRowTag>())
                .FirstOrDefault(r => r != null
                    && !r.IsGroupHeader
                    && !r.IsGap
                    && LateDriversTripQueryMatches(r.TripNo, query));
            if (hit == null)
                return false;
            tripNo = hit.TripNo?.Trim();
            return !string.IsNullOrEmpty(tripNo);
        }

        private bool IsLateDriversTripOffSchedule(string tripNo, string serviceDateIso)
        {
            var rows = BuildLateDriversOffScheduleRows(serviceDateIso);
            return (rows ?? new List<LateDriversTripRowTag>())
                .Any(r => r != null
                    && !r.IsGroupHeader
                    && !r.IsGap
                    && LateDriversTripQueryMatches(r.TripNo, tripNo));
        }

        private HiatmeAiClient.TripScoutServerTripRow FindLateDriversWrTripByQuery(string query)
        {
            if (string.IsNullOrWhiteSpace(query) || _ldWrTripsByTripNo.Count == 0)
                return null;
            HiatmeAiClient.TripScoutServerTripRow best = null;
            foreach (var wr in _ldWrTripsByTripNo.Values.Distinct())
            {
                if (wr == null || !LateDriversTripQueryMatches(wr.TripNo, query))
                    continue;
                if (best == null
                    || (wr.TripNo ?? "").Length > (best.TripNo ?? "").Length)
                    best = wr;
            }
            return best;
        }

        private void StyleLateDriversDriverTiles()
        {
            foreach (var tile in _ldDriverTiles)
            {
                if (tile == null || tile.IsDisposed)
                    continue;
                var summary = tile.Tag as HiatmeAiClient.LateDriversDriverSummary;
                bool selected = summary == null
                    ? string.IsNullOrEmpty(_ldSelectedDriver)
                    : string.Equals(
                        summary.Driver ?? "",
                        _ldSelectedDriver ?? "",
                        StringComparison.OrdinalIgnoreCase);
                tile.Accent = selected ? SupeyCard.AccentEdge.Top : SupeyCard.AccentEdge.None;
                tile.SurfaceLevel = selected ? SupeyCard.Surface.Elevated : SupeyCard.Surface.Standard;
                tile.ShowBorder = true;
            }
            ApplyLateDriversDriverAlertBlinkPhase();
            ApplyLateDriversTripAlertBlinkPhase();
        }

        private void RefreshLateDriversScorecard()
        {
            if (_ldScoreValues.Count == 0)
                return;

            if (LateDriversReservedSelected)
            {
                RefreshLateDriversReservedScorecard();
                return;
            }

            SetLateDriversScoreCaption("all", "All");
            SetLateDriversScoreCaption("late_pu", "Late PU");
            SetLateDriversScoreCaption("late_do", "Late DO");
            SetLateDriversScoreCaption("early_pu", "Early PU");
            SetLateDriversScoreCaption("early_do", "Early DO");
            SetLateDriversScoreCaption("unfinished_ticket", "Unfinished");
            SetLateDriversScoreCaption("billed_unfinished", "Billed skip");
            SetLateDriversScoreCaption("open", "Open now");

            // Totals stay stable while a filter is active (use full roster / selected driver).
            int latePu = 0, lateDo = 0, earlyPu = 0, earlyDo = 0;
            int unfinished = 0, billed = 0, openN = 0, allN = 0;
            IEnumerable<HiatmeAiClient.LateDriversEventRow> events;
            if (!string.IsNullOrEmpty(_ldSelectedDriver))
            {
                var d = (_ldDriverRows ?? new List<HiatmeAiClient.LateDriversDriverSummary>())
                    .FirstOrDefault(x => x != null
                        && string.Equals(x.Driver, _ldSelectedDriver, StringComparison.OrdinalIgnoreCase));
                events = d?.Trips ?? Enumerable.Empty<HiatmeAiClient.LateDriversEventRow>();
            }
            else
            {
                events = _ldEventRows ?? Enumerable.Empty<HiatmeAiClient.LateDriversEventRow>();
            }

            foreach (var e in events)
            {
                if (e == null) continue;
                allN++;
                if (e.Open) openN++;
                string hk = HabitKeyOf(e);
                if (hk == "late_pu") latePu++;
                else if (hk == "late_do") lateDo++;
                else if (hk == "early_pu") earlyPu++;
                else if (hk == "early_do") earlyDo++;
                else if (hk == "unfinished_ticket") unfinished++;
                else if (hk == "billed_unfinished") billed++;
            }

            SetLateDriversScoreValue("all", allN.ToString(CultureInfo.InvariantCulture));
            SetLateDriversScoreValue("late_pu", latePu.ToString(CultureInfo.InvariantCulture));
            SetLateDriversScoreValue("late_do", lateDo.ToString(CultureInfo.InvariantCulture));
            SetLateDriversScoreValue("early_pu", earlyPu.ToString(CultureInfo.InvariantCulture));
            SetLateDriversScoreValue("early_do", earlyDo.ToString(CultureInfo.InvariantCulture));
            SetLateDriversScoreValue("unfinished_ticket", unfinished.ToString(CultureInfo.InvariantCulture));
            SetLateDriversScoreValue("billed_unfinished", billed.ToString(CultureInfo.InvariantCulture));
            SetLateDriversScoreValue("open", openN.ToString(CultureInfo.InvariantCulture));

            Color valueColor = string.IsNullOrEmpty(_ldSelectedDriver)
                ? SupeyTheme.TextPrimary
                : SupeyTheme.AccentPrimary;
            foreach (var lbl in _ldScoreValues.Values)
            {
                if (lbl != null && !lbl.IsDisposed)
                    lbl.ForeColor = valueColor;
            }
            StyleLateDriversScoreFilters();
            if (ldHeroCard != null && !ldHeroCard.IsDisposed)
            {
                ldHeroCard.Accent = string.IsNullOrEmpty(_ldSelectedDriver)
                    ? SupeyCard.AccentEdge.None
                    : SupeyCard.AccentEdge.Left;
            }
        }

        /// <summary>
        /// Reserved tile: show reserved count under All; habit chips unused.
        /// </summary>
        private void RefreshLateDriversReservedScorecard()
        {
            SetLateDriversScoreCaption("all", "All");
            SetLateDriversScoreCaption("late_pu", "—");
            SetLateDriversScoreCaption("late_do", "—");
            SetLateDriversScoreCaption("early_pu", "—");
            SetLateDriversScoreCaption("early_do", "—");
            SetLateDriversScoreCaption("unfinished_ticket", "—");
            SetLateDriversScoreCaption("billed_unfinished", "—");
            SetLateDriversScoreCaption("open", "—");

            string sd = LateDriversSelectedServiceDateIso();
            var rows = BuildLateDriversOffScheduleRows(sd)
                ?? new List<LateDriversTripRowTag>();
            int allN = rows.Count(r => r != null && !r.IsGroupHeader && !r.IsGap);

            SetLateDriversScoreValue("all", allN.ToString(CultureInfo.InvariantCulture));
            SetLateDriversScoreValue("late_pu", "—");
            SetLateDriversScoreValue("late_do", "—");
            SetLateDriversScoreValue("early_pu", "—");
            SetLateDriversScoreValue("early_do", "—");
            SetLateDriversScoreValue("unfinished_ticket", "—");
            SetLateDriversScoreValue("billed_unfinished", "—");
            SetLateDriversScoreValue("open", "—");

            foreach (var lbl in _ldScoreValues.Values)
            {
                if (lbl != null && !lbl.IsDisposed)
                    lbl.ForeColor = SupeyTheme.AccentPrimary;
            }
            StyleLateDriversScoreFilters();
            if (ldHeroCard != null && !ldHeroCard.IsDisposed)
                ldHeroCard.Accent = SupeyCard.AccentEdge.Left;
        }

        private void RefreshLateDriversOtherScorecard() => RefreshLateDriversReservedScorecard();

        private void SetLateDriversScoreCaption(string key, string text)
        {
            if (ldScorecardHost == null || ldScorecardHost.IsDisposed)
                return;
            var cap = ldScorecardHost.Controls.Find("ldScoreCap_" + key, true).FirstOrDefault() as Label;
            if (cap != null && !cap.IsDisposed)
                cap.Text = text ?? "";
        }

        private void SetLateDriversScoreValue(string key, string text)
        {
            if (_ldScoreValues.TryGetValue(key, out var lbl) && lbl != null && !lbl.IsDisposed)
                lbl.Text = text ?? "0";
        }

        private static string HabitKeyOf(HiatmeAiClient.LateDriversEventRow e)
        {
            if (e == null) return "";
            string h = (e.Habit ?? e.Kind ?? "").Trim().ToLowerInvariant()
                .Replace(' ', '_')
                .Replace('-', '_');
            switch (h)
            {
                case "latepu":
                case "late_pickup":
                case "late_pick_up":
                    return "late_pu";
                case "latedo":
                case "late_dropoff":
                case "late_drop_off":
                    return "late_do";
                case "earlypu":
                case "early_pickup":
                case "early_pick_up":
                    return "early_pu";
                case "earlydo":
                case "early_dropoff":
                case "early_drop_off":
                    return "early_do";
                case "unfinished":
                case "unfinished_ticket":
                    return "unfinished_ticket";
                case "billed_skip":
                case "billed_unfinished":
                    return "billed_unfinished";
            }
            if (!string.IsNullOrEmpty(h))
                return h;
            return string.Equals(e.Side, "do", StringComparison.OrdinalIgnoreCase)
                ? "late_do"
                : "late_pu";
        }

        private static string HabitLabelOf(string key)
        {
            switch ((key ?? "").Trim().ToLowerInvariant())
            {
                case "late_pu": return "Late PU";
                case "late_do": return "Late DO";
                case "early_pu": return "Early PU";
                case "early_do": return "Early DO";
                case "unfinished_ticket": return "Unfinished";
                case "billed_unfinished": return "Billed skip";
                default: return string.IsNullOrEmpty(key) ? "Late" : key;
            }
        }

        /// <summary>
        /// Mins column: minutes past the allowed window (A PU +14 / B-C +29 / early caps),
        /// not raw vs scheduled. Recomputes from clocks when present so legacy API rows
        /// that stored raw-vs-sched still display correctly.
        /// </summary>
        private static double LateDriversDisplayHabitMinutes(HiatmeAiClient.LateDriversEventRow e)
        {
            if (e == null) return 0;
            string hk = HabitKeyOf(e);
            bool isDo = LateDriversHabitIsSide(e, "do");
            bool isPu = LateDriversHabitIsSide(e, "pu");

            if (TryParseLateDriversIso(e.SchedIso, out var sched)
                && TryParseLateDriversIso(e.ActualIso, out var actual))
            {
                if (hk.StartsWith("late", StringComparison.Ordinal))
                {
                    double late = McTripTimingRules.MinutesLate(actual, sched);
                    return McTripTimingRules.ExcessLateMinutes(e.TripNo, late, isDo);
                }
                if (hk.StartsWith("early", StringComparison.Ordinal))
                {
                    double early = McTripTimingRules.MinutesEarly(sched, actual);
                    return McTripTimingRules.ExcessEarlyMinutes(e.TripNo, early, isDo);
                }
            }

            double stored = Math.Max(0, e.MinutesLate);
            if (hk.StartsWith("late", StringComparison.Ordinal))
            {
                int grace = isDo
                    ? McTripTimingRules.DoLateMaxMinutes
                    : (e.GraceMinutes > 0
                        ? e.GraceMinutes
                        : McTripTimingRules.PuLateMaxMinutes(e.TripNo));
                int whole = McTripTimingRules.FloorMinutes(stored);
                // Legacy raw-vs-sched is typically > grace; new API already stores excess.
                if (whole > grace)
                    return Math.Max(0, whole - grace);
                return whole;
            }
            if (hk.StartsWith("early", StringComparison.Ordinal))
            {
                int cap = isDo
                    ? McTripTimingRules.LenientDoEarlyMinMinutes
                    : McTripTimingRules.PuEarlyMaxMinutes(e.TripNo);
                int whole = McTripTimingRules.FloorMinutes(stored);
                if (whole > cap)
                    return Math.Max(0, whole - cap);
                return whole;
            }
            return stored;
        }

        private static bool TryParseLateDriversIso(string iso, out DateTime dt)
        {
            dt = default;
            if (string.IsNullOrWhiteSpace(iso))
                return false;
            return DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out dt)
                || DateTime.TryParse(iso, out dt);
        }

        private void BindLateDriversTripPane()
        {
            if (ldTripLv == null || ldTripLv.IsDisposed)
                return;

            string mode = LateDriversSelectedMode();
            bool singleDay = mode == "day" || mode == "live";
            string q = LateDriversSearchQuery;

            if (singleDay)
                LateDriversEnsureChangesLoadedForBind(LateDriversSelectedServiceDateIso());

            // Live search (Trip Scout style): flat matches across the day while typing.
            if (singleDay && q.Length > 0)
            {
                string sd = LateDriversSelectedServiceDateIso();
                EnsureLateDriversScheduleCache(sd, forceReload: false);
                int corpus;
                var hits = BuildLateDriversSearchResultRows(sd, q, out corpus);
                BindLateDriversMergedTripRows(hits, showDriver: true);
                AppendLateDriversScheduleStatus(
                    hits.Count == 0
                        ? ("Status: 0 of " + corpus + " trips match \"" + q + "\".")
                        : ("Status: " + hits.Count + " of " + corpus
                            + " trips match \"" + q + "\".  Enter → jump to driver"));
                if (ldTripCaptionLbl != null && !ldTripCaptionLbl.IsDisposed)
                    ldTripCaptionLbl.Text = "Search — \"" + q + "\"";
                return;
            }

            UpdateLateDriversTripCaption();

            if (singleDay && LateDriversReservedSelected)
            {
                string sd = LateDriversSelectedServiceDateIso();
                var reserved = BuildLateDriversOffScheduleRows(sd);
                BindLateDriversMergedTripRows(reserved, showDriver: true);
                int n = reserved?.Count(r => r != null && !r.IsGroupHeader && !r.IsGap) ?? 0;
                AppendLateDriversScheduleStatus(
                    n == 0
                        ? "Reserved: no WellRyde trips with Reserved status"
                        : ("Reserved: " + n + " WellRyde trip" + (n == 1 ? "" : "s")
                            + " with Reserved status"));
                return;
            }

            // All drivers = habit alerts only (late/early/etc.). Full schedule is per-driver.
            bool useSchedule = singleDay
                && !string.IsNullOrWhiteSpace(_ldSelectedDriver)
                && !LateDriversReservedSelected;

            if (useSchedule)
            {
                string sd = LateDriversSelectedServiceDateIso();
                EnsureLateDriversScheduleCache(sd, forceReload: false);
                var merged = BuildLateDriversMergedScheduleRows(sd, _ldSelectedDriver);
                if (merged != null)
                {
                    BindLateDriversMergedTripRows(merged, showDriver: false);
                    AppendLateDriversScheduleStatus(FormatLateDriversScheduleStatusNote(merged));
                    return;
                }
                AppendLateDriversScheduleStatus(
                    string.IsNullOrWhiteSpace(_ldScheduleCacheError)
                        ? "missing — habits only"
                        : _ldScheduleCacheError);
            }
            else
            {
                AppendLateDriversScheduleStatus(null);
            }

            BindLateDriversHabitOnlyTripPane();
        }

        /// <summary>
        /// Flat day-wide search hits (workbook + WR Other + habit leftovers), like Trip Scout.
        /// </summary>
        private List<LateDriversTripRowTag> BuildLateDriversSearchResultRows(
            string serviceDateIso,
            string query,
            out int corpusCount)
        {
            var rows = new List<LateDriversTripRowTag>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            corpusCount = 0;
            query = (query ?? "").Trim();
            if (query.Length == 0)
                return rows;

            string TripSearchKey(string tripNo)
            {
                if (string.IsNullOrWhiteSpace(tripNo))
                    return "";
                string key = ScheduleBuilderPreviewDrag.TripLegKey(tripNo);
                if (string.IsNullOrEmpty(key))
                    key = ScheduleBuilderModivcareTripMatch.NormalizeTripNumber(tripNo);
                if (string.IsNullOrEmpty(key))
                    key = tripNo.Trim();
                return key;
            }

            void addRow(LateDriversTripRowTag row)
            {
                if (row == null || string.IsNullOrWhiteSpace(row.TripNo))
                    return;
                string key = TripSearchKey(row.TripNo);
                if (string.IsNullOrEmpty(key) || !seen.Add(key))
                    return;
                rows.Add(row);
            }

            // Workbook: prefer preview lines per driver (same as schedule pane); else flat trips.
            var driverNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (_ldScheduleCache?.DriverLines != null)
            {
                foreach (var k in _ldScheduleCache.DriverLines.Keys)
                {
                    if (!string.IsNullOrWhiteSpace(k)
                        && !k.Equals("Reserves", StringComparison.OrdinalIgnoreCase))
                        driverNames.Add(k);
                }
            }
            if (_ldScheduleCache?.DriverTrips != null)
            {
                foreach (var k in _ldScheduleCache.DriverTrips.Keys)
                {
                    if (!string.IsNullOrWhiteSpace(k)
                        && !k.Equals("Reserves", StringComparison.OrdinalIgnoreCase))
                        driverNames.Add(k);
                }
            }

            foreach (string driverName in driverNames)
            {
                var habits = CollectLateDriversHabitsForDriverDay(serviceDateIso, driverName);
                var matched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var lines = FindLateDriversScheduleLinesForDriver(driverName);
                var flat = FindLateDriversScheduleTripsForDriver(driverName);
                IEnumerable<MCDownloadedTrip> tripsEnum;
                if (lines != null && lines.Count > 0)
                {
                    tripsEnum = lines
                        .Where(l => l != null
                            && l.Kind == ScheduleBuilderPreviewLine.LineKind.Trip
                            && l.Trip != null
                            && !string.IsNullOrWhiteSpace(l.Trip.TripNumber))
                        .Select(l => l.Trip);
                }
                else
                {
                    tripsEnum = (flat ?? new List<MCDownloadedTrip>())
                        .Where(t => t != null && !string.IsNullOrWhiteSpace(t.TripNumber));
                }

                foreach (var trip in tripsEnum)
                {
                    corpusCount++;
                    if (!LateDriversScheduleTripMatchesSearch(trip, driverName, query))
                        continue;
                    foreach (var row in MakeLateDriversScheduleTripRows(
                        serviceDateIso, driverName, trip, habits, matched, 0, null))
                        addRow(row);
                }
            }

            // Other / off-schedule WR trips.
            foreach (var off in BuildLateDriversOffScheduleRows(serviceDateIso)
                ?? new List<LateDriversTripRowTag>())
            {
                if (off == null || off.IsGroupHeader || off.IsGap)
                    continue;
                corpusCount++;
                if (!LateDriversMergedRowMatchesSearch(off, query))
                    continue;
                addRow(off);
            }

            // Habit-only leftovers (not already on a sheet row) — don't inflate corpus.
            foreach (var e in _ldEventRows ?? new List<HiatmeAiClient.LateDriversEventRow>())
            {
                if (e == null || !LateDriversHabitMatchesSearch(e, query))
                    continue;
                string key = TripSearchKey(e.TripNo);
                if (!string.IsNullOrEmpty(key) && seen.Contains(key))
                    continue;
                var added = new LateDriversTripRowTag
                {
                    ScheduleTrip = null,
                    HabitEvent = e,
                    FromSchedule = false,
                    HabitOnly = true,
                    DriverDisplay = string.IsNullOrWhiteSpace(e.Driver)
                        ? "(unassigned)"
                        : e.Driver.Trim(),
                    TripNo = e.TripNo?.Trim() ?? "",
                    ServiceDate = string.IsNullOrWhiteSpace(e.ServiceDate)
                        ? serviceDateIso
                        : e.ServiceDate.Trim(),
                    Client = e.Client ?? "",
                    SortTime = LateDriversHabitSortTime(serviceDateIso, e),
                    HabitChipKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                };
                string hk = HabitKeyOf(e);
                if (!string.IsNullOrEmpty(hk))
                    added.HabitChipKeys.Add(hk);
                added.HabitChipOpen = e.Open;
                ApplyLateDriversPuDoTimes(
                    added, trip: null, habits: new List<HiatmeAiClient.LateDriversEventRow> { e });
                addRow(added);
            }

            rows.Sort((a, b) =>
            {
                int cmp = a.SortTime.CompareTo(b.SortTime);
                if (cmp != 0) return cmp;
                return string.Compare(a.TripNo, b.TripNo, StringComparison.OrdinalIgnoreCase);
            });
            return rows;
        }

        private bool LateDriversScheduleTripMatchesSearch(
            MCDownloadedTrip trip,
            string driverName,
            string query)
        {
            if (trip == null || string.IsNullOrWhiteSpace(query))
                return false;
            if (LateDriversSearchBlobMatches(
                    query,
                    trip.TripNumber,
                    trip.ClientFullName,
                    trip.ClientFirstName,
                    trip.ClientLastName,
                    driverName,
                    trip.DriverNameParsed,
                    trip.PUStreet,
                    trip.PUCity,
                    trip.DOStreet,
                    trip.DOCITY,
                    trip.PUTelephone,
                    trip.DOTelephone,
                    trip.Comments,
                    trip.Miles))
                return true;

            var wr = FindLateDriversWrTrip(trip.TripNumber);
            if (wr != null
                && LateDriversSearchBlobMatches(
                    query,
                    wr.TripNo,
                    wr.Client,
                    wr.Driver,
                    wr.Status,
                    wr.PuStreet,
                    wr.PuCity,
                    wr.DoStreet,
                    wr.DoCity))
                return true;
            return false;
        }

        private static bool LateDriversSearchBlobMatches(
            string query,
            params string[] fields)
        {
            if (string.IsNullOrWhiteSpace(query))
                return true;
            foreach (var f in fields)
            {
                if (string.IsNullOrWhiteSpace(f))
                    continue;
                if (f.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
                if (LateDriversTripQueryMatches(f, query))
                    return true;
            }
            return false;
        }

        private static bool LateDriversMergedRowMatchesSearch(LateDriversTripRowTag row, string query)
        {
            if (row == null) return false;
            if (string.IsNullOrWhiteSpace(query)) return true;
            string habit = !string.IsNullOrWhiteSpace(row.OffScheduleKind)
                ? LateDriversOffScheduleKindLabel(row.OffScheduleKind)
                : (row.HabitEvent != null ? HabitLabelOf(HabitKeyOf(row.HabitEvent)) : null);
            return LateDriversSearchBlobMatches(
                query,
                row.TripNo,
                row.Client,
                row.DriverDisplay,
                row.ReassignedToDriver,
                row.ReceivedFromDriver,
                row.StatusDisplay,
                row.StateDisplay,
                habit,
                row.OffScheduleKind);
        }

        private static bool LateDriversHabitMatchesSearch(
            HiatmeAiClient.LateDriversEventRow e,
            string query)
        {
            if (e == null) return false;
            return LateDriversSearchBlobMatches(
                query,
                e.TripNo,
                e.Client,
                e.Driver,
                e.StatusLatest,
                HabitLabelOf(HabitKeyOf(e)),
                e.Habit,
                e.Kind);
        }

        private void BindLateDriversHabitOnlyTripPane()
        {
            bool showDriver = string.IsNullOrEmpty(_ldSelectedDriver);
            EnsureLateDriversTripColumns(showDriver);

            List<HiatmeAiClient.LateDriversEventRow> trips;
            if (!string.IsNullOrEmpty(_ldSelectedDriver))
            {
                var driver = (_ldDriverRows ?? new List<HiatmeAiClient.LateDriversDriverSummary>())
                    .FirstOrDefault(d => d != null
                        && string.Equals(d.Driver, _ldSelectedDriver, StringComparison.OrdinalIgnoreCase));
                trips = driver?.Trips ?? (_ldEventRows ?? new List<HiatmeAiClient.LateDriversEventRow>())
                    .Where(e => e != null
                        && string.Equals(
                            string.IsNullOrWhiteSpace(e.Driver) ? "(unassigned)" : e.Driver.Trim(),
                            _ldSelectedDriver,
                            StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }
            else
            {
                trips = (_ldEventRows ?? new List<HiatmeAiClient.LateDriversEventRow>())
                    .Where(e => e != null)
                    .ToList();
            }

            // Keep every trip visible; score tiles highlight matches in owner-draw.
            string chip = (_ldHabitChip ?? "all").Trim().ToLowerInvariant();
            string q = LateDriversSearchQuery;
            int totalBeforeSearch = trips.Count;
            if (q.Length > 0)
                trips = trips.Where(e => LateDriversHabitMatchesSearch(e, q)).ToList();

            trips = trips
                .OrderByDescending(e => chip != "all" && LateDriversHabitMatchesChip(e, chip))
                .ThenByDescending(e => e.Open)
                .ThenByDescending(e => LateDriversDisplayHabitMinutes(e))
                .ThenBy(e => e.Driver ?? "", StringComparer.OrdinalIgnoreCase)
                .ThenBy(e => e.ServiceDate ?? "")
                .ThenBy(e => e.TripNo ?? "")
                .ToList();

            ldTripLv.BeginUpdate();
            try
            {
                ldTripLv.Items.Clear();
                LateDriversPruneExpandedTrips();
                foreach (var e in trips)
                {
                    if (e == null) continue;
                    var item = CreateLateDriversHabitListItem(e, showDriver);
                    string tripDisp = e.TripNo ?? "";
                    LateDriversApplyExpandChrome(item, e.TripNo, tripDisp);
                    ldTripLv.Items.Add(item);
                    LateDriversAppendExpandedChangeRows(ldTripLv.Items, e.TripNo, showDriver);
                }
            }
            finally
            {
                ldTripLv.EndUpdate();
            }
            ApplyLateDriversTripAlertBlinkPhase();

            if (q.Length > 0)
            {
                AppendLateDriversScheduleStatus(
                    trips.Count == 0
                        ? ("Status: 0 of " + totalBeforeSearch + " habits match \"" + q + "\".")
                        : ("Status: " + trips.Count + " of " + totalBeforeSearch
                            + " habits match \"" + q + "\"."));
                if (ldTripCaptionLbl != null && !ldTripCaptionLbl.IsDisposed)
                    ldTripCaptionLbl.Text = "Search — \"" + q + "\"";
            }
        }

        private void BindLateDriversMergedTripRows(List<LateDriversTripRowTag> rows, bool showDriver)
        {
            EnsureLateDriversTripColumns(showDriver);
            LateDriversPruneExpandedTrips();

            ldTripLv.BeginUpdate();
            try
            {
                ldTripLv.Items.Clear();
                foreach (var row in rows ?? new List<LateDriversTripRowTag>())
                {
                    if (row == null) continue;
                    var item = CreateLateDriversMergedListItem(row, showDriver);
                    LateDriversApplyExpandChrome(item, row, showDriver);
                    ldTripLv.Items.Add(item);
                    LateDriversAppendExpandedChangeRows(ldTripLv.Items, row, showDriver);
                }
            }
            finally
            {
                ldTripLv.EndUpdate();
            }
            ApplyLateDriversTripAlertBlinkPhase();
        }

        private static bool LateDriversHabitMatchesChip(
            HiatmeAiClient.LateDriversEventRow e, string chip)
        {
            if (e == null) return chip == "all";
            chip = (chip ?? "all").Trim().ToLowerInvariant();
            if (chip == "all") return true;
            if (chip == "open") return e.Open;
            string hk = HabitKeyOf(e);
            return !string.IsNullOrEmpty(hk) && hk == chip;
        }

        /// <summary>Same trip identity for chip keys — leg-aware, no partner-leg bleed.</summary>
        private static bool LateDriversTripNosEqualForChip(string a, string b)
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
                return false;
            string ka = ScheduleBuilderPreviewDrag.TripLegKey(a);
            string kb = ScheduleBuilderPreviewDrag.TripLegKey(b);
            if (ka.Length > 0 && kb.Length > 0)
                return string.Equals(ka, kb, StringComparison.OrdinalIgnoreCase);
            string na = ScheduleBuilderModivcareTripMatch.NormalizeTripNumber(a);
            string nb = ScheduleBuilderModivcareTripMatch.NormalizeTripNumber(b);
            return na.Length > 0
                && string.Equals(na, nb, StringComparison.OrdinalIgnoreCase);
        }

        private static void AssignLateDriversChipKeys(
            LateDriversTripRowTag row,
            List<HiatmeAiClient.LateDriversEventRow> habits,
            string tripNo)
        {
            if (row == null) return;
            // Highlight follows the Habit column (HabitEvent), not every habit on the trip.
            // A trip often has both late_pu + late_do; lighting both for Late DO looked wrong.
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool anyOpen = false;
            string want = (tripNo ?? row.TripNo ?? "").Trim();
            if (row.HabitEvent != null
                && (string.IsNullOrEmpty(want)
                    || LateDriversTripNosEqualForChip(row.HabitEvent.TripNo, want)))
            {
                string hk = HabitKeyOf(row.HabitEvent);
                if (!string.IsNullOrEmpty(hk))
                    keys.Add(hk);
                if (row.HabitEvent.Open)
                    anyOpen = true;
            }
            else if (!string.IsNullOrEmpty(want) && habits != null)
            {
                // No attached event — still allow Open chip via any open habit on this leg.
                foreach (var e in habits)
                {
                    if (e == null || !e.Open) continue;
                    if (!LateDriversTripNosEqualForChip(e.TripNo, want))
                        continue;
                    anyOpen = true;
                    break;
                }
            }
            row.HabitChipKeys = keys;
            row.HabitChipOpen = anyOpen;
        }

        /// <summary>
        /// True if this list row matches the score tile. Matches the visible Habit
        /// on the row only — never other habit types sharing the same trip #.
        /// </summary>
        private bool LateDriversRowMatchesChip(object tag, string chip)
        {
            chip = (chip ?? "all").Trim().ToLowerInvariant();
            if (chip == "all")
                return true;
            if (tag is LateDriversTripRowTag sep
                && (sep.IsGroupHeader || sep.IsGap || sep.IsChangeDetail))
                return true;

            // Habit-only list: one event per row — match that event only.
            if (tag is HiatmeAiClient.LateDriversEventRow ev)
                return LateDriversHabitMatchesChip(ev, chip);

            if (!(tag is LateDriversTripRowTag row))
                return false;

            // Other/Reserved tile rows: only All filter applies.
            if (!string.IsNullOrWhiteSpace(row.OffScheduleKind))
            {
                return chip == "all";
            }

            // Reassigned away: still on this sheet, but not this driver's habit.
            if (row.ReassignedAway)
                return false;

            if (chip == "open")
                return row.HabitChipOpen
                    || (row.HabitEvent != null && row.HabitEvent.Open);

            // Visible habit only (Habit column), not sibling habits on the same trip.
            if (row.HabitEvent != null)
                return LateDriversHabitMatchesChip(row.HabitEvent, chip);

            return row.HabitChipKeys != null && row.HabitChipKeys.Contains(chip);
        }

        /// <summary>
        /// Soft accent wash for score-tile matches (owner-draw reads this via
        /// <see cref="LateDriversTripChipVisual"/> — item.ForeColor is ignored by Supey lists).
        /// </summary>
        private static Color LateDriversChipMatchBackColor()
        {
            Color a = SupeyTheme.AccentPrimary;
            Color b = SupeyTheme.ListBody;
            return Color.FromArgb(
                (a.R * 50 + b.R * 50) / 100,
                (a.G * 50 + b.G * 50) / 100,
                (a.B * 50 + b.B * 50) / 100);
        }

        /// <summary>
        /// Score-tile visual for a trip row: match (highlight) or dim. Separators ignored.
        /// </summary>
        private void LateDriversTripChipVisual(object tag, out bool chipMatch, out bool chipDim)
        {
            chipMatch = false;
            chipDim = false;
            string chip = (_ldHabitChip ?? "all").Trim().ToLowerInvariant();
            if (chip == "all")
                return;
            if (tag is LateDriversTripRowTag sep
                && (sep.IsGroupHeader || sep.IsGap || sep.IsChangeDetail))
                return;

            if (LateDriversRowMatchesChip(tag, chip))
                chipMatch = true;
            else
                chipDim = true;
        }

        private void FocusLateDriversFirstChipMatch()
        {
            if (ldTripLv == null || ldTripLv.IsDisposed)
                return;
            string chip = (_ldHabitChip ?? "all").Trim().ToLowerInvariant();
            if (chip == "all")
                return;

            ListViewItem first = null;
            foreach (ListViewItem item in ldTripLv.Items)
            {
                if (item == null) continue;
                if (item.Tag is LateDriversTripRowTag sep
                    && (sep.IsGroupHeader || sep.IsGap || sep.IsChangeDetail))
                    continue;
                if (!LateDriversRowMatchesChip(item.Tag, chip))
                    continue;
                first = item;
                break;
            }
            if (first == null)
                return;
            try
            {
                // Don't select — selection paint hides the accent wash on that row.
                ldTripLv.SelectedIndices.Clear();
                first.EnsureVisible();
            }
            catch { }
            try { ldTripLv.Invalidate(true); } catch { }
        }

        /// <summary>
        /// Trip grid columns. Driver is shown only on the all-drivers habit list.
        /// </summary>
        private void EnsureLateDriversTripColumns(bool showDriver)
        {
            if (ldTripLv == null || ldTripLv.IsDisposed)
                return;

            // Layout without Driver: Group, Date, Trip, Habit… → col 3 is Habit
            // Layout with Driver:    Group, Date, Trip, Driver, Habit… → col 3 is Driver
            bool hasDriverCol = ldTripLv.Columns.Count > 3
                && string.Equals(ldTripLv.Columns[3].Text, "Driver", StringComparison.OrdinalIgnoreCase);
            bool tripBeforeHabit = ldTripLv.Columns.Count > 2
                && string.Equals(ldTripLv.Columns[2].Text, "Trip", StringComparison.OrdinalIgnoreCase);
            bool hasPuDoCols = false;
            for (int i = 0; i < ldTripLv.Columns.Count; i++)
            {
                if (string.Equals(ldTripLv.Columns[i].Text, "Sched PU", StringComparison.OrdinalIgnoreCase))
                {
                    hasPuDoCols = true;
                    break;
                }
            }
            if (ldTripLv.Columns.Count > 0 && hasDriverCol == showDriver && hasPuDoCols && tripBeforeHabit)
                return;

            ldTripLv.BeginUpdate();
            try
            {
                ldTripLv.Items.Clear();
                ldTripLv.Columns.Clear();
                ldTripLv.Columns.Add("Group", 52);
                ldTripLv.Columns.Add("Date", 90);
                ldTripLv.Columns.Add("Trip", 120);
                if (showDriver)
                    ldTripLv.Columns.Add("Driver", 140);
                ldTripLv.Columns.Add("Habit", 78);
                ldTripLv.Columns.Add("Client", 160);
                ldTripLv.Columns.Add("Sched PU", 78);
                ldTripLv.Columns.Add("Actual PU", 78);
                ldTripLv.Columns.Add("Sched DO", 78);
                ldTripLv.Columns.Add("Actual DO", 78);
                ldTripLv.Columns.Add("Mins", 56);
                ldTripLv.Columns.Add("Status", 120);
                ldTripLv.Columns.Add("State", 72);
            }
            finally
            {
                ldTripLv.EndUpdate();
            }
        }

        private ListViewItem CreateLateDriversHabitListItem(
            HiatmeAiClient.LateDriversEventRow e,
            bool showDriver)
        {
            string habit = HabitLabelOf(HabitKeyOf(e));
            string driver = string.IsNullOrWhiteSpace(e?.Driver)
                ? "(unassigned)"
                : e.Driver.Trim();
            bool isPu = LateDriversHabitIsSide(e, "pu");
            bool isDo = LateDriversHabitIsSide(e, "do");
            // Unfinished / unknown: treat as PU so the row is not blank.
            if (!isPu && !isDo)
                isPu = true;

            // Start from WellRyde day row (both sides). Habit actuals overlay the alert
            // side; habit sched only fills when WR has no clock (live ticket wins).
            string schedPu = "—", actPu = "—", schedDo = "—", actDo = "—";
            var wr = FindLateDriversWrTrip(e?.TripNo);
            if (wr != null)
            {
                schedPu = FormatLateDriversTime(wr.SchedPuIso, blank: "—");
                actPu = FormatLateDriversTime(wr.ActualPuIso, blank: "—");
                schedDo = FormatLateDriversTime(wr.SchedDoIso, blank: "—");
                actDo = FormatLateDriversTime(wr.ActualDoIso, blank: "—");
            }
            string habitSched = FormatLateDriversTime(e?.SchedIso, blank: "—");
            string habitActual = FormatLateDriversTime(e?.ActualIso, blank: "—");
            if (isPu)
            {
                if (habitSched != "—" && (string.IsNullOrWhiteSpace(schedPu) || schedPu == "—"))
                    schedPu = habitSched;
                if (habitActual != "—") actPu = habitActual;
            }
            if (isDo)
            {
                if (habitSched != "—" && (string.IsNullOrWhiteSpace(schedDo) || schedDo == "—"))
                    schedDo = habitSched;
                if (habitActual != "—") actDo = habitActual;
            }

            if (LateDriversSchedPuIsWillCall(schedPu, trip: null, wr))
                schedPu = "Will call";

            var item = new ListViewItem("—");
            item.SubItems.Add(e.ServiceDate ?? "");
            item.SubItems.Add(e.TripNo ?? "");
            if (showDriver)
                item.SubItems.Add(driver);
            item.SubItems.Add(habit);
            item.SubItems.Add(e.Client ?? "");
            item.SubItems.Add(schedPu);
            item.SubItems.Add(actPu);
            item.SubItems.Add(schedDo);
            item.SubItems.Add(actDo);
            bool noActual = string.IsNullOrWhiteSpace(e.ActualIso);
            string minsText = noActual && e.Open
                ? "—"
                : LateDriversDisplayHabitMinutes(e).ToString("0", CultureInfo.InvariantCulture) + "m";
            item.SubItems.Add(minsText);
            string status = !string.IsNullOrWhiteSpace(wr?.Status)
                ? wr.Status.Trim()
                : (!string.IsNullOrWhiteSpace(e.StatusLatest) ? e.StatusLatest.Trim() : "");
            item.SubItems.Add(string.IsNullOrEmpty(status) ? "—" : status);
            item.SubItems.Add(e.Open ? "Open" : "Closed");
            item.Tag = e;
            string hk = HabitKeyOf(e);
            if (e.Open)
                item.ForeColor = Color.FromArgb(200, 80, 60);
            else if (hk.StartsWith("early", StringComparison.Ordinal))
                item.ForeColor = Color.FromArgb(180, 120, 40);
            else if (hk == "unfinished_ticket" || hk == "billed_unfinished")
                item.ForeColor = Color.FromArgb(160, 90, 40);
            return item;
        }

        private bool LateDriversTryGetEntireRowBounds(ListViewItem item, out Rectangle bounds)
        {
            bounds = Rectangle.Empty;
            if (ldTripLv == null || item == null)
                return false;
            try
            {
                bounds = ldTripLv.GetItemRect(item.Index, ItemBoundsPortion.Entire);
                int contentW = SupeyListViewHelpers.GetDetailsContentWidth(ldTripLv);
                if (contentW > bounds.Width)
                    bounds.Width = contentW;
            }
            catch (ArgumentException)
            {
                bounds = item.Bounds;
            }
            return bounds.Width > 0 && bounds.Height > 0;
        }

        /// <summary>
        /// Group/gap separator: full-row bar. G# sits in the Group column with the same
        /// +10 inset as trip cells; notes are centered in the remaining columns.
        /// </summary>
        private void LateDriversPaintMergedSepRow(Graphics g, ListViewItem item, bool selected)
        {
            if (g == null || !(item?.Tag is LateDriversTripRowTag tag)
                || (!tag.IsGroupHeader && !tag.IsGap))
                return;
            if (!LateDriversTryGetEntireRowBounds(item, out Rectangle rowBounds))
                return;

            Color bg;
            if (selected)
                bg = SupeyTheme.ListSelected;
            else if (tag.IsGroupHeader)
            {
                bg = LateDriversGroupColorsEnabled
                    ? (tag.GroupColor ?? SupeyTheme.SurfaceElevated)
                    : SupeyTheme.SurfaceElevated;
            }
            else
                bg = SupeyTheme.ListBody;

            Color fg = selected
                ? SupeyTheme.ListSelectedText
                : (tag.IsGroupHeader
                    ? ScheduleBuilderPreviewStyle.ContrastText(bg)
                    : SupeyTheme.TextMuted);

            string note = (tag.GroupLabel ?? "").Trim();
            string gLabel = tag.IsGroupHeader && tag.GroupNumber > 0
                ? ("G" + tag.GroupNumber)
                : "";

            // Match listView_DrawSubItem cell inset so G# lines up with trip Group cells.
            const int cellPadL = 10;
            int groupColW = (ldTripLv != null && !ldTripLv.IsDisposed && ldTripLv.Columns.Count > 0)
                ? Math.Max(0, ldTripLv.Columns[0].Width)
                : 52;

            var state = g.Save();
            try
            {
                g.SetClip(rowBounds);
                using (var brush = new SolidBrush(bg))
                    g.FillRectangle(brush, rowBounds);

                Font font = item.Font ?? ListViewOwnerDrawFonts.Cell;
                Font drawFont = tag.IsGroupHeader && (gLabel.Length > 0 || note.Length > 0)
                    ? new Font(font, FontStyle.Bold)
                    : font;
                try
                {
                    if (gLabel.Length > 0)
                    {
                        var groupTextBounds = new Rectangle(
                            rowBounds.Left + cellPadL,
                            rowBounds.Top,
                            Math.Max(0, groupColW - cellPadL - 1),
                            rowBounds.Height);
                        TextRenderer.DrawText(
                            g,
                            gLabel,
                            drawFont,
                            groupTextBounds,
                            fg,
                            TextFormatFlags.Left
                                | TextFormatFlags.SingleLine
                                | TextFormatFlags.VerticalCenter
                                | TextFormatFlags.EndEllipsis
                                | TextFormatFlags.NoPrefix
                                | TextFormatFlags.GlyphOverhangPadding);
                    }

                    if (note.Length > 0)
                    {
                        // Keep Group column clear for G#; otherwise use the full bar.
                        int noteLeft = gLabel.Length > 0
                            ? rowBounds.Left + groupColW
                            : rowBounds.Left;
                        var noteBounds = new Rectangle(
                            noteLeft + 6,
                            rowBounds.Top,
                            Math.Max(0, rowBounds.Right - noteLeft - 12),
                            rowBounds.Height);
                        TextRenderer.DrawText(
                            g,
                            note,
                            drawFont,
                            noteBounds,
                            fg,
                            TextFormatFlags.HorizontalCenter
                                | TextFormatFlags.SingleLine
                                | TextFormatFlags.VerticalCenter
                                | TextFormatFlags.EndEllipsis
                                | TextFormatFlags.NoPrefix);
                    }
                }
                finally
                {
                    if (!ReferenceEquals(drawFont, font))
                        drawFont.Dispose();
                }

                using (var pen = new Pen(SupeyListViewHelpers.ListGridLineColor, 1f))
                    g.DrawLine(pen, rowBounds.Left, rowBounds.Bottom - 1, rowBounds.Right - 1, rowBounds.Bottom - 1);
            }
            finally
            {
                g.Restore(state);
            }
        }

        /// <summary>
        /// Re-apply or strip group palette tints without rebuilding the trip list.
        /// Group structure (headers + G labels) is unchanged.
        /// </summary>
        private void ApplyLateDriversTripListColorMode()
        {
            if (ldTripLv == null || ldTripLv.IsDisposed)
                return;

            bool colorsOn = LateDriversGroupColorsEnabled;
            Color neutralBar = SupeyTheme.SurfaceElevated;
            Color neutralFg = SupeyTheme.TextPrimary;

            ldTripLv.BeginUpdate();
            try
            {
                foreach (ListViewItem item in ldTripLv.Items)
                {
                    if (!(item?.Tag is LateDriversTripRowTag tag) || !tag.IsGroupHeader)
                        continue;

                    Color bar = colorsOn
                        ? (tag.GroupColor ?? neutralBar)
                        : neutralBar;
                    Color fg = colorsOn
                        ? ScheduleBuilderPreviewStyle.ContrastText(bar)
                        : neutralFg;
                    item.BackColor = bar;
                    item.ForeColor = fg;
                    foreach (ListViewItem.ListViewSubItem si in item.SubItems)
                    {
                        si.BackColor = bar;
                        si.ForeColor = fg;
                    }
                }
            }
            finally
            {
                try { ldTripLv.EndUpdate(); } catch { }
            }

            try { ldTripLv.Invalidate(true); } catch { }
        }

        private ListViewItem CreateLateDriversMergedListItem(LateDriversTripRowTag row, bool showDriver)
        {
            // After Trip (+ optional Driver) + Habit: Client, 4 times, Mins, Status, State
            // Group/gap notes live in Tag.GroupLabel and paint as a full-row merged bar
            // (not Habit cell text — that would widen the Habit column via autofit).
            int subCount = showDriver ? 12 : 11;

            if (row.IsGap)
            {
                string note = (row.GroupLabel ?? "").Trim();
                var gap = new ListViewItem("");
                gap.UseItemStyleForSubItems = false;
                for (int c = 0; c < subCount; c++)
                    gap.SubItems.Add("");
                gap.Tag = row;
                gap.ForeColor = SupeyTheme.TextMuted;
                gap.BackColor = SupeyTheme.ListBody;
                foreach (ListViewItem.ListViewSubItem si in gap.SubItems)
                    si.BackColor = SupeyTheme.ListBody;
                if (!string.IsNullOrEmpty(note))
                    gap.Font = new Font("Segoe UI", 8.5f, FontStyle.Italic);
                return gap;
            }

            if (row.IsGroupHeader)
            {
                Color bar = LateDriversGroupColorsEnabled
                    ? (row.GroupColor ?? SupeyTheme.SurfaceElevated)
                    : SupeyTheme.SurfaceElevated;
                var hdr = new ListViewItem("");
                hdr.UseItemStyleForSubItems = false;
                for (int c = 0; c < subCount; c++)
                    hdr.SubItems.Add("");
                hdr.Tag = row;
                Color fg = LateDriversGroupColorsEnabled
                    ? ScheduleBuilderPreviewStyle.ContrastText(bar)
                    : SupeyTheme.TextPrimary;
                hdr.ForeColor = fg;
                hdr.BackColor = bar;
                hdr.Font = new Font("Segoe UI Semibold", 9f, FontStyle.Bold);
                foreach (ListViewItem.ListViewSubItem si in hdr.SubItems)
                {
                    si.BackColor = bar;
                    si.ForeColor = fg;
                }
                return hdr;
            }

            var habit = row.HabitEvent;
            string date = row.ServiceDate ?? "";
            string habitLabel;
            if (!string.IsNullOrWhiteSpace(row.OffScheduleKind))
            {
                habitLabel = LateDriversOffScheduleKindLabel(row.OffScheduleKind);
            }
            else if (row.ReassignedAway)
            {
                string to = LateDriversShortDriverLabel(row.ReassignedToDriver);
                habitLabel = string.IsNullOrEmpty(to) ? "Moved" : ("Moved → " + to);
            }
            else
            {
                habitLabel = habit != null ? HabitLabelOf(HabitKeyOf(habit)) : "—";
            }
            string tripNo = row.TripNo ?? "";
            if (row.HabitOnly && !string.IsNullOrEmpty(tripNo) && !tripNo.StartsWith("+", StringComparison.Ordinal))
                tripNo = "+" + tripNo;
            string groupCol = row.GroupNumber > 0 ? ("G" + row.GroupNumber) : "—";
            string driverCol = !string.IsNullOrWhiteSpace(row.DriverDisplay)
                ? row.DriverDisplay.Trim()
                : (habit?.Driver ?? "").Trim();
            string client = row.Client ?? "";
            if (!string.IsNullOrWhiteSpace(row.ReceivedFromDriver))
            {
                string from = LateDriversShortDriverLabel(row.ReceivedFromDriver);
                if (!string.IsNullOrEmpty(from))
                    client = (string.IsNullOrWhiteSpace(client) ? "" : client.Trim() + " · ")
                        + "from " + from;
            }
            string schedPu = string.IsNullOrWhiteSpace(row.SchedPuDisplay) ? "—" : row.SchedPuDisplay;
            string actPu = string.IsNullOrWhiteSpace(row.ActualPuDisplay) ? "—" : row.ActualPuDisplay;
            string schedDo = string.IsNullOrWhiteSpace(row.SchedDoDisplay) ? "—" : row.SchedDoDisplay;
            string actDo = string.IsNullOrWhiteSpace(row.ActualDoDisplay) ? "—" : row.ActualDoDisplay;

            var item = new ListViewItem(groupCol);
            item.UseItemStyleForSubItems = false;
            item.SubItems.Add(date);
            item.SubItems.Add(tripNo);
            if (showDriver)
                item.SubItems.Add(string.IsNullOrEmpty(driverCol) ? "—" : driverCol);
            item.SubItems.Add(habitLabel);
            item.SubItems.Add(client);
            item.SubItems.Add(schedPu);
            item.SubItems.Add(actPu);
            item.SubItems.Add(schedDo);
            item.SubItems.Add(actDo);

            // Status/State/times come from WR for every trip; habit overlays Mins + coloring.
            string status = !string.IsNullOrWhiteSpace(row.StatusDisplay)
                ? row.StatusDisplay.Trim()
                : (habit?.StatusLatest ?? "").Trim();
            if (string.IsNullOrEmpty(status))
            {
                var wr = FindLateDriversWrTrip(tripNo.TrimStart('+'));
                status = (wr?.Status ?? "").Trim();
            }
            string state = !string.IsNullOrWhiteSpace(row.StateDisplay)
                ? row.StateDisplay.Trim()
                : (habit != null
                    ? (habit.Open ? "Open" : "Closed")
                    : LateDriversStateFromStatus(status));

            if (!string.IsNullOrWhiteSpace(row.OffScheduleKind))
            {
                item.SubItems.Add("—");
                item.SubItems.Add(string.IsNullOrEmpty(status) ? "—" : status);
                item.SubItems.Add(string.IsNullOrEmpty(state) ? "—" : state);
                string kind = row.OffScheduleKind.Trim().ToLowerInvariant();
                if (kind == "reserved" || kind == "reserves")
                    item.ForeColor = Color.FromArgb(120, 140, 200);
                else if (kind == "unassigned")
                    item.ForeColor = Color.FromArgb(200, 120, 60);
                else if (kind == "cancelled")
                    item.ForeColor = SupeyTheme.TextMuted;
                else
                    item.ForeColor = Color.FromArgb(100, 160, 190);
            }
            else if (row.ReassignedAway)
            {
                // Still on this sheet in the workbook, but WR gave it to someone else.
                item.SubItems.Add("—");
                item.SubItems.Add(string.IsNullOrEmpty(status) ? "—" : status);
                item.SubItems.Add(string.IsNullOrEmpty(state) ? "—" : state);
                item.ForeColor = Color.FromArgb(100, 140, 190);
            }
            else if (habit != null)
            {
                bool noActual = string.IsNullOrWhiteSpace(habit.ActualIso);
                string minsText = noActual && habit.Open
                    ? "—"
                    : LateDriversDisplayHabitMinutes(habit).ToString("0", CultureInfo.InvariantCulture) + "m";
                item.SubItems.Add(minsText);
                item.SubItems.Add(string.IsNullOrEmpty(status) ? "—" : status);
                item.SubItems.Add(string.IsNullOrEmpty(state) ? "—" : state);
                string hk = HabitKeyOf(habit);
                if (habit.Open || hk.StartsWith("early", StringComparison.Ordinal)
                    || hk.StartsWith("late", StringComparison.Ordinal))
                    item.ForeColor = Color.FromArgb(200, 80, 60);
                else if (hk == "unfinished_ticket" || hk == "billed_unfinished")
                    item.ForeColor = Color.FromArgb(160, 90, 40);
                else if (row.HabitOnly)
                    item.ForeColor = Color.FromArgb(120, 160, 220);
            }
            else
            {
                item.SubItems.Add("—");
                item.SubItems.Add(string.IsNullOrEmpty(status) ? "—" : status);
                item.SubItems.Add(string.IsNullOrEmpty(state) ? "—" : state);
                item.ForeColor = SupeyTheme.TextSecondary;
            }

            // Group cell color is painted from Tag.GroupColor in owner-draw
            // (do not set Item.BackColor — that is shared with SubItems[0] and blink clears it).
            item.Tag = row;
            return item;
        }

        private List<LateDriversTripRowTag> BuildLateDriversMergedScheduleRows(
            string serviceDateIso,
            string driverName)
        {
            if (_ldScheduleCache == null || string.IsNullOrWhiteSpace(driverName))
                return null;

            // Prefer ordered preview lines (groups/gaps). Fall back to flat trips.
            var lines = FindLateDriversScheduleLinesForDriver(driverName);
            var scheduleTrips = FindLateDriversScheduleTripsForDriver(driverName);
            if (lines == null && scheduleTrips == null)
                return null;

            var habits = CollectLateDriversHabitsForDriverDay(serviceDateIso, driverName);
            var matchedHabitKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var rows = new List<LateDriversTripRowTag>();

            if (lines != null && lines.Count > 0)
            {
                // Same grouping as Schedule Builder: batches between Gap/GroupHeader rows.
                // Habits omits blank gap spacers — only colored group bars + trips.
                var groups = ScheduleBuilderPreviewGroups.BuildFromPreviewLines(lines);
                SupeyTripCluster lastHeaderGroup = null;

                for (int li = 0; li < lines.Count; li++)
                {
                    var line = lines[li];
                    if (line == null) continue;

                    if (line.Kind == ScheduleBuilderPreviewLine.LineKind.Gap)
                    {
                        lastHeaderGroup = null;
                        continue;
                    }

                    if (line.Kind == ScheduleBuilderPreviewLine.LineKind.GroupHeader)
                    {
                        // Prefer the next trip's batch — stored GroupNumber can be stale vs gap renumbering.
                        var headerGroup = FindLateDriversGroupAfterLine(groups, lines, li);
                        if (headerGroup == null && line.GroupNumber > 0)
                        {
                            headerGroup = groups.FirstOrDefault(grp =>
                                grp != null && grp.GroupNumber == line.GroupNumber);
                        }

                        if (ScheduleBuilderGroupNotes.ShouldShowNoteRow(line, showGroupColors: true)
                            && headerGroup != null)
                        {
                            rows.Add(MakeLateDriversGroupHeaderRow(
                                serviceDateIso, headerGroup, line.GroupNoteText));
                            lastHeaderGroup = headerGroup;
                        }
                        continue;
                    }

                    if (line.Kind != ScheduleBuilderPreviewLine.LineKind.Trip || line.Trip == null)
                        continue;
                    if (string.IsNullOrWhiteSpace(line.Trip.TripNumber))
                        continue;

                    var trip = line.Trip;
                    var g = ScheduleBuilderPreviewGroups.FindGroupForTrip(groups, trip);
                    if (g == null)
                        continue;

                    // Synthetic colored group bar when group changes (SB does this too).
                    if (!ReferenceEquals(g, lastHeaderGroup))
                    {
                        rows.Add(MakeLateDriversGroupHeaderRow(serviceDateIso, g, null));
                        lastHeaderGroup = g;
                    }

                    rows.AddRange(MakeLateDriversScheduleTripRows(
                        serviceDateIso,
                        driverName,
                        trip,
                        habits,
                        matchedHabitKeys,
                        groupNumber: g.GroupNumber,
                        groupColor: g.DisplayColor));
                }
            }
            else
            {
                foreach (var trip in (scheduleTrips ?? new List<MCDownloadedTrip>())
                    .Where(t => t != null && !string.IsNullOrWhiteSpace(t.TripNumber))
                    .OrderBy(t => LateDriversScheduleSortTime(serviceDateIso, t))
                    .ThenBy(t => t.TripNumber ?? "", StringComparer.OrdinalIgnoreCase))
                {
                    rows.AddRange(MakeLateDriversScheduleTripRows(
                        serviceDateIso,
                        driverName,
                        trip,
                        habits,
                        matchedHabitKeys,
                        groupNumber: 0,
                        groupColor: null));
                }
            }

            // Habit-only: insert by sched time when not already on a schedule trip row.
            // These are usually WR-driver habits for trips still printed on someone else's sheet.
            foreach (var e in habits)
            {
                string hk = LateDriversHabitIdentityKey(e);
                if (!string.IsNullOrEmpty(hk) && matchedHabitKeys.Contains(hk))
                    continue;

                DateTime sort = LateDriversHabitSortTime(serviceDateIso, e);
                int groupAt = InferLateDriversGroupNumberAtTime(rows, sort);
                Color? groupColor = InferLateDriversGroupColor(rows, groupAt);
                var added = new LateDriversTripRowTag
                {
                    ScheduleTrip = null,
                    HabitEvent = e,
                    FromSchedule = false,
                    HabitOnly = true,
                    GroupNumber = groupAt,
                    GroupLabel = groupAt > 0 ? ("G" + groupAt) : "",
                    GroupColor = groupColor,
                    DriverDisplay = driverName,
                    TripNo = e.TripNo?.Trim() ?? "",
                    ServiceDate = string.IsNullOrWhiteSpace(e.ServiceDate) ? serviceDateIso : e.ServiceDate.Trim(),
                    Client = e.Client ?? "",
                    SchedDisplay = FormatLateDriversTime(e.SchedIso, blank: "—"),
                    SortTime = sort,
                };
                string sheetOwner = FindLateDriversWorkbookOwnerForTrip(e.TripNo);
                if (!string.IsNullOrWhiteSpace(sheetOwner)
                    && !LateDriversDriverNamesMatch(sheetOwner, driverName))
                    added.ReceivedFromDriver = sheetOwner.Trim();
                // Habit-only rows: chip keys for this event alone (not every habit on the trip #).
                added.HabitChipKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                string eventKey = HabitKeyOf(e);
                if (!string.IsNullOrEmpty(eventKey))
                    added.HabitChipKeys.Add(eventKey);
                added.HabitChipOpen = e.Open;
                ApplyLateDriversPuDoTimes(added, trip: null, habits: new List<HiatmeAiClient.LateDriversEventRow> { e });
                InsertLateDriversRowInGroup(rows, added);
            }

            AppendLateDriversWrChangedTripsMissingFromSchedule(
                rows, serviceDateIso, driverName, habits, matchedHabitKeys);

            return rows;
        }

        /// <summary>
        /// WR trips assigned to this driver but missing from their printed sheet
        /// (mid-day reassign / stolen / new) — insert by sched time so they land in the
        /// right group. Expand comes from journal and/or print-vs-WR driver/time diffs.
        /// </summary>
        private void AppendLateDriversWrChangedTripsMissingFromSchedule(
            List<LateDriversTripRowTag> rows,
            string serviceDateIso,
            string driverName,
            List<HiatmeAiClient.LateDriversEventRow> habits,
            HashSet<string> matchedHabitKeys)
        {
            if (rows == null || _ldWrTripsByTripNo.Count == 0)
                return;
            if (string.IsNullOrWhiteSpace(driverName))
                return;

            var onList = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in rows)
            {
                if (r == null || r.IsGroupHeader || r.IsGap || string.IsNullOrWhiteSpace(r.TripNo))
                    continue;
                string k = LateDriversNormalizeChangeTripNo(r.TripNo);
                if (k.Length > 0)
                    onList.Add(k);
                string leg = ScheduleBuilderPreviewDrag.TripLegKey(k);
                if (!string.IsNullOrEmpty(leg))
                    onList.Add(leg);
            }

            foreach (var wr in _ldWrTripsByTripNo.Values.Distinct())
            {
                if (wr == null || string.IsNullOrWhiteSpace(wr.TripNo))
                    continue;
                if (!LateDriversDriverNamesMatch(wr.Driver, driverName))
                    continue;
                string tripNo = wr.TripNo.Trim();

                string key = LateDriversNormalizeChangeTripNo(tripNo);
                string leg = ScheduleBuilderPreviewDrag.TripLegKey(key);
                if (onList.Contains(key) || (!string.IsNullOrEmpty(leg) && onList.Contains(leg)))
                    continue;

                DateTime sort = DateTime.MaxValue;
                if (TryParseLateDriversIso(wr.SchedPuIso, out var pu))
                    sort = pu;
                else if (TryParseLateDriversIso(wr.SchedDoIso, out var dro))
                    sort = dro;
                else if (TryParseLateDriversIso(wr.ActualPuIso, out var apu))
                    sort = apu;

                int groupAt = InferLateDriversGroupNumberAtTime(rows, sort);
                Color? groupColor = InferLateDriversGroupColor(rows, groupAt);
                var added = new LateDriversTripRowTag
                {
                    ScheduleTrip = null,
                    HabitEvent = null,
                    FromSchedule = false,
                    HabitOnly = true,
                    GroupNumber = groupAt,
                    GroupLabel = groupAt > 0 ? ("G" + groupAt) : "",
                    GroupColor = groupColor,
                    DriverDisplay = driverName,
                    TripNo = tripNo,
                    ServiceDate = serviceDateIso,
                    Client = wr.Client ?? "",
                    SortTime = sort,
                    HabitChipKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                    HabitChipOpen = false,
                };

                string sheetOwner = FindLateDriversWorkbookOwnerForTrip(tripNo);
                if (!string.IsNullOrWhiteSpace(sheetOwner)
                    && !LateDriversIsReservesTabName(sheetOwner)
                    && !LateDriversDriverNamesMatch(sheetOwner, driverName))
                    added.ReceivedFromDriver = sheetOwner.Trim();

                // Attach any habit already blamed on this WR driver / trip.
                var tripHabits = FindAllLateDriversHabitsForTrip(habits, tripNo, preferHabitKey: null);
                if (tripHabits != null && tripHabits.Count > 0)
                {
                    added.HabitEvent = tripHabits[0];
                    added.HabitChipKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var he in tripHabits)
                    {
                        string eventKey = HabitKeyOf(he);
                        if (!string.IsNullOrEmpty(eventKey))
                            added.HabitChipKeys.Add(eventKey);
                        if (he.Open)
                            added.HabitChipOpen = true;
                        string hk = LateDriversHabitIdentityKey(he);
                        if (!string.IsNullOrEmpty(hk))
                            matchedHabitKeys?.Add(hk);
                    }
                }

                ApplyLateDriversPuDoTimes(
                    added,
                    trip: null,
                    habits: habits ?? new List<HiatmeAiClient.LateDriversEventRow>());
                InsertLateDriversRowInGroup(rows, added);
                onList.Add(key);
                if (!string.IsNullOrEmpty(leg))
                    onList.Add(leg);
            }
        }

        /// <summary>
        /// One list row per habit on the schedule trip (Late PU and Late DO both show).
        /// Clean trips still get a single row with no habit.
        /// </summary>
        private List<LateDriversTripRowTag> MakeLateDriversScheduleTripRows(
            string serviceDateIso,
            string scheduleDriver,
            MCDownloadedTrip trip,
            List<HiatmeAiClient.LateDriversEventRow> habits,
            HashSet<string> matchedHabitKeys,
            int groupNumber,
            Color? groupColor)
        {
            bool moved = LateDriversTripReassignedAway(
                scheduleDriver, trip?.TripNumber, out string wrDriver);

            var result = new List<LateDriversTripRowTag>();
            if (moved)
            {
                result.Add(MakeLateDriversScheduleTripRow(
                    serviceDateIso,
                    scheduleDriver,
                    trip,
                    habit: null,
                    habits,
                    matchedHabitKeys,
                    groupNumber,
                    groupColor,
                    moved: true,
                    wrDriver: wrDriver));
                return result;
            }

            string prefer = (_ldHabitChip ?? "all").Trim().ToLowerInvariant();
            if (prefer == "all" || prefer == "open")
                prefer = null;

            var tripHabits = FindAllLateDriversHabitsForTrip(habits, trip?.TripNumber, prefer);
            if (tripHabits.Count == 0)
            {
                result.Add(MakeLateDriversScheduleTripRow(
                    serviceDateIso,
                    scheduleDriver,
                    trip,
                    habit: null,
                    habits,
                    matchedHabitKeys,
                    groupNumber,
                    groupColor,
                    moved: false,
                    wrDriver: null));
                return result;
            }

            foreach (var habit in tripHabits)
            {
                MarkLateDriversHabitAndSiblingsMatched(habit, habits, matchedHabitKeys);
                result.Add(MakeLateDriversScheduleTripRow(
                    serviceDateIso,
                    scheduleDriver,
                    trip,
                    habit,
                    habits,
                    matchedHabitKeys,
                    groupNumber,
                    groupColor,
                    moved: false,
                    wrDriver: null));
            }
            return result;
        }

        private LateDriversTripRowTag MakeLateDriversScheduleTripRow(
            string serviceDateIso,
            string scheduleDriver,
            MCDownloadedTrip trip,
            HiatmeAiClient.LateDriversEventRow habit,
            List<HiatmeAiClient.LateDriversEventRow> habits,
            HashSet<string> matchedHabitKeys,
            int groupNumber,
            Color? groupColor,
            bool moved,
            string wrDriver)
        {
            var schedRow = new LateDriversTripRowTag
            {
                ScheduleTrip = trip,
                HabitEvent = habit,
                FromSchedule = true,
                HabitOnly = false,
                ReassignedAway = moved,
                ReassignedToDriver = moved ? wrDriver : null,
                GroupNumber = groupNumber,
                GroupLabel = groupNumber > 0 ? ("G" + groupNumber) : "",
                GroupColor = groupColor,
                DriverDisplay = scheduleDriver,
                TripNo = trip.TripNumber?.Trim() ?? "",
                ServiceDate = serviceDateIso,
                Client = trip.ClientFullName ?? habit?.Client ?? "",
                SchedDisplay = FormatLateDriversScheduleClock(
                    PreferSchedTimeForHabit(trip, habit)),
                SortTime = LateDriversScheduleSortTime(serviceDateIso, trip),
            };
            if (moved)
            {
                schedRow.HabitChipKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                schedRow.HabitChipOpen = false;
                if (string.IsNullOrWhiteSpace(schedRow.Client))
                {
                    var wr = FindLateDriversWrTrip(trip.TripNumber);
                    if (!string.IsNullOrWhiteSpace(wr?.Client))
                        schedRow.Client = wr.Client.Trim();
                }
            }
            else
            {
                AssignLateDriversChipKeys(schedRow, habits, trip.TripNumber);
            }
            ApplyLateDriversPuDoTimes(schedRow, trip, habits);
            return schedRow;
        }

        /// <summary>
        /// True when the workbook still lists the trip under <paramref name="scheduleDriver"/>
        /// but WellRyde shows a different driver (reassigned / stolen / unassigned).
        /// </summary>
        private bool LateDriversTripReassignedAway(
            string scheduleDriver,
            string tripNo,
            out string wrDriver)
        {
            wrDriver = null;
            if (string.IsNullOrWhiteSpace(scheduleDriver) || string.IsNullOrWhiteSpace(tripNo))
                return false;
            if (_ldWrTripsByTripNo.Count == 0)
                return false;

            var wr = FindLateDriversWrTrip(tripNo);
            if (wr == null)
                return false;

            wrDriver = string.IsNullOrWhiteSpace(wr.Driver)
                ? "(unassigned)"
                : wr.Driver.Trim();
            if (LateDriversDriverNamesMatch(wrDriver, scheduleDriver))
                return false;
            return true;
        }

        /// <summary>Which workbook tab still owns this trip #, if any.</summary>
        private string FindLateDriversWorkbookOwnerForTrip(string tripNo)
        {
            if (_ldScheduleCache == null || string.IsNullOrWhiteSpace(tripNo))
                return null;

            if (_ldScheduleCache.DriverTrips != null)
            {
                foreach (var kv in _ldScheduleCache.DriverTrips)
                {
                    if (string.IsNullOrWhiteSpace(kv.Key) || kv.Value == null)
                        continue;
                    foreach (var t in kv.Value)
                    {
                        if (t == null) continue;
                        if (LateDriversTripNosEqualForChip(t.TripNumber, tripNo))
                            return kv.Key.Trim();
                    }
                }
            }
            if (_ldScheduleCache.DriverLines != null)
            {
                foreach (var kv in _ldScheduleCache.DriverLines)
                {
                    if (string.IsNullOrWhiteSpace(kv.Key) || kv.Value == null)
                        continue;
                    foreach (var line in kv.Value)
                    {
                        if (line?.Trip == null) continue;
                        if (LateDriversTripNosEqualForChip(line.Trip.TripNumber, tripNo))
                            return kv.Key.Trim();
                    }
                }
            }
            return null;
        }

        private static string LateDriversShortDriverLabel(string driver)
        {
            if (string.IsNullOrWhiteSpace(driver))
                return "";
            string s = string.Join(" ", driver.Trim().Split(
                new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
            if (s.Length <= 22)
                return s;
            return s.Substring(0, 21) + "…";
        }

        private static string LateDriversHabitIdentityKey(HiatmeAiClient.LateDriversEventRow e)
        {
            if (e == null) return "";
            if (!string.IsNullOrWhiteSpace(e.EventId))
                return "id:" + e.EventId.Trim();
            string trip = ScheduleBuilderModivcareTripMatch.NormalizeTripNumber(e.TripNo);
            return "h:" + trip + "|" + HabitKeyOf(e) + "|" + (e.Side ?? "").Trim().ToLowerInvariant();
        }

        private static bool LateDriversHabitIsSide(HiatmeAiClient.LateDriversEventRow e, string side)
        {
            if (e == null || string.IsNullOrWhiteSpace(side))
                return false;
            string want = side.Trim().ToLowerInvariant();
            string s = (e.Side ?? "").Trim().ToLowerInvariant();
            if (s == want)
                return true;
            string hk = HabitKeyOf(e);
            return hk.EndsWith("_" + want, StringComparison.Ordinal);
        }

        private void ApplyLateDriversPuDoTimes(
            LateDriversTripRowTag row,
            MCDownloadedTrip trip,
            List<HiatmeAiClient.LateDriversEventRow> habits)
        {
            if (row == null) return;

            var habitPu = FindBestLateDriversHabitForTripSide(habits, trip?.TripNumber ?? row.TripNo, "pu");
            var habitDo = FindBestLateDriversHabitForTripSide(habits, trip?.TripNumber ?? row.TripNo, "do");
            var wr = FindLateDriversWrTrip(trip?.TripNumber ?? row.TripNo);

            // Live WellRyde ticket clocks first — dispatch edits land here in real time.
            // Workbook / MC morning print is fallback when WR has no sched for that side.
            string schedPu = wr != null
                ? FormatLateDriversTime(wr.SchedPuIso, blank: "—")
                : "—";
            string schedDo = wr != null
                ? FormatLateDriversTime(wr.SchedDoIso, blank: "—")
                : "—";

            if ((string.IsNullOrWhiteSpace(schedPu) || schedPu == "—") && trip != null)
                schedPu = SupeyTripTimes.FormatTimeOfDay(SupeyTripTimes.TryParsePU(trip));
            if ((string.IsNullOrWhiteSpace(schedDo) || schedDo == "—") && trip != null)
                schedDo = FormatLateDriversTripSchedDo(trip);

            if ((string.IsNullOrWhiteSpace(schedPu) || schedPu == "—") && habitPu != null)
                schedPu = FormatLateDriversTime(habitPu.SchedIso, blank: "—");
            if ((string.IsNullOrWhiteSpace(schedDo) || schedDo == "—") && habitDo != null)
                schedDo = FormatLateDriversTime(habitDo.SchedIso, blank: "—");

            // Habit-only row with a single side: still show that side's times.
            if (trip == null && row.HabitEvent != null && habitPu == null && habitDo == null)
            {
                if (LateDriversHabitIsSide(row.HabitEvent, "do"))
                    habitDo = row.HabitEvent;
                else
                    habitPu = row.HabitEvent;
                if (habitPu != null && (string.IsNullOrWhiteSpace(schedPu) || schedPu == "—"))
                    schedPu = FormatLateDriversTime(habitPu.SchedIso, blank: "—");
                if (habitDo != null && (string.IsNullOrWhiteSpace(schedDo) || schedDo == "—"))
                    schedDo = FormatLateDriversTime(habitDo.SchedIso, blank: "—");
            }

            string actPu = habitPu != null
                ? FormatLateDriversTime(habitPu.ActualIso, blank: "—")
                : "—";
            string actDo = habitDo != null
                ? FormatLateDriversTime(habitDo.ActualIso, blank: "—")
                : "—";
            // Habits only cover alert sides — fill the rest from WellRyde day trips.
            if ((string.IsNullOrWhiteSpace(actPu) || actPu == "—") && wr != null)
                actPu = FormatLateDriversTime(wr.ActualPuIso, blank: "—");
            if ((string.IsNullOrWhiteSpace(actDo) || actDo == "—") && wr != null)
                actDo = FormatLateDriversTime(wr.ActualDoIso, blank: "—");

            // Blank / midnight sched PU = will-call (common on Reserved / B-leg returns).
            if (LateDriversSchedPuIsWillCall(schedPu, trip, wr))
                schedPu = "Will call";

            row.SchedPuDisplay = string.IsNullOrWhiteSpace(schedPu) || schedPu == "—" ? "—" : schedPu;
            row.SchedDoDisplay = string.IsNullOrWhiteSpace(schedDo) || schedDo == "—" ? "—" : schedDo;
            row.ActualPuDisplay = string.IsNullOrWhiteSpace(actPu) || actPu == "—" ? "—" : actPu;
            row.ActualDoDisplay = string.IsNullOrWhiteSpace(actDo) || actDo == "—" ? "—" : actDo;
            row.SchedDisplay = !string.IsNullOrWhiteSpace(row.SchedPuDisplay) && row.SchedPuDisplay != "—"
                ? row.SchedPuDisplay
                : row.SchedDoDisplay;

            // Status: WellRyde is source of truth (late events often freeze at resolve-time
            // statuses like Pickup Departed). State Open/Closed still comes from the habit row.
            string status = "";
            string state = "—";
            if (wr != null)
                status = (wr.Status ?? "").Trim();
            if (row.HabitEvent != null)
            {
                if (string.IsNullOrEmpty(status))
                    status = (row.HabitEvent.StatusLatest ?? "").Trim();
                state = row.HabitEvent.Open ? "Open" : "Closed";
            }
            if (state == "—" && !string.IsNullOrEmpty(status))
                state = LateDriversStateFromStatus(status);
            row.StatusDisplay = string.IsNullOrEmpty(status) ? "—" : status;
            row.StateDisplay = string.IsNullOrEmpty(state) ? "—" : state;

            if (string.IsNullOrWhiteSpace(row.Client) && wr != null && !string.IsNullOrWhiteSpace(wr.Client))
                row.Client = wr.Client.Trim();
        }

        private static string LateDriversStateFromStatus(string status)
        {
            if (string.IsNullOrWhiteSpace(status))
                return "—";
            string s = status.Trim().ToLowerInvariant();
            if (s.Contains("cancel")
                || s.Contains("complete")
                || s.Contains("billed")
                || s.Contains("no show")
                || s.Contains("noshow")
                || s.Contains("no-show")
                || s.Contains("suspended")
                || s.Contains("will call")
                || s.Contains("willcall"))
                return "Closed";
            return "Open";
        }

        /// <summary>Sched DO for display — A-leg appt / B-C scheduled_dropoff (SupeyTripTimes rules).</summary>
        private static string FormatLateDriversTripSchedDo(MCDownloadedTrip trip)
        {
            if (trip == null) return "—";
            var ts = SupeyTripTimes.TryParseDO(trip);
            if (ts.HasValue)
                return SupeyTripTimes.FormatTimeOfDay(ts);
            // TryParseDO hides midnight will-calls; leave blank rather than "12:00 AM".
            string raw = PreferNonEmpty(trip.SchedDOTime, trip.DOTime);
            var parsed = SupeyTripTimes.TryParse(raw);
            if (parsed.HasValue && parsed.Value == TimeSpan.Zero)
                return "—";
            return FormatLateDriversScheduleClock(raw);
        }

        private static HiatmeAiClient.LateDriversEventRow FindBestLateDriversHabitForTrip(
            List<HiatmeAiClient.LateDriversEventRow> habits,
            string scheduleTripNo,
            string preferHabitKey = null)
        {
            var all = FindAllLateDriversHabitsForTrip(habits, scheduleTripNo, preferHabitKey);
            return all.Count > 0 ? all[0] : null;
        }

        /// <summary>
        /// Every habit on this schedule trip # (PU and DO both), stable order.
        /// Dedupes by event identity so live + habits merge never double-lists one mess-up.
        /// </summary>
        private static List<HiatmeAiClient.LateDriversEventRow> FindAllLateDriversHabitsForTrip(
            List<HiatmeAiClient.LateDriversEventRow> habits,
            string scheduleTripNo,
            string preferHabitKey = null)
        {
            var list = new List<HiatmeAiClient.LateDriversEventRow>();
            if (habits == null || string.IsNullOrWhiteSpace(scheduleTripNo))
                return list;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in habits)
            {
                if (e == null) continue;
                if (!LateDriversTripNosEqualForChip(e.TripNo, scheduleTripNo))
                    continue;
                // One row per habit kind on the trip (live + habits merge can share a side).
                string soft = ScheduleBuilderModivcareTripMatch.NormalizeTripNumber(e.TripNo)
                    + "|" + HabitKeyOf(e);
                if (string.IsNullOrWhiteSpace(soft) || soft == "|")
                    soft = LateDriversHabitIdentityKey(e);
                if (!seen.Add(soft))
                    continue;
                list.Add(e);
            }

            string prefer = (preferHabitKey ?? "").Trim().ToLowerInvariant();
            if (prefer == "all" || prefer == "open")
                prefer = "";

            list.Sort((a, b) =>
            {
                if (a == null && b == null) return 0;
                if (a == null) return 1;
                if (b == null) return -1;
                if (!string.IsNullOrEmpty(prefer))
                {
                    bool ap = HabitKeyOf(a) == prefer;
                    bool bp = HabitKeyOf(b) == prefer;
                    if (ap != bp) return ap ? -1 : 1;
                }
                bool aDo = LateDriversHabitIsSide(a, "do");
                bool bDo = LateDriversHabitIsSide(b, "do");
                if (aDo != bDo) return aDo ? 1 : -1; // PU before DO
                if (a.Open != b.Open) return a.Open ? -1 : 1;
                int cmp = LateDriversDisplayHabitMinutes(b)
                    .CompareTo(LateDriversDisplayHabitMinutes(a));
                if (cmp != 0) return cmp;
                return string.Compare(HabitKeyOf(a), HabitKeyOf(b), StringComparison.Ordinal);
            });
            return list;
        }

        private static HiatmeAiClient.LateDriversEventRow FindBestLateDriversHabitForTripSide(
            List<HiatmeAiClient.LateDriversEventRow> habits,
            string scheduleTripNo,
            string side)
        {
            if (habits == null || string.IsNullOrWhiteSpace(scheduleTripNo))
                return null;

            HiatmeAiClient.LateDriversEventRow best = null;
            foreach (var e in habits)
            {
                if (e == null) continue;
                if (!LateDriversTripNosEqualForChip(e.TripNo, scheduleTripNo))
                    continue;
                if (!LateDriversHabitIsSide(e, side))
                    continue;
                best = PreferLateDriversHabitEvent(best, e);
            }
            return best;
        }

        private static void MarkLateDriversHabitMatched(
            HiatmeAiClient.LateDriversEventRow habit,
            HashSet<string> matchedHabitKeys)
        {
            if (habit == null || matchedHabitKeys == null)
                return;
            string key = LateDriversHabitIdentityKey(habit);
            if (!string.IsNullOrEmpty(key))
                matchedHabitKeys.Add(key);
        }

        /// <summary>
        /// Mark this habit and any live/habits duplicate of the same trip + kind
        /// so orphan habit-only rows are not added twice.
        /// </summary>
        private static void MarkLateDriversHabitAndSiblingsMatched(
            HiatmeAiClient.LateDriversEventRow habit,
            List<HiatmeAiClient.LateDriversEventRow> habits,
            HashSet<string> matchedHabitKeys)
        {
            if (habit == null || matchedHabitKeys == null)
                return;
            string wantKind = HabitKeyOf(habit);
            MarkLateDriversHabitMatched(habit, matchedHabitKeys);
            if (habits == null || string.IsNullOrEmpty(wantKind))
                return;
            foreach (var sib in habits)
            {
                if (sib == null || ReferenceEquals(sib, habit))
                    continue;
                if (!LateDriversTripNosEqualForChip(sib.TripNo, habit.TripNo))
                    continue;
                if (HabitKeyOf(sib) != wantKind)
                    continue;
                MarkLateDriversHabitMatched(sib, matchedHabitKeys);
            }
        }

        private static LateDriversTripRowTag MakeLateDriversGroupHeaderRow(
            string serviceDateIso,
            SupeyTripCluster group,
            string noteText)
        {
            int n = group?.GroupNumber ?? 0;
            return new LateDriversTripRowTag
            {
                IsGroupHeader = true,
                GroupNumber = n,
                GroupLabel = (noteText ?? "").Trim(),
                GroupColor = group?.DisplayColor,
                ServiceDate = serviceDateIso,
                SortTime = DateTime.MinValue,
            };
        }

        private static SupeyTripCluster FindLateDriversGroupAfterLine(
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
                    return ScheduleBuilderPreviewGroups.FindGroupForTrip(groups, line.Trip);
            }

            return null;
        }

        private List<HiatmeAiClient.LateDriversEventRow> CollectLateDriversHabitsForDriverDay(
            string serviceDateIso,
            string driverName)
        {
            string want = (driverName ?? "").Trim();
            var habits = (_ldEventRows ?? new List<HiatmeAiClient.LateDriversEventRow>())
                .Where(e => e != null
                    && LateDriversDriverNamesMatch(
                        string.IsNullOrWhiteSpace(e.Driver) ? "(unassigned)" : e.Driver.Trim(),
                        want)
                    && (string.IsNullOrWhiteSpace(e.ServiceDate)
                        || string.Equals(e.ServiceDate.Trim(), serviceDateIso, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            foreach (var driver in _ldDriverRows ?? new List<HiatmeAiClient.LateDriversDriverSummary>())
            {
                if (driver?.Trips == null || !LateDriversDriverNamesMatch(driver.Driver, want))
                    continue;
                foreach (var e in driver.Trips)
                {
                    if (e == null) continue;
                    if (!string.IsNullOrWhiteSpace(e.ServiceDate)
                        && !string.Equals(e.ServiceDate.Trim(), serviceDateIso, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (!habits.Any(h => ReferenceEquals(h, e)
                        || (!string.IsNullOrWhiteSpace(h.EventId)
                            && string.Equals(h.EventId, e.EventId, StringComparison.OrdinalIgnoreCase))
                        || (ScheduleBuilderModivcareTripMatch.TripNumbersMatch(h.TripNo, e.TripNo)
                            && HabitKeyOf(h) == HabitKeyOf(e))))
                        habits.Add(e);
                }
            }
            return habits;
        }

        /// <summary>Exact, normalized, first+last-initial, or same schedule-tab fuzzy match.</summary>
        private bool LateDriversDriverNamesMatch(string a, string b)
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
                return false;
            string aa = a.Trim();
            string bb = b.Trim();
            if (string.Equals(aa, bb, StringComparison.OrdinalIgnoreCase))
                return true;
            if (NormalizeLateDriversDriverKey(aa) == NormalizeLateDriversDriverKey(bb))
                return true;
            if (LateDriversDriverNamesMatchFirstLastInitial(aa, bb))
                return true;

            string ka = ResolveLateDriversScheduleDriverKey(aa);
            string kb = ResolveLateDriversScheduleDriverKey(bb);
            if (!string.IsNullOrEmpty(ka) && !string.IsNullOrEmpty(kb)
                && string.Equals(ka, kb, StringComparison.OrdinalIgnoreCase))
                return true;
            if (!string.IsNullOrEmpty(ka)
                && string.Equals(ka, bb, StringComparison.OrdinalIgnoreCase))
                return true;
            if (!string.IsNullOrEmpty(kb)
                && string.Equals(kb, aa, StringComparison.OrdinalIgnoreCase))
                return true;
            return false;
        }

        /// <summary>
        /// Workbook ``Dean D`` / ``Jeffrey B`` matches WellRyde ``DEAN DAVIS`` / ``Jeffrey J Brown``.
        /// </summary>
        private static bool LateDriversDriverNamesMatchFirstLastInitial(string a, string b)
        {
            var ta = NormalizeLateDriversDriverKey(a)
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var tb = NormalizeLateDriversDriverKey(b)
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (ta.Length < 2 || tb.Length < 2)
                return false;
            if (!string.Equals(ta[0], tb[0], StringComparison.Ordinal))
                return false;

            string aLast = ta[ta.Length - 1];
            string bLast = tb[tb.Length - 1];
            // Short tab second token is often the last initial ("Dean D").
            string aSecond = ta[1];
            string bSecond = tb[1];
            if (aSecond.Length == 1 && bLast.StartsWith(aSecond, StringComparison.Ordinal))
                return true;
            if (bSecond.Length == 1 && aLast.StartsWith(bSecond, StringComparison.Ordinal))
                return true;
            if ((aSecond.Length == 1 || bSecond.Length == 1)
                && aLast.Length > 0 && bLast.Length > 0
                && aLast[0] == bLast[0])
                return true;
            return false;
        }

        /// <summary>
        /// Collapse WR full-name tiles with workbook short-name tiles for the same person.
        /// </summary>
        private void CollapseLateDriversDuplicateDriverRows()
        {
            var rows = _ldDriverRows;
            if (rows == null || rows.Count < 2)
                return;

            var used = new bool[rows.Count];
            var collapsed = new List<HiatmeAiClient.LateDriversDriverSummary>();
            for (int i = 0; i < rows.Count; i++)
            {
                if (used[i] || rows[i] == null || string.IsNullOrWhiteSpace(rows[i].Driver))
                    continue;

                var group = new List<HiatmeAiClient.LateDriversDriverSummary> { rows[i] };
                used[i] = true;
                for (int j = i + 1; j < rows.Count; j++)
                {
                    if (used[j] || rows[j] == null || string.IsNullOrWhiteSpace(rows[j].Driver))
                        continue;
                    if (group.Any(g => LateDriversDriverNamesMatch(g.Driver, rows[j].Driver)))
                    {
                        group.Add(rows[j]);
                        used[j] = true;
                    }
                }
                collapsed.Add(MergeLateDriversDriverSummaries(group));
            }

            _ldDriverRows = collapsed;
        }

        private HiatmeAiClient.LateDriversDriverSummary MergeLateDriversDriverSummaries(
            List<HiatmeAiClient.LateDriversDriverSummary> group)
        {
            if (group == null || group.Count == 0)
                return new HiatmeAiClient.LateDriversDriverSummary();

            string canonical = PreferLateDriversCanonicalDriverName(
                group.Select(g => g?.Driver).Where(s => !string.IsNullOrWhiteSpace(s)));

            if (group.Count == 1)
            {
                group[0].Driver = canonical;
                return group[0];
            }

            var trips = new List<HiatmeAiClient.LateDriversEventRow>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var g in group)
            {
                if (g?.Trips == null) continue;
                foreach (var t in g.Trips)
                {
                    if (t == null) continue;
                    string key = LateDriversHabitIdentityKey(t);
                    if (string.IsNullOrEmpty(key))
                        key = LateDriversTripKey(t.ServiceDate, t.TripNo, t.Side);
                    if (!seen.Add(key))
                        continue;
                    trips.Add(t);
                }
            }

            var merged = new HiatmeAiClient.LateDriversDriverSummary
            {
                Driver = canonical,
                Trips = trips,
            };
            merged.OpenCount = trips.Count(t => t != null && t.Open);
            merged.Unfinished = group.Sum(g => g?.Unfinished ?? 0);
            merged.UnfinishedOpen = group.Sum(g => g?.UnfinishedOpen ?? 0);

            int pu = 0, doN = 0, earlyPu = 0, earlyDo = 0;
            double lateMins = 0;
            foreach (var t in trips)
            {
                if (t == null) continue;
                string k = (t.Habit ?? t.Kind ?? "").Trim().ToLowerInvariant();
                string side = (t.Side ?? "").Trim().ToLowerInvariant();
                bool latePu = k == "late_pu" || (string.IsNullOrEmpty(k) && side == "pu");
                bool lateDo = k == "late_do" || (string.IsNullOrEmpty(k) && side == "do");
                if (latePu) { pu++; lateMins += LateDriversDisplayHabitMinutes(t); }
                else if (lateDo) { doN++; lateMins += LateDriversDisplayHabitMinutes(t); }
                else if (k == "early_pu") earlyPu++;
                else if (k == "early_do") earlyDo++;
            }
            merged.PuCount = pu;
            merged.DoCount = doN;
            merged.LateCount = pu + doN;
            merged.TotalMinutes = lateMins;
            merged.EarlyPu = earlyPu;
            merged.EarlyDo = earlyDo;
            merged.EarlyCount = earlyPu + earlyDo;
            return merged;
        }

        /// <summary>Always prefer workbook tab spelling when a schedule match exists.</summary>
        private string CanonicalizeLateDriversDriverLabel(string driverName)
        {
            if (string.IsNullOrWhiteSpace(driverName))
                return "(unassigned)";
            string want = driverName.Trim();
            if (want.Equals("(unassigned)", StringComparison.OrdinalIgnoreCase))
                return "(unassigned)";
            string key = ResolveLateDriversScheduleDriverKey(want);
            return !string.IsNullOrEmpty(key) ? key : want;
        }

        private string PreferLateDriversCanonicalDriverName(IEnumerable<string> names)
        {
            var list = (names ?? Enumerable.Empty<string>())
                .Select(n => (n ?? "").Trim())
                .Where(n => n.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (list.Count == 0)
                return "(unassigned)";

            // Always prefer printed schedule tab name when any variant resolves to one.
            foreach (string n in list)
            {
                string key = ResolveLateDriversScheduleDriverKey(n);
                if (!string.IsNullOrEmpty(key))
                    return key;
            }

            return CanonicalizeLateDriversDriverLabel(list[0]);
        }

        /// <summary>
        /// Rename every strip tile to its workbook tab and collapse WR duplicates onto it.
        /// </summary>
        private void RemapLateDriversRowsToScheduleNames()
        {
            if (_ldDriverRows == null || _ldDriverRows.Count == 0)
                return;

            foreach (var row in _ldDriverRows)
            {
                if (row == null || string.IsNullOrWhiteSpace(row.Driver))
                    continue;
                row.Driver = CanonicalizeLateDriversDriverLabel(row.Driver);
            }

            if (!string.IsNullOrWhiteSpace(_ldSelectedDriver)
                && !LateDriversIsOtherSelected(_ldSelectedDriver))
                _ldSelectedDriver = CanonicalizeLateDriversDriverLabel(_ldSelectedDriver);

            CollapseLateDriversDuplicateDriverRows();
            SortLateDriversByMinutes(_ldDriverRows);
        }

        private static int InferLateDriversGroupNumberAtTime(
            List<LateDriversTripRowTag> rows,
            DateTime sortTime)
        {
            int group = 0;
            foreach (var r in rows ?? new List<LateDriversTripRowTag>())
            {
                if (r == null) continue;
                if (r.IsGroupHeader && r.GroupNumber > 0)
                    group = r.GroupNumber;
                if (!r.IsGroupHeader && !r.IsGap && r.SortTime > sortTime)
                    return group > 0 ? group : r.GroupNumber;
                if (!r.IsGroupHeader && !r.IsGap)
                    group = r.GroupNumber > 0 ? r.GroupNumber : group;
            }
            return group;
        }

        private static Color? InferLateDriversGroupColor(List<LateDriversTripRowTag> rows, int groupNumber)
        {
            if (groupNumber <= 0 || rows == null) return null;
            foreach (var r in rows)
            {
                if (r == null || r.GroupNumber != groupNumber || !r.GroupColor.HasValue)
                    continue;
                return r.GroupColor;
            }
            return null;
        }

        /// <summary>
        /// Insert habit-only trips inside their inferred group block so gaps/headers stay intact.
        /// </summary>
        private static void InsertLateDriversRowInGroup(
            List<LateDriversTripRowTag> rows,
            LateDriversTripRowTag added)
        {
            if (rows == null || added == null) return;

            if (added.GroupNumber <= 0)
            {
                int insertAt = rows.Count;
                for (int i = 0; i < rows.Count; i++)
                {
                    var r = rows[i];
                    if (r == null || r.IsGroupHeader || r.IsGap)
                        continue;
                    if (r.SortTime > added.SortTime)
                    {
                        insertAt = i;
                        break;
                    }
                }
                rows.Insert(insertAt, added);
                return;
            }

            int headerIdx = -1;
            int firstTrip = -1;
            int lastTrip = -1;
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                if (r == null) continue;
                if (r.IsGroupHeader && r.GroupNumber == added.GroupNumber)
                    headerIdx = i;
                if (r.IsGroupHeader || r.IsGap)
                    continue;
                if (r.GroupNumber != added.GroupNumber)
                    continue;
                if (firstTrip < 0) firstTrip = i;
                lastTrip = i;
            }

            if (firstTrip < 0)
            {
                int at = headerIdx >= 0 ? headerIdx + 1 : rows.Count;
                rows.Insert(at, added);
                return;
            }

            int insertInGroup = lastTrip + 1;
            for (int i = firstTrip; i <= lastTrip; i++)
            {
                var r = rows[i];
                if (r == null || r.IsGroupHeader || r.IsGap)
                    continue;
                if (r.SortTime > added.SortTime)
                {
                    insertInGroup = i;
                    break;
                }
            }
            rows.Insert(insertInGroup, added);
        }

        private static HiatmeAiClient.LateDriversEventRow PreferLateDriversHabitEvent(
            HiatmeAiClient.LateDriversEventRow a,
            HiatmeAiClient.LateDriversEventRow b)
        {
            if (a == null) return b;
            if (b == null) return a;
            // Prefer open / late for schedule row attachment (blink state is Suite-local).
            bool aCand = LateDriversEventIsTimingAlertCandidate(a);
            bool bCand = LateDriversEventIsTimingAlertCandidate(b);
            if (aCand != bCand) return aCand ? a : b;
            if (a.Open != b.Open) return a.Open ? a : b;
            string ha = HabitKeyOf(a);
            string hb = HabitKeyOf(b);
            bool aLate = ha.StartsWith("late", StringComparison.Ordinal);
            bool bLate = hb.StartsWith("late", StringComparison.Ordinal);
            if (aLate != bLate) return aLate ? a : b;
            if (Math.Abs(LateDriversDisplayHabitMinutes(a) - LateDriversDisplayHabitMinutes(b)) > 0.01)
                return LateDriversDisplayHabitMinutes(a) >= LateDriversDisplayHabitMinutes(b) ? a : b;
            return a;
        }

        private static string PreferSchedTimeForHabit(
            MCDownloadedTrip trip,
            HiatmeAiClient.LateDriversEventRow habit)
        {
            if (habit != null && string.Equals(habit.Side, "do", StringComparison.OrdinalIgnoreCase))
            {
                string doSide = PreferNonEmpty(
                    trip?.SchedDOTime,
                    PreferNonEmpty(trip?.DOTime, null));
                if (!string.IsNullOrWhiteSpace(doSide))
                    return doSide;
            }
            return PreferNonEmpty(trip?.PUTime, PreferNonEmpty(trip?.DOTime, trip?.SchedDOTime));
        }

        private static string PreferNonEmpty(string a, string b) =>
            !string.IsNullOrWhiteSpace(a) ? a.Trim() : (b ?? "");

        private List<MCDownloadedTrip> FindLateDriversScheduleTripsForDriver(string driverName)
        {
            string key = ResolveLateDriversScheduleDriverKey(driverName);
            if (key == null || _ldScheduleCache?.DriverTrips == null)
                return null;
            if (_ldScheduleCache.DriverTrips.TryGetValue(key, out var trips) && trips != null)
                return trips;
            return new List<MCDownloadedTrip>();
        }

        private List<ScheduleBuilderPreviewLine> FindLateDriversScheduleLinesForDriver(string driverName)
        {
            string key = ResolveLateDriversScheduleDriverKey(driverName);
            if (key == null || _ldScheduleCache?.DriverLines == null)
                return null;
            if (_ldScheduleCache.DriverLines.TryGetValue(key, out var lines) && lines != null)
                return lines;
            return new List<ScheduleBuilderPreviewLine>();
        }

        /// <summary>Resolve workbook tab name for a habits driver label (exact / normalized / first+initial).</summary>
        private string ResolveLateDriversScheduleDriverKey(string driverName)
        {
            if (_ldScheduleCache == null || string.IsNullOrWhiteSpace(driverName))
                return null;

            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (_ldScheduleCache.DriverTrips != null)
            {
                foreach (var k in _ldScheduleCache.DriverTrips.Keys)
                    if (!string.IsNullOrWhiteSpace(k)) keys.Add(k);
            }
            if (_ldScheduleCache.DriverLines != null)
            {
                foreach (var k in _ldScheduleCache.DriverLines.Keys)
                    if (!string.IsNullOrWhiteSpace(k)) keys.Add(k);
            }
            if (keys.Count == 0)
                return null;

            string want = driverName.Trim();
            foreach (string k in keys)
            {
                if (string.Equals(k.Trim(), want, StringComparison.OrdinalIgnoreCase))
                    return k;
            }

            string wantKey = NormalizeLateDriversDriverKey(want);
            foreach (string k in keys)
            {
                if (NormalizeLateDriversDriverKey(k) == wantKey)
                    return k;
            }

            string wantFirst = wantKey.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
            if (wantFirst.Length >= 2)
            {
                var candidates = keys
                    .Where(k =>
                    {
                        string nk = NormalizeLateDriversDriverKey(k);
                        return nk.StartsWith(wantFirst + " ", StringComparison.Ordinal)
                            || nk == wantFirst
                            || wantKey.StartsWith(
                                (nk.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "") + " ",
                                StringComparison.Ordinal);
                    })
                    .ToList();
                if (candidates.Count == 1)
                    return candidates[0];
            }

            return null; // miss
        }

        private static string NormalizeLateDriversDriverKey(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";
            var chars = name.Trim().ToLowerInvariant()
                .Where(c => char.IsLetterOrDigit(c) || c == ' ')
                .ToArray();
            return string.Join(" ", new string(chars).Split(
                new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
        }

        private static string LateDriversOffScheduleKindLabel(string kind)
        {
            switch ((kind ?? "").Trim().ToLowerInvariant())
            {
                case "reserved": return "Reserved";
                case "unassigned": return "Unassigned";
                case "on_driver": return "On a driver";
                case "reserves": return "Reserves";
                case "cancelled": return "Cancelled";
                default: return "Reserved";
            }
        }

        private static bool LateDriversIsReservedStatus(string status)
        {
            if (string.IsNullOrWhiteSpace(status))
                return false;
            string s = status.Trim();
            return s.Equals("Reserved", StringComparison.OrdinalIgnoreCase)
                || s.Equals("Reserve", StringComparison.OrdinalIgnoreCase);
        }

        private void AddLateDriversWorkbookTripKeys(string tripNo, HashSet<string> keys)
        {
            if (keys == null || string.IsNullOrWhiteSpace(tripNo))
                return;
            string raw = tripNo.Trim().TrimStart('+');
            if (string.IsNullOrEmpty(raw))
                return;
            keys.Add(raw);
            string norm = ScheduleBuilderModivcareTripMatch.NormalizeTripNumber(raw);
            if (!string.IsNullOrEmpty(norm))
                keys.Add(norm);
            string leg = ScheduleBuilderPreviewDrag.TripLegKey(raw);
            if (!string.IsNullOrEmpty(leg))
                keys.Add(leg);
            string fmt = WellRydeFilterDataParser.FormatTripIdForScheduleMatch(raw);
            if (!string.IsNullOrEmpty(fmt))
            {
                keys.Add(fmt);
                string fmtLeg = ScheduleBuilderPreviewDrag.TripLegKey(fmt);
                if (!string.IsNullOrEmpty(fmtLeg))
                    keys.Add(fmtLeg);
            }
        }

        private static bool LateDriversIsReservesTabName(string tab)
        {
            if (string.IsNullOrWhiteSpace(tab))
                return false;
            string t = tab.Trim();
            return t.Equals("Reserves", StringComparison.OrdinalIgnoreCase)
                || t.Equals("Reserve", StringComparison.OrdinalIgnoreCase)
                || t.StartsWith("Reserve ", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Trip #s printed on a real driver sheet (not Reserves). Used so Other can show
        /// will-calls / reserve-file trips — there is no Reserves tile on Driver Habits.
        /// </summary>
        private HashSet<string> CollectLateDriversDriverSheetTripKeys()
        {
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (_ldScheduleCache == null)
                return keys;

            if (_ldScheduleCache.DriverTrips != null)
            {
                foreach (var kv in _ldScheduleCache.DriverTrips)
                {
                    if (LateDriversIsReservesTabName(kv.Key) || kv.Value == null)
                        continue;
                    foreach (var t in kv.Value)
                        AddLateDriversWorkbookTripKeys(t?.TripNumber, keys);
                }
            }
            if (_ldScheduleCache.DriverLines != null)
            {
                foreach (var kv in _ldScheduleCache.DriverLines)
                {
                    if (LateDriversIsReservesTabName(kv.Key) || kv.Value == null)
                        continue;
                    foreach (var line in kv.Value)
                        AddLateDriversWorkbookTripKeys(line?.Trip?.TripNumber, keys);
                }
            }
            return keys;
        }

        private HashSet<string> CollectLateDriversWorkbookTripKeys()
        {
            var keys = CollectLateDriversDriverSheetTripKeys();
            if (_ldScheduleCache?.ReserveFileTrips != null)
            {
                foreach (var t in _ldScheduleCache.ReserveFileTrips)
                    AddLateDriversWorkbookTripKeys(t?.TripNumber, keys);
            }
            if (_ldScheduleCache?.DriverTrips != null)
            {
                foreach (var kv in _ldScheduleCache.DriverTrips)
                {
                    if (!LateDriversIsReservesTabName(kv.Key) || kv.Value == null)
                        continue;
                    foreach (var t in kv.Value)
                        AddLateDriversWorkbookTripKeys(t?.TripNumber, keys);
                }
            }
            if (_ldScheduleCache?.DriverLines != null)
            {
                foreach (var kv in _ldScheduleCache.DriverLines)
                {
                    if (!LateDriversIsReservesTabName(kv.Key) || kv.Value == null)
                        continue;
                    foreach (var line in kv.Value)
                        AddLateDriversWorkbookTripKeys(line?.Trip?.TripNumber, keys);
                }
            }
            return keys;
        }

        private bool LateDriversTripOnWorkbook(string tripNo, HashSet<string> workbookKeys)
        {
            if (workbookKeys == null || workbookKeys.Count == 0 || string.IsNullOrWhiteSpace(tripNo))
                return false;
            string raw = tripNo.Trim().TrimStart('+');
            if (workbookKeys.Contains(raw))
                return true;
            string norm = ScheduleBuilderModivcareTripMatch.NormalizeTripNumber(raw);
            if (!string.IsNullOrEmpty(norm) && workbookKeys.Contains(norm))
                return true;
            string leg = ScheduleBuilderPreviewDrag.TripLegKey(raw);
            if (!string.IsNullOrEmpty(leg) && workbookKeys.Contains(leg))
                return true;
            string fmt = WellRydeFilterDataParser.FormatTripIdForScheduleMatch(raw);
            if (!string.IsNullOrEmpty(fmt))
            {
                if (workbookKeys.Contains(fmt))
                    return true;
                string fmtLeg = ScheduleBuilderPreviewDrag.TripLegKey(fmt);
                if (!string.IsNullOrEmpty(fmtLeg) && workbookKeys.Contains(fmtLeg))
                    return true;
            }
            // Hash lookup only — no O(n×m) fuzzy scan (that froze the driver strip).
            return false;
        }

        private void ClearLateDriversOffScheduleCache()
        {
            _ldOffScheduleCacheDateIso = null;
            _ldOffScheduleCache = null;
            _ldOffScheduleCount = 0;
        }

        /// <summary>
        /// Rebuild Reserved-tile cache after WR trips are ready. Safe to call from Present.
        /// </summary>
        private void RefreshLateDriversOffScheduleCache()
        {
            string mode = LateDriversSelectedMode();
            if (mode != "day" && mode != "live")
            {
                ClearLateDriversOffScheduleCache();
                return;
            }
            string sd = LateDriversSelectedServiceDateIso();
            ClearLateDriversOffScheduleCache();
            var rows = BuildLateDriversOffScheduleRows(sd);
            _ldOffScheduleCount = rows?.Count(r => r != null && !r.IsGroupHeader && !r.IsGap) ?? 0;
        }

        /// <summary>
        /// WellRyde trips whose live status is Reserved (not the printed Reserves sheet).
        /// </summary>
        private List<LateDriversTripRowTag> BuildLateDriversOffScheduleRows(string serviceDateIso)
        {
            serviceDateIso = (serviceDateIso ?? "").Trim();
            if (string.IsNullOrEmpty(serviceDateIso))
                return new List<LateDriversTripRowTag>();

            if (string.Equals(_ldOffScheduleCacheDateIso, serviceDateIso, StringComparison.Ordinal)
                && _ldOffScheduleCache != null)
                return _ldOffScheduleCache;

            var rows = new List<LateDriversTripRowTag>();
            if (_ldWrTripsByTripNo.Count == 0)
            {
                _ldOffScheduleCacheDateIso = serviceDateIso;
                _ldOffScheduleCache = rows;
                _ldOffScheduleCount = 0;
                return rows;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var emptyHabits = new List<HiatmeAiClient.LateDriversEventRow>();

            void Remember(string tripNo)
            {
                string dedupe = ScheduleBuilderPreviewDrag.TripLegKey(tripNo);
                if (string.IsNullOrEmpty(dedupe))
                    dedupe = WellRydeFilterDataParser.FormatTripIdForScheduleMatch(tripNo);
                if (string.IsNullOrEmpty(dedupe))
                    dedupe = ScheduleBuilderModivcareTripMatch.NormalizeTripNumber(tripNo);
                if (string.IsNullOrEmpty(dedupe))
                    dedupe = tripNo;
                seen.Add(dedupe);
            }

            bool AlreadyHave(string tripNo)
            {
                string dedupe = ScheduleBuilderPreviewDrag.TripLegKey(tripNo);
                if (!string.IsNullOrEmpty(dedupe) && seen.Contains(dedupe))
                    return true;
                string fmt = WellRydeFilterDataParser.FormatTripIdForScheduleMatch(tripNo);
                if (!string.IsNullOrEmpty(fmt))
                {
                    if (seen.Contains(fmt))
                        return true;
                    string fmtLeg = ScheduleBuilderPreviewDrag.TripLegKey(fmt);
                    if (!string.IsNullOrEmpty(fmtLeg) && seen.Contains(fmtLeg))
                        return true;
                }
                string norm = ScheduleBuilderModivcareTripMatch.NormalizeTripNumber(tripNo);
                return !string.IsNullOrEmpty(norm) && seen.Contains(norm);
            }

            foreach (var wr in _ldWrTripsByTripNo.Values.Distinct())
            {
                if (wr == null || string.IsNullOrWhiteSpace(wr.TripNo))
                    continue;
                if (!LateDriversIsReservedStatus(wr.Status))
                    continue;

                string tripNo = wr.TripNo.Trim();
                if (AlreadyHave(tripNo))
                    continue;
                Remember(tripNo);

                string driver = (wr.Driver ?? "").Trim();
                if (string.IsNullOrEmpty(driver)
                    || driver.IndexOf("unassign", StringComparison.OrdinalIgnoreCase) >= 0)
                    driver = "(unassigned)";

                DateTime sort = DateTime.MaxValue;
                if (TryParseLateDriversIso(wr.SchedPuIso, out var pu))
                    sort = pu;
                else if (TryParseLateDriversIso(wr.SchedDoIso, out var dro))
                    sort = dro;
                else if (TryParseLateDriversIso(wr.ActualPuIso, out var apu))
                    sort = apu;

                var row = new LateDriversTripRowTag
                {
                    ScheduleTrip = null,
                    HabitEvent = null,
                    FromSchedule = false,
                    HabitOnly = false,
                    OffScheduleKind = "reserved",
                    DriverDisplay = driver,
                    TripNo = tripNo,
                    ServiceDate = serviceDateIso,
                    Client = wr.Client ?? "",
                    StatusDisplay = wr.Status ?? "Reserved",
                    StateDisplay = LateDriversStateFromStatus(wr.Status),
                    SortTime = sort,
                    HabitChipKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                    HabitChipOpen = false,
                };
                ApplyLateDriversPuDoTimes(row, trip: null, habits: emptyHabits);
                rows.Add(row);
            }

            rows.Sort((a, b) =>
            {
                int cmp = a.SortTime.CompareTo(b.SortTime);
                if (cmp != 0) return cmp;
                return string.Compare(a.TripNo, b.TripNo, StringComparison.OrdinalIgnoreCase);
            });
            _ldOffScheduleCacheDateIso = serviceDateIso;
            _ldOffScheduleCache = rows;
            _ldOffScheduleCount = rows.Count;
            return rows;
        }

        private void ClearLateDriversScheduleCache()
        {
            _ldScheduleCacheDateIso = null;
            _ldScheduleCachePath = null;
            _ldScheduleCacheFileName = null;
            _ldScheduleCacheSource = null;
            _ldScheduleCacheEtag = null;
            _ldScheduleCacheFileStamp = null;
            _ldScheduleCache = null;
            _ldScheduleCacheError = null;
            ClearLateDriversOffScheduleCache();
            // Do not clear WR trips here. PresentLateDrivers loads WR first, then
            // EnsureLateDriversScheduleCache reloads the workbook and would wipe
            // status/actuals for clean (non-habit) trips.
        }

        private static string LateDriversScheduleFileStamp(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return "";
            try
            {
                var fi = new FileInfo(path);
                return fi.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture)
                    + ":"
                    + fi.Length.ToString(CultureInfo.InvariantCulture);
            }
            catch
            {
                return "";
            }
        }

        private void ClearLateDriversWrTripsCache()
        {
            _ldWrTripsDateIso = null;
            _ldWrTripsByTripNo.Clear();
            ClearLateDriversOffScheduleCache();
        }

        private async Task EnsureLateDriversWrTripsAsync(
            HiatmeAiSettings settings,
            string serviceDateIso,
            bool forceRefresh = false)
        {
            serviceDateIso = (serviceDateIso ?? "").Trim();
            if (string.IsNullOrEmpty(serviceDateIso) || settings == null)
                return;
            if (!forceRefresh
                && string.Equals(_ldWrTripsDateIso, serviceDateIso, StringComparison.Ordinal)
                && _ldWrTripsByTripNo.Count > 0)
                return;

            ClearLateDriversWrTripsCache();
            try
            {
                var doc = await HiatmeAiClient.GetTripScoutServerTripsAsync(settings, serviceDateIso)
                    .ConfigureAwait(true);
                if (doc == null || !doc.Ok || doc.Trips == null)
                    return;

                _ldWrTripsDateIso = serviceDateIso;
                foreach (var t in doc.Trips)
                {
                    if (t == null || string.IsNullOrWhiteSpace(t.TripNo))
                        continue;
                    IndexLateDriversWrTrip(t);
                }
            }
            catch { }
        }

        private void IndexLateDriversWrTrip(HiatmeAiClient.TripScoutServerTripRow t)
        {
            if (t == null || string.IsNullOrWhiteSpace(t.TripNo))
                return;

            void put(string key)
            {
                if (string.IsNullOrWhiteSpace(key))
                    return;
                key = key.Trim();
                if (_ldWrTripsByTripNo.TryGetValue(key, out var existing)
                    && LateDriversWrTripScore(t) <= LateDriversWrTripScore(existing))
                    return;
                _ldWrTripsByTripNo[key] = t;
            }

            string raw = t.TripNo.Trim();
            put(raw);
            put(ScheduleBuilderModivcareTripMatch.NormalizeTripNumber(raw));
            put(ScheduleBuilderPreviewDrag.TripLegKey(raw));
            put(WellRydeFilterDataParser.FormatTripIdForScheduleMatch(raw));
        }

        private static int LateDriversWrTripScore(HiatmeAiClient.TripScoutServerTripRow t)
        {
            if (t == null) return 0;
            return (!string.IsNullOrWhiteSpace(t.ActualPuIso) ? 1 : 0)
                + (!string.IsNullOrWhiteSpace(t.ActualDoIso) ? 1 : 0)
                + (!string.IsNullOrWhiteSpace(t.SchedPuIso) ? 1 : 0)
                + (!string.IsNullOrWhiteSpace(t.SchedDoIso) ? 1 : 0)
                + (!string.IsNullOrWhiteSpace(t.Status) ? 1 : 0);
        }

        private HiatmeAiClient.TripScoutServerTripRow FindLateDriversWrTrip(string tripNo)
        {
            if (_ldWrTripsByTripNo.Count == 0 || string.IsNullOrWhiteSpace(tripNo))
                return null;

            string raw = tripNo.Trim().TrimStart('+');
            if (_ldWrTripsByTripNo.TryGetValue(raw, out var byRaw))
                return byRaw;

            string key = ScheduleBuilderModivcareTripMatch.NormalizeTripNumber(raw);
            if (!string.IsNullOrEmpty(key) && _ldWrTripsByTripNo.TryGetValue(key, out var row))
                return row;

            string leg = ScheduleBuilderPreviewDrag.TripLegKey(raw);
            if (!string.IsNullOrEmpty(leg) && _ldWrTripsByTripNo.TryGetValue(leg, out var byLeg))
                return byLeg;

            // Schedule vs WR trip # formatting can differ slightly — fuzzy match.
            foreach (var kv in _ldWrTripsByTripNo)
            {
                if (kv.Value == null) continue;
                if (ScheduleBuilderModivcareTripMatch.TripNumbersMatch(raw, kv.Value.TripNo)
                    || ScheduleBuilderModivcareTripMatch.TripNumbersMatch(raw, kv.Key))
                    return kv.Value;
            }
            return null;
        }

        /// <summary>
        /// UI-safe schedule resolve (await HTTP). Prefer this from refresh paths.
        /// Never use ResolveForRead().GetResult() on the UI thread — off-days freeze the app.
        /// Parses the workbook off the UI thread when a reload is needed.
        /// </summary>
        private async Task EnsureLateDriversScheduleCacheAsync(
            string serviceDateIso,
            bool forceReload)
        {
            serviceDateIso = (serviceDateIso ?? "").Trim();
            if (string.IsNullOrEmpty(serviceDateIso))
            {
                _ldScheduleCacheError = "no service date";
                return;
            }

            if (!DateTime.TryParseExact(
                    serviceDateIso,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var day))
            {
                ClearLateDriversScheduleCache();
                _ldScheduleCacheDateIso = serviceDateIso;
                _ldScheduleCacheError = "bad date";
                return;
            }

            ScheduleWorkbookResolveResult resolved;
            try
            {
                resolved = await ScheduleWorkbookResolver.ResolveForReadAsync(
                        day, LateDriversAiSettings())
                    .ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                resolved = new ScheduleWorkbookResolveResult
                {
                    ServiceDateIso = serviceDateIso,
                    Error = ex.Message,
                };
            }

            if (!forceReload && LateDriversScheduleCacheFresh(serviceDateIso, resolved))
                return;

            string fullPath = resolved?.FullPath;
            ScheduleBuilderLoadResult preloaded = null;
            string preloadError = null;
            if (!string.IsNullOrWhiteSpace(fullPath) && File.Exists(fullPath))
            {
                try
                {
                    preloaded = await Task.Run(
                            () => ScheduleBuilderScheduleLoad.LoadFromWorkbook(fullPath))
                        .ConfigureAwait(true);
                }
                catch (Exception ex)
                {
                    preloadError = ex.Message;
                }
            }

            ApplyLateDriversScheduleResolve(
                serviceDateIso, day, resolved, forceReload: true, preloaded, preloadError);
        }

        /// <summary>
        /// Sync path for strip/trip clicks: Desktop or already-cached file only.
        /// Does not call the AI server (avoids UI-thread GetResult freeze on off-days).
        /// </summary>
        private void EnsureLateDriversScheduleCache(string serviceDateIso, bool forceReload)
        {
            serviceDateIso = (serviceDateIso ?? "").Trim();
            if (string.IsNullOrEmpty(serviceDateIso))
            {
                _ldScheduleCacheError = "no service date";
                return;
            }

            if (!DateTime.TryParseExact(
                    serviceDateIso,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var day))
            {
                ClearLateDriversScheduleCache();
                _ldScheduleCacheDateIso = serviceDateIso;
                _ldScheduleCacheError = "bad date";
                return;
            }

            ScheduleExportPaths.GetDefaultWorkbookSaveLocation(
                day, out _, out string fileName, out string desktopPath);
            string cachePath = ScheduleWorkbookResolver.LocalCachePath(day);
            string fullPath = null;
            string source = null;
            string etag = "";
            if (!string.IsNullOrWhiteSpace(desktopPath) && File.Exists(desktopPath))
            {
                fullPath = desktopPath;
                source = "desktop";
            }
            else if (File.Exists(cachePath))
            {
                fullPath = cachePath;
                source = "server_cache";
                etag = ScheduleWorkbookResolver.ReadCachedEtag(cachePath) ?? "";
            }

            var resolved = new ScheduleWorkbookResolveResult
            {
                FullPath = fullPath,
                FileName = fileName,
                Source = source,
                Etag = etag,
                ServiceDateIso = serviceDateIso,
                Error = string.IsNullOrWhiteSpace(fullPath)
                    ? (fileName + " missing — habits only")
                    : null,
            };
            ApplyLateDriversScheduleResolve(serviceDateIso, day, resolved, forceReload);
        }

        private bool LateDriversScheduleCacheFresh(
            string serviceDateIso,
            ScheduleWorkbookResolveResult resolved)
        {
            string fullPath = resolved?.FullPath;
            string etag = resolved?.Etag ?? "";
            string stamp = LateDriversScheduleFileStamp(fullPath);
            return string.Equals(_ldScheduleCacheDateIso, serviceDateIso, StringComparison.Ordinal)
                && string.Equals(
                    _ldScheduleCachePath ?? "",
                    fullPath ?? "",
                    StringComparison.OrdinalIgnoreCase)
                && string.Equals(_ldScheduleCacheEtag ?? "", etag, StringComparison.Ordinal)
                && string.Equals(_ldScheduleCacheFileStamp ?? "", stamp, StringComparison.Ordinal)
                && (_ldScheduleCache != null || !string.IsNullOrEmpty(_ldScheduleCacheError));
        }

        private void ApplyLateDriversScheduleResolve(
            string serviceDateIso,
            DateTime day,
            ScheduleWorkbookResolveResult resolved,
            bool forceReload,
            ScheduleBuilderLoadResult preloaded = null,
            string preloadedError = null)
        {
            string fullPath = resolved?.FullPath;
            string fileName = resolved?.FileName
                ?? ScheduleExportPaths.WorkbookFileName(
                    day.ToString("MMMM"), day.Day, day.Year);
            string etag = resolved?.Etag ?? "";
            string stamp = LateDriversScheduleFileStamp(fullPath);

            if (!forceReload
                && string.Equals(_ldScheduleCacheDateIso, serviceDateIso, StringComparison.Ordinal)
                && string.Equals(_ldScheduleCachePath ?? "", fullPath ?? "", StringComparison.OrdinalIgnoreCase)
                && string.Equals(_ldScheduleCacheEtag ?? "", etag, StringComparison.Ordinal)
                && string.Equals(_ldScheduleCacheFileStamp ?? "", stamp, StringComparison.Ordinal)
                && (_ldScheduleCache != null || !string.IsNullOrEmpty(_ldScheduleCacheError)))
                return;

            ClearLateDriversScheduleCache();
            _ldScheduleCacheDateIso = serviceDateIso;
            _ldScheduleCacheFileName = fileName;
            _ldScheduleCachePath = fullPath;
            _ldScheduleCacheSource = resolved?.Source;
            _ldScheduleCacheEtag = etag;
            _ldScheduleCacheFileStamp = stamp;

            if (string.IsNullOrWhiteSpace(fullPath) || !File.Exists(fullPath))
            {
                _ldScheduleCacheError = string.IsNullOrWhiteSpace(resolved?.Error)
                    ? (fileName + " missing — habits only")
                    : (resolved.Error + " — habits only");
                return;
            }

            if (!string.IsNullOrWhiteSpace(preloadedError) && preloaded == null)
            {
                _ldScheduleCache = null;
                _ldScheduleCacheError = "load failed (" + preloadedError + ") — habits only";
                return;
            }

            try
            {
                _ldScheduleCache = preloaded
                    ?? ScheduleBuilderScheduleLoad.LoadFromWorkbook(fullPath);
                if (_ldScheduleCache == null
                    || _ldScheduleCache.DriverTrips == null
                    || _ldScheduleCache.DriverTrips.Count == 0)
                {
                    _ldScheduleCache = null;
                    _ldScheduleCacheError = fileName + " empty — habits only";
                    return;
                }
                _ldScheduleCacheError = null;
            }
            catch (Exception ex)
            {
                _ldScheduleCache = null;
                _ldScheduleCacheError = "load failed (" + ex.Message + ") — habits only";
            }
        }

        private string FormatLateDriversScheduleStatusNote(List<LateDriversTripRowTag> merged)
        {
            string file = _ldScheduleCacheFileName ?? "schedule";
            int schedN = merged?.Count(r => r != null && r.FromSchedule) ?? 0;
            int habitN = merged?.Count(r => r?.HabitEvent != null) ?? 0;
            int addedN = merged?.Count(r => r != null && r.HabitOnly) ?? 0;
            int movedN = merged?.Count(r => r != null && r.ReassignedAway) ?? 0;
            int groupsN = merged?.Count(r => r != null && r.IsGroupHeader) ?? 0;
            string origin = string.Equals(_ldScheduleCacheSource, "server_cache", StringComparison.OrdinalIgnoreCase)
                ? "server cache"
                : (string.Equals(_ldScheduleCacheSource, "desktop", StringComparison.OrdinalIgnoreCase)
                    ? "Desktop"
                    : null);
            string note = file;
            if (!string.IsNullOrEmpty(origin))
                note += " (" + origin + ")";
            note += " · " + schedN + " trips · " + habitN + " habit alerts";
            if (groupsN > 0)
                note += " · " + groupsN + " groups";
            if (movedN > 0)
                note += " · " + movedN + " moved on WR";
            if (addedN > 0)
                note += " · " + addedN + " not on sheet";
            if (_ldWrTripsByTripNo.Count > 0)
                note += " · WR " + _ldWrTripsByTripNo.Values.Distinct().Count();
            return note;
        }

        private void AppendLateDriversScheduleStatus(string scheduleNote)
        {
            if (ldStatusLbl == null || ldStatusLbl.IsDisposed)
                return;
            string cur = ldStatusLbl.Text ?? "";
            const string marker = " · Schedule: ";
            int idx = cur.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
                cur = cur.Substring(0, idx);
            if (string.IsNullOrWhiteSpace(scheduleNote))
            {
                ldStatusLbl.Text = cur;
                return;
            }
            ldStatusLbl.Text = cur + marker + scheduleNote.Trim();
        }

        private static DateTime LateDriversScheduleSortTime(string serviceDateIso, MCDownloadedTrip trip)
        {
            string clock = PreferNonEmpty(trip?.PUTime, PreferNonEmpty(trip?.DOTime, trip?.SchedDOTime));
            return CombineLateDriversDateAndClock(serviceDateIso, clock);
        }

        private static DateTime LateDriversHabitSortTime(
            string serviceDateIso,
            HiatmeAiClient.LateDriversEventRow e)
        {
            if (!string.IsNullOrWhiteSpace(e?.SchedIso)
                && (DateTime.TryParse(e.SchedIso, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt)
                    || DateTime.TryParse(e.SchedIso, out dt)))
                return dt;
            return CombineLateDriversDateAndClock(serviceDateIso, null);
        }

        private static DateTime CombineLateDriversDateAndClock(string serviceDateIso, string clock)
        {
            DateTime day = DateTime.Today;
            if (!string.IsNullOrWhiteSpace(serviceDateIso)
                && DateTime.TryParseExact(
                    serviceDateIso.Trim(),
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var parsedDay))
                day = parsedDay.Date;

            if (string.IsNullOrWhiteSpace(clock))
                return day.AddHours(23).AddMinutes(59);

            if (DateTime.TryParse(clock.Trim(), CultureInfo.CurrentCulture, DateTimeStyles.None, out var t)
                || DateTime.TryParse(clock.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out t))
                return day.Add(t.TimeOfDay);

            return day.AddHours(23).AddMinutes(59);
        }

        private static string FormatLateDriversScheduleClock(string clock)
        {
            if (string.IsNullOrWhiteSpace(clock))
                return "—";
            // SupeyTripTimes handles Excel day-fractions and "0830"-style clocks.
            var ts = SupeyTripTimes.TryParse(clock);
            if (ts.HasValue)
                return SupeyTripTimes.FormatTimeOfDay(ts);
            if (DateTime.TryParse(clock.Trim(), CultureInfo.CurrentCulture, DateTimeStyles.None, out var t)
                || DateTime.TryParse(clock.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out t))
                return t.ToString("h:mm tt", CultureInfo.CurrentCulture);
            return clock.Trim();
        }

        private static string FormatLateDriversTime(string iso, string blank = "")
        {
            if (string.IsNullOrWhiteSpace(iso))
                return blank ?? "";
            if (DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt)
                || DateTime.TryParse(iso, out dt))
                return dt.ToString("h:mm tt", CultureInfo.CurrentCulture);
            return iso;
        }

        /// <summary>
        /// Missing or midnight scheduled pickup means will-call (not a literal dash).
        /// </summary>
        private static bool LateDriversSchedPuIsWillCall(
            string schedPuDisplay,
            MCDownloadedTrip trip,
            HiatmeAiClient.TripScoutServerTripRow wr)
        {
            if (trip != null && SupeyWillCallPickup.IsPickupWillCall(trip))
                return true;
            if (wr != null)
            {
                if (SupeyWillCallPickup.IsPickupWillCallTime(wr.SchedPuIso))
                    return true;
                // WR ticket with no PU clock (typical Reserved will-call).
                if (string.IsNullOrWhiteSpace(wr.SchedPuIso)
                    && (string.IsNullOrWhiteSpace(schedPuDisplay) || schedPuDisplay.Trim() == "—"))
                    return true;
            }
            if (SupeyWillCallPickup.IsPickupWillCallTime(schedPuDisplay))
                return true;
            // FormatLateDriversTime turns 00:00 into "12:00 AM".
            string shown = (schedPuDisplay ?? "").Trim();
            if (string.Equals(shown, "12:00 AM", StringComparison.OrdinalIgnoreCase)
                || string.Equals(shown, "12:00AM", StringComparison.OrdinalIgnoreCase))
                return true;
            return false;
        }

        private void LdTripLv_DoubleClick(object sender, EventArgs e)
        {
            if (ldTripLv?.SelectedItems == null || ldTripLv.SelectedItems.Count == 0)
                return;

            if (ldTripLv.SelectedItems[0].Tag is LateDriversTripRowTag changeRow
                && changeRow.IsChangeDetail)
                return;

            // On a driver's schedule: expand/collapse WR time/address/driver changes.
            if (LateDriversTryToggleExpandFromListItem(ldTripLv.SelectedItems[0]))
                return;

            string serviceDate = null;
            string tripNo = null;
            string driverName = null;
            if (ldTripLv.SelectedItems[0].Tag is LateDriversTripRowTag wrap
                && (wrap.IsGroupHeader || wrap.IsGap))
                return;
            var habit = LateDriversHabitFromTag(ldTripLv.SelectedItems[0].Tag);
            if (habit != null)
            {
                serviceDate = habit.ServiceDate;
                tripNo = habit.TripNo;
                driverName = habit.Driver;
            }
            else if (ldTripLv.SelectedItems[0].Tag is LateDriversTripRowTag tripWrap)
            {
                serviceDate = tripWrap.ServiceDate;
                tripNo = tripWrap.TripNo;
                driverName = tripWrap.DriverDisplay
                    ?? tripWrap.HabitEvent?.Driver
                    ?? tripWrap.ScheduleTrip?.DriverNameParsed;
            }
            if (string.IsNullOrWhiteSpace(tripNo))
                return;

            // From All drivers: open that driver's Habits schedule and land on the trip.
            if (string.IsNullOrEmpty(_ldSelectedDriver) && !string.IsNullOrWhiteSpace(driverName))
            {
                string resolved = ResolveLateDriversDriverNameForSelect(driverName);
                if (!string.IsNullOrEmpty(resolved))
                {
                    SelectLateDriversDriver(resolved, focusTripNo: tripNo);
                    return;
                }
            }

            // Already on a driver (or no driver name): jump to Trip Scout for that trip.
            try
            {
                if (tsdatepicker != null && !string.IsNullOrWhiteSpace(serviceDate)
                    && DateTime.TryParseExact(
                        serviceDate.Trim(),
                        "yyyy-MM-dd",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out var d))
                {
                    try { tsdatepicker.Value = d; } catch { }
                }
                if (tssearchbox != null)
                {
                    try { tssearchbox.Text = tripNo.Trim().TrimStart('+'); } catch { }
                }
                if (tabPage9 != null)
                    hiatmeTabControl.SelectedTab = tabPage9;
            }
            catch { }
        }

        /// <summary>Match habit driver label to a strip/roster name (exact / normalized).</summary>
        private string ResolveLateDriversDriverNameForSelect(string driverName)
        {
            if (string.IsNullOrWhiteSpace(driverName))
                return null;

            string want = driverName.Trim();
            var roster = _ldDriverRows ?? new List<HiatmeAiClient.LateDriversDriverSummary>();
            var exact = roster.FirstOrDefault(d =>
                d != null && string.Equals(d.Driver, want, StringComparison.OrdinalIgnoreCase));
            if (exact != null)
                return exact.Driver;

            // Fall back to schedule-key resolver if the roster label differs slightly.
            string key = ResolveLateDriversScheduleDriverKey(want);
            if (!string.IsNullOrEmpty(key))
            {
                var viaSched = roster.FirstOrDefault(d =>
                    d != null && string.Equals(d.Driver, key, StringComparison.OrdinalIgnoreCase));
                if (viaSched != null)
                    return viaSched.Driver;
                return key;
            }

            return want;
        }

        private void SetLateDriversStatus(string text)
        {
            if (ldStatusLbl == null || ldStatusLbl.IsDisposed)
                return;
            try { ldStatusLbl.Text = text ?? ""; } catch { }
        }
    }
}
