using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>Driver Habits Live switch: today-only auto-refresh + loady/timer chip.</summary>
    partial class Form1
    {
        private const int LateDriversLivePollIntervalMs = 60_000;
        private const int LateDriversLiveScanMinVisibleMs = 1200;

        private SupeySwitch ldLiveSwitch;
        private SupeySwitch ldColorsSwitch;
        private System.Windows.Forms.Timer _ldPollTimer;
        private System.Windows.Forms.Timer _ldPollCountdownTimer;
        private DateTime _ldPollNextUtc;
        private DateTime _ldScanStartedUtc;
        private Panel _ldLiveChromeHost;
        private Panel _ldLiveDivider;
        private Panel _ldLiveScanCard;
        private SupeyCard _ldLiveTimerCard;
        private TripScoutLiveScanIndicator _ldLiveScan;
        private Label _ldLiveCountdown;

        private bool LateDriversLiveEnabled =>
            ldLiveSwitch != null && !ldLiveSwitch.IsDisposed && ldLiveSwitch.Checked;

        /// <summary>When off, schedule groups stay (G1/G2 + headers) but lose palette tint.</summary>
        private bool LateDriversGroupColorsEnabled =>
            ldColorsSwitch != null && !ldColorsSwitch.IsDisposed && ldColorsSwitch.Checked;

        private void BuildLateDriversLiveSwitch()
        {
            if (ldLiveSwitch != null && !ldLiveSwitch.IsDisposed)
                return;

            ldLiveSwitch = new SupeySwitch
            {
                Name = "ldLiveSwitch",
                Text = "Live",
                AutoSize = true,
                Margin = Padding.Empty,
                Checked = false,
            };
            StyleLateDriversLiveSwitch();
            ldLiveSwitch.CheckedChanged += LdLiveSwitch_CheckedChanged;

            BuildLateDriversColorsSwitch();
        }

        private void BuildLateDriversColorsSwitch()
        {
            if (ldColorsSwitch != null && !ldColorsSwitch.IsDisposed)
                return;

            ldColorsSwitch = new SupeySwitch
            {
                Name = "ldColorsSwitch",
                Text = "Colors",
                AutoSize = true,
                Margin = Padding.Empty,
                Checked = false,
            };
            StyleLateDriversColorsSwitch();
            ldColorsSwitch.CheckedChanged += LdColorsSwitch_CheckedChanged;
        }

        private void StyleLateDriversLiveSwitch()
        {
            if (ldLiveSwitch == null || ldLiveSwitch.IsDisposed)
                return;
            ldLiveSwitch.AutoSize = true;
            ldLiveSwitch.Text = "Live";
            ldLiveSwitch.Margin = Padding.Empty;
            ldLiveSwitch.BackColor = SupeyTheme.SurfaceElevated;
            ldLiveSwitch.Font = SupeyTheme.BodyFont;
            ldLiveSwitch.ForeColor = SupeyTheme.TextPrimary;
            try { ldLiveSwitch.Size = ldLiveSwitch.GetPreferredSize(Size.Empty); } catch { }
        }

        private void StyleLateDriversColorsSwitch()
        {
            if (ldColorsSwitch == null || ldColorsSwitch.IsDisposed)
                return;
            ldColorsSwitch.AutoSize = true;
            ldColorsSwitch.Text = "Colors";
            ldColorsSwitch.Margin = Padding.Empty;
            ldColorsSwitch.BackColor = SupeyTheme.SurfaceElevated;
            ldColorsSwitch.Font = SupeyTheme.BodyFont;
            ldColorsSwitch.ForeColor = SupeyTheme.TextPrimary;
            try { ldColorsSwitch.Size = ldColorsSwitch.GetPreferredSize(Size.Empty); } catch { }
        }

        private void LdColorsSwitch_CheckedChanged(object sender, EventArgs e)
        {
            if (!_ldBuilt)
                return;
            ApplyLateDriversTripListColorMode();
        }

        private void LdLiveSwitch_CheckedChanged(object sender, EventArgs e)
        {
            if (!_ldBuilt)
                return;

            if (LateDriversLiveEnabled)
            {
                _ldSuppressDateChanged = true;
                try
                {
                    _ldDayDate = DateTime.Today;
                    _ldAnchorDate = DateTime.Today;
                    if (ldDatePicker != null && !ldDatePicker.IsDisposed)
                        ldDatePicker.Value = DateTime.Today;
                }
                catch { }
                finally { _ldSuppressDateChanged = false; }

                _ldSelectedPeriod = "day";
                StyleLateDriversPeriodButtons();
                StartLateDriversLivePolling();
            }
            else
            {
                StopLateDriversLivePolling();
                HideLateDriversBellAlertBar();
                _ldLiveBell?.SetNotificationState(0, false);
            }

            UpdateLateDriversPeriodPickerChrome();
            UpdateLateDriversToolbarHints();
            SetLateDriversPeriodControlsEnabled(!LateDriversLiveEnabled);
            LayoutLateDriversToolbar();
            _ = LateDriversRefreshAsync(force: true);
        }

        private void SetLateDriversPeriodControlsEnabled(bool enabled)
        {
            foreach (var btn in _ldPeriodButtons)
            {
                if (btn != null && !btn.IsDisposed)
                    btn.Enabled = enabled;
            }
            if (ldDatePicker != null && !ldDatePicker.IsDisposed)
                ldDatePicker.Enabled = enabled;
            if (ldWeekPrevBtn != null && !ldWeekPrevBtn.IsDisposed)
                ldWeekPrevBtn.Enabled = enabled;
            if (ldWeekNextBtn != null && !ldWeekNextBtn.IsDisposed)
                ldWeekNextBtn.Enabled = enabled;
            if (ldMonthCombo != null && !ldMonthCombo.IsDisposed)
                ldMonthCombo.Enabled = enabled;
            if (ldYearCombo != null && !ldYearCombo.IsDisposed)
                ldYearCombo.Enabled = enabled;
        }

        private void EnsureLateDriversLiveChrome()
        {
            if (_ldLiveChromeHost == null || _ldLiveChromeHost.IsDisposed)
            {
                _ldLiveChromeHost = new Panel
                {
                    Name = "ldLiveChromeHost",
                    BackColor = Color.Transparent,
                    Size = new Size(140, TripScoutLiveCardHeight),
                    Visible = false,
                };
            }

            if (_ldLiveTimerCard == null || _ldLiveTimerCard.IsDisposed)
            {
                _ldLiveTimerCard = MakeTripScoutLiveMiniCard("ldLiveTimerCard");
                _ldLiveTimerCard.ShowBorder = true;
                _ldLiveTimerCard.SurfaceLevel = SupeyCard.Surface.Elevated;
                _ldLiveTimerCard.CornerRadius = 6;
                _ldLiveChromeHost.Controls.Add(_ldLiveTimerCard);
            }

            if (_ldLiveScanCard == null || _ldLiveScanCard.IsDisposed)
            {
                _ldLiveScanCard = new Panel
                {
                    Name = "ldLiveScanCard",
                    BackColor = Color.Transparent,
                };
                _ldLiveTimerCard.Controls.Add(_ldLiveScanCard);
            }
            else if (!ReferenceEquals(_ldLiveScanCard.Parent, _ldLiveTimerCard))
            {
                _ldLiveTimerCard.Controls.Add(_ldLiveScanCard);
                _ldLiveScanCard.BackColor = Color.Transparent;
            }

            if (_ldLiveDivider == null || _ldLiveDivider.IsDisposed)
            {
                _ldLiveDivider = MakeTripScoutLiveDivider("ldLiveDivider");
                _ldLiveDivider.Visible = false;
                _ldLiveChromeHost.Controls.Add(_ldLiveDivider);
            }

            if (_ldLiveScan == null || _ldLiveScan.IsDisposed)
            {
                _ldLiveScan = new TripScoutLiveScanIndicator
                {
                    Name = "ldLiveScan",
                    Dock = DockStyle.Fill,
                    BackColor = SupeyTheme.SurfaceElevated,
                };
                _ldLiveScanCard.Controls.Add(_ldLiveScan);
            }

            if (_ldLiveCountdown == null || _ldLiveCountdown.IsDisposed)
            {
                _ldLiveCountdown = new Label
                {
                    Name = "ldLiveCountdown",
                    AutoSize = false,
                    TextAlign = ContentAlignment.MiddleCenter,
                    Font = new Font("Segoe UI Semibold", 10f, FontStyle.Bold),
                    Text = "60s",
                    BackColor = Color.Transparent,
                    ForeColor = SupeyTheme.TextPrimary,
                };
                _ldLiveTimerCard.Controls.Add(_ldLiveCountdown);
            }
            else if (!ReferenceEquals(_ldLiveCountdown.Parent, _ldLiveTimerCard))
            {
                _ldLiveTimerCard.Controls.Add(_ldLiveCountdown);
            }

            if (_ldLiveBellCard == null || _ldLiveBellCard.IsDisposed)
            {
                // Same as scan: transparent slot on the elevated chip (no nested SupeyCard).
                _ldLiveBellCard = new Panel
                {
                    Name = "ldLiveBellCard",
                    BackColor = Color.Transparent,
                };
                _ldLiveTimerCard.Controls.Add(_ldLiveBellCard);
            }
            else if (!ReferenceEquals(_ldLiveBellCard.Parent, _ldLiveTimerCard))
            {
                _ldLiveTimerCard.Controls.Add(_ldLiveBellCard);
                _ldLiveBellCard.BackColor = Color.Transparent;
            }

            if (_ldLiveBell == null || _ldLiveBell.IsDisposed)
            {
                _ldLiveBell = new TripScoutLiveBellControl
                {
                    Name = "ldLiveBell",
                    Dock = DockStyle.Fill,
                };
                _ldLiveBell.SetHostBackColor(SupeyTheme.SurfaceElevated);
                _ldLiveBell.BellClicked += (_, __) => LateDriversLiveBell_Click();
                _ldLiveBellCard.Controls.Add(_ldLiveBell);
            }
            else
            {
                _ldLiveBell.SetHostBackColor(SupeyTheme.SurfaceElevated);
            }
        }

        private int MeasureLateDriversLiveChromeWidth()
        {
            const int padL = 8, padR = 8, gap = 8;
            // scan | bell | timer
            return padL
                + TripScoutLiveScanSlot + gap
                + TripScoutLiveBellSlot + gap
                + TripScoutLiveTimerCardW
                + padR;
        }

        private void LayoutLateDriversLiveChromeHost()
        {
            if (_ldLiveChromeHost == null || _ldLiveChromeHost.IsDisposed)
                return;

            int hostH = TripScoutLiveCardHeight;
            int chipH = hostH;
            int chipY = 0;
            int chipW = MeasureLateDriversLiveChromeWidth();

            if (_ldLiveDivider != null && !_ldLiveDivider.IsDisposed)
                _ldLiveDivider.Visible = false;
            if (_ldLiveTimerCard != null && !_ldLiveTimerCard.IsDisposed)
            {
                _ldLiveTimerCard.Visible = true;
                _ldLiveTimerCard.Padding = Padding.Empty;
                _ldLiveTimerCard.SetBounds(0, chipY, chipW, chipH);

                const int padL = 8, padR = 8, padV = 8, gap = 8;
                int innerH = Math.Max(16, chipH - (padV * 2));
                int scanY = padV + Math.Max(0, (innerH - TripScoutLiveScanSlot) / 2);
                // Bell slot is 2px taller; sit it on the scan Y then nudge up so the glyph
                // optically matches the loady (badge/clapper sit a touch low in the control).
                int bellY = Math.Max(0, scanY - 2);
                int x = padL;

                if (_ldLiveScanCard != null && !_ldLiveScanCard.IsDisposed)
                {
                    _ldLiveScanCard.Visible = true;
                    _ldLiveScanCard.SetBounds(x, scanY, TripScoutLiveScanSlot, TripScoutLiveScanSlot);
                    x += TripScoutLiveScanSlot + gap;
                }
                if (_ldLiveBellCard != null && !_ldLiveBellCard.IsDisposed)
                {
                    _ldLiveBellCard.Visible = true;
                    _ldLiveBellCard.SetBounds(x, bellY, TripScoutLiveBellSlot, TripScoutLiveBellSlot);
                    x += TripScoutLiveBellSlot + gap;
                }
                if (_ldLiveCountdown != null && !_ldLiveCountdown.IsDisposed)
                {
                    _ldLiveCountdown.SetBounds(
                        x, padV, Math.Max(24, chipW - x - padR), innerH);
                }
            }
        }

        private void SyncLateDriversLivePollingForMode()
        {
            if (LateDriversLiveEnabled)
                StartLateDriversLivePolling();
            else
                StopLateDriversLivePolling();
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
                if (!LateDriversLiveEnabled)
                    return;
                if (_ldLoadInFlight)
                {
                    // Watchdog: a refresh whose await chain never returned would otherwise
                    // block every future poll. After the timeout window, reclaim the flag so
                    // live polling resumes (the zombie task's finally is a harmless double-reset).
                    if (_ldLoadStartedUtc != DateTime.MinValue
                        && DateTime.UtcNow - _ldLoadStartedUtc > LateDriversLoadWatchdog)
                    {
                        _ldLoadInFlight = false;
                        SetLateDriversStatus("Status: Previous refresh timed out — retrying…");
                    }
                    else
                    {
                        return;
                    }
                }
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
            if (_ldLiveChromeHost != null && !_ldLiveChromeHost.IsDisposed)
                _ldLiveChromeHost.Visible = false;
            LateDriversUpdateLivePollCountdownLabel();
        }

        private void StartLateDriversLivePolling()
        {
            EnsureLateDriversLiveChrome();
            EnsureLateDriversLivePollTimer();
            EnsureLateDriversLivePollCountdownTimer();
            if (_ldLiveChromeHost != null)
                _ldLiveChromeHost.Visible = true;
            _ldPollTimer.Start();
            _ldPollCountdownTimer.Start();
            LateDriversScheduleNextLivePoll();
            LayoutLateDriversLiveChromeHost();
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
            if (!LateDriversLiveEnabled)
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
                    await Task.Delay((int)(LateDriversLiveScanMinVisibleMs - elapsed))
                        .ConfigureAwait(true);
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

        private void StyleLateDriversLiveChromeTheme()
        {
            StyleLateDriversLiveSwitch();
            StyleLateDriversColorsSwitch();
            if (_ldLiveCountdown != null && !_ldLiveCountdown.IsDisposed)
            {
                try { _ldLiveCountdown.Font = new Font("Segoe UI Semibold", 10f, FontStyle.Bold); }
                catch { }
                _ldLiveCountdown.ForeColor = SupeyTheme.TextPrimary;
                _ldLiveCountdown.BackColor = Color.Transparent;
            }
            if (_ldLiveScan != null && !_ldLiveScan.IsDisposed)
                _ldLiveScan.BackColor = SupeyTheme.SurfaceElevated;
            if (_ldLiveBell != null && !_ldLiveBell.IsDisposed)
                _ldLiveBell.SetHostBackColor(SupeyTheme.SurfaceElevated);
            if (_ldLiveBellCard != null && !_ldLiveBellCard.IsDisposed)
                _ldLiveBellCard.BackColor = Color.Transparent;
            if (_ldLiveTimerCard != null && !_ldLiveTimerCard.IsDisposed)
            {
                _ldLiveTimerCard.ShowBorder = true;
                _ldLiveTimerCard.SurfaceLevel = SupeyCard.Surface.Elevated;
            }
            StyleLateDriversBellAlertTheme();
            LateDriversUpdateLivePollCountdownLabel();
            LateDriversUpdateLiveBellIndicator();
        }
    }
}
