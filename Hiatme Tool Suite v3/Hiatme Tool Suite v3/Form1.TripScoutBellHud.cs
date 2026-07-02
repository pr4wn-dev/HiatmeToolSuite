using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    partial class Form1
    {
        private TripScoutLiveBellControl _tripScoutLiveBell;
        private TripScoutLiveScanIndicator _tripScoutLiveScan;
        private Label _tripScoutLivePollCountdown;
        private SupeyCard _tripScoutLiveToolbarCard;
        private Panel _tripScoutLiveToolbarHost;
        private Panel _tripScoutLiveDividerA;
        private Panel _tripScoutLiveDividerB;
        private SupeyCard _tripScoutLiveTimerCard;
        private SupeyCard _tripScoutLiveScanCard;
        private SupeyCard _tripScoutLiveBellCard;
        private DateTime _tripScoutLiveScanStartedUtc;
        private const int TripScoutLiveScanMinVisibleMs = 1200;

        private const int TripScoutLiveCardHeight = TripScoutTbHeaderRowH;
        private const int TripScoutLiveCardPadH = 14;
        private const int TripScoutLiveCardPadV = 8;
        private const int TripScoutLiveCardGap = 6;
        private const int TripScoutLiveScanSlot = 28;
        private const int TripScoutLiveBellSlot = 30;
        private const int TripScoutLiveTimerCardW = 56;

        private void EnsureTripScoutLiveBell()
        {
            if (_tripScoutToolbarPanel == null || _tripScoutToolbarPanel.IsDisposed)
                return;

            EnsureTripScoutLiveToolbarCard();

            if (_tripScoutLiveScanCard == null || _tripScoutLiveScanCard.IsDisposed)
            {
                _tripScoutLiveScanCard = MakeTripScoutLiveMiniCard("tripScoutLiveScanCard");
                _tripScoutLiveToolbarHost.Controls.Add(_tripScoutLiveScanCard);
            }

            if (_tripScoutLiveBellCard == null || _tripScoutLiveBellCard.IsDisposed)
            {
                _tripScoutLiveBellCard = MakeTripScoutLiveMiniCard("tripScoutLiveBellCard");
                _tripScoutLiveToolbarHost.Controls.Add(_tripScoutLiveBellCard);
            }

            if (_tripScoutLiveScan == null || _tripScoutLiveScan.IsDisposed)
            {
                _tripScoutLiveScan = new TripScoutLiveScanIndicator
                {
                    Name = "tripScoutLiveScan",
                    Dock = DockStyle.Fill,
                };
                _tripScoutLiveScanCard.Controls.Add(_tripScoutLiveScan);
            }

            if (_tripScoutLivePollCountdown == null || _tripScoutLivePollCountdown.IsDisposed)
            {
                _tripScoutLivePollCountdown = new Label
                {
                    Name = "tripScoutLivePollCountdown",
                    AutoSize = false,
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleCenter,
                    Font = new Font("Segoe UI Semibold", 10f, FontStyle.Bold),
                    ForeColor = SupeyTheme.TextPrimary,
                    BackColor = Color.Transparent,
                    Text = "60s",
                };
                _tripScoutLiveTimerCard.Controls.Add(_tripScoutLivePollCountdown);
            }

            if (_tripScoutLiveBell == null || _tripScoutLiveBell.IsDisposed)
            {
                _tripScoutLiveBell = new TripScoutLiveBellControl
                {
                    Name = "tripScoutLiveBell",
                    Dock = DockStyle.Fill,
                };
                _tripScoutLiveBell.BellClicked += (_, __) => TripScoutLiveBell_Click();
                _tripScoutLiveBellCard.Controls.Add(_tripScoutLiveBell);
            }

            WireTripScoutLivePanelSwitchIntoCard();
        }

        private static SupeyCard MakeTripScoutLiveMiniCard(string name)
        {
            return new SupeyCard
            {
                Name = name,
                SurfaceLevel = SupeyCard.Surface.Standard,
                ShowBorder = true,
                CornerRadius = 6,
                Visible = false,
            };
        }

        private void EnsureTripScoutLiveToolbarCard()
        {
            if (_tripScoutToolbarPanel == null || _tripScoutToolbarPanel.IsDisposed)
                return;

            if (_tripScoutLiveToolbarCard != null && !_tripScoutLiveToolbarCard.IsDisposed)
                return;

            _tripScoutLiveToolbarCard = new SupeyCard
            {
                Name = "tripScoutLiveToolbarCard",
                SurfaceLevel = SupeyCard.Surface.Elevated,
                ShowBorder = true,
                CornerRadius = 8,
            };

            _tripScoutLiveToolbarHost = new Panel
            {
                Name = "tripScoutLiveToolbarHost",
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
            };

            _tripScoutLiveDividerA = MakeTripScoutLiveDivider("tripScoutLiveDividerA");
            _tripScoutLiveDividerB = MakeTripScoutLiveDivider("tripScoutLiveDividerB");

            _tripScoutLiveTimerCard = new SupeyCard
            {
                Name = "tripScoutLiveTimerCard",
                SurfaceLevel = SupeyCard.Surface.Standard,
                ShowBorder = true,
                CornerRadius = 6,
                Visible = false,
            };

            _tripScoutLiveToolbarHost.Controls.Add(_tripScoutLiveDividerA);
            _tripScoutLiveToolbarHost.Controls.Add(_tripScoutLiveDividerB);
            _tripScoutLiveToolbarHost.Controls.Add(_tripScoutLiveTimerCard);

            _tripScoutLiveToolbarCard.Controls.Add(_tripScoutLiveToolbarHost);
            _tripScoutToolbarPanel.Controls.Add(_tripScoutLiveToolbarCard);
        }

        private static Panel MakeTripScoutLiveDivider(string name)
        {
            return new Panel
            {
                Name = name,
                Width = 1,
                BackColor = SupeyTheme.BorderSubtle,
                Visible = false,
            };
        }

        private void WireTripScoutLivePanelSwitchIntoCard()
        {
            if (tsLivePanelSwitch == null || tsLivePanelSwitch.IsDisposed || _tripScoutLiveToolbarHost == null)
                return;

            StyleTripScoutLivePanelSwitch();
            if (tsLivePanelSwitch.Parent == _tripScoutLiveToolbarHost)
                return;

            _tripScoutToolbarPanel?.Controls.Remove(tsLivePanelSwitch);
            _tripScoutActionsHost?.Controls.Remove(tsLivePanelSwitch);
            tsmaterialCard?.Controls.Remove(tsLivePanelSwitch);
            _tripScoutLiveToolbarHost.Controls.Add(tsLivePanelSwitch);
        }

        private void StyleTripScoutLiveToolbarCard(bool live)
        {
            if (_tripScoutLiveToolbarCard == null || _tripScoutLiveToolbarCard.IsDisposed)
                return;

            _tripScoutLiveToolbarCard.Accent = live
                ? SupeyCard.AccentEdge.Left
                : SupeyCard.AccentEdge.None;
        }

        internal int TripScoutLiveToolbarCardReservedWidth()
        {
            if (_tripScoutLiveToolbarCard == null || _tripScoutLiveToolbarCard.IsDisposed)
                return 0;
            return _tripScoutLiveToolbarCard.Width + 12;
        }

        private void LayoutTripScoutLiveBell()
        {
            if (_tripScoutLiveToolbarCard == null || _tripScoutLiveToolbarCard.IsDisposed ||
                _tripScoutLiveToolbarHost == null || _tripScoutToolbarPanel == null)
                return;

            bool live = TripScoutLivePanelEnabled;
            StyleTripScoutLiveToolbarCard(live);

            bool showLiveChrome = live;
            if (_tripScoutLiveScanCard != null && !_tripScoutLiveScanCard.IsDisposed)
                _tripScoutLiveScanCard.Visible = showLiveChrome;
            if (_tripScoutLiveBellCard != null && !_tripScoutLiveBellCard.IsDisposed)
                _tripScoutLiveBellCard.Visible = showLiveChrome;
            if (_tripScoutLiveTimerCard != null && !_tripScoutLiveTimerCard.IsDisposed)
                _tripScoutLiveTimerCard.Visible = showLiveChrome;
            if (_tripScoutLiveDividerA != null)
                _tripScoutLiveDividerA.Visible = showLiveChrome;
            if (_tripScoutLiveDividerB != null)
                _tripScoutLiveDividerB.Visible = showLiveChrome;

            if (!live)
            {
                _tripScoutLiveBell?.SetNotificationState(0, false);
                StopTripScoutLiveScan();
            }

            WireTripScoutLivePanelSwitchIntoCard();

            int padR = _tripScoutToolbarPanel.Padding.Right;
            int clientW = _tripScoutToolbarPanel.ClientSize.Width;
            int cardW = MeasureTripScoutLiveToolbarCardWidth(live);
            int cardY = TripScoutTbHeaderY;

            _tripScoutLiveToolbarHost.SuspendLayout();
            try
            {
                _tripScoutLiveToolbarCard.SetBounds(clientW - padR - cardW, cardY, cardW, TripScoutLiveCardHeight);
                LayoutTripScoutLiveToolbarHost(live);
            }
            finally
            {
                _tripScoutLiveToolbarHost.ResumeLayout(true);
            }

            TripScoutUpdateLivePollCountdownLabel();
        }

        private int MeasureTripScoutLiveToolbarCardWidth(bool live)
        {
            int switchW = 0;
            if (tsLivePanelSwitch != null && !tsLivePanelSwitch.IsDisposed)
            {
                StyleTripScoutLivePanelSwitch();
                switchW = tsLivePanelSwitch.GetPreferredSize(Size.Empty).Width;
            }

            int width = TripScoutLiveCardPadH * 2 + switchW;
            if (!live)
                return Math.Max(width, 118);

            width += TripScoutLiveScanSlot + TripScoutLiveCardGap;
            width += TripScoutLiveBellSlot + TripScoutLiveCardGap;
            width += 1 + TripScoutLiveCardGap;
            width += TripScoutLiveTimerCardW + TripScoutLiveCardGap;
            width += 1 + TripScoutLiveCardGap;
            return width;
        }

        private void LayoutTripScoutLiveToolbarHost(bool live)
        {
            int hostH = TripScoutLiveCardHeight;
            int x = TripScoutLiveCardPadH;

            if (live)
            {
                int slotY = (hostH - TripScoutLiveScanSlot) / 2;
                _tripScoutLiveScanCard?.SetBounds(x, slotY, TripScoutLiveScanSlot, TripScoutLiveScanSlot);
                x += TripScoutLiveScanSlot + TripScoutLiveCardGap;

                slotY = (hostH - TripScoutLiveBellSlot) / 2;
                _tripScoutLiveBellCard?.SetBounds(x, slotY, TripScoutLiveBellSlot, TripScoutLiveBellSlot);
                x += TripScoutLiveBellSlot + TripScoutLiveCardGap;

                int divH = hostH - (TripScoutLiveCardPadV * 2);
                int divY = TripScoutLiveCardPadV;
                _tripScoutLiveDividerA.SetBounds(x, divY, 1, divH);
                x += 1 + TripScoutLiveCardGap;

                int timerH = hostH - (TripScoutLiveCardPadV * 2);
                int timerY = TripScoutLiveCardPadV;
                _tripScoutLiveTimerCard.SetBounds(x, timerY, TripScoutLiveTimerCardW, timerH);
                x += TripScoutLiveTimerCardW + TripScoutLiveCardGap;

                _tripScoutLiveDividerB.SetBounds(x, divY, 1, divH);
                x += 1 + TripScoutLiveCardGap;
            }

            if (tsLivePanelSwitch != null && !tsLivePanelSwitch.IsDisposed)
            {
                StyleTripScoutLivePanelSwitch();
                int switchW = tsLivePanelSwitch.GetPreferredSize(Size.Empty).Width;
                int switchH = tsLivePanelSwitch.GetPreferredSize(Size.Empty).Height;
                int switchY = Math.Max(0, (hostH - switchH) / 2);
                tsLivePanelSwitch.SetBounds(x, switchY, switchW, switchH);
            }
        }

        private void StartTripScoutLiveScan()
        {
            EnsureTripScoutLiveBell();
            if (_tripScoutLiveScan == null || _tripScoutLiveScan.IsDisposed)
                return;

            _tripScoutLiveScanStartedUtc = DateTime.UtcNow;
            _tripScoutLiveScan.Scanning = true;
            TripScoutUpdateLivePollCountdownLabel();
        }

        private async Task StopTripScoutLiveScanAfterMinimumAsync()
        {
            double elapsed = (DateTime.UtcNow - _tripScoutLiveScanStartedUtc).TotalMilliseconds;
            if (elapsed < TripScoutLiveScanMinVisibleMs)
            {
                try
                {
                    await Task.Delay((int)(TripScoutLiveScanMinVisibleMs - elapsed)).ConfigureAwait(true);
                }
                catch
                {
                    // tab closed mid-poll
                }
            }

            if (_tripScoutLiveScan == null || _tripScoutLiveScan.IsDisposed)
                return;

            _tripScoutLiveScan.Scanning = false;
        }

        private void StopTripScoutLiveScan()
        {
            if (_tripScoutLiveScan == null || _tripScoutLiveScan.IsDisposed)
                return;

            if (_tripScoutLiveScan.Scanning)
                _tripScoutLiveScan.Scanning = false;
        }

        internal void TripScoutSyncLiveBellVisibility()
        {
            EnsureTripScoutLiveBell();
            LayoutTripScoutLiveBell();
            TripScoutUpdateLiveBellIndicator();
        }

        private void TripScoutUpdateLiveBellIndicator()
        {
            if (_tripScoutLiveBell == null || _tripScoutLiveBell.IsDisposed || !TripScoutLivePanelEnabled)
                return;

            int cached = _tripScoutWillCalls?.Count ?? 0;
            int badge = _tripScoutLastBellStatus?.WillcallCount ?? 0;
            if (cached > badge)
                badge = cached;
            else if (badge <= 0)
                badge = cached;

            bool shouldShake = badge > 0 && !_tripScoutIsBellAcked();
            if (_tripScoutLastBellStatus != null && _tripScoutLastBellStatus.HasNew)
                shouldShake = badge > 0 || (_tripScoutLastBellStatus.WillcallCount > 0);

            _tripScoutLiveBell.SetNotificationState(badge, shouldShake);
        }

        private void TripScoutLiveBell_Click()
        {
            TripScoutWillCallsBtn_Click(null, EventArgs.Empty);
        }
    }
}
