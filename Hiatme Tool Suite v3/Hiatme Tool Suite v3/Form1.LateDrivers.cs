using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Forms.DataVisualization.Charting;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>Driver Habits — Live/Day/Week/Month/Year scorecard with roster, trip detail, charts.</summary>
    partial class Form1
    {
        // tabPageLateDrivers is declared in Form1.Designer.cs (always under Trip Scout).
        private SupeyCard ldMainCard;
        private SupeyCard ldStatusCard;
        private SupeyLabel ldStatusLbl;
        private Panel ldToolbar;
        private RJDatePicker ldDatePicker;
        private FlowLayoutPanel ldPeriodStrip;
        private SupeyComboBox ldFilterCombo;
        private Label ldDateHintLbl;
        private Label ldRangeCaptionLbl;
        private Label ldDriverCaptionLbl;
        private Label ldTripCaptionLbl;
        private Label ldInsightsCaptionLbl;
        private Label ldHeroTitleLbl;
        private Panel ldDriverStripHost;
        private Panel ldDriverStripHeader;
        private FlowLayoutPanel ldDriverStrip;
        private Panel ldTripHeader;
        private Panel ldChartsHost;
        private Panel ldStageHost;
        private Panel ldHeroHost;
        private Panel ldHeroInner;
        private SupeyCard ldHeroCard;
        private SupeyListView ldTripLv;
        private SupeyCard ldChartMinutesCard;
        private SupeyCard ldChartSideCard;
        private Chart ldChartMinutes;
        private Chart ldChartSide;

        private const int LateDriversLivePollIntervalMs = 60_000;
        private const int LateDriversLiveScanMinVisibleMs = 1200;
        private const int LateDriversChartH = 160;
        private const int LateDriversDriverStripH = 118;
        private const int LateDriversHeroH = 148;
        private const int LateDriversDriverTileW = 148;
        private const int LateDriversDriverTileH = 78;
        private System.Windows.Forms.Timer _ldPollTimer;
        private System.Windows.Forms.Timer _ldPollCountdownTimer;
        private DateTime _ldPollNextUtc;
        private DateTime _ldScanStartedUtc;
        private SupeyCard _ldLiveChromeCard;
        private Panel _ldLiveChromeHost;
        private Panel _ldLiveDivider;
        private SupeyCard _ldLiveScanCard;
        private SupeyCard _ldLiveTimerCard;
        private TripScoutLiveScanIndicator _ldLiveScan;
        private Label _ldLiveCountdown;
        private string _ldLastHash;
        private string _ldHabitsHash;
        private bool _ldLoadInFlight;
        private bool _ldBuilt;
        private bool _ldFirstLoadDone;
        private bool _ldSuppressDateChanged;
        private string _ldSelectedPeriod = "live";
        private string _ldSelectedDriver; // null = All drivers
        private string _ldFilter = "all";
        private string _ldHabitChip = "all";
        private string _ldRangeLabel = "";
        private List<HiatmeAiClient.LateDriversEventRow> _ldEventRows;
        private List<HiatmeAiClient.LateDriversDriverSummary> _ldDriverRows;
        private readonly List<SupeyMaterialButton> _ldPeriodButtons = new List<SupeyMaterialButton>();
        private readonly List<SupeyMaterialButton> _ldHabitChipButtons = new List<SupeyMaterialButton>();
        private TableLayoutPanel ldScorecardHost;
        private FlowLayoutPanel ldHabitChipStrip;
        private readonly Dictionary<string, Label> _ldScoreValues =
            new Dictionary<string, Label>(StringComparer.OrdinalIgnoreCase);
        private readonly List<SupeyCard> _ldDriverTiles = new List<SupeyCard>();

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
                    ldStageHost = null;
                    ldTripLv = null;
                    ldChartMinutesCard = null;
                    ldChartSideCard = null;
                    ldChartMinutes = null;
                    ldChartSide = null;
                    ldChartsHost = null;
                    ldPeriodStrip = null;
                    ldFilterCombo = null;
                    ldDatePicker = null;
                    ldDateHintLbl = null;
                    ldRangeCaptionLbl = null;
                    ldDriverCaptionLbl = null;
                    ldTripCaptionLbl = null;
                    ldInsightsCaptionLbl = null;
                    ldHeroTitleLbl = null;
                    ldDriverStripHost = null;
                    ldDriverStripHeader = null;
                    ldDriverStrip = null;
                    ldTripHeader = null;
                    ldHeroHost = null;
                    ldHeroInner = null;
                    ldHeroCard = null;
                    ldScorecardHost = null;
                    ldHabitChipStrip = null;
                    _ldLiveChromeCard = null;
                    _ldLiveChromeHost = null;
                    _ldPeriodButtons.Clear();
                    _ldHabitChipButtons.Clear();
                    _ldScoreValues.Clear();
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
                try { ldDatePicker.Value = DateTime.Today; } catch { }
                ldDatePicker.ValueChanged += (_, __) =>
                {
                    if (_ldSuppressDateChanged || !_ldBuilt)
                        return;
                    if (LateDriversSelectedMode() == "live")
                    {
                        _ldSuppressDateChanged = true;
                        try { ldDatePicker.Value = DateTime.Today; } catch { }
                        _ldSuppressDateChanged = false;
                        UpdateLateDriversToolbarHints();
                        return;
                    }
                    UpdateLateDriversToolbarHints();
                    _ = LateDriversRefreshAsync(force: true);
                };

                ldDateHintLbl = new Label
                {
                    Name = "ldDateHintLbl",
                    AutoSize = false,
                    TextAlign = ContentAlignment.MiddleLeft,
                    BackColor = Color.Transparent,
                    Text = "Date:",
                };
                ldRangeCaptionLbl = new Label
                {
                    Name = "ldRangeCaptionLbl",
                    AutoSize = false,
                    TextAlign = ContentAlignment.MiddleLeft,
                    BackColor = Color.Transparent,
                    Text = "Showing: today’s driver habits (auto-refresh)",
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
                foreach (string label in new[] { "Live", "Day", "Week", "Month", "Year" })
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

                EnsureLateDriversLiveChrome();
                BuildLateDriversBodyChrome();

                // Dock: Fill first, then Bottom, then Tops (last Top = topmost).
                ldMainCard.Controls.Add(ldStageHost);
                ldMainCard.Controls.Add(ldChartsHost);
                ldMainCard.Controls.Add(ldHeroHost);
                ldMainCard.Controls.Add(ldDriverStripHost);
                ldMainCard.Controls.Add(ldRangeCaptionLbl);
                ldMainCard.Controls.Add(ldToolbar);

                tabPageLateDrivers.Controls.Add(ldStatusCard);
                tabPageLateDrivers.Controls.Add(ldMainCard);
                tabPageLateDrivers.Resize += (_, __) => LayoutLateDriversTabPanels();

                tabPageLateDrivers.Text = "Driver Habits";
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
            // ── Top toolbar (period + date + live chrome) ─────────────────
            ldToolbar = new Panel
            {
                Name = "ldToolbar",
                Dock = DockStyle.Top,
                Height = 52,
                Padding = new Padding(10, 8, 10, 8),
                BackColor = SupeyTheme.SurfaceHeader,
            };
            ldToolbar.Resize += (_, __) => LayoutLateDriversToolbar();

            if (ldPeriodStrip != null)
                ldToolbar.Controls.Add(ldPeriodStrip);
            if (ldDateHintLbl != null)
                ldToolbar.Controls.Add(ldDateHintLbl);
            if (ldDatePicker != null)
                ldToolbar.Controls.Add(ldDatePicker);
            if (_ldLiveChromeCard != null)
                ldToolbar.Controls.Add(_ldLiveChromeCard);

            ldRangeCaptionLbl.Dock = DockStyle.Top;
            ldRangeCaptionLbl.Height = 24;
            ldRangeCaptionLbl.Padding = new Padding(12, 0, 12, 0);
            ldRangeCaptionLbl.BackColor = SupeyTheme.Surface;

            // ── Driver card strip ─────────────────────────────────────────
            ldDriverStripHost = new Panel
            {
                Name = "ldDriverStripHost",
                Dock = DockStyle.Top,
                Height = LateDriversDriverStripH,
                Padding = new Padding(10, 4, 10, 4),
                BackColor = SupeyTheme.Surface,
            };
            ldDriverStripHeader = new Panel
            {
                Name = "ldDriverStripHeader",
                Dock = DockStyle.Top,
                Height = 32,
                BackColor = Color.Transparent,
            };
            ldDriverCaptionLbl = new Label
            {
                Name = "ldDriverCaptionLbl",
                AutoSize = false,
                Text = "Drivers",
                TextAlign = ContentAlignment.MiddleLeft,
                Dock = DockStyle.Left,
                Width = 90,
                BackColor = Color.Transparent,
            };
            ldFilterCombo = new SupeyComboBox { Name = "ldFilterCombo" };
            ConfigureToolbarSupeyCombo(ldFilterCombo, 150);
            ldFilterCombo.Dock = DockStyle.Right;
            ldFilterCombo.Width = 150;
            ldFilterCombo.Items.AddRange(new object[]
            {
                "Show: All",
                "Show: Open now",
                "Show: Early",
                "Show: Unfinished",
                "Show: Repeat (2+)",
            });
            ldFilterCombo.SelectedIndex = 0;
            ldFilterCombo.SelectedIndexChanged += (_, __) =>
            {
                _ldFilter = LateDriversFilterKey();
                BindLateDriversDriverStrip();
                RefreshLateDriversScorecard();
                BindLateDriversTripPane();
                RefreshLateDriversCharts();
            };
            ldDriverStripHeader.Controls.Add(ldFilterCombo);
            ldDriverStripHeader.Controls.Add(ldDriverCaptionLbl);

            ldDriverStrip = new FlowLayoutPanel
            {
                Name = "ldDriverStrip",
                Dock = DockStyle.Fill,
                WrapContents = false,
                FlowDirection = FlowDirection.LeftToRight,
                AutoScroll = true,
                Padding = new Padding(0, 2, 0, 2),
                BackColor = Color.Transparent,
            };
            ldDriverStripHost.Controls.Add(ldDriverStrip);
            ldDriverStripHost.Controls.Add(ldDriverStripHeader);

            // ── Scorecard hero (host adds side padding; docked Margin is ignored) ─
            ldHeroHost = new Panel
            {
                Name = "ldHeroHost",
                Dock = DockStyle.Top,
                Height = LateDriversHeroH + 12,
                Padding = new Padding(12, 4, 12, 10),
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
                Padding = new Padding(20, 16, 20, 16),
                BackColor = Color.Transparent,
            };
            ldHeroTitleLbl = new Label
            {
                Name = "ldHeroTitleLbl",
                AutoSize = false,
                Dock = DockStyle.Top,
                Height = 28,
                Text = "All drivers",
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(2, 0, 2, 6),
                BackColor = Color.Transparent,
                ForeColor = SupeyTheme.TextPrimary,
                Font = SupeyTheme.SubHeaderFont,
            };
            ldScorecardHost = new TableLayoutPanel
            {
                Name = "ldScorecardHost",
                Dock = DockStyle.Fill,
                ColumnCount = 6,
                RowCount = 1,
                Padding = new Padding(0, 4, 0, 0),
                Margin = Padding.Empty,
                BackColor = Color.Transparent,
            };
            for (int i = 0; i < 6; i++)
                ldScorecardHost.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 6f));
            ldScorecardHost.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            BuildLateDriversScorecardWidgets();
            ldHeroInner.Controls.Add(ldScorecardHost);
            ldHeroInner.Controls.Add(ldHeroTitleLbl);
            ldHeroCard.Controls.Add(ldHeroInner);
            ldHeroHost.Controls.Add(ldHeroCard);

            // ── Insights (charts, bottom) ─────────────────────────────────
            ldChartsHost = new Panel
            {
                Name = "ldChartsHost",
                Dock = DockStyle.Bottom,
                Height = LateDriversChartH + 28,
                Padding = new Padding(10, 0, 10, 8),
                BackColor = Color.Transparent,
            };
            ldChartsHost.Resize += (_, __) => LayoutLateDriversChartsHost();
            ldInsightsCaptionLbl = new Label
            {
                Name = "ldInsightsCaptionLbl",
                AutoSize = false,
                Dock = DockStyle.Top,
                Height = 22,
                Text = "Insights",
                TextAlign = ContentAlignment.MiddleLeft,
                BackColor = Color.Transparent,
                ForeColor = SupeyTheme.TextSecondary,
            };
            ldChartMinutesCard = new SupeyCard
            {
                Name = "ldChartMinutesCard",
                SurfaceLevel = SupeyCard.Surface.Elevated,
                ShowBorder = true,
            };
            ldChartSideCard = new SupeyCard
            {
                Name = "ldChartSideCard",
                SurfaceLevel = SupeyCard.Surface.Elevated,
                ShowBorder = true,
            };
            ldChartMinutes = CreateLateDriversChart("ldChartMinutes", "Minutes");
            ldChartSide = CreateLateDriversChart("ldChartSide", "Habits");
            ldChartMinutes.Dock = DockStyle.Fill;
            ldChartSide.Dock = DockStyle.Fill;
            ldChartMinutesCard.Padding = new Padding(6);
            ldChartSideCard.Padding = new Padding(6);
            ldChartMinutesCard.Controls.Add(ldChartMinutes);
            ldChartSideCard.Controls.Add(ldChartSide);
            ldChartsHost.Controls.Add(ldChartMinutesCard);
            ldChartsHost.Controls.Add(ldChartSideCard);
            ldChartsHost.Controls.Add(ldInsightsCaptionLbl);

            // ── Stage: chips + trip list (Fill) ───────────────────────────
            ldStageHost = new Panel
            {
                Name = "ldStageHost",
                Dock = DockStyle.Fill,
                Padding = new Padding(10, 4, 10, 4),
                BackColor = SupeyTheme.Surface,
            };

            ldHabitChipStrip = new FlowLayoutPanel
            {
                Name = "ldHabitChipStrip",
                Dock = DockStyle.Top,
                Height = 38,
                WrapContents = false,
                FlowDirection = FlowDirection.LeftToRight,
                Padding = new Padding(0, 2, 0, 2),
                BackColor = Color.Transparent,
                AutoScroll = true,
            };
            foreach (var pair in new[]
            {
                ("All", "all"),
                ("Late PU", "late_pu"),
                ("Late DO", "late_do"),
                ("Early PU", "early_pu"),
                ("Early DO", "early_do"),
                ("Unfinished", "unfinished_ticket"),
                ("Billed skip", "billed_unfinished"),
                ("Open now", "open"),
            })
            {
                var btn = new SupeyMaterialButton
                {
                    Name = "ldHabitChip_" + pair.Item2,
                    Text = pair.Item1,
                    Type = SupeyMaterialButton.MaterialButtonType.Outlined,
                    Size = new Size(pair.Item1.Length <= 4 ? 56 : (pair.Item1.Length <= 8 ? 86 : 96), 28),
                    Margin = new Padding(0, 2, 6, 2),
                    Tag = pair.Item2,
                };
                btn.Click += LdHabitChip_Click;
                _ldHabitChipButtons.Add(btn);
                ldHabitChipStrip.Controls.Add(btn);
            }
            StyleLateDriversHabitChips();

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
            ldTripLv.Columns.Add("Date", 90);
            ldTripLv.Columns.Add("Habit", 96);
            ldTripLv.Columns.Add("Trip", 120);
            ldTripLv.Columns.Add("Client", 160);
            ldTripLv.Columns.Add("Sched", 90);
            ldTripLv.Columns.Add("Actual", 90);
            ldTripLv.Columns.Add("Mins", 60);
            ldTripLv.Columns.Add("Status", 130);
            ldTripLv.Columns.Add("State", 80);
            ldTripLv.DoubleClick += LdTripLv_DoubleClick;

            ldStageHost.Controls.Add(ldTripLv);
            ldStageHost.Controls.Add(ldTripHeader);
            ldStageHost.Controls.Add(ldHabitChipStrip);
        }

        private void BuildLateDriversScorecardWidgets()
        {
            if (ldScorecardHost == null)
                return;
            ldScorecardHost.Controls.Clear();
            _ldScoreValues.Clear();
            var metrics = new[]
            {
                ("late_pu", "Late PU"),
                ("late_do", "Late DO"),
                ("early_pu", "Early PU"),
                ("early_do", "Early DO"),
                ("unfinished", "Unfin/Bill"),
                ("late_minutes", "Late mins"),
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
                    Margin = new Padding(i == 0 ? 0 : 4, 0, i == metrics.Length - 1 ? 0 : 4, 0),
                    Padding = new Padding(10, 10, 10, 10),
                    Tag = pair.Item1,
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
                frame.RowStyles.Add(new RowStyle(SizeType.Absolute, 56f));
                frame.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));

                var stack = new Panel
                {
                    Name = "ldScoreStack_" + pair.Item1,
                    Dock = DockStyle.Fill,
                    Margin = Padding.Empty,
                    Padding = Padding.Empty,
                    BackColor = Color.Transparent,
                };
                var caption = new Label
                {
                    Name = "ldScoreCap_" + pair.Item1,
                    Text = pair.Item2,
                    AutoSize = false,
                    Dock = DockStyle.Top,
                    Height = 22,
                    TextAlign = ContentAlignment.MiddleCenter,
                    ForeColor = SupeyTheme.TextSecondary,
                    BackColor = Color.Transparent,
                    Font = SupeyTheme.CaptionFont,
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
                    Font = new Font("Segoe UI Semibold", 18f, FontStyle.Bold),
                };
                stack.Controls.Add(value);
                stack.Controls.Add(caption);
                frame.Controls.Add(new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent }, 0, 0);
                frame.Controls.Add(stack, 0, 1);
                frame.Controls.Add(new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent }, 0, 2);

                card.Controls.Add(frame);
                _ldScoreValues[pair.Item1] = value;
                ldScorecardHost.Controls.Add(card, i, 0);
            }
        }

        private void LdHabitChip_Click(object sender, EventArgs e)
        {
            var btn = sender as SupeyMaterialButton;
            string key = (btn?.Tag as string ?? "all").Trim().ToLowerInvariant();
            if (key == _ldHabitChip)
                return;
            _ldHabitChip = key;
            StyleLateDriversHabitChips();
            BindLateDriversTripPane();
        }

        private void StyleLateDriversHabitChips()
        {
            foreach (var btn in _ldHabitChipButtons)
            {
                if (btn == null || btn.IsDisposed)
                    continue;
                string key = (btn.Tag as string ?? "").Trim().ToLowerInvariant();
                bool on = key == _ldHabitChip;
                btn.Type = on
                    ? SupeyMaterialButton.MaterialButtonType.Contained
                    : SupeyMaterialButton.MaterialButtonType.Outlined;
            }
        }

        private void LayoutLateDriversToolbar()
        {
            if (ldToolbar == null || ldToolbar.IsDisposed)
                return;
            int padL = ldToolbar.Padding.Left;
            int padR = ldToolbar.Padding.Right;
            int y = ldToolbar.Padding.Top;
            int innerH = Math.Max(28, ldToolbar.ClientSize.Height - ldToolbar.Padding.Vertical);
            int x = padL;

            if (ldPeriodStrip != null && !ldPeriodStrip.IsDisposed)
            {
                ldPeriodStrip.SetBounds(x, y + Math.Max(0, (innerH - 30) / 2), 380, 30);
                x = ldPeriodStrip.Right + 10;
            }

            if (ldDateHintLbl != null && !ldDateHintLbl.IsDisposed)
            {
                int hintW = 48;
                switch (LateDriversSelectedMode())
                {
                    case "week": hintW = 118; break;
                    case "month": hintW = 128; break;
                    case "year": hintW = 118; break;
                    case "day": hintW = 40; break;
                }
                ldDateHintLbl.SetBounds(x, y + Math.Max(0, (innerH - 20) / 2), hintW, 20);
                x = ldDateHintLbl.Right + 4;
            }

            if (ldDatePicker != null && !ldDatePicker.IsDisposed)
            {
                const int dateH = 34;
                ldDatePicker.SetBounds(x, y + Math.Max(0, (innerH - dateH) / 2), 214, dateH);
                x = ldDatePicker.Right + 10;
            }

            EnsureLateDriversLiveChrome();
            bool liveMode = LateDriversSelectedMode() == "live";
            if (_ldLiveChromeCard != null && !_ldLiveChromeCard.IsDisposed)
            {
                _ldLiveChromeCard.Visible = liveMode;
                StyleLateDriversLiveChromeCard(liveMode);
                if (liveMode)
                {
                    int chromeW = MeasureLateDriversLiveChromeWidth();
                    int chromeX = Math.Max(x + 8, ldToolbar.ClientSize.Width - padR - chromeW);
                    _ldLiveChromeHost?.SuspendLayout();
                    try
                    {
                        _ldLiveChromeCard.Parent = ldToolbar;
                        _ldLiveChromeCard.SetBounds(chromeX, y + Math.Max(0, (innerH - TripScoutLiveCardHeight) / 2),
                            chromeW, TripScoutLiveCardHeight);
                        LayoutLateDriversLiveChromeHost();
                    }
                    finally
                    {
                        _ldLiveChromeHost?.ResumeLayout(true);
                    }
                    _ldLiveChromeCard.BringToFront();
                }
            }
        }

        private void LayoutLateDriversChartsHost()
        {
            if (ldChartsHost == null || ldChartsHost.IsDisposed)
                return;
            int padL = ldChartsHost.Padding.Left;
            int padR = ldChartsHost.Padding.Right;
            int padT = ldChartsHost.Padding.Top;
            int padB = ldChartsHost.Padding.Bottom;
            int gap = 10;
            int captionH = 0;
            if (ldInsightsCaptionLbl != null && !ldInsightsCaptionLbl.IsDisposed)
            {
                captionH = ldInsightsCaptionLbl.Height;
                ldInsightsCaptionLbl.SetBounds(padL, padT, Math.Max(40, ldChartsHost.ClientSize.Width - padL - padR), captionH);
            }
            int top = padT + captionH + 2;
            int w = Math.Max(40, ldChartsHost.ClientSize.Width - padL - padR);
            int h = Math.Max(40, ldChartsHost.ClientSize.Height - top - padB);
            int leftW = Math.Max(120, (w - gap) * 2 / 3);
            int rightW = Math.Max(100, w - gap - leftW);
            if (ldChartMinutesCard != null && !ldChartMinutesCard.IsDisposed)
                ldChartMinutesCard.SetBounds(padL, top, leftW, h);
            if (ldChartSideCard != null && !ldChartSideCard.IsDisposed)
                ldChartSideCard.SetBounds(padL + leftW + gap, top, rightW, h);
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

        private static Chart CreateLateDriversChart(string name, string seriesName)
        {
            var chart = new Chart { Name = name };
            var area = new ChartArea("main")
            {
                AxisX = { IntervalAutoMode = IntervalAutoMode.VariableCount, MajorGrid = { Enabled = false } },
                AxisY = { MajorGrid = { LineDashStyle = ChartDashStyle.Dot } },
            };
            chart.ChartAreas.Add(area);
            chart.Series.Add(new Series(seriesName)
            {
                ChartType = SeriesChartType.Column,
                IsValueShownAsLabel = true,
            });
            chart.Titles.Add(new Title(seriesName));
            return chart;
        }

        private void LdPeriodButton_Click(object sender, EventArgs e)
        {
            var btn = sender as SupeyMaterialButton;
            string period = (btn?.Tag as string ?? "live").Trim().ToLowerInvariant();
            if (period == _ldSelectedPeriod)
                return;
            _ldSelectedPeriod = period;
            StyleLateDriversPeriodButtons();
            if (period == "live")
            {
                _ldSuppressDateChanged = true;
                try { ldDatePicker.Value = DateTime.Today; } catch { }
                _ldSuppressDateChanged = false;
            }
            _ldSelectedDriver = null;
            UpdateLateDriversToolbarHints();
            SyncLateDriversLivePollingForMode();
            _ = LateDriversRefreshAsync(force: true);
        }

        private void UpdateLateDriversToolbarHints()
        {
            string mode = LateDriversSelectedMode();
            DateTime anchor = DateTime.Today;
            try
            {
                if (ldDatePicker != null)
                    anchor = ldDatePicker.Value.Date;
            }
            catch { }

            if (ldDateHintLbl != null && !ldDateHintLbl.IsDisposed)
            {
                switch (mode)
                {
                    case "day":
                        ldDateHintLbl.Text = "Day:";
                        break;
                    case "week":
                        ldDateHintLbl.Text = "Any day in week:";
                        break;
                    case "month":
                        ldDateHintLbl.Text = "Any day in month:";
                        break;
                    case "year":
                        ldDateHintLbl.Text = "Any day in year:";
                        break;
                    default:
                        ldDateHintLbl.Text = "Today:";
                        break;
                }
            }

            if (ldRangeCaptionLbl != null && !ldRangeCaptionLbl.IsDisposed)
            {
                if (!string.IsNullOrWhiteSpace(_ldRangeLabel) && mode != "live")
                {
                    ldRangeCaptionLbl.Text = "Loaded: " + CultureInfo.CurrentCulture.TextInfo.ToTitleCase(mode)
                        + "  " + _ldRangeLabel
                        + "  — pick a different date to change the range";
                }
                else
                {
                    switch (mode)
                    {
                        case "live":
                            ldRangeCaptionLbl.Text = "Live = today only, refreshes every 60s";
                            break;
                        case "day":
                            ldRangeCaptionLbl.Text = "Will load day " + anchor.ToString("ddd MMM d, yyyy", CultureInfo.CurrentCulture);
                            break;
                        case "week":
                            {
                                // Monday-start week (matches server _range_bounds).
                                int offset = ((int)anchor.DayOfWeek + 6) % 7;
                                var start = anchor.AddDays(-offset);
                                var end = start.AddDays(6);
                                ldRangeCaptionLbl.Text = "Will load week "
                                    + start.ToString("MMM d", CultureInfo.CurrentCulture)
                                    + " – " + end.ToString("MMM d, yyyy", CultureInfo.CurrentCulture)
                                    + "  (change date, loads automatically)";
                                break;
                            }
                        case "month":
                            ldRangeCaptionLbl.Text = "Will load "
                                + anchor.ToString("MMMM yyyy", CultureInfo.CurrentCulture)
                                + "  (change date, loads automatically)";
                            break;
                        case "year":
                            ldRangeCaptionLbl.Text = "Will load year " + anchor.Year
                                + "  (change date, loads automatically)";
                            break;
                        default:
                            ldRangeCaptionLbl.Text = "";
                            break;
                    }
                }
            }

            if (ldDatePicker != null && !ldDatePicker.IsDisposed)
                ldDatePicker.Enabled = mode != "live";

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

        private void EnsureLateDriversLiveChrome()
        {
            if (_ldLiveChromeCard == null || _ldLiveChromeCard.IsDisposed)
            {
                _ldLiveChromeCard = new SupeyCard
                {
                    Name = "ldLiveChromeCard",
                    SurfaceLevel = SupeyCard.Surface.Elevated,
                    ShowBorder = true,
                    CornerRadius = 8,
                    Size = new Size(140, TripScoutLiveCardHeight),
                };
            }

            if (_ldLiveChromeHost == null || _ldLiveChromeHost.IsDisposed)
            {
                _ldLiveChromeHost = new Panel
                {
                    Name = "ldLiveChromeHost",
                    Dock = DockStyle.Fill,
                    BackColor = Color.Transparent,
                };
                _ldLiveChromeCard.Controls.Add(_ldLiveChromeHost);
            }

            if (_ldLiveScanCard == null || _ldLiveScanCard.IsDisposed)
            {
                _ldLiveScanCard = MakeTripScoutLiveMiniCard("ldLiveScanCard");
                _ldLiveChromeHost.Controls.Add(_ldLiveScanCard);
            }

            if (_ldLiveTimerCard == null || _ldLiveTimerCard.IsDisposed)
            {
                _ldLiveTimerCard = MakeTripScoutLiveMiniCard("ldLiveTimerCard");
                _ldLiveChromeHost.Controls.Add(_ldLiveTimerCard);
            }

            if (_ldLiveDivider == null || _ldLiveDivider.IsDisposed)
            {
                _ldLiveDivider = MakeTripScoutLiveDivider("ldLiveDivider");
                _ldLiveChromeHost.Controls.Add(_ldLiveDivider);
            }

            if (_ldLiveScan == null || _ldLiveScan.IsDisposed)
            {
                _ldLiveScan = new TripScoutLiveScanIndicator
                {
                    Name = "ldLiveScan",
                    Dock = DockStyle.Fill,
                };
                _ldLiveScanCard.Controls.Add(_ldLiveScan);
            }

            if (_ldLiveCountdown == null || _ldLiveCountdown.IsDisposed)
            {
                _ldLiveCountdown = new Label
                {
                    Name = "ldLiveCountdown",
                    AutoSize = false,
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleCenter,
                    Font = new Font("Segoe UI Semibold", 10f, FontStyle.Bold),
                    Text = "60s",
                    BackColor = Color.Transparent,
                    ForeColor = SupeyTheme.TextPrimary,
                };
                _ldLiveTimerCard.Controls.Add(_ldLiveCountdown);
            }
        }

        private void StyleLateDriversLiveChromeCard(bool live)
        {
            if (_ldLiveChromeCard == null || _ldLiveChromeCard.IsDisposed)
                return;
            _ldLiveChromeCard.Accent = live
                ? SupeyCard.AccentEdge.Left
                : SupeyCard.AccentEdge.None;
        }

        private int MeasureLateDriversLiveChromeWidth()
        {
            int width = TripScoutLiveCardPadH * 2;
            width += TripScoutLiveScanSlot + TripScoutLiveCardGap;
            width += TripScoutLiveTimerCardW + TripScoutLiveCardGap;
            return width;
        }

        private void LayoutLateDriversLiveChromeHost()
        {
            if (_ldLiveChromeHost == null || _ldLiveChromeHost.IsDisposed)
                return;

            int hostH = TripScoutLiveCardHeight;
            int x = TripScoutLiveCardPadH;

            if (_ldLiveScanCard != null && !_ldLiveScanCard.IsDisposed)
                _ldLiveScanCard.Visible = true;
            if (_ldLiveTimerCard != null && !_ldLiveTimerCard.IsDisposed)
                _ldLiveTimerCard.Visible = true;
            if (_ldLiveDivider != null && !_ldLiveDivider.IsDisposed)
                _ldLiveDivider.Visible = false;

            int slotY = (hostH - TripScoutLiveScanSlot) / 2;
            _ldLiveScanCard?.SetBounds(x, slotY, TripScoutLiveScanSlot, TripScoutLiveScanSlot);
            x += TripScoutLiveScanSlot + TripScoutLiveCardGap;

            int timerH = hostH - (TripScoutLiveCardPadV * 2);
            int timerY = TripScoutLiveCardPadV;
            _ldLiveTimerCard?.SetBounds(x, timerY, TripScoutLiveTimerCardW, timerH);
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
            if (ldFilterCombo != null && !ldFilterCombo.IsDisposed)
                ConfigureToolbarSupeyCombo(ldFilterCombo, 160);
            StyleLateDriversPeriodButtons();
            StyleLateDriversCaption(ldDateHintLbl, secondary: true);
            StyleLateDriversCaption(ldRangeCaptionLbl, secondary: true);
            StyleLateDriversCaption(ldDriverCaptionLbl, secondary: false);
            StyleLateDriversCaption(ldTripCaptionLbl, secondary: false);
            StyleLateDriversCaption(ldInsightsCaptionLbl, secondary: true);
            StyleLateDriversCaption(ldHeroTitleLbl, secondary: false);
            if (ldToolbar != null && !ldToolbar.IsDisposed)
                ldToolbar.BackColor = SupeyTheme.SurfaceHeader;
            if (ldDriverStripHost != null && !ldDriverStripHost.IsDisposed)
                ldDriverStripHost.BackColor = SupeyTheme.Surface;
            if (ldDriverStrip != null && !ldDriverStrip.IsDisposed)
                ldDriverStrip.BackColor = Color.Transparent;
            if (ldTripHeader != null && !ldTripHeader.IsDisposed)
                ldTripHeader.BackColor = Color.Transparent;
            if (ldHabitChipStrip != null && !ldHabitChipStrip.IsDisposed)
                ldHabitChipStrip.BackColor = Color.Transparent;
            if (ldScorecardHost != null && !ldScorecardHost.IsDisposed)
                ldScorecardHost.BackColor = Color.Transparent;
            if (ldStageHost != null && !ldStageHost.IsDisposed)
                ldStageHost.BackColor = SupeyTheme.Surface;
            if (ldHeroCard != null && !ldHeroCard.IsDisposed)
            {
                StyleToolTabCard(ldHeroCard, SupeyCard.Surface.Elevated);
                // StyleToolTabCard zeros Padding; inset lives on ldHeroInner.
            }
            if (ldHeroInner != null && !ldHeroInner.IsDisposed)
            {
                ldHeroInner.Padding = new Padding(20, 16, 20, 16);
                ldHeroInner.BackColor = Color.Transparent;
            }
            if (ldRangeCaptionLbl != null && !ldRangeCaptionLbl.IsDisposed)
                ldRangeCaptionLbl.BackColor = SupeyTheme.Surface;
            StyleLateDriversHabitChips();
            StyleLateDriversDriverTiles();
            StyleLateDriversList(ldTripLv);
            if (ldChartMinutesCard != null)
                StyleToolTabCard(ldChartMinutesCard, SupeyCard.Surface.Elevated);
            if (ldChartSideCard != null)
                StyleToolTabCard(ldChartSideCard, SupeyCard.Surface.Elevated);
            if (ldChartMinutes != null)
                SupeyChartTheme.Apply(ldChartMinutes);
            if (ldChartSide != null)
                SupeyChartTheme.Apply(ldChartSide);
            if (_ldLiveChromeCard != null)
            {
                _ldLiveChromeCard.SurfaceLevel = SupeyCard.Surface.Elevated;
                _ldLiveChromeCard.ShowBorder = true;
                StyleLateDriversLiveChromeCard(LateDriversSelectedMode() == "live");
            }
            if (_ldLiveCountdown != null && !_ldLiveCountdown.IsDisposed)
            {
                try { _ldLiveCountdown.Font = new Font("Segoe UI Semibold", 10f, FontStyle.Bold); } catch { }
                _ldLiveCountdown.ForeColor = SupeyTheme.TextPrimary;
                _ldLiveCountdown.BackColor = Color.Transparent;
            }
            if (_ldLiveScan != null && !_ldLiveScan.IsDisposed)
                _ldLiveScan.BackColor = SupeyTheme.Surface;
            if (_ldLiveDivider != null && !_ldLiveDivider.IsDisposed)
                _ldLiveDivider.BackColor = SupeyTheme.BorderSubtle;
            SupeyDarkScrollBars.Apply(tabPageLateDrivers);
            if (layout)
                LayoutLateDriversTabPanels();
            LateDriversUpdateLivePollCountdownLabel();
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

            // Inner chrome is Dock-based (toolbar Top / range Top / charts Bottom / split Fill).
            LayoutLateDriversToolbar();
            LayoutLateDriversChartsHost();
        }

        private string LateDriversSelectedMode()
        {
            string s = (_ldSelectedPeriod ?? "live").Trim().ToLowerInvariant();
            if (s == "day" || s == "week" || s == "month" || s == "year")
                return s;
            return "live";
        }

        private string LateDriversFilterKey()
        {
            string s = (ldFilterCombo?.SelectedItem as string ?? "Show: All").Trim().ToLowerInvariant();
            if (s.Contains("open"))
                return "open";
            if (s.Contains("early"))
                return "early";
            if (s.Contains("unfinished"))
                return "unfinished";
            if (s.Contains("repeat"))
                return "repeat";
            return "all";
        }

        private string LateDriversSelectedServiceDateIso()
        {
            if (LateDriversSelectedMode() == "live")
                return DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            try
            {
                if (ldDatePicker != null)
                    return ldDatePicker.Value.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
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

            SetLateDriversStatus("Status: Downloading Modivcare schedule for " + serviceDateIso + "…");
            if (!await EnsureModivcareSessionAsync().ConfigureAwait(true))
                return (false, "Need Modivcare login to load schedule for " + serviceDateIso, false);

            List<MCDownloadedTrip> downloaded = null;
            try
            {
                var dler = new MCTripDownloader();
                downloaded = await dler.DownloadTripRecords(day, mcLoginHandler).ConfigureAwait(true);
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
            SyncLateDriversLivePollingForMode();
            _ = LateDriversRefreshAsync(force: true);
        }

        private void SyncLateDriversLivePollingForMode()
        {
            if (LateDriversSelectedMode() == "live")
                StartLateDriversLivePolling();
            else
                StopLateDriversLivePolling();
            LayoutLateDriversTabPanels();
        }

        private void EnsureLateDriversLivePollTimer()
        {
            if (_ldPollTimer != null)
                return;
            _ldPollTimer = new System.Windows.Forms.Timer { Interval = LateDriversLivePollIntervalMs };
            _ldPollTimer.Tick += (_, __) =>
            {
                if (hiatmeTabControl?.SelectedTab != tabPageLateDrivers)
                    return;
                if (LateDriversSelectedMode() != "live" || _ldLoadInFlight)
                    return;
                _ = LateDriversRefreshAsync(force: false);
            };
        }

        private void EnsureLateDriversLivePollCountdownTimer()
        {
            if (_ldPollCountdownTimer != null)
                return;
            _ldPollCountdownTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            _ldPollCountdownTimer.Tick += (_, __) => LateDriversUpdateLivePollCountdownLabel();
        }

        private void StopLateDriversLivePolling()
        {
            _ldPollTimer?.Stop();
            _ldPollCountdownTimer?.Stop();
            StopLateDriversLiveScan();
            if (_ldLiveChromeCard != null && !_ldLiveChromeCard.IsDisposed)
                _ldLiveChromeCard.Visible = false;
            StyleLateDriversLiveChromeCard(false);
            LateDriversUpdateLivePollCountdownLabel();
        }

        private void StartLateDriversLivePolling()
        {
            EnsureLateDriversLiveChrome();
            EnsureLateDriversLivePollTimer();
            EnsureLateDriversLivePollCountdownTimer();
            if (_ldLiveChromeCard != null)
                _ldLiveChromeCard.Visible = true;
            StyleLateDriversLiveChromeCard(true);
            _ldPollTimer.Start();
            _ldPollCountdownTimer.Start();
            LateDriversScheduleNextLivePoll();
        }

        private void LateDriversScheduleNextLivePoll()
        {
            _ldPollNextUtc = DateTime.UtcNow.AddMilliseconds(LateDriversLivePollIntervalMs);
            LateDriversUpdateLivePollCountdownLabel();
        }

        private void LateDriversUpdateLivePollCountdownLabel()
        {
            if (_ldLiveCountdown == null || _ldLiveCountdown.IsDisposed)
                return;
            if (LateDriversSelectedMode() != "live")
            {
                _ldLiveCountdown.Visible = false;
                return;
            }
            _ldLiveCountdown.Visible = true;
            if (_ldLoadInFlight)
            {
                _ldLiveCountdown.Text = "…";
                _ldLiveCountdown.ForeColor = SupeyTheme.AccentPrimary;
                return;
            }
            int seconds = Math.Max(0, (int)Math.Ceiling((_ldPollNextUtc - DateTime.UtcNow).TotalSeconds));
            _ldLiveCountdown.Text = seconds + "s";
            _ldLiveCountdown.ForeColor = seconds <= 5
                ? SupeyTheme.AccentPrimary
                : SupeyTheme.TextPrimary;
        }

        private void StartLateDriversLiveScan()
        {
            if (_ldLiveScan == null || _ldLiveScan.IsDisposed)
                return;
            _ldScanStartedUtc = DateTime.UtcNow;
            _ldLiveScan.Scanning = true;
            LateDriversUpdateLivePollCountdownLabel();
        }

        private async Task StopLateDriversLiveScanAfterMinimumAsync()
        {
            double elapsed = (DateTime.UtcNow - _ldScanStartedUtc).TotalMilliseconds;
            if (elapsed < LateDriversLiveScanMinVisibleMs)
            {
                try
                {
                    await Task.Delay((int)(LateDriversLiveScanMinVisibleMs - elapsed)).ConfigureAwait(true);
                }
                catch { }
            }
            StopLateDriversLiveScan();
        }

        private void StopLateDriversLiveScan()
        {
            if (_ldLiveScan == null || _ldLiveScan.IsDisposed)
                return;
            if (_ldLiveScan.Scanning)
                _ldLiveScan.Scanning = false;
        }

        private async Task LateDriversRefreshAsync(bool force)
        {
            if (!_ldBuilt || _ldLoadInFlight || IsDisposed)
                return;
            _ldLoadInFlight = true;
            bool liveMode = LateDriversSelectedMode() == "live";
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
                var habitsTask = HiatmeAiClient.GetLateDriversHabitsAsync(
                    settings, habitPeriod, sd);

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
                        ApplyLateDriversEventPayload(
                            doc.Events,
                            doc.ContentHash,
                            sd,
                            sd,
                            doc.ModivcareTripCount,
                            live: true,
                            habits: habits);
                    }
                    else
                    {
                        var doc = await HiatmeAiClient.GetLateDriversDayAsync(settings, sd)
                            .ConfigureAwait(true);
                        if (doc == null || !doc.Ok)
                        {
                            SetLateDriversStatus("Status: " + (doc?.Error ?? "day load failed"));
                            return;
                        }
                        if (!doc.ModivcareExists)
                        {
                            SetLateDriversStatus("Status: Need Modivcare schedule for " + sd);
                            return;
                        }
                        var habits = await habitsTask.ConfigureAwait(true);
                        ApplyLateDriversEventPayload(
                            doc.Events,
                            doc.ContentHash,
                            sd,
                            sd,
                            doc.ModivcareTripCount,
                            live: false,
                            habits: habits);
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
                    _ldEventRows = doc.Events ?? new List<HiatmeAiClient.LateDriversEventRow>();
                    _ldDriverRows = doc.Drivers ?? new List<HiatmeAiClient.LateDriversDriverSummary>();
                    if (_ldDriverRows.Count == 0 && _ldEventRows.Count > 0)
                        _ldDriverRows = BuildLateDriversRollup(_ldEventRows);
                    else
                        SortLateDriversByMinutes(_ldDriverRows);
                    _ldRangeLabel = (doc.FromDate ?? "") + " → " + (doc.ToDate ?? "");
                    var habits = await habitsTask.ConfigureAwait(true);
                    MergeLateDriversHabits(habits);
                    BindLateDriversDriverStrip();
                    RefreshLateDriversScorecard();
                    BindLateDriversTripPane();
                    RefreshLateDriversCharts();
                    UpdateLateDriversToolbarHints();
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
                else
                {
                    StopLateDriversLiveScan();
                }
                LateDriversUpdateLivePollCountdownLabel();
            }
        }

        private void ApplyLateDriversEventPayload(
            List<HiatmeAiClient.LateDriversEventRow> events,
            string contentHash,
            string fromDate,
            string toDate,
            int mcTripCount,
            bool live,
            HiatmeAiClient.LateDriversHabitsDoc habits = null)
        {
            _ldLastHash = contentHash ?? "";
            _ldEventRows = events ?? new List<HiatmeAiClient.LateDriversEventRow>();
            foreach (var e in _ldEventRows)
            {
                if (e == null || !string.IsNullOrWhiteSpace(e.Habit))
                    continue;
                e.Habit = string.Equals(e.Side, "do", StringComparison.OrdinalIgnoreCase)
                    ? "late_do"
                    : "late_pu";
            }
            _ldDriverRows = BuildLateDriversRollup(_ldEventRows);
            _ldRangeLabel = fromDate == toDate ? fromDate : (fromDate + " → " + toDate);
            MergeLateDriversHabits(habits);
            BindLateDriversDriverStrip();
            RefreshLateDriversScorecard();
            BindLateDriversTripPane();
            RefreshLateDriversCharts();
            UpdateLateDriversToolbarHints();
            int openN = _ldEventRows.Count(e => e != null && e.Open);
            int earlyN = (_ldDriverRows ?? new List<HiatmeAiClient.LateDriversDriverSummary>())
                .Sum(d => d?.EarlyCount ?? 0);
            int unfinOpen = (_ldDriverRows ?? new List<HiatmeAiClient.LateDriversDriverSummary>())
                .Sum(d => d?.UnfinishedOpen ?? 0);
            if (live)
            {
                SetLateDriversStatus(
                    "Status: Live — " + _ldEventRows.Count + " events ("
                    + openN + " open"
                    + (earlyN > 0 ? ", " + earlyN + " early" : "")
                    + (unfinOpen > 0 ? ", " + unfinOpen + " unfinished" : "")
                    + ") · MC " + mcTripCount
                    + " · " + DateTime.Now.ToString("h:mm:ss tt", CultureInfo.CurrentCulture));
            }
            else
            {
                SetLateDriversStatus(
                    "Status: Day " + fromDate + " — " + _ldEventRows.Count + " events ("
                    + openN + " open"
                    + (earlyN > 0 ? ", " + earlyN + " early" : "")
                    + (unfinOpen > 0 ? ", " + unfinOpen + " unfinished" : "")
                    + ") · MC " + mcTripCount);
            }
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
            _ldEventRows = merged;

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
            var src = _ldDriverRows ?? new List<HiatmeAiClient.LateDriversDriverSummary>();
            switch (_ldFilter)
            {
                case "open":
                    return src.Where(d => d != null && (d.OpenCount > 0 || d.UnfinishedOpen > 0)).ToList();
                case "early":
                    return src.Where(d => d != null && d.EarlyCount > 0).ToList();
                case "unfinished":
                    return src.Where(d => d != null && d.Unfinished > 0).ToList();
                case "repeat":
                    return src.Where(d => d != null && (d.LateCount + d.EarlyCount) >= 2).ToList();
                default:
                    return src.Where(d => d != null).ToList();
            }
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

            ldDriverStrip.SuspendLayout();
            try
            {
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

                foreach (var d in rows)
                {
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

                _ldSelectedDriver = keep;
                StyleLateDriversDriverTiles();
            }
            finally
            {
                ldDriverStrip.ResumeLayout(true);
            }
        }

        private SupeyCard CreateLateDriversDriverTile(
            string title,
            int lates,
            int early,
            int unfin,
            double minutes,
            HiatmeAiClient.LateDriversDriverSummary summary)
        {
            var card = new SupeyCard
            {
                Name = "ldDriverTile_" + (summary?.Driver ?? "all").Replace(" ", "_"),
                SurfaceLevel = SupeyCard.Surface.Elevated,
                ShowBorder = true,
                CornerRadius = 8,
                Margin = new Padding(0, 0, 8, 0),
                Padding = new Padding(10, 8, 10, 8),
                Size = new Size(LateDriversDriverTileW, LateDriversDriverTileH),
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
                _ldSelectedDriver = chosen?.Driver;
                StyleLateDriversDriverTiles();
                UpdateLateDriversTripCaption();
                RefreshLateDriversScorecard();
                BindLateDriversTripPane();
            }
            card.Click += pick;
            foreach (Control c in card.Controls)
                c.Click += pick;

            return card;
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
        }

        private void RefreshLateDriversScorecard()
        {
            if (_ldScoreValues.Count == 0)
                return;

            int latePu = 0, lateDo = 0, earlyPu = 0, earlyDo = 0, unfinished = 0;
            double lateMins = 0;
            string heroTitle = "All drivers";
            if (!string.IsNullOrEmpty(_ldSelectedDriver))
            {
                var d = (_ldDriverRows ?? new List<HiatmeAiClient.LateDriversDriverSummary>())
                    .FirstOrDefault(x => x != null
                        && string.Equals(x.Driver, _ldSelectedDriver, StringComparison.OrdinalIgnoreCase));
                if (d != null)
                {
                    heroTitle = d.Driver ?? _ldSelectedDriver;
                    latePu = d.PuCount;
                    lateDo = d.DoCount;
                    earlyPu = d.EarlyPu;
                    earlyDo = d.EarlyDo;
                    unfinished = d.Unfinished;
                    lateMins = d.TotalMinutes;
                    if (latePu == 0 && lateDo == 0 && d.Trips != null)
                    {
                        latePu = d.Trips.Count(t => HabitKeyOf(t) == "late_pu");
                        lateDo = d.Trips.Count(t => HabitKeyOf(t) == "late_do");
                    }
                }
                else
                {
                    heroTitle = _ldSelectedDriver;
                }
            }
            else
            {
                foreach (var d in FilteredLateDrivers())
                {
                    latePu += d.PuCount;
                    lateDo += d.DoCount;
                    earlyPu += d.EarlyPu;
                    earlyDo += d.EarlyDo;
                    unfinished += d.Unfinished;
                    lateMins += d.TotalMinutes;
                }
            }

            if (ldHeroTitleLbl != null && !ldHeroTitleLbl.IsDisposed)
                ldHeroTitleLbl.Text = heroTitle;

            SetLateDriversScoreValue("late_pu", latePu.ToString(CultureInfo.InvariantCulture));
            SetLateDriversScoreValue("late_do", lateDo.ToString(CultureInfo.InvariantCulture));
            SetLateDriversScoreValue("early_pu", earlyPu.ToString(CultureInfo.InvariantCulture));
            SetLateDriversScoreValue("early_do", earlyDo.ToString(CultureInfo.InvariantCulture));
            SetLateDriversScoreValue("unfinished", unfinished.ToString(CultureInfo.InvariantCulture));
            SetLateDriversScoreValue("late_minutes", lateMins.ToString("0", CultureInfo.InvariantCulture));

            Color valueColor = string.IsNullOrEmpty(_ldSelectedDriver)
                ? SupeyTheme.TextPrimary
                : SupeyTheme.AccentPrimary;
            foreach (var lbl in _ldScoreValues.Values)
            {
                if (lbl != null && !lbl.IsDisposed)
                    lbl.ForeColor = valueColor;
            }
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
                var filteredDrivers = new HashSet<string>(
                    FilteredLateDrivers().Select(d => d.Driver ?? ""),
                    StringComparer.OrdinalIgnoreCase);
                trips = (_ldEventRows ?? new List<HiatmeAiClient.LateDriversEventRow>())
                    .Where(e =>
                    {
                        if (e == null) return false;
                        if (_ldFilter == "all") return true;
                        string key = string.IsNullOrWhiteSpace(e.Driver) ? "(unassigned)" : e.Driver.Trim();
                        return filteredDrivers.Contains(key);
                    })
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
                .ThenBy(e => e.ServiceDate ?? "")
                .ThenBy(e => e.TripNo ?? "")
                .ToList();

            ldTripLv.BeginUpdate();
            try
            {
                ldTripLv.Items.Clear();
                foreach (var e in trips)
                {
                    string habit = HabitLabelOf(HabitKeyOf(e));
                    var item = new ListViewItem(e.ServiceDate ?? "");
                    item.SubItems.Add(habit);
                    item.SubItems.Add(e.TripNo ?? "");
                    item.SubItems.Add(e.Client ?? "");
                    item.SubItems.Add(FormatLateDriversTime(e.SchedIso));
                    item.SubItems.Add(FormatLateDriversTime(e.ActualIso, blank: "—"));
                    item.SubItems.Add(e.MinutesLate.ToString("0", CultureInfo.InvariantCulture) + "m");
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
                    ldTripLv.Items.Add(item);
                }
            }
            finally
            {
                ldTripLv.EndUpdate();
            }
        }

        private void RefreshLateDriversCharts()
        {
            var drivers = FilteredLateDrivers()
                .OrderByDescending(d => d.TotalMinutes)
                .ThenByDescending(d => d.LateCount)
                .Take(10)
                .ToList();

            if (ldChartMinutes != null && !ldChartMinutes.IsDisposed)
            {
                var s = ldChartMinutes.Series[0];
                s.Points.Clear();
                foreach (var d in drivers)
                {
                    int idx = s.Points.AddXY(ShortLateDriversName(d.Driver), Math.Round(d.TotalMinutes, 0));
                    s.Points[idx].ToolTip = (d.Driver ?? "") + ": " + d.TotalMinutes.ToString("0") + "m late";
                }
                if (ldChartMinutes.Titles.Count > 0)
                    ldChartMinutes.Titles[0].Text = "Top late minutes by driver";
                SupeyChartTheme.Apply(ldChartMinutes);
                s.Color = SupeyTheme.AccentPrimary;
            }

            if (ldChartSide != null && !ldChartSide.IsDisposed)
            {
                var s = ldChartSide.Series[0];
                s.Points.Clear();
                int late = FilteredLateDrivers().Sum(d => d.LateCount);
                int early = FilteredLateDrivers().Sum(d => d.EarlyCount);
                int unfin = FilteredLateDrivers().Sum(d => d.Unfinished);
                s.Points.AddXY("Late", late);
                s.Points.AddXY("Early", early);
                s.Points.AddXY("Unfin", unfin);
                if (ldChartSide.Titles.Count > 0)
                    ldChartSide.Titles[0].Text = "Early vs Late vs Unfinished";
                SupeyChartTheme.Apply(ldChartSide);
                s.Color = SupeyTheme.AccentPrimary;
            }
        }

        private static string ShortLateDriversName(string driver)
        {
            if (string.IsNullOrWhiteSpace(driver))
                return "?";
            string[] parts = driver.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 1)
                return parts[0].Length <= 10 ? parts[0] : parts[0].Substring(0, 10);
            string last = parts[parts.Length - 1];
            return (parts[0][0] + ". " + last);
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
            if (!(ldTripLv.SelectedItems[0].Tag is HiatmeAiClient.LateDriversEventRow row))
                return;
            try
            {
                if (tsdatepicker != null && !string.IsNullOrWhiteSpace(row.ServiceDate)
                    && DateTime.TryParseExact(
                        row.ServiceDate.Trim(),
                        "yyyy-MM-dd",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out var d))
                {
                    try { tsdatepicker.Value = d; } catch { }
                }
                if (!string.IsNullOrWhiteSpace(row.TripNo) && tssearchbox != null)
                {
                    try { tssearchbox.Text = row.TripNo.Trim(); } catch { }
                }
                if (tabPage9 != null)
                    hiatmeTabControl.SelectedTab = tabPage9;
            }
            catch { }
        }

        private void SetLateDriversStatus(string text)
        {
            if (ldStatusLbl == null || ldStatusLbl.IsDisposed)
                return;
            try { ldStatusLbl.Text = text ?? ""; } catch { }
        }
    }
}
