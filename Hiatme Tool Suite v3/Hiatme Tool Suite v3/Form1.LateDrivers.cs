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
    /// <summary>Late Drivers — Live/Day/Week/Month/Year analytics with driver roster, trip detail, charts.</summary>
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
        private Panel ldDriverHeader;
        private Panel ldTripHeader;
        private Panel ldChartsHost;
        private SplitContainer ldSplit;
        private SupeyListView ldDriverLv;
        private SupeyListView ldTripLv;
        private SupeyCard ldChartMinutesCard;
        private SupeyCard ldChartSideCard;
        private Chart ldChartMinutes;
        private Chart ldChartSide;

        private const int LateDriversLivePollIntervalMs = 60_000;
        private const int LateDriversLiveScanMinVisibleMs = 1200;
        private const int LateDriversChartH = 200;
        private const int LateDriversDriverPaneW = 300;
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
        private bool _ldLoadInFlight;
        private bool _ldBuilt;
        private bool _ldFirstLoadDone;
        private bool _ldSuppressDateChanged;
        private string _ldSelectedPeriod = "live";
        private string _ldSelectedDriver; // null = All drivers
        private string _ldFilter = "all";
        private string _ldRangeLabel = "";
        private List<HiatmeAiClient.LateDriversEventRow> _ldEventRows;
        private List<HiatmeAiClient.LateDriversDriverSummary> _ldDriverRows;
        private readonly List<SupeyMaterialButton> _ldPeriodButtons = new List<SupeyMaterialButton>();

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
                    ldSplit = null;
                    ldDriverLv = null;
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
                    ldDriverHeader = null;
                    ldTripHeader = null;
                    _ldLiveChromeCard = null;
                    _ldLiveChromeHost = null;
                    _ldPeriodButtons.Clear();
                }
                if (tabImageList != null && tabImageList.Images.ContainsKey("late-drivers.png"))
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
                    Text = "Status: Late Drivers — idle",
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
                    Text = "Showing: today’s late trips (auto-refresh)",
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

                // Dock order (Schedule Builder style): Fill first, then Bottom/Top so Fill gets remainder.
                ldMainCard.Controls.Add(ldSplit);
                ldMainCard.Controls.Add(ldChartsHost);
                ldMainCard.Controls.Add(ldRangeCaptionLbl);
                ldMainCard.Controls.Add(ldToolbar);

                tabPageLateDrivers.Controls.Add(ldStatusCard);
                tabPageLateDrivers.Controls.Add(ldMainCard);
                tabPageLateDrivers.Resize += (_, __) => LayoutLateDriversTabPanels();

                UpdateLateDriversToolbarHints();
                ApplyLateDriversVisualTheme(layout: true);
                _ldBuilt = true;
            }
            catch (Exception ex)
            {
                // Do NOT set _ldBuilt — allow a retry on next tab select.
                try
                {
                    tabPageLateDrivers.Text = "Late Drivers";
                    System.Diagnostics.Debug.WriteLine("Late Drivers UI failed: " + ex);
                    if (ldStatusLbl != null && !ldStatusLbl.IsDisposed)
                        ldStatusLbl.Text = "Status: Late Drivers UI failed — " + ex.Message;
                    else if (tabPageLateDrivers.Controls.Count == 0)
                    {
                        tabPageLateDrivers.Controls.Add(new Label
                        {
                            Dock = DockStyle.Fill,
                            TextAlign = ContentAlignment.MiddleCenter,
                            Text = "Late Drivers failed to build:\r\n" + ex.Message,
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

            // ── Charts strip (bottom) ─────────────────────────────────────
            ldChartsHost = new Panel
            {
                Name = "ldChartsHost",
                Dock = DockStyle.Bottom,
                Height = LateDriversChartH,
                Padding = new Padding(10, 0, 10, 10),
                BackColor = Color.Transparent,
            };
            ldChartsHost.Resize += (_, __) => LayoutLateDriversChartsHost();

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
            ldChartSide = CreateLateDriversChart("ldChartSide", "PU / DO");
            ldChartMinutes.Dock = DockStyle.Fill;
            ldChartSide.Dock = DockStyle.Fill;
            ldChartMinutesCard.Padding = new Padding(6);
            ldChartSideCard.Padding = new Padding(6);
            ldChartMinutesCard.Controls.Add(ldChartMinutes);
            ldChartSideCard.Controls.Add(ldChartSide);
            ldChartsHost.Controls.Add(ldChartMinutesCard);
            ldChartsHost.Controls.Add(ldChartSideCard);

            // ── Master/detail split (Fill) — sized before mins ────────────
            ldSplit = new SplitContainer
            {
                Name = "ldSplit",
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterWidth = 6,
                Panel1MinSize = 0,
                Panel2MinSize = 0,
                FixedPanel = FixedPanel.Panel1,
                Width = Math.Max(640, LateDriversDriverPaneW + 320),
                Height = 400,
                BackColor = SupeyTheme.Divider,
                Padding = new Padding(10, 8, 10, 8),
            };
            ldSplit.Panel1.BackColor = SupeyTheme.Surface;
            ldSplit.Panel2.BackColor = SupeyTheme.Surface;
            ldSplit.SizeChanged += (_, __) => ApplyLateDriversSplitterMins();

            // Driver pane: header Top + list Fill (same pattern as Schedule Builder trips)
            ldDriverHeader = new Panel
            {
                Name = "ldDriverHeader",
                Dock = DockStyle.Top,
                Height = 40,
                Padding = new Padding(8, 4, 8, 4),
                BackColor = SupeyTheme.SurfaceHeader,
            };
            var driverDivider = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 1,
                BackColor = SupeyTheme.Divider,
            };
            var driverHeaderRow = new TableLayoutPanel
            {
                Name = "ldDriverHeaderRow",
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = Color.Transparent,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
            };
            driverHeaderRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            driverHeaderRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 158f));
            driverHeaderRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            ldDriverCaptionLbl = new Label
            {
                Name = "ldDriverCaptionLbl",
                AutoSize = false,
                Text = "Drivers",
                TextAlign = ContentAlignment.MiddleLeft,
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                Margin = Padding.Empty,
            };
            ldFilterCombo = new SupeyComboBox { Name = "ldFilterCombo" };
            ConfigureToolbarSupeyCombo(ldFilterCombo, 150);
            ldFilterCombo.Dock = DockStyle.Fill;
            ldFilterCombo.Margin = new Padding(6, 0, 0, 0);
            ldFilterCombo.Items.AddRange(new object[]
            {
                "Show: All",
                "Show: Open now",
                "Show: PU-heavy",
                "Show: DO-heavy",
                "Show: Repeat (2+)",
            });
            ldFilterCombo.SelectedIndex = 0;
            ldFilterCombo.SelectedIndexChanged += (_, __) =>
            {
                _ldFilter = LateDriversFilterKey();
                BindLateDriversDriverRoster();
                BindLateDriversTripPane();
                RefreshLateDriversCharts();
            };
            driverHeaderRow.Controls.Add(ldDriverCaptionLbl, 0, 0);
            driverHeaderRow.Controls.Add(ldFilterCombo, 1, 0);
            ldDriverHeader.Controls.Add(driverHeaderRow);
            ldDriverHeader.Controls.Add(driverDivider);

            ldDriverLv = CreateLateDriversListView("ldDriverLv");
            ldDriverLv.Columns.Add("Driver", 150);
            ldDriverLv.Columns.Add("Lates", 50);
            ldDriverLv.Columns.Add("PU", 40);
            ldDriverLv.Columns.Add("DO", 40);
            ldDriverLv.Columns.Add("Open", 44);
            ldDriverLv.Columns.Add("Minutes", 64);
            ldDriverLv.SelectedIndexChanged += (_, __) =>
            {
                if (ldDriverLv.SelectedItems.Count == 0)
                    _ldSelectedDriver = null;
                else
                {
                    var tag = ldDriverLv.SelectedItems[0].Tag as HiatmeAiClient.LateDriversDriverSummary;
                    string name = tag?.Driver ?? ldDriverLv.SelectedItems[0].Text;
                    _ldSelectedDriver = string.Equals(name, "(All drivers)", StringComparison.OrdinalIgnoreCase)
                        ? null
                        : name;
                }
                UpdateLateDriversTripCaption();
                BindLateDriversTripPane();
            };

            // Fill first, then Top header (WinForms dock order)
            ldSplit.Panel1.Controls.Add(ldDriverLv);
            ldSplit.Panel1.Controls.Add(ldDriverHeader);

            // Trip pane
            ldTripHeader = new Panel
            {
                Name = "ldTripHeader",
                Dock = DockStyle.Top,
                Height = 40,
                Padding = new Padding(8, 6, 8, 4),
                BackColor = SupeyTheme.SurfaceHeader,
            };
            var tripDivider = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 1,
                BackColor = SupeyTheme.Divider,
            };
            ldTripCaptionLbl = new Label
            {
                Name = "ldTripCaptionLbl",
                AutoSize = false,
                Text = "Late trips",
                TextAlign = ContentAlignment.MiddleLeft,
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
            };
            ldTripHeader.Controls.Add(ldTripCaptionLbl);
            ldTripHeader.Controls.Add(tripDivider);

            ldTripLv = CreateLateDriversListView("ldTripLv");
            ldTripLv.Columns.Add("Date", 90);
            ldTripLv.Columns.Add("Side", 50);
            ldTripLv.Columns.Add("Trip", 110);
            ldTripLv.Columns.Add("Client", 140);
            ldTripLv.Columns.Add("Sched", 90);
            ldTripLv.Columns.Add("Actual", 90);
            ldTripLv.Columns.Add("Late", 70);
            ldTripLv.Columns.Add("Status", 120);
            ldTripLv.Columns.Add("State", 80);
            ldTripLv.DoubleClick += LdTripLv_DoubleClick;

            ldSplit.Panel2.Controls.Add(ldTripLv);
            ldSplit.Panel2.Controls.Add(ldTripHeader);

            SupeyListViewHelpers.WireSplitContainerSmoothResize(ldSplit);
            ApplyLateDriversSplitterMins();
        }

        private void ApplyLateDriversSplitterMins()
        {
            if (ldSplit == null || ldSplit.IsDisposed || ldSplit.Width < 80)
                return;
            try
            {
                ldSplit.Panel1MinSize = 0;
                ldSplit.Panel2MinSize = 0;
                int bodyW = ldSplit.Width;
                int min1 = Math.Min(160, Math.Max(80, bodyW / 5));
                int min2 = Math.Min(200, Math.Max(80, bodyW / 5));
                if (min1 + min2 + ldSplit.SplitterWidth > bodyW)
                {
                    min1 = Math.Max(40, (bodyW - ldSplit.SplitterWidth) / 3);
                    min2 = Math.Max(40, (bodyW - ldSplit.SplitterWidth) / 3);
                }
                int want = Math.Min(LateDriversDriverPaneW, bodyW - min2 - ldSplit.SplitterWidth);
                want = Math.Max(min1, want);
                if (Math.Abs(ldSplit.SplitterDistance - want) > 8)
                    ldSplit.SplitterDistance = want;
                ldSplit.Panel1MinSize = min1;
                ldSplit.Panel2MinSize = min2;
            }
            catch { }
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
            int w = Math.Max(40, ldChartsHost.ClientSize.Width - padL - padR);
            int h = Math.Max(40, ldChartsHost.ClientSize.Height - padT - padB);
            int leftW = Math.Max(120, (w - gap) * 2 / 3);
            int rightW = Math.Max(100, w - gap - leftW);
            if (ldChartMinutesCard != null && !ldChartMinutesCard.IsDisposed)
                ldChartMinutesCard.SetBounds(padL, padT, leftW, h);
            if (ldChartSideCard != null && !ldChartSideCard.IsDisposed)
                ldChartSideCard.SetBounds(padL + leftW + gap, padT, rightW, h);
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
                ldTripCaptionLbl.Text = "Late trips — all drivers (click a driver to focus)";
            else
                ldTripCaptionLbl.Text = "Late trips — " + _ldSelectedDriver + "  (All drivers to clear)";
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
            if (ldToolbar != null && !ldToolbar.IsDisposed)
                ldToolbar.BackColor = SupeyTheme.SurfaceHeader;
            if (ldDriverHeader != null && !ldDriverHeader.IsDisposed)
                ldDriverHeader.BackColor = SupeyTheme.SurfaceHeader;
            if (ldTripHeader != null && !ldTripHeader.IsDisposed)
                ldTripHeader.BackColor = SupeyTheme.SurfaceHeader;
            if (ldRangeCaptionLbl != null && !ldRangeCaptionLbl.IsDisposed)
                ldRangeCaptionLbl.BackColor = SupeyTheme.Surface;
            StyleLateDriversList(ldDriverLv);
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
            ApplyLateDriversSplitterMins();
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
            if (s.Contains("pu-heavy") || s.Contains("pu heavy"))
                return "pu";
            if (s.Contains("do-heavy") || s.Contains("do heavy"))
                return "do";
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
                    if (st != null && st.Ok && string.Equals(st.ContentHash, _ldLastHash, StringComparison.Ordinal))
                    {
                        SetLateDriversStatus(
                            "Status: Live — " + st.EventCount + " late today ("
                            + st.OpenCount + " still open) · unchanged · "
                            + DateTime.Now.ToString("h:mm:ss tt", CultureInfo.CurrentCulture));
                        return;
                    }
                }

                SetLateDriversStatus("Status: Loading " + mode + "…");

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
                        ApplyLateDriversEventPayload(
                            doc.Events,
                            doc.ContentHash,
                            sd,
                            sd,
                            doc.ModivcareTripCount,
                            live: true);
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
                        ApplyLateDriversEventPayload(
                            doc.Events,
                            doc.ContentHash,
                            sd,
                            sd,
                            doc.ModivcareTripCount,
                            live: false);
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
                    // Prefer minutes-then-count like Live/Day rollup (server may sort by count only).
                    if (_ldDriverRows.Count == 0 && _ldEventRows.Count > 0)
                        _ldDriverRows = BuildLateDriversRollup(_ldEventRows);
                    else
                        SortLateDriversByMinutes(_ldDriverRows);
                    _ldRangeLabel = (doc.FromDate ?? "") + " → " + (doc.ToDate ?? "");
                    BindLateDriversDriverRoster();
                    BindLateDriversTripPane();
                    RefreshLateDriversCharts();
                    UpdateLateDriversToolbarHints();
                    SetLateDriversStatus(
                        "Status: " + CultureInfo.CurrentCulture.TextInfo.ToTitleCase(mode)
                        + " " + _ldRangeLabel
                        + " — " + (_ldDriverRows?.Count ?? 0) + " drivers · "
                        + (_ldEventRows?.Count ?? 0) + " late events");
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
            bool live)
        {
            _ldLastHash = contentHash ?? "";
            _ldEventRows = events ?? new List<HiatmeAiClient.LateDriversEventRow>();
            _ldDriverRows = BuildLateDriversRollup(_ldEventRows);
            _ldRangeLabel = fromDate == toDate ? fromDate : (fromDate + " → " + toDate);
            BindLateDriversDriverRoster();
            BindLateDriversTripPane();
            RefreshLateDriversCharts();
            UpdateLateDriversToolbarHints();
            int openN = _ldEventRows.Count(e => e != null && e.Open);
            if (live)
            {
                SetLateDriversStatus(
                    "Status: Live — " + _ldEventRows.Count + " late today ("
                    + openN + " still open) · MC " + mcTripCount
                    + " · " + DateTime.Now.ToString("h:mm:ss tt", CultureInfo.CurrentCulture));
            }
            else
            {
                SetLateDriversStatus(
                    "Status: Day " + fromDate + " — " + _ldEventRows.Count + " late ("
                    + openN + " still open) · MC " + mcTripCount);
            }
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
                int cmp = b.TotalMinutes.CompareTo(a.TotalMinutes);
                if (cmp != 0) return cmp;
                return b.LateCount.CompareTo(a.LateCount);
            });
        }

        private List<HiatmeAiClient.LateDriversDriverSummary> FilteredLateDrivers()
        {
            var src = _ldDriverRows ?? new List<HiatmeAiClient.LateDriversDriverSummary>();
            switch (_ldFilter)
            {
                case "open":
                    return src.Where(d => d != null && d.OpenCount > 0).ToList();
                case "pu":
                    return src.Where(d => d != null && d.PuCount >= d.DoCount && d.LateCount > 0).ToList();
                case "do":
                    return src.Where(d => d != null && d.DoCount > d.PuCount).ToList();
                case "repeat":
                    return src.Where(d => d != null && d.LateCount >= 2).ToList();
                default:
                    return src.Where(d => d != null).ToList();
            }
        }

        private void BindLateDriversDriverRoster()
        {
            if (ldDriverLv == null || ldDriverLv.IsDisposed)
                return;
            var rows = FilteredLateDrivers();
            string keep = _ldSelectedDriver;
            ldDriverLv.BeginUpdate();
            try
            {
                ldDriverLv.Items.Clear();
                int allLates = rows.Sum(d => d.LateCount);
                int allPu = rows.Sum(d => d.PuCount);
                int allDo = rows.Sum(d => d.DoCount);
                int allOpen = rows.Sum(d => d.OpenCount);
                double allMins = rows.Sum(d => d.TotalMinutes);
                var allItem = new ListViewItem("(All drivers)");
                allItem.SubItems.Add(allLates.ToString(CultureInfo.InvariantCulture));
                allItem.SubItems.Add(allPu.ToString(CultureInfo.InvariantCulture));
                allItem.SubItems.Add(allDo.ToString(CultureInfo.InvariantCulture));
                allItem.SubItems.Add(allOpen.ToString(CultureInfo.InvariantCulture));
                allItem.SubItems.Add(allMins.ToString("0", CultureInfo.InvariantCulture) + "m");
                allItem.Tag = null;
                allItem.Font = new Font(ldDriverLv.Font, FontStyle.Bold);
                ldDriverLv.Items.Add(allItem);

                int selectAt = 0;
                for (int i = 0; i < rows.Count; i++)
                {
                    var d = rows[i];
                    var item = new ListViewItem(d.Driver ?? "");
                    item.SubItems.Add(d.LateCount.ToString(CultureInfo.InvariantCulture));
                    item.SubItems.Add(d.PuCount.ToString(CultureInfo.InvariantCulture));
                    item.SubItems.Add(d.DoCount.ToString(CultureInfo.InvariantCulture));
                    item.SubItems.Add(d.OpenCount.ToString(CultureInfo.InvariantCulture));
                    item.SubItems.Add(d.TotalMinutes.ToString("0", CultureInfo.InvariantCulture) + "m");
                    item.Tag = d;
                    if (d.OpenCount > 0)
                        item.ForeColor = Color.FromArgb(200, 80, 60);
                    ldDriverLv.Items.Add(item);
                    if (!string.IsNullOrEmpty(keep)
                        && string.Equals(d.Driver, keep, StringComparison.OrdinalIgnoreCase))
                        selectAt = i + 1;
                }
                if (ldDriverLv.Items.Count > selectAt)
                    ldDriverLv.Items[selectAt].Selected = true;
            }
            finally
            {
                ldDriverLv.EndUpdate();
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
                        && string.Equals(e.Driver ?? "(unassigned)", _ldSelectedDriver, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }
            else
            {
                // Respect roster filter for "All"
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
                    string side = string.Equals(e.Side, "do", StringComparison.OrdinalIgnoreCase) ? "DO" : "PU";
                    var item = new ListViewItem(e.ServiceDate ?? "");
                    item.SubItems.Add(side);
                    item.SubItems.Add(e.TripNo ?? "");
                    item.SubItems.Add(e.Client ?? "");
                    item.SubItems.Add(FormatLateDriversTime(e.SchedIso));
                    item.SubItems.Add(FormatLateDriversTime(e.ActualIso, blank: "—"));
                    item.SubItems.Add(e.MinutesLate.ToString("0", CultureInfo.InvariantCulture) + "m");
                    item.SubItems.Add(e.StatusLatest ?? "");
                    item.SubItems.Add(e.Open ? "Open" : "Resolved");
                    item.Tag = e;
                    if (e.Open)
                        item.ForeColor = Color.FromArgb(200, 80, 60);
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
                    ldChartMinutes.Titles[0].Text = "Top late drivers (minutes)";
                SupeyChartTheme.Apply(ldChartMinutes);
                s.Color = SupeyTheme.AccentPrimary;
            }

            if (ldChartSide != null && !ldChartSide.IsDisposed)
            {
                var s = ldChartSide.Series[0];
                s.Points.Clear();
                int pu = FilteredLateDrivers().Sum(d => d.PuCount);
                int dO = FilteredLateDrivers().Sum(d => d.DoCount);
                s.Points.AddXY("PU", pu);
                s.Points.AddXY("DO", dO);
                if (ldChartSide.Titles.Count > 0)
                    ldChartSide.Titles[0].Text = "PU vs DO late counts";
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
