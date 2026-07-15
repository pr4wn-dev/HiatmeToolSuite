using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>Late Drivers tool tab — live open lates + day/week/month history from the AI panel.</summary>
    partial class Form1
    {
        // tabPageLateDrivers is declared in Form1.Designer.cs (always under Trip Scout).
        private SupeyCard ldMainCard;
        private SupeyCard ldStatusCard;
        private SupeyLabel ldStatusLbl;
        private SupeyListView ldlv;
        private RJDatePicker ldDatePicker;
        private SupeyComboBox ldModeCombo;

        private const int LateDriversLivePollIntervalMs = 60_000;
        private const int LateDriversLiveScanMinVisibleMs = 1200;
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
        private List<HiatmeAiClient.LateDriversEventRow> _ldEventRows;
        private List<HiatmeAiClient.LateDriversDriverSummary> _ldDriverRows;

        private void InitializeLateDriversTab()
        {
            if (_ldBuilt || hiatmeTabControl == null || tabPageLateDrivers == null)
                return;

            try
            {
                if (tabImageList != null && tabImageList.Images.ContainsKey("late-drivers.png"))
                    tabPageLateDrivers.ImageKey = "late-drivers.png";
                else if (tabImageList != null && tabImageList.Images.ContainsKey("magnify.png"))
                    tabPageLateDrivers.ImageKey = "magnify.png";

                // Keep directly under Trip Scout if strip order drifted.
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

                ldModeCombo = new SupeyComboBox
                {
                    Name = "ldModeCombo",
                };
                ConfigureToolbarSupeyCombo(ldModeCombo, 112);
                ldModeCombo.Items.AddRange(new object[] { "Live", "Day", "Week", "Month" });
                ldModeCombo.SelectedIndex = 0;
                ldModeCombo.SelectedIndexChanged += (_, __) =>
                {
                    SyncLateDriversLivePollingForMode();
                    _ = LateDriversRefreshAsync(force: true);
                };

                EnsureLateDriversLiveChrome();

                ldlv = new SupeyListView
                {
                    Name = "ldlv",
                    View = View.Details,
                    FullRowSelect = true,
                    HideSelection = false,
                    HeaderStyle = ColumnHeaderStyle.Clickable,
                    BorderStyle = BorderStyle.None,
                    MultiSelect = false,
                };
                try { ldlv.Font = ListViewOwnerDrawFonts.Cell; } catch { }
                ldlv.Columns.Add("Driver", 140);
                ldlv.Columns.Add("Side", 50);
                ldlv.Columns.Add("Trip", 110);
                ldlv.Columns.Add("Client", 140);
                ldlv.Columns.Add("Sched", 90);
                ldlv.Columns.Add("Actual", 90);
                ldlv.Columns.Add("Late", 70);
                ldlv.Columns.Add("Status", 120);
                ldlv.Columns.Add("State", 80);
                ldlv.Columns.Add("Date", 90);
                ldlv.DoubleClick += Ldlv_DoubleClick;
                ldlv.DrawColumnHeader += listView_DrawColumnHeader;
                ldlv.DrawItem += listView_DrawItem;
                ldlv.DrawSubItem += listView_DrawSubItem;
                ListViewSorter.Attach(ldlv);
                ListViewMinWidthEnforcer.Attach(ldlv);
                ListViewHeaderEmptyAreaPainter.Attach(ldlv);
                SupeyListViewHelpers.EnableDoubleBufferRecursively(ldlv);

                ldMainCard.Controls.Add(ldlv);
                ldMainCard.Controls.Add(ldDatePicker);
                if (_ldLiveChromeCard != null)
                    ldMainCard.Controls.Add(_ldLiveChromeCard);
                ldDatePicker.BringToFront();
                _ldLiveChromeCard?.BringToFront();
                ldlv.SendToBack();

                tabPageLateDrivers.Controls.Add(ldStatusCard);
                tabPageLateDrivers.Controls.Add(ldMainCard);
                tabPageLateDrivers.Resize += (_, __) => LayoutLateDriversTabPanels();

                ApplyLateDriversVisualTheme(layout: true);
                _ldBuilt = true;
            }
            catch (Exception ex)
            {
                // Designer tab stays on the strip even if chrome fails.
                try
                {
                    tabPageLateDrivers.Text = "Late Drivers";
                    System.Diagnostics.Debug.WriteLine("Late Drivers UI failed: " + ex);
                }
                catch { }
                _ldBuilt = true;
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
                    Size = new Size(200, TripScoutLiveCardHeight),
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

            if (ldModeCombo != null && !ldModeCombo.IsDisposed
                && !ReferenceEquals(ldModeCombo.Parent, _ldLiveChromeHost))
            {
                _ldLiveChromeHost.Controls.Add(ldModeCombo);
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

        private int MeasureLateDriversLiveChromeWidth(bool live)
        {
            const int comboW = 112;
            int width = TripScoutLiveCardPadH * 2 + comboW;
            if (!live)
                return width;

            width += TripScoutLiveScanSlot + TripScoutLiveCardGap;
            width += TripScoutLiveTimerCardW + TripScoutLiveCardGap;
            width += 1 + TripScoutLiveCardGap; // divider
            return width;
        }

        private void LayoutLateDriversLiveChromeHost(bool live)
        {
            if (_ldLiveChromeHost == null || _ldLiveChromeHost.IsDisposed)
                return;

            const int comboW = 112;
            int hostH = TripScoutLiveCardHeight;
            int x = TripScoutLiveCardPadH;

            if (_ldLiveScanCard != null && !_ldLiveScanCard.IsDisposed)
                _ldLiveScanCard.Visible = live;
            if (_ldLiveTimerCard != null && !_ldLiveTimerCard.IsDisposed)
                _ldLiveTimerCard.Visible = live;
            if (_ldLiveDivider != null && !_ldLiveDivider.IsDisposed)
                _ldLiveDivider.Visible = live;

            if (live)
            {
                int slotY = (hostH - TripScoutLiveScanSlot) / 2;
                _ldLiveScanCard?.SetBounds(x, slotY, TripScoutLiveScanSlot, TripScoutLiveScanSlot);
                x += TripScoutLiveScanSlot + TripScoutLiveCardGap;

                int timerH = hostH - (TripScoutLiveCardPadV * 2);
                int timerY = TripScoutLiveCardPadV;
                _ldLiveTimerCard?.SetBounds(x, timerY, TripScoutLiveTimerCardW, timerH);
                x += TripScoutLiveTimerCardW + TripScoutLiveCardGap;

                int divH = hostH - (TripScoutLiveCardPadV * 2);
                int divY = TripScoutLiveCardPadV;
                _ldLiveDivider?.SetBounds(x, divY, 1, divH);
                x += 1 + TripScoutLiveCardGap;
            }

            if (ldModeCombo != null && !ldModeCombo.IsDisposed)
            {
                int comboH = Math.Max(30, ldModeCombo.Height);
                int comboY = Math.Max(0, (hostH - comboH) / 2);
                ldModeCombo.SetBounds(x, comboY, comboW, comboH);
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
            if (ldModeCombo != null && !ldModeCombo.IsDisposed)
                ConfigureToolbarSupeyCombo(ldModeCombo, 112);
            if (ldlv != null)
            {
                ldlv.BackColor = SupeyTheme.ListBody;
                ldlv.ForeColor = SupeyTheme.ListText;
                try { ldlv.Font = ListViewOwnerDrawFonts.Cell; } catch { }
                ldlv.BorderStyle = BorderStyle.None;
                ldlv.FullRowSelect = true;
                ldlv.HideSelection = false;
                ldlv.HeaderStyle = ColumnHeaderStyle.Clickable;
                ldlv.View = View.Details;
                ldlv.OwnerDraw = true;
            }
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

            const int pad = 14;
            const int toolbarH = TripScoutLiveCardHeight;
            int y = pad;
            int x = pad;
            if (ldDatePicker != null && !ldDatePicker.IsDisposed)
            {
                const int dateH = 36;
                ldDatePicker.SetBounds(x, y + (toolbarH - dateH) / 2, 214, dateH);
                x = ldDatePicker.Right + 10;
            }

            EnsureLateDriversLiveChrome();
            bool liveMode = LateDriversSelectedMode() == "live";
            if (_ldLiveChromeCard != null && !_ldLiveChromeCard.IsDisposed)
            {
                _ldLiveChromeCard.Visible = true;
                StyleLateDriversLiveChromeCard(liveMode);
                int chromeW = MeasureLateDriversLiveChromeWidth(liveMode);
                int chromeX = Math.Max(x + 8, ldMainCard.ClientSize.Width - pad - chromeW);
                _ldLiveChromeHost?.SuspendLayout();
                try
                {
                    _ldLiveChromeCard.SetBounds(chromeX, y, chromeW, toolbarH);
                    LayoutLateDriversLiveChromeHost(liveMode);
                }
                finally
                {
                    _ldLiveChromeHost?.ResumeLayout(true);
                }
                _ldLiveChromeCard.BringToFront();
            }

            int listTop = y + toolbarH + 12;
            if (ldlv != null && !ldlv.IsDisposed)
            {
                ldlv.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
                ldlv.SetBounds(
                    pad,
                    listTop,
                    Math.Max(100, ldMainCard.ClientSize.Width - (pad * 2)),
                    Math.Max(80, ldMainCard.ClientSize.Height - listTop - pad));
            }
        }

        private string LateDriversSelectedMode()
        {
            string s = (ldModeCombo?.SelectedItem as string ?? "Live").Trim().ToLowerInvariant();
            if (s == "day" || s == "week" || s == "month")
                return s;
            return "live";
        }

        private string LateDriversSelectedServiceDateIso()
        {
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
            if (_ldLiveScanCard != null && !_ldLiveScanCard.IsDisposed)
                _ldLiveScanCard.Visible = false;
            if (_ldLiveTimerCard != null && !_ldLiveTimerCard.IsDisposed)
                _ldLiveTimerCard.Visible = false;
            if (_ldLiveDivider != null && !_ldLiveDivider.IsDisposed)
                _ldLiveDivider.Visible = false;
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
            if (_ldLiveScanCard != null && !_ldLiveScanCard.IsDisposed)
                _ldLiveScanCard.Visible = true;
            if (_ldLiveTimerCard != null && !_ldLiveTimerCard.IsDisposed)
                _ldLiveTimerCard.Visible = true;
            if (_ldLiveDivider != null && !_ldLiveDivider.IsDisposed)
                _ldLiveDivider.Visible = true;
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

                if (mode == "live")
                {
                    var doc = await HiatmeAiClient.GetLateDriversLiveAsync(settings, sd)
                        .ConfigureAwait(true);
                    if (doc == null || !doc.Ok)
                    {
                        SetLateDriversStatus("Status: " + (doc?.Error ?? "live load failed"));
                        return;
                    }
                    _ldLastHash = doc.ContentHash ?? "";
                    _ldDriverRows = null;
                    _ldEventRows = doc.Events ?? new List<HiatmeAiClient.LateDriversEventRow>();
                    BindLateDriversEventList(_ldEventRows, showDate: false);
                    int openN = 0;
                    foreach (var ev in _ldEventRows)
                    {
                        if (ev != null && ev.Open)
                            openN++;
                    }
                    SetLateDriversStatus(
                        "Status: Live — " + _ldEventRows.Count + " late today ("
                        + openN + " still open) · "
                        + DateTime.Now.ToString("h:mm:ss tt", CultureInfo.CurrentCulture));
                }
                else if (mode == "day")
                {
                    var doc = await HiatmeAiClient.GetLateDriversDayAsync(settings, sd)
                        .ConfigureAwait(true);
                    if (doc == null || !doc.Ok)
                    {
                        SetLateDriversStatus("Status: " + (doc?.Error ?? "day load failed"));
                        return;
                    }
                    _ldLastHash = doc.ContentHash ?? "";
                    _ldDriverRows = null;
                    _ldEventRows = doc.Events ?? new List<HiatmeAiClient.LateDriversEventRow>();
                    BindLateDriversEventList(_ldEventRows, showDate: false);
                    SetLateDriversStatus(
                        "Status: Day " + sd + " — " + doc.Count + " late (" + doc.OpenCount + " still open)");
                }
                else
                {
                    var doc = await HiatmeAiClient.GetLateDriversPeriodAsync(settings, mode, sd)
                        .ConfigureAwait(true);
                    if (doc == null || !doc.Ok)
                    {
                        SetLateDriversStatus("Status: " + (doc?.Error ?? mode + " load failed"));
                        return;
                    }
                    _ldLastHash = doc.ContentHash ?? "";
                    _ldEventRows = doc.Events;
                    _ldDriverRows = doc.Drivers ?? new List<HiatmeAiClient.LateDriversDriverSummary>();
                    BindLateDriversDriverList(_ldDriverRows);
                    SetLateDriversStatus(
                        "Status: " + CultureInfo.CurrentCulture.TextInfo.ToTitleCase(mode)
                        + " " + doc.FromDate + " → " + doc.ToDate
                        + " — " + doc.DriverCount + " drivers · " + doc.EventCount + " late events"
                        + " (double-click a driver for trips)");
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

        private void BindLateDriversEventList(
            List<HiatmeAiClient.LateDriversEventRow> rows,
            bool showDate)
        {
            if (ldlv == null || ldlv.IsDisposed)
                return;
            ldlv.BeginUpdate();
            try
            {
                ldlv.Items.Clear();
                EnsureLateDriversEventColumns(showDate);
                if (rows == null)
                    return;
                foreach (var e in rows)
                {
                    if (e == null)
                        continue;
                    string side = string.Equals(e.Side, "do", StringComparison.OrdinalIgnoreCase) ? "DO" : "PU";
                    var item = new ListViewItem(e.Driver ?? "");
                    item.SubItems.Add(side);
                    item.SubItems.Add(e.TripNo ?? "");
                    item.SubItems.Add(e.Client ?? "");
                    item.SubItems.Add(FormatLateDriversTime(e.SchedIso));
                    item.SubItems.Add(FormatLateDriversTime(e.ActualIso, blank: "—"));
                    item.SubItems.Add(e.MinutesLate.ToString("0", CultureInfo.InvariantCulture) + "m");
                    item.SubItems.Add(e.StatusLatest ?? "");
                    item.SubItems.Add(e.Open ? "Open" : "Resolved");
                    item.SubItems.Add(showDate ? (e.ServiceDate ?? "") : "");
                    item.Tag = e;
                    if (e.Open)
                        item.ForeColor = Color.FromArgb(200, 80, 60);
                    ldlv.Items.Add(item);
                }
            }
            finally
            {
                ldlv.EndUpdate();
            }
        }

        private void BindLateDriversDriverList(List<HiatmeAiClient.LateDriversDriverSummary> rows)
        {
            if (ldlv == null || ldlv.IsDisposed)
                return;
            ldlv.BeginUpdate();
            try
            {
                ldlv.Items.Clear();
                EnsureLateDriversDriverColumns();
                if (rows == null)
                    return;
                foreach (var d in rows)
                {
                    if (d == null)
                        continue;
                    var item = new ListViewItem(d.Driver ?? "");
                    item.SubItems.Add(d.LateCount.ToString(CultureInfo.InvariantCulture));
                    item.SubItems.Add(d.PuCount.ToString(CultureInfo.InvariantCulture));
                    item.SubItems.Add(d.DoCount.ToString(CultureInfo.InvariantCulture));
                    item.SubItems.Add(d.OpenCount.ToString(CultureInfo.InvariantCulture));
                    item.SubItems.Add(d.TotalMinutes.ToString("0", CultureInfo.InvariantCulture) + "m");
                    item.Tag = d;
                    ldlv.Items.Add(item);
                }
            }
            finally
            {
                ldlv.EndUpdate();
            }
        }

        private void EnsureLateDriversEventColumns(bool showDate)
        {
            if (ldlv.Columns.Count >= 9
                && string.Equals(ldlv.Columns[1].Text, "Side", StringComparison.Ordinal)
                && string.Equals(ldlv.Columns[5].Text, "Actual", StringComparison.Ordinal))
            {
                if (ldlv.Columns.Count >= 10)
                    ldlv.Columns[9].Width = showDate ? 90 : 0;
                return;
            }
            ldlv.Columns.Clear();
            ldlv.Columns.Add("Driver", 140);
            ldlv.Columns.Add("Side", 50);
            ldlv.Columns.Add("Trip", 110);
            ldlv.Columns.Add("Client", 140);
            ldlv.Columns.Add("Sched", 90);
            ldlv.Columns.Add("Actual", 90);
            ldlv.Columns.Add("Late", 70);
            ldlv.Columns.Add("Status", 120);
            ldlv.Columns.Add("State", 80);
            ldlv.Columns.Add("Date", showDate ? 90 : 0);
        }

        private void EnsureLateDriversDriverColumns()
        {
            if (ldlv.Columns.Count >= 6
                && string.Equals(ldlv.Columns[1].Text, "Lates", StringComparison.Ordinal))
                return;
            ldlv.Columns.Clear();
            ldlv.Columns.Add("Driver", 180);
            ldlv.Columns.Add("Lates", 70);
            ldlv.Columns.Add("PU", 50);
            ldlv.Columns.Add("DO", 50);
            ldlv.Columns.Add("Open", 50);
            ldlv.Columns.Add("Minutes", 80);
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

        private void Ldlv_DoubleClick(object sender, EventArgs e)
        {
            if (ldlv?.SelectedItems == null || ldlv.SelectedItems.Count == 0)
                return;
            var tag = ldlv.SelectedItems[0].Tag;
            if (tag is HiatmeAiClient.LateDriversDriverSummary summary)
            {
                var trips = summary.Trips ?? new List<HiatmeAiClient.LateDriversEventRow>();
                _ldEventRows = trips;
                BindLateDriversEventList(trips, showDate: true);
                SetLateDriversStatus(
                    "Status: Trips for " + (summary.Driver ?? "") + " — " + trips.Count
                    + " late events (use mode combo / Refresh to return)");
                return;
            }
            if (tag is HiatmeAiClient.LateDriversEventRow row)
            {
                try
                {
                    // Prefer Jump to Trip Scout for the same service date when possible.
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
        }

        private void SetLateDriversStatus(string text)
        {
            if (ldStatusLbl == null || ldStatusLbl.IsDisposed)
                return;
            try { ldStatusLbl.Text = text ?? ""; } catch { }
        }
    }
}
