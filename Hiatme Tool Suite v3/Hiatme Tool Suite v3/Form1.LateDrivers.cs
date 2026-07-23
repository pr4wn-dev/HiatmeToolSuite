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
        private string _ldSelectedDriver; // null = All drivers
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
        /// <summary>Blink/chirp window after a late opens or an early actual lands.</summary>
        private const int LateDriversAlertWindowSeconds = 75;
        private const string LateDriversAlertSoundFileName = "law-and-order-alert.mp3";
        /// <summary>Event keys we already dun-dunned for (so Live refresh doesn't spam).</summary>
        private readonly HashSet<string> _ldAlertChirpKeys =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Cached SCHEDULES FOR {year} workbook for Day/Live schedule ListView merge.</summary>
        private string _ldScheduleCacheDateIso;
        private string _ldScheduleCachePath;
        private string _ldScheduleCacheFileName;
        private string _ldScheduleCacheSource;
        private string _ldScheduleCacheEtag;
        private ScheduleBuilderLoadResult _ldScheduleCache;
        private string _ldScheduleCacheError;

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
            public DateTime SortTime;
        }

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
                    ldLiveSwitch = null;
                    _ldLiveChromeHost = null;
                    _ldLiveTimerCard = null;
                    _ldLiveScanCard = null;
                    _ldLiveScan = null;
                    _ldLiveCountdown = null;
                    _ldLiveDivider = null;
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

                // Dock: Fill first, then Tops (last Top = topmost).
                ldMainCard.Controls.Add(ldStageHost);
                ldMainCard.Controls.Add(ldHeroHost);
                ldMainCard.Controls.Add(ldDriverStripHost);
                ldMainCard.Controls.Add(ldRangeCaptionLbl);
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
            if (ldLiveSwitch != null)
                ldToolbarInner.Controls.Add(ldLiveSwitch);
            if (_ldLiveChromeHost != null)
                ldToolbarInner.Controls.Add(_ldLiveChromeHost);
            if (ldRefreshBtn != null)
                ldToolbarInner.Controls.Add(ldRefreshBtn);

            ldToolbarCard.Controls.Add(ldToolbarInner);
            ldToolbar.Controls.Add(ldToolbarCard);

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

            // ── Stage: trip list (Fill) — filters live on the scorecard tiles ─
            ldStageHost = new Panel
            {
                Name = "ldStageHost",
                Dock = DockStyle.Fill,
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
            // Clicking the active tile again clears the filter.
            if (next == _ldHabitChip && next != "all")
                next = "all";
            else if (next == _ldHabitChip)
                return;
            _ldHabitChip = next;
            StyleLateDriversScoreFilters();
            BindLateDriversDriverStrip();
            RefreshLateDriversScorecard();
            BindLateDriversTripPane();
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
            }
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

            // Inner chrome is Dock-based (toolbar Top / range Top / stage Fill).
            LayoutLateDriversToolbar();
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
                var dler = new MCTripDownloader();
                downloaded = await dler.DownloadTripRecords(day, mcLoginHandler).ConfigureAwait(true);
                if (dler.InvalidDate)
                {
                    return (false,
                        "Need Modivcare schedule for " + serviceDateIso + " (date not available in portal)",
                        false);
                }
            }
            catch (Exception ex)
            {
                return (false, "Modivcare download failed: " + ex.Message, false);
            }

            if (downloaded == null || downloaded.Count == 0)
                return (false, "Need Modivcare schedule for " + serviceDateIso + " (empty download)", false);

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
                if (force && (mode == "live" || mode == "day"))
                    ClearLateDriversScheduleCache();

                if (mode == "live" || mode == "day")
                {
                    var ensured = await EnsureModivcareDaySnapshotAsync(settings, sd)
                        .ConfigureAwait(true);
                    if (!ensured.Ok)
                    {
                        SetLateDriversStatus("Status: " + ensured.Message);
                        return;
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
                        SetLateDriversStatus(
                            "Status: Live — " + st.EventCount + " habit signals today ("
                            + st.OpenCount + " still open) · unchanged · "
                            + DateTime.Now.ToString("h:mm:ss tt", CultureInfo.CurrentCulture));
                        return;
                    }
                }

                SetLateDriversStatus("Status: Loading " + mode + "…");

                string habitPeriod = mode == "live" ? "day" : mode;
                var habitsTask = HiatmeAiClient.GetLateDriversHabitsAsync(settings, habitPeriod, sd);

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
                        _ldDayPerf = doc.DayPerformance;
                        ApplyLateDriversEventPayload(
                            doc.Events ?? new List<HiatmeAiClient.LateDriversEventRow>(),
                            doc.ContentHash,
                            sd,
                            sd,
                            doc.ModivcareTripCount,
                            habits: habits);
                        SetLateDriversStatus(
                            "Status: Live " + sd + " — "
                            + (_ldEventRows?.Count ?? 0) + " events · "
                            + DateTime.Now.ToString("h:mm:ss tt", CultureInfo.CurrentCulture));
                    }
                    else
                    {
                        // Habits is the scorecard source of truth; day late-events are merged in.
                        var dayTask = HiatmeAiClient.GetLateDriversDayAsync(settings, sd);
                        var habits = await habitsTask.ConfigureAwait(true);
                        var doc = await dayTask.ConfigureAwait(true);

                        if ((doc == null || !doc.Ok) && (habits == null || !habits.Ok))
                        {
                            SetLateDriversStatus("Status: " + (doc?.Error ?? habits?.Error ?? "day load failed"));
                            return;
                        }
                        if (doc != null && doc.Ok && !doc.ModivcareExists
                            && (habits == null || !habits.Ok || (habits.EventCount <= 0 && (habits.Events == null || habits.Events.Count == 0))))
                        {
                            SetLateDriversStatus("Status: Need Modivcare schedule for " + sd);
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
                    }
                }
                else
                {
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
                    MergeLateDriversHabits(habits);
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
            LayoutLateDriversTabPanels();
            LayoutLateDriversDriverStripRow();
            BindLateDriversDriverStrip();
            RefreshLateDriversScorecard();
            RefreshLateDriversOtpMeter();
            BindLateDriversTripPane();
            UpdateLateDriversToolbarHints();
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

            foreach (string name in names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
            {
                bool exists = _ldDriverRows.Any(d =>
                    d != null
                    && string.Equals(d.Driver, name, StringComparison.OrdinalIgnoreCase));
                if (exists)
                    continue;
                _ldDriverRows.Add(new HiatmeAiClient.LateDriversDriverSummary
                {
                    Driver = name,
                    Trips = new List<HiatmeAiClient.LateDriversEventRow>(),
                });
            }

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
                    .Where(e => e != null
                        && string.Equals(
                            string.IsNullOrWhiteSpace(e.Driver) ? "(unassigned)" : e.Driver.Trim(),
                            driver,
                            StringComparison.OrdinalIgnoreCase))
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
                    if (latePu) { pu++; lateMins += Math.Max(0, t.MinutesLate); }
                    else if (lateDo) { doN++; lateMins += Math.Max(0, t.MinutesLate); }
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
                row.TotalMinutes += e.MinutesLate;
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
            list.Sort((a, b) =>
            {
                if (a == null && b == null) return 0;
                if (a == null) return 1;
                if (b == null) return -1;
                int cmp = b.UnfinishedOpen.CompareTo(a.UnfinishedOpen);
                if (cmp != 0) return cmp;
                cmp = b.TotalMinutes.CompareTo(a.TotalMinutes);
                if (cmp != 0) return cmp;
                cmp = b.LateCount.CompareTo(a.LateCount);
                if (cmp != 0) return cmp;
                return b.EarlyCount.CompareTo(a.EarlyCount);
            });
        }

        private List<HiatmeAiClient.LateDriversDriverSummary> FilteredLateDrivers()
        {
            var src = (_ldDriverRows ?? new List<HiatmeAiClient.LateDriversDriverSummary>())
                .Where(d => d != null)
                .ToList();
            string chip = (_ldHabitChip ?? "all").Trim().ToLowerInvariant();
            if (chip == "all")
                return src;
            if (chip == "open")
                return src.Where(d => d.OpenCount > 0 || d.UnfinishedOpen > 0
                    || (d.Trips != null && d.Trips.Any(t => t != null && t.Open))).ToList();
            if (chip == "early_pu")
                return src.Where(d => d.EarlyPu > 0
                    || DriverHasHabitTrip(d, "early_pu")).ToList();
            if (chip == "early_do")
                return src.Where(d => d.EarlyDo > 0
                    || DriverHasHabitTrip(d, "early_do")).ToList();
            if (chip == "late_pu")
                return src.Where(d => d.PuCount > 0
                    || DriverHasHabitTrip(d, "late_pu")).ToList();
            if (chip == "late_do")
                return src.Where(d => d.DoCount > 0
                    || DriverHasHabitTrip(d, "late_do")).ToList();
            if (chip == "unfinished_ticket")
                return src.Where(d => d.UnfinishedOpen > 0
                    || DriverHasHabitTrip(d, "unfinished_ticket")).ToList();
            if (chip == "billed_unfinished")
                return src.Where(d => DriverHasHabitTrip(d, "billed_unfinished")).ToList();
            return src.Where(d => DriverHasHabitTrip(d, chip)).ToList();
        }

        private static bool DriverHasHabitTrip(
            HiatmeAiClient.LateDriversDriverSummary d, string habitKey)
        {
            if (d?.Trips == null || string.IsNullOrEmpty(habitKey))
                return false;
            return d.Trips.Any(t => t != null && HabitKeyOf(t) == habitKey);
        }

        private void BindLateDriversDriverStrip()
        {
            if (ldDriverStrip == null || ldDriverStrip.IsDisposed)
                return;

            var rows = FilteredLateDrivers();
            string keep = _ldSelectedDriver;
            // Drop selection if filtered out
            if (!string.IsNullOrEmpty(keep)
                && !rows.Any(d => string.Equals(d.Driver, keep, StringComparison.OrdinalIgnoreCase)))
                keep = null;

            _ldStripDrivers = rows ?? new List<HiatmeAiClient.LateDriversDriverSummary>();
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
            // Reserve one slot for the pinned "All drivers" tile.
            int totalSlots = Math.Max(1, inner / Math.Max(1, slot));
            return Math.Max(1, totalSlots - 1);
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
                bool canPage = rows.Count > Math.Max(1, slotsNoNav - 1);
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

        private static bool LateDriversEarlyStillHot(HiatmeAiClient.LateDriversEventRow e)
        {
            return LateDriversTsStillHot(LateDriversEarlyEventTs(e));
        }

        /// <summary>
        /// Late blink: ~75s after open detect, or right after a late actual/resolve
        /// so a just-completed late still flashes once.
        /// </summary>
        private static bool LateDriversLateStillHot(HiatmeAiClient.LateDriversEventRow e)
        {
            if (e == null || e.Excluded)
                return false;
            if (e.Open)
                return LateDriversTsStillHot(e.DetectedAt);
            // Closed late: flash from actual (or resolve) so we don't miss quick completes.
            if (!string.IsNullOrWhiteSpace(e.ActualIso)
                && LateDriversTsStillHot(LateDriversEarlyEventTs(e)))
                return true;
            return LateDriversTsStillHot(e.ResolvedAt);
        }

        private static bool LateDriversEventNeedsCallAlert(HiatmeAiClient.LateDriversEventRow e)
        {
            if (e == null || e.Excluded)
                return false;
            string hk = HabitKeyOf(e);
            if (!LateDriversIsTimingHabitKey(hk))
                return false;
            if (hk.StartsWith("early", StringComparison.Ordinal))
                return LateDriversEarlyStillHot(e);
            return LateDriversLateStillHot(e);
        }

        private static string LateDriversAlertEventKey(HiatmeAiClient.LateDriversEventRow e)
        {
            if (e == null) return "";
            if (!string.IsNullOrWhiteSpace(e.EventId))
                return e.EventId.Trim();
            return (e.ServiceDate ?? "").Trim() + "|"
                + (e.TripNo ?? "").Trim() + "|"
                + (e.Side ?? "").Trim().ToLowerInvariant() + "|"
                + HabitKeyOf(e);
        }

        private static bool LateDriversDriverNeedsCallAlert(
            HiatmeAiClient.LateDriversDriverSummary summary)
        {
            if (summary?.Trips == null || summary.Trips.Count == 0)
                return false;
            return summary.Trips.Any(LateDriversEventNeedsCallAlert);
        }

        private HashSet<string> CollectLateDriversActiveAlertKeys()
        {
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var d in _ldDriverRows ?? new List<HiatmeAiClient.LateDriversDriverSummary>())
            {
                if (d?.Trips == null) continue;
                foreach (var e in d.Trips)
                {
                    if (!LateDriversEventNeedsCallAlert(e))
                        continue;
                    string key = LateDriversAlertEventKey(e);
                    if (!string.IsNullOrEmpty(key))
                        keys.Add(key);
                }
            }
            return keys;
        }

        private void MaybePlayLateDriversAlertChirp(HashSet<string> activeKeys)
        {
            if (activeKeys == null || activeKeys.Count == 0)
            {
                _ldAlertChirpKeys.Clear();
                return;
            }

            bool fresh = false;
            foreach (string key in activeKeys)
            {
                if (_ldAlertChirpKeys.Add(key))
                    fresh = true;
            }
            // Drop keys that are no longer alerting so a later re-open can chirp again.
            _ldAlertChirpKeys.RemoveWhere(k => !activeKeys.Contains(k));

            if (fresh)
                TryPlayLateDriversAlertSoundOnce();
        }

        private static void TryPlayLateDriversAlertSoundOnce()
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory ?? "";
                if (string.IsNullOrEmpty(baseDir))
                    return;
                string path = Path.Combine(baseDir, "Resources", LateDriversAlertSoundFileName);
                if (!File.Exists(path))
                    path = Path.Combine(baseDir, LateDriversAlertSoundFileName);
                if (!File.Exists(path))
                    return;

                string fullPath = Path.GetFullPath(path);
                var playThread = new Thread(() =>
                {
                    try
                    {
                        using (var reader = new MediaFoundationReader(fullPath))
                        using (var output = new WasapiOut(AudioClientShareMode.Shared, 150))
                        {
                            output.Init(reader);
                            output.Play();
                            while (output.PlaybackState == PlaybackState.Playing)
                                Thread.Sleep(25);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine("Driver Habits alert sound: " + ex);
                    }
                })
                {
                    IsBackground = true,
                    Name = "LateDriversAlertSound",
                };
                playThread.SetApartmentState(ApartmentState.STA);
                playThread.Start();
            }
            catch
            {
                /* optional audio */
            }
        }

        private static bool LateDriversDriverAlertIsEarly(
            HiatmeAiClient.LateDriversDriverSummary summary)
        {
            if (summary?.Trips == null)
                return false;
            bool anyEarly = false;
            bool anyLateOpen = false;
            foreach (var e in summary.Trips)
            {
                if (e == null || e.Excluded) continue;
                string hk = HabitKeyOf(e);
                if (hk == "late_pu" || hk == "late_do")
                {
                    if (LateDriversLateStillHot(e)) anyLateOpen = true;
                }
                else if (hk == "early_pu" || hk == "early_do")
                {
                    if (LateDriversEarlyStillHot(e))
                        anyEarly = true;
                }
            }
            // Prefer late (red) when both; early alone → amber.
            return anyEarly && !anyLateOpen;
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

        private static Color LateDriversAlertFlashColor(bool earlyOnly)
        {
            return earlyOnly
                ? Color.FromArgb(210, 140, 40)
                : Color.FromArgb(220, 70, 55);
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
                    : LateDriversDriverNeedsCallAlert(summary);

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

                bool earlyOnly = summary != null && LateDriversDriverAlertIsEarly(summary);
                Color flash = LateDriversAlertFlashColor(earlyOnly);
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
                if (item.Tag is LateDriversTripRowTag sep && (sep.IsGroupHeader || sep.IsGap))
                    continue;
                var row = LateDriversHabitFromTag(item.Tag);
                bool alert = LateDriversEventNeedsCallAlert(row);
                if (!alert || !_ldDriverAlertBlinkOn)
                {
                    if (item.BackColor != Color.Empty)
                        item.BackColor = Color.Empty;
                    continue;
                }

                anyHot = true;
                string hk = HabitKeyOf(row);
                bool early = hk.StartsWith("early", StringComparison.Ordinal);
                Color flash = LateDriversAlertFlashColor(early);
                if (item.BackColor != flash)
                    item.BackColor = flash;
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
            var nameLbl = new Label
            {
                Name = "ldTileName",
                AutoSize = false,
                Dock = DockStyle.Top,
                Height = 22,
                Text = title ?? "",
                TextAlign = ContentAlignment.MiddleLeft,
                BackColor = Color.Transparent,
                ForeColor = SupeyTheme.TextPrimary,
                Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold),
            };
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
            card.Controls.Add(nameLbl);

            void pick(object s, EventArgs e)
            {
                var chosen = card.Tag as HiatmeAiClient.LateDriversDriverSummary;
                SelectLateDriversDriver(chosen?.Driver, focusTripNo: null);
            }
            card.Click += pick;
            foreach (Control c in card.Controls)
                c.Click += pick;

            return card;
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

            _ldSelectedDriver = next;

            if (changed && !string.IsNullOrEmpty(next))
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

            if (!string.IsNullOrWhiteSpace(focusTripNo))
                FocusLateDriversTripInList(focusTripNo);
        }

        private void FocusLateDriversTripInList(string tripNo)
        {
            if (ldTripLv == null || ldTripLv.IsDisposed || string.IsNullOrWhiteSpace(tripNo))
                return;

            string want = ScheduleBuilderModivcareTripMatch.NormalizeTripNumber(
                tripNo.Trim().TrimStart('+'));
            if (string.IsNullOrEmpty(want))
                return;

            ListViewItem match = null;
            foreach (ListViewItem item in ldTripLv.Items)
            {
                if (item?.Tag is LateDriversTripRowTag wrap)
                {
                    if (wrap.IsGroupHeader || wrap.IsGap)
                        continue;
                    string tn = ScheduleBuilderModivcareTripMatch.NormalizeTripNumber(
                        (wrap.TripNo ?? "").Trim().TrimStart('+'));
                    if (string.Equals(tn, want, StringComparison.OrdinalIgnoreCase))
                    {
                        match = item;
                        break;
                    }
                }
                else
                {
                    var habit = LateDriversHabitFromTag(item?.Tag);
                    string tn = ScheduleBuilderModivcareTripMatch.NormalizeTripNumber(
                        (habit?.TripNo ?? "").Trim().TrimStart('+'));
                    if (string.Equals(tn, want, StringComparison.OrdinalIgnoreCase))
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

        private void StyleLateDriversDriverTiles()
        {
            foreach (var tile in _ldDriverTiles)
            {
                if (tile == null || tile.IsDisposed)
                    continue;
                var summary = tile.Tag as HiatmeAiClient.LateDriversDriverSummary;
                bool selected = summary == null
                    ? string.IsNullOrEmpty(_ldSelectedDriver)
                    : string.Equals(summary.Driver, _ldSelectedDriver, StringComparison.OrdinalIgnoreCase);
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

        private void SetLateDriversScoreValue(string key, string text)
        {
            if (_ldScoreValues.TryGetValue(key, out var lbl) && lbl != null && !lbl.IsDisposed)
                lbl.Text = text ?? "0";
        }

        private static string HabitKeyOf(HiatmeAiClient.LateDriversEventRow e)
        {
            if (e == null) return "";
            string h = (e.Habit ?? e.Kind ?? "").Trim().ToLowerInvariant();
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

        private void BindLateDriversTripPane()
        {
            if (ldTripLv == null || ldTripLv.IsDisposed)
                return;

            string mode = LateDriversSelectedMode();
            bool singleDay = mode == "day" || mode == "live";
            // All drivers = habit alerts only (late/early/etc.). Full schedule is per-driver.
            bool useSchedule = singleDay && !string.IsNullOrWhiteSpace(_ldSelectedDriver);

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

            string chip = (_ldHabitChip ?? "all").Trim().ToLowerInvariant();
            if (chip == "open")
                trips = trips.Where(e => e != null && e.Open).ToList();
            else if (chip != "all")
                trips = trips.Where(e => HabitKeyOf(e) == chip).ToList();

            trips = trips
                .OrderByDescending(e => e.Open)
                .ThenByDescending(e => e.MinutesLate)
                .ThenBy(e => e.Driver ?? "", StringComparer.OrdinalIgnoreCase)
                .ThenBy(e => e.ServiceDate ?? "")
                .ThenBy(e => e.TripNo ?? "")
                .ToList();

            ldTripLv.BeginUpdate();
            try
            {
                ldTripLv.Items.Clear();
                foreach (var e in trips)
                    ldTripLv.Items.Add(CreateLateDriversHabitListItem(e, showDriver));
            }
            finally
            {
                ldTripLv.EndUpdate();
            }
            ApplyLateDriversTripAlertBlinkPhase();
        }

        private void BindLateDriversMergedTripRows(List<LateDriversTripRowTag> rows, bool showDriver)
        {
            EnsureLateDriversTripColumns(showDriver);

            string chip = (_ldHabitChip ?? "all").Trim().ToLowerInvariant();
            ldTripLv.BeginUpdate();
            try
            {
                ldTripLv.Items.Clear();
                foreach (var row in rows ?? new List<LateDriversTripRowTag>())
                {
                    if (row == null) continue;
                    var item = CreateLateDriversMergedListItem(row, showDriver);
                    bool chipMatch = LateDriversMergedRowMatchesChip(row, chip);
                    if (chip != "all" && !chipMatch && !row.IsGroupHeader && !row.IsGap)
                        item.ForeColor = SupeyTheme.TextMuted;
                    ldTripLv.Items.Add(item);
                }
            }
            finally
            {
                ldTripLv.EndUpdate();
            }
            ApplyLateDriversTripAlertBlinkPhase();
        }

        /// <summary>
        /// Trip grid columns. Driver is shown only on the all-drivers habit list.
        /// </summary>
        private void EnsureLateDriversTripColumns(bool showDriver)
        {
            if (ldTripLv == null || ldTripLv.IsDisposed)
                return;

            // Layout without Driver: Group, Date, Habit, Trip… → col 3 is Trip
            // Layout with Driver:    Group, Date, Habit, Driver, Trip… → col 3 is Driver
            bool hasDriverCol = ldTripLv.Columns.Count > 3
                && string.Equals(ldTripLv.Columns[3].Text, "Driver", StringComparison.OrdinalIgnoreCase);
            if (ldTripLv.Columns.Count > 0 && hasDriverCol == showDriver)
                return;

            ldTripLv.BeginUpdate();
            try
            {
                ldTripLv.Items.Clear();
                ldTripLv.Columns.Clear();
                ldTripLv.Columns.Add("Group", 52);
                ldTripLv.Columns.Add("Date", 90);
                ldTripLv.Columns.Add("Habit", 78);
                if (showDriver)
                    ldTripLv.Columns.Add("Driver", 140);
                ldTripLv.Columns.Add("Trip", 120);
                ldTripLv.Columns.Add("Client", 160);
                ldTripLv.Columns.Add("Sched", 90);
                ldTripLv.Columns.Add("Actual", 90);
                ldTripLv.Columns.Add("Mins", 60);
                ldTripLv.Columns.Add("Status", 130);
                ldTripLv.Columns.Add("State", 80);
            }
            finally
            {
                ldTripLv.EndUpdate();
            }
        }

        private static bool LateDriversMergedRowMatchesChip(LateDriversTripRowTag row, string chip)
        {
            // Keep group/gap separators visible while filtering trips.
            if (row != null && (row.IsGroupHeader || row.IsGap))
                return true;
            if (row?.HabitEvent == null) return chip == "all";
            if (chip == "all") return true;
            if (chip == "open") return row.HabitEvent.Open;
            return HabitKeyOf(row.HabitEvent) == chip;
        }

        private ListViewItem CreateLateDriversHabitListItem(
            HiatmeAiClient.LateDriversEventRow e,
            bool showDriver)
        {
            string habit = HabitLabelOf(HabitKeyOf(e));
            string driver = string.IsNullOrWhiteSpace(e?.Driver)
                ? "(unassigned)"
                : e.Driver.Trim();
            var item = new ListViewItem("—");
            item.SubItems.Add(e.ServiceDate ?? "");
            item.SubItems.Add(habit);
            if (showDriver)
                item.SubItems.Add(driver);
            item.SubItems.Add(e.TripNo ?? "");
            item.SubItems.Add(e.Client ?? "");
            item.SubItems.Add(FormatLateDriversTime(e.SchedIso, blank: "—"));
            item.SubItems.Add(FormatLateDriversTime(e.ActualIso, blank: "—"));
            bool noActual = string.IsNullOrWhiteSpace(e.ActualIso);
            string minsText = noActual && e.Open
                ? "—"
                : e.MinutesLate.ToString("0", CultureInfo.InvariantCulture) + "m";
            item.SubItems.Add(minsText);
            item.SubItems.Add(e.StatusLatest ?? "");
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

        private ListViewItem CreateLateDriversMergedListItem(LateDriversTripRowTag row, bool showDriver)
        {
            int trailing = showDriver ? 8 : 7;

            if (row.IsGap)
            {
                // Blank spacer (or note in Habit col) — mirrors Schedule Builder gap rows.
                string note = (row.GroupLabel ?? "").Trim();
                var gap = new ListViewItem("");
                gap.UseItemStyleForSubItems = false;
                gap.SubItems.Add("");
                gap.SubItems.Add(note);
                if (showDriver)
                    gap.SubItems.Add("");
                for (int c = 0; c < trailing - (showDriver ? 1 : 0); c++)
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
                // Colored bar like Schedule Builder group headers.
                Color bar = row.GroupColor ?? SupeyTheme.SurfaceElevated;
                string gLabel = row.GroupNumber > 0 ? ("G" + row.GroupNumber) : "";
                string note = (row.GroupLabel ?? "").Trim();
                var hdr = new ListViewItem(gLabel);
                hdr.UseItemStyleForSubItems = false;
                hdr.SubItems.Add("");
                hdr.SubItems.Add(note);
                if (showDriver)
                    hdr.SubItems.Add(row.DriverDisplay ?? "");
                for (int c = 0; c < trailing - (showDriver ? 1 : 0); c++)
                    hdr.SubItems.Add("");
                hdr.Tag = row;
                Color fg = ScheduleBuilderPreviewStyle.ContrastText(bar);
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
            string habitLabel = habit != null ? HabitLabelOf(HabitKeyOf(habit)) : "—";
            string tripNo = row.TripNo ?? "";
            if (row.HabitOnly && !string.IsNullOrEmpty(tripNo) && !tripNo.StartsWith("+", StringComparison.Ordinal))
                tripNo = "+" + tripNo;
            string groupCol = row.GroupNumber > 0 ? ("G" + row.GroupNumber) : "—";
            string driverCol = !string.IsNullOrWhiteSpace(row.DriverDisplay)
                ? row.DriverDisplay.Trim()
                : (habit?.Driver ?? "").Trim();

            var item = new ListViewItem(groupCol);
            item.UseItemStyleForSubItems = false;
            item.SubItems.Add(date);
            item.SubItems.Add(habitLabel);
            if (showDriver)
                item.SubItems.Add(string.IsNullOrEmpty(driverCol) ? "—" : driverCol);
            item.SubItems.Add(tripNo);
            item.SubItems.Add(row.Client ?? "");
            item.SubItems.Add(string.IsNullOrWhiteSpace(row.SchedDisplay) ? "—" : row.SchedDisplay);
            if (habit != null)
            {
                item.SubItems.Add(FormatLateDriversTime(habit.ActualIso, blank: "—"));
                bool noActual = string.IsNullOrWhiteSpace(habit.ActualIso);
                string minsText = noActual && habit.Open
                    ? "—"
                    : habit.MinutesLate.ToString("0", CultureInfo.InvariantCulture) + "m";
                item.SubItems.Add(minsText);
                item.SubItems.Add(habit.StatusLatest ?? "");
                item.SubItems.Add(habit.Open ? "Open" : "Closed");
                string hk = HabitKeyOf(habit);
                if (habit.Open)
                    item.ForeColor = Color.FromArgb(200, 80, 60);
                else if (hk.StartsWith("early", StringComparison.Ordinal))
                    item.ForeColor = Color.FromArgb(180, 120, 40);
                else if (hk == "unfinished_ticket" || hk == "billed_unfinished")
                    item.ForeColor = Color.FromArgb(160, 90, 40);
                else if (row.HabitOnly)
                    item.ForeColor = Color.FromArgb(120, 160, 220);
            }
            else
            {
                item.SubItems.Add("—");
                item.SubItems.Add("—");
                item.SubItems.Add("");
                item.SubItems.Add("");
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

            var habitByTrip = new Dictionary<string, HiatmeAiClient.LateDriversEventRow>(
                StringComparer.OrdinalIgnoreCase);
            foreach (var e in habits)
            {
                string key = ScheduleBuilderModivcareTripMatch.NormalizeTripNumber(e.TripNo);
                if (string.IsNullOrEmpty(key)) continue;
                if (!habitByTrip.TryGetValue(key, out var existing)
                    || PreferLateDriversHabitEvent(e, existing) == e)
                    habitByTrip[key] = e;
            }

            var matchedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var rows = new List<LateDriversTripRowTag>();

            if (lines != null && lines.Count > 0)
            {
                // Same grouping as Schedule Builder: batches between Gap/GroupHeader rows.
                var groups = ScheduleBuilderPreviewGroups.BuildFromPreviewLines(lines);
                SupeyTripCluster lastHeaderGroup = null;
                bool sawTripRow = false;

                for (int li = 0; li < lines.Count; li++)
                {
                    var line = lines[li];
                    if (line == null) continue;

                    if (line.Kind == ScheduleBuilderPreviewLine.LineKind.Gap)
                    {
                        lastHeaderGroup = null;
                        // Skip trailing pad spam (SB keeps 12 blank pads; Habits does not need them).
                        if (line.TrailingPad)
                            continue;
                        if (ScheduleBuilderGapNotes.HasNoteContent(line))
                        {
                            rows.Add(new LateDriversTripRowTag
                            {
                                IsGap = true,
                                GroupLabel = (line.GapNoteText ?? "").Trim(),
                                ServiceDate = serviceDateIso,
                                SortTime = DateTime.MinValue,
                            });
                        }
                        else if (sawTripRow)
                        {
                            // Blank spacer between groups — matches SB "Show gap rows".
                            rows.Add(new LateDriversTripRowTag
                            {
                                IsGap = true,
                                GroupLabel = "",
                                ServiceDate = serviceDateIso,
                                SortTime = DateTime.MinValue,
                            });
                        }
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
                        else if (sawTripRow)
                        {
                            rows.Add(new LateDriversTripRowTag
                            {
                                IsGap = true,
                                GroupLabel = (line.GroupNoteText ?? "").Trim(),
                                ServiceDate = serviceDateIso,
                                SortTime = DateTime.MinValue,
                            });
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

                    string key = ScheduleBuilderModivcareTripMatch.NormalizeTripNumber(trip.TripNumber);
                    habitByTrip.TryGetValue(key, out var habit);
                    if (!string.IsNullOrEmpty(key) && habit != null)
                        matchedKeys.Add(key);

                    rows.Add(new LateDriversTripRowTag
                    {
                        ScheduleTrip = trip,
                        HabitEvent = habit,
                        FromSchedule = true,
                        HabitOnly = false,
                        GroupNumber = g.GroupNumber,
                        GroupLabel = "G" + g.GroupNumber,
                        GroupColor = g.DisplayColor,
                        TripNo = trip.TripNumber?.Trim() ?? "",
                        ServiceDate = serviceDateIso,
                        Client = trip.ClientFullName ?? habit?.Client ?? "",
                        SchedDisplay = FormatLateDriversScheduleClock(
                            PreferSchedTimeForHabit(trip, habit)),
                        SortTime = LateDriversScheduleSortTime(serviceDateIso, trip),
                    });
                    sawTripRow = true;
                }
            }
            else
            {
                foreach (var trip in (scheduleTrips ?? new List<MCDownloadedTrip>())
                    .Where(t => t != null && !string.IsNullOrWhiteSpace(t.TripNumber))
                    .OrderBy(t => LateDriversScheduleSortTime(serviceDateIso, t))
                    .ThenBy(t => t.TripNumber ?? "", StringComparer.OrdinalIgnoreCase))
                {
                    string key = ScheduleBuilderModivcareTripMatch.NormalizeTripNumber(trip.TripNumber);
                    habitByTrip.TryGetValue(key, out var habit);
                    if (!string.IsNullOrEmpty(key) && habit != null)
                        matchedKeys.Add(key);

                    rows.Add(new LateDriversTripRowTag
                    {
                        ScheduleTrip = trip,
                        HabitEvent = habit,
                        FromSchedule = true,
                        HabitOnly = false,
                        TripNo = trip.TripNumber?.Trim() ?? "",
                        ServiceDate = serviceDateIso,
                        Client = trip.ClientFullName ?? habit?.Client ?? "",
                        SchedDisplay = FormatLateDriversScheduleClock(
                            PreferSchedTimeForHabit(trip, habit)),
                        SortTime = LateDriversScheduleSortTime(serviceDateIso, trip),
                    });
                }
            }

            // Habit-only: insert by sched time, inherit nearby group number.
            foreach (var e in habits)
            {
                string key = ScheduleBuilderModivcareTripMatch.NormalizeTripNumber(e.TripNo);
                if (!string.IsNullOrEmpty(key) && matchedKeys.Contains(key))
                    continue;
                if (!string.IsNullOrEmpty(key)
                    && habitByTrip.TryGetValue(key, out var primary)
                    && !ReferenceEquals(primary, e)
                    && PreferLateDriversHabitEvent(primary, e) == primary)
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
                    TripNo = e.TripNo?.Trim() ?? "",
                    ServiceDate = string.IsNullOrWhiteSpace(e.ServiceDate) ? serviceDateIso : e.ServiceDate.Trim(),
                    Client = e.Client ?? "",
                    SchedDisplay = FormatLateDriversTime(e.SchedIso, blank: "—"),
                    SortTime = sort,
                };
                InsertLateDriversRowInGroup(rows, added);
            }

            return rows;
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
            var habits = (_ldEventRows ?? new List<HiatmeAiClient.LateDriversEventRow>())
                .Where(e => e != null
                    && string.Equals(
                        string.IsNullOrWhiteSpace(e.Driver) ? "(unassigned)" : e.Driver.Trim(),
                        driverName.Trim(),
                        StringComparison.OrdinalIgnoreCase)
                    && (string.IsNullOrWhiteSpace(e.ServiceDate)
                        || string.Equals(e.ServiceDate.Trim(), serviceDateIso, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            var driver = (_ldDriverRows ?? new List<HiatmeAiClient.LateDriversDriverSummary>())
                .FirstOrDefault(d => d != null
                    && string.Equals(d.Driver, driverName, StringComparison.OrdinalIgnoreCase));
            if (driver?.Trips == null)
                return habits;

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
            return habits;
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
            if (a.Open != b.Open) return a.Open ? a : b;
            string ha = HabitKeyOf(a);
            string hb = HabitKeyOf(b);
            bool aLate = ha.StartsWith("late", StringComparison.Ordinal);
            bool bLate = hb.StartsWith("late", StringComparison.Ordinal);
            if (aLate != bLate) return aLate ? a : b;
            if (Math.Abs(a.MinutesLate - b.MinutesLate) > 0.01)
                return a.MinutesLate >= b.MinutesLate ? a : b;
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

            return want; // miss — callers treat as empty sheet
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

        private void ClearLateDriversScheduleCache()
        {
            _ldScheduleCacheDateIso = null;
            _ldScheduleCachePath = null;
            _ldScheduleCacheFileName = null;
            _ldScheduleCacheSource = null;
            _ldScheduleCacheEtag = null;
            _ldScheduleCache = null;
            _ldScheduleCacheError = null;
        }

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

            var resolved = ScheduleWorkbookResolver.ResolveForRead(day, LateDriversAiSettings());
            string fullPath = resolved?.FullPath;
            string fileName = resolved?.FileName
                ?? ScheduleExportPaths.WorkbookFileName(
                    day.ToString("MMMM"), day.Day, day.Year);
            string etag = resolved?.Etag ?? "";

            if (!forceReload
                && string.Equals(_ldScheduleCacheDateIso, serviceDateIso, StringComparison.Ordinal)
                && string.Equals(_ldScheduleCachePath ?? "", fullPath ?? "", StringComparison.OrdinalIgnoreCase)
                && string.Equals(_ldScheduleCacheEtag ?? "", etag, StringComparison.Ordinal)
                && (_ldScheduleCache != null || !string.IsNullOrEmpty(_ldScheduleCacheError)))
                return;

            ClearLateDriversScheduleCache();
            _ldScheduleCacheDateIso = serviceDateIso;
            _ldScheduleCacheFileName = fileName;
            _ldScheduleCachePath = fullPath;
            _ldScheduleCacheSource = resolved?.Source;
            _ldScheduleCacheEtag = etag;

            if (string.IsNullOrWhiteSpace(fullPath) || !File.Exists(fullPath))
            {
                _ldScheduleCacheError = string.IsNullOrWhiteSpace(resolved?.Error)
                    ? (fileName + " missing — habits only")
                    : (resolved.Error + " — habits only");
                return;
            }

            try
            {
                // Sync load only — async+GetResult on the UI thread deadlocks on Task.Yield.
                _ldScheduleCache = ScheduleBuilderScheduleLoad.LoadFromWorkbook(fullPath);
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
            if (addedN > 0)
                note += " · " + addedN + " not on sheet";
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

        private void LdTripLv_DoubleClick(object sender, EventArgs e)
        {
            if (ldTripLv?.SelectedItems == null || ldTripLv.SelectedItems.Count == 0)
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
