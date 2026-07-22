using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Market Performance — ModivCare TP scorecard meters + peer pulse (no Driver Habits list).
    /// </summary>
    partial class Form1
    {
        private SupeyCard mpMainCard;
        private SupeyCard mpStatusCard;
        private SupeyLabel mpStatusLbl;
        private Panel mpToolbar;
        private SupeyCard mpToolbarCard;
        private Panel mpToolbarInner;
        private SupeyMaterialButton mpRefreshBtn;
        private SupeyMaterialButton mpPullBtn;
        private SupeyMaterialButton mpHabitsBtn;
        private Panel mpBodyHost;
        private Panel mpScrollBody;
        private SupeyCard mpHeroCard;
        private Panel mpHeroInner;
        private Label mpHeroGrade;
        private Label mpHeroTitle;
        private Label mpHeroSub;
        private TableLayoutPanel mpMeterGrid;
        private Label mpPulseTitle;
        private Label mpPulseSub;
        private TableLayoutPanel mpPulseGrid;

        private readonly List<MarketMeterControl> _mpMeters = new List<MarketMeterControl>();
        private readonly List<MarketPeerPulseControl> _mpPulses = new List<MarketPeerPulseControl>();

        private bool _mpBuilt;
        private bool _mpFirstLoadDone;
        private bool _mpLoadInFlight;
        private CancellationTokenSource _mpLoadCts;

        /// <summary>Last good scorecard — shared with Driver Habits live OTP meter.</summary>
        private HiatmeAiClient.ModivcareMarketScorecard _mpLastScorecard;

        private void InitializeMarketPerformanceTab()
        {
            if (_mpBuilt || hiatmeTabControl == null || tabPageMarketPerformance == null)
                return;

            try
            {
                tabPageMarketPerformance.SuspendLayout();
                tabPageMarketPerformance.Controls.Clear();

                if (tabImageList != null && tabImageList.Images.ContainsKey("market-performance.png"))
                    tabPageMarketPerformance.ImageKey = "market-performance.png";
                else if (tabImageList != null && tabImageList.Images.ContainsKey("late-drivers.png"))
                    tabPageMarketPerformance.ImageKey = "late-drivers.png";

                tabPageMarketPerformance.Text = "Market Performance";
                tabPageMarketPerformance.BackColor = SupeyTheme.SurfaceBase;
                tabPageMarketPerformance.ForeColor = SupeyTheme.TextPrimary;
                tabPageMarketPerformance.Padding = new Padding(ToolTabInset);

                int habitsAt = hiatmeTabControl.TabPages.IndexOf(tabPageLateDrivers);
                int marketAt = hiatmeTabControl.TabPages.IndexOf(tabPageMarketPerformance);
                if (habitsAt >= 0)
                {
                    int want = habitsAt + 1;
                    if (marketAt != want)
                    {
                        hiatmeTabControl.TabPages.Remove(tabPageMarketPerformance);
                        if (want > hiatmeTabControl.TabPages.Count)
                            want = hiatmeTabControl.TabPages.Count;
                        hiatmeTabControl.TabPages.Insert(want, tabPageMarketPerformance);
                    }
                }

                mpStatusCard = new SupeyCard
                {
                    Name = "mpStatusCard",
                    Dock = DockStyle.Bottom,
                    Height = ToolTabStatusH,
                    SurfaceLevel = SupeyCard.Surface.StatusBar,
                    ShowBorder = true,
                    CornerRadius = 6,
                };
                mpStatusLbl = new SupeyLabel
                {
                    Name = "mpStatusLbl",
                    Text = "Status: ready — press Refresh to load scorecard from the AI panel.",
                    AutoSize = false,
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleLeft,
                    Padding = new Padding(10, 0, 10, 0),
                    ForeColor = SupeyTheme.TextSecondary,
                    Font = SupeyTheme.BodyFont,
                    BackColor = SupeyTheme.SurfaceStatusBar,
                };
                mpStatusCard.Controls.Add(mpStatusLbl);

                mpMainCard = new SupeyCard
                {
                    Name = "mpMainCard",
                    Dock = DockStyle.Fill,
                    SurfaceLevel = SupeyCard.Surface.Standard,
                    ShowBorder = true,
                    CornerRadius = 8,
                    Margin = new Padding(0, 0, 0, ToolTabGap),
                };

                BuildMarketPerformanceToolbar();
                BuildMarketPerformanceBody();

                mpMainCard.Controls.Add(mpBodyHost);
                mpMainCard.Controls.Add(mpToolbar);

                tabPageMarketPerformance.Controls.Add(mpMainCard);
                tabPageMarketPerformance.Controls.Add(mpStatusCard);

                tabPageMarketPerformance.VisibleChanged -= MarketPerformanceTab_VisibleChanged;
                tabPageMarketPerformance.VisibleChanged += MarketPerformanceTab_VisibleChanged;

                ApplyMarketPerformanceVisualTheme(layout: true);
                SupeyDarkScrollBars.Apply(tabPageMarketPerformance);

                _mpBuilt = true;
                tabPageMarketPerformance.ResumeLayout(true);
            }
            catch (Exception ex)
            {
                _mpBuilt = false;
                try { tabPageMarketPerformance.ResumeLayout(true); } catch { }
                try
                {
                    tabPageMarketPerformance.Controls.Clear();
                    tabPageMarketPerformance.Controls.Add(new Label
                    {
                        Dock = DockStyle.Fill,
                        TextAlign = ContentAlignment.MiddleCenter,
                        BackColor = Color.FromArgb(40, 20, 20),
                        ForeColor = Color.OrangeRed,
                        Font = new Font("Segoe UI", 11f),
                        Text = "Market Performance failed to build:\r\n\r\n" + ex.Message,
                    });
                }
                catch { }
            }
        }

        private void MarketPerformanceTab_VisibleChanged(object sender, EventArgs e)
        {
            if (tabPageMarketPerformance == null || !tabPageMarketPerformance.Visible) return;
            if (!_mpBuilt)
                InitializeMarketPerformanceTab();
            EnsureMarketPerformanceFirstLoad();
        }

        private void EnsureMarketPerformanceFirstLoad()
        {
            if (_mpFirstLoadDone || !_mpBuilt) return;
            _mpFirstLoadDone = true;
            try
            {
                BeginInvoke(new Action(() => _ = MarketPerformanceRefreshAsync(pull: false)));
            }
            catch
            {
                _ = MarketPerformanceRefreshAsync(pull: false);
            }
        }

        private void BuildMarketPerformanceToolbar()
        {
            mpToolbar = new Panel
            {
                Name = "mpToolbar",
                Dock = DockStyle.Top,
                Height = 56,
                Padding = new Padding(10, 6, 10, 4),
                BackColor = SupeyTheme.Surface,
            };
            mpToolbarCard = new SupeyCard
            {
                Name = "mpToolbarCard",
                Dock = DockStyle.Fill,
                SurfaceLevel = SupeyCard.Surface.Elevated,
                ShowBorder = true,
                CornerRadius = 8,
                Padding = Padding.Empty,
            };
            mpToolbarInner = new Panel
            {
                Name = "mpToolbarInner",
                Dock = DockStyle.Fill,
                Padding = new Padding(12, 8, 12, 8),
                BackColor = SupeyTheme.SurfaceElevated,
            };

            mpRefreshBtn = new SupeyMaterialButton
            {
                Name = "mpRefreshBtn",
                Text = "Refresh",
                Type = SupeyMaterialButton.MaterialButtonType.Contained,
                UseAccentColor = true,
                Size = new Size(100, 32),
                Location = new Point(0, 2),
            };
            mpRefreshBtn.Click += async (_, __) => await MarketPerformanceRefreshAsync(pull: false);

            mpPullBtn = new SupeyMaterialButton
            {
                Name = "mpPullBtn",
                Text = "Pull from panel",
                Type = SupeyMaterialButton.MaterialButtonType.Outlined,
                Size = new Size(130, 32),
                Location = new Point(112, 2),
            };
            mpPullBtn.Click += async (_, __) => await MarketPerformanceRefreshAsync(pull: true);

            mpHabitsBtn = new SupeyMaterialButton
            {
                Name = "mpHabitsBtn",
                Text = "Open Driver Habits",
                Type = SupeyMaterialButton.MaterialButtonType.Outlined,
                Size = new Size(150, 32),
                Location = new Point(252, 2),
            };
            mpHabitsBtn.Click += (_, __) =>
            {
                if (tabPageLateDrivers != null)
                    hiatmeTabControl.SelectedTab = tabPageLateDrivers;
            };

            mpToolbarInner.Controls.Add(mpRefreshBtn);
            mpToolbarInner.Controls.Add(mpPullBtn);
            mpToolbarInner.Controls.Add(mpHabitsBtn);
            mpToolbarCard.Controls.Add(mpToolbarInner);
            mpToolbar.Controls.Add(mpToolbarCard);
        }

        private void BuildMarketPerformanceBody()
        {
            mpBodyHost = new Panel
            {
                Name = "mpBodyHost",
                Dock = DockStyle.Fill,
                Padding = new Padding(12, 8, 12, 8),
                BackColor = SupeyTheme.Surface,
                AutoScroll = true,
            };

            mpScrollBody = new Panel
            {
                Name = "mpScrollBody",
                Dock = DockStyle.Top,
                Height = 680,
                BackColor = SupeyTheme.Surface,
            };

            mpHeroCard = new SupeyCard
            {
                Name = "mpHeroCard",
                Location = new Point(8, 4),
                Size = new Size(900, 84),
                SurfaceLevel = SupeyCard.Surface.Elevated,
                ShowBorder = true,
                CornerRadius = 8,
                Padding = Padding.Empty,
            };
            mpHeroInner = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(12, 10, 12, 10),
                BackColor = SupeyTheme.SurfaceElevated,
            };
            mpHeroGrade = new Label
            {
                Name = "mpHeroGrade",
                Text = "—",
                AutoSize = false,
                Size = new Size(64, 64),
                Location = new Point(0, 0),
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 20f, FontStyle.Bold),
                ForeColor = SupeyTheme.AccentPrimary,
                BackColor = SupeyTheme.Surface,
            };
            mpHeroTitle = new Label
            {
                Name = "mpHeroTitle",
                Text = "TP Performance",
                AutoSize = false,
                Location = new Point(80, 4),
                Size = new Size(700, 26),
                Font = new Font("Segoe UI Semibold", 14f),
                ForeColor = SupeyTheme.TextPrimary,
                BackColor = Color.Transparent,
            };
            mpHeroSub = new Label
            {
                Name = "mpHeroSub",
                Text = "Loading scorecard from AI panel…",
                AutoSize = false,
                Location = new Point(80, 34),
                Size = new Size(780, 36),
                Font = SupeyTheme.BodyFont,
                ForeColor = SupeyTheme.TextSecondary,
                BackColor = Color.Transparent,
            };
            mpHeroInner.Controls.Add(mpHeroGrade);
            mpHeroInner.Controls.Add(mpHeroTitle);
            mpHeroInner.Controls.Add(mpHeroSub);
            mpHeroCard.Controls.Add(mpHeroInner);

            mpMeterGrid = new TableLayoutPanel
            {
                Name = "mpMeterGrid",
                Location = new Point(8, 100),
                Size = new Size(900, 300),
                ColumnCount = 3,
                RowCount = 2,
                BackColor = SupeyTheme.Surface,
            };
            for (int c = 0; c < 3; c++)
                mpMeterGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
            for (int r = 0; r < 2; r++)
                mpMeterGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));

            _mpMeters.Clear();
            var meterDefs = new[]
            {
                ("On-time", false),
                ("TP score", false),
                ("Digital", false),
                ("Reroute <24h", true),
                ("No-shows", true),
                ("Complaints", true),
            };
            for (int i = 0; i < meterDefs.Length; i++)
            {
                var meter = new MarketMeterControl
                {
                    Dock = DockStyle.Fill,
                    Margin = new Padding(6),
                    Caption = meterDefs[i].Item1,
                    InvertGood = meterDefs[i].Item2,
                };
                _mpMeters.Add(meter);
                mpMeterGrid.Controls.Add(meter, i % 3, i / 3);
            }

            mpPulseTitle = new Label
            {
                Name = "mpPulseTitle",
                Text = "Peer pulse — how each meter sits vs ME / US",
                AutoSize = false,
                Location = new Point(8, 414),
                Size = new Size(700, 22),
                Font = new Font("Segoe UI Semibold", 11f),
                ForeColor = SupeyTheme.TextPrimary,
                BackColor = Color.Transparent,
            };
            mpPulseSub = new Label
            {
                Name = "mpPulseSub",
                Text = "Bars animate when the scorecard refreshes. Live On-time pressure lives on Driver Habits.",
                AutoSize = false,
                Location = new Point(8, 438),
                Size = new Size(900, 22),
                Font = SupeyTheme.CaptionFont,
                ForeColor = SupeyTheme.TextSecondary,
                BackColor = Color.Transparent,
            };

            mpPulseGrid = new TableLayoutPanel
            {
                Name = "mpPulseGrid",
                Location = new Point(8, 466),
                Size = new Size(900, 190),
                ColumnCount = 3,
                RowCount = 2,
                BackColor = SupeyTheme.Surface,
            };
            for (int c = 0; c < 3; c++)
                mpPulseGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
            for (int r = 0; r < 2; r++)
                mpPulseGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));

            _mpPulses.Clear();
            var pulseDefs = new[]
            {
                ("On-time", false),
                ("TP score", false),
                ("Digital", false),
                ("Reroute <24h", true),
                ("No-shows", true),
                ("Complaints", true),
            };
            for (int i = 0; i < pulseDefs.Length; i++)
            {
                var pulse = new MarketPeerPulseControl
                {
                    Dock = DockStyle.Fill,
                    Margin = new Padding(6),
                    Title = pulseDefs[i].Item1,
                    InvertGood = pulseDefs[i].Item2,
                };
                _mpPulses.Add(pulse);
                mpPulseGrid.Controls.Add(pulse, i % 3, i / 3);
            }

            mpScrollBody.Controls.Add(mpHeroCard);
            mpScrollBody.Controls.Add(mpMeterGrid);
            mpScrollBody.Controls.Add(mpPulseTitle);
            mpScrollBody.Controls.Add(mpPulseSub);
            mpScrollBody.Controls.Add(mpPulseGrid);
            mpBodyHost.Controls.Add(mpScrollBody);

            mpBodyHost.Resize += (_, __) => LayoutMarketPerformanceBody();
        }

        private void LayoutMarketPerformanceBody()
        {
            if (mpBodyHost == null || mpScrollBody == null) return;
            int w = Math.Max(640, mpBodyHost.ClientSize.Width - 28);
            mpScrollBody.Width = w + 8;
            if (mpHeroCard != null) mpHeroCard.Width = w;
            if (mpHeroSub != null) mpHeroSub.Width = Math.Max(280, w - 100);
            if (mpHeroTitle != null) mpHeroTitle.Width = mpHeroSub?.Width ?? w;
            if (mpMeterGrid != null) mpMeterGrid.Width = w;
            if (mpPulseSub != null) mpPulseSub.Width = w;
            if (mpPulseGrid != null) mpPulseGrid.Width = w;
            mpScrollBody.Height = (mpPulseGrid?.Bottom ?? 640) + 16;
        }

        private async Task MarketPerformanceRefreshAsync(bool pull)
        {
            if (!_mpBuilt || _mpLoadInFlight)
                return;

            _mpLoadInFlight = true;
            try
            {
                _mpLoadCts?.Cancel();
                _mpLoadCts = new CancellationTokenSource();
                var ct = _mpLoadCts.Token;

                if (mpRefreshBtn != null) mpRefreshBtn.Enabled = false;
                if (mpPullBtn != null) mpPullBtn.Enabled = false;
                SetMarketPerformanceStatus(pull
                    ? "Asking AI panel to pull a fresh Market scorecard…"
                    : "Loading Market scorecard from AI panel…");

                var settings = HiatmeAiSettings.Load();
                if (pull)
                {
                    var pullResult = await HiatmeAiClient.PostModivcareMarketPullAsync(settings, ct)
                        .ConfigureAwait(true);
                    if (!pullResult.Ok)
                    {
                        SetMarketPerformanceStatus("Pull failed: " + (pullResult.Error ?? "unknown"));
                        return;
                    }
                }

                var statusTask = HiatmeAiClient.GetModivcareMarketStatusAsync(settings, ct);
                var cardTask = HiatmeAiClient.GetModivcareMarketScorecardAsync(settings, ct);
                await Task.WhenAll(statusTask, cardTask).ConfigureAwait(true);

                var status = statusTask.Result;
                var card = cardTask.Result;
                if (!card.Ok && (card.Error ?? "").Length > 0)
                {
                    SetMarketPerformanceStatus("Scorecard error: " + card.Error);
                    return;
                }

                _mpLastScorecard = card;
                ApplyMarketScorecard(card, status);
                PushMarketOtpToDriverHabits();

                string when = !string.IsNullOrWhiteSpace(card?.PulledAtIso)
                    ? card.PulledAtIso
                    : (card?.PullDate ?? "unknown");
                SetMarketPerformanceStatus(
                    "Scorecard ready · panel snapshot " + when +
                    (status != null && status.Enabled ? " · daily auto-pull on" : ""));
            }
            catch (OperationCanceledException)
            {
                SetMarketPerformanceStatus("Refresh cancelled.");
            }
            catch (Exception ex)
            {
                SetMarketPerformanceStatus("Refresh failed: " + ex.Message);
            }
            finally
            {
                _mpLoadInFlight = false;
                if (mpRefreshBtn != null) mpRefreshBtn.Enabled = true;
                if (mpPullBtn != null) mpPullBtn.Enabled = true;
            }
        }

        private void ApplyMarketScorecard(
            HiatmeAiClient.ModivcareMarketScorecard card,
            HiatmeAiClient.ModivcareMarketStatus status)
        {
            var s = card?.Summary;
            if (card == null || !card.HasData || s == null)
            {
                if (mpHeroGrade != null) mpHeroGrade.Text = "—";
                if (mpHeroSub != null)
                    mpHeroSub.Text = "No scorecard on the AI panel yet. Use Pull from panel (or wait for the 7am pull).";
                foreach (var m in _mpMeters)
                    m.SetValue(null, "—", "—");
                foreach (var p in _mpPulses)
                    p.SetValues(null, null, null);
                return;
            }

            if (mpHeroGrade != null)
                mpHeroGrade.Text = string.IsNullOrWhiteSpace(s.Grade) ? "—" : s.Grade.Trim();

            string tp = s.TpCode?.ToString() ?? card.TpCode?.ToString() ?? "—";
            string rides = s.TotalRides.HasValue
                ? s.TotalRides.Value.ToLocaleString()
                : "—";
            if (mpHeroTitle != null)
                mpHeroTitle.Text = "TP Performance · " + (s.State ?? "—") + " · " + tp;
            if (mpHeroSub != null)
            {
                mpHeroSub.Text =
                    "Period " + (s.PeriodStart ?? "?") + " → " + (s.PeriodEnd ?? "?") +
                    " · " + rides + " rides" +
                    (string.IsNullOrWhiteSpace(card.PulledAtIso) ? "" : " · updated " + card.PulledAtIso);
            }

            SetMeter(0, s.Otp, false, PeerLine(s.OtpRegionalPeers, s.OtpNationalPeers));
            SetMeter(1, s.TpScore, false, PeerLine(s.RegionalPeers, s.NationalPeers));
            SetMeter(2, s.DigitalLevel, false, PeerLine(s.DigitalRegionalPeers, s.DigitalNationalPeers));
            SetMeter(3, s.Reroute24h, true, PeerLine(s.RerouteRegionalPeers, s.RerouteNationalPeers), scaleCap: 20);
            SetMeter(4, s.DriverNoShows, true, "lower is better", scaleCap: 2);
            SetMeter(5, s.MemberComplaints, true, "lower is better", scaleCap: 1);

            SetPulse(0, s.Otp, s.OtpRegionalPeers, s.OtpNationalPeers, false);
            SetPulse(1, s.TpScore, s.RegionalPeers, s.NationalPeers, false);
            SetPulse(2, s.DigitalLevel, s.DigitalRegionalPeers, s.DigitalNationalPeers, false);
            SetPulse(3, s.Reroute24h, s.RerouteRegionalPeers, s.RerouteNationalPeers, true);
            SetPulse(4, s.DriverNoShows, null, null, true);
            SetPulse(5, s.MemberComplaints, null, null, true);
        }

        private void SetMeter(int index, double? value, bool invert, string detail, double scaleCap = 100)
        {
            if (index < 0 || index >= _mpMeters.Count) return;
            var meter = _mpMeters[index];
            if (!value.HasValue)
            {
                meter.SetValue(null, "—", detail ?? "—");
                return;
            }

            double ring;
            if (invert)
                ring = Math.Min(100, Math.Max(0, (value.Value / Math.Max(0.0001, scaleCap)) * 100.0));
            else
                ring = Math.Min(100, Math.Max(0, value.Value));

            meter.InvertGood = invert;
            meter.SetValue(ring, string.Format(CultureInfo.InvariantCulture, "{0:0.0}%", value.Value), detail);
        }

        private void SetPulse(int index, double? you, double? regional, double? national, bool invert)
        {
            if (index < 0 || index >= _mpPulses.Count) return;
            _mpPulses[index].InvertGood = invert;
            _mpPulses[index].SetValues(you, regional, national);
        }

        private static string PeerLine(double? regional, double? national)
        {
            var bits = new List<string>();
            if (regional.HasValue)
                bits.Add("ME " + regional.Value.ToString("0.0", CultureInfo.InvariantCulture) + "%");
            if (national.HasValue)
                bits.Add("US " + national.Value.ToString("0.0", CultureInfo.InvariantCulture) + "%");
            return bits.Count == 0 ? "—" : string.Join(" · ", bits);
        }

        private void SetMarketPerformanceStatus(string text)
        {
            if (mpStatusLbl == null) return;
            mpStatusLbl.Text = "Status: " + (text ?? "");
        }

        private void ApplyMarketPerformanceVisualTheme(bool layout)
        {
            if (!_mpBuilt && mpMainCard == null) return;
            try
            {
                if (tabPageMarketPerformance != null)
                {
                    tabPageMarketPerformance.BackColor = SupeyTheme.SurfaceBase;
                    tabPageMarketPerformance.ForeColor = SupeyTheme.TextPrimary;
                }
                if (mpMainCard != null) StyleToolTabCard(mpMainCard, SupeyCard.Surface.Standard);
                if (mpStatusCard != null) StyleToolTabStatusBar(mpStatusCard);
                if (mpStatusLbl != null)
                {
                    mpStatusLbl.ForeColor = SupeyTheme.TextSecondary;
                    mpStatusLbl.Font = SupeyTheme.BodyFont;
                    mpStatusLbl.BackColor = SupeyTheme.SurfaceStatusBar;
                }
                if (mpToolbar != null) mpToolbar.BackColor = SupeyTheme.Surface;
                if (mpToolbarCard != null)
                {
                    mpToolbarCard.SurfaceLevel = SupeyCard.Surface.Elevated;
                    mpToolbarCard.BackColor = SupeyTheme.SurfaceElevated;
                }
                if (mpToolbarInner != null) mpToolbarInner.BackColor = SupeyTheme.SurfaceElevated;
                if (mpBodyHost != null) mpBodyHost.BackColor = SupeyTheme.Surface;
                if (mpScrollBody != null) mpScrollBody.BackColor = SupeyTheme.Surface;
                if (mpHeroCard != null)
                {
                    StyleToolTabCard(mpHeroCard, SupeyCard.Surface.Elevated);
                }
                if (mpHeroInner != null) mpHeroInner.BackColor = SupeyTheme.SurfaceElevated;
                if (mpHeroGrade != null)
                {
                    mpHeroGrade.ForeColor = SupeyTheme.AccentPrimary;
                    mpHeroGrade.BackColor = SupeyTheme.Surface;
                }
                if (mpHeroTitle != null) mpHeroTitle.ForeColor = SupeyTheme.TextPrimary;
                if (mpHeroSub != null)
                {
                    mpHeroSub.ForeColor = SupeyTheme.TextSecondary;
                    mpHeroSub.Font = SupeyTheme.BodyFont;
                }
                if (mpMeterGrid != null) mpMeterGrid.BackColor = SupeyTheme.Surface;
                if (mpPulseGrid != null) mpPulseGrid.BackColor = SupeyTheme.Surface;
                if (mpPulseTitle != null) mpPulseTitle.ForeColor = SupeyTheme.TextPrimary;
                if (mpPulseSub != null)
                {
                    mpPulseSub.ForeColor = SupeyTheme.TextSecondary;
                    mpPulseSub.Font = SupeyTheme.CaptionFont;
                }
                foreach (var m in _mpMeters) m?.Invalidate();
                foreach (var p in _mpPulses) p?.Invalidate();
                if (layout) LayoutMarketPerformanceBody();
            }
            catch { }
        }
    }

    internal static class MarketPerfFormatExtensions
    {
        public static string ToLocaleString(this int value)
        {
            return value.ToString("N0", CultureInfo.CurrentCulture);
        }
    }
}
