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
        private DateTime _tripScoutLiveScanStartedUtc;
        private const int TripScoutLiveScanMinVisibleMs = 1200;

        private void EnsureTripScoutLiveBell()
        {
            if (_tripScoutToolbarPanel == null || _tripScoutToolbarPanel.IsDisposed)
                return;

            if (_tripScoutLiveScan == null || _tripScoutLiveScan.IsDisposed)
            {
                _tripScoutLiveScan = new TripScoutLiveScanIndicator
                {
                    Name = "tripScoutLiveScan",
                    Visible = false,
                };
                _tripScoutToolbarPanel.Controls.Add(_tripScoutLiveScan);
            }

            if (_tripScoutLiveBell != null && !_tripScoutLiveBell.IsDisposed)
                return;

            _tripScoutLiveBell = new TripScoutLiveBellControl
            {
                Name = "tripScoutLiveBell",
                Visible = false,
            };
            _tripScoutLiveBell.BellClicked += (_, __) => TripScoutLiveBell_Click();
            _tripScoutToolbarPanel.Controls.Add(_tripScoutLiveBell);
            _tripScoutLiveBell.BringToFront();
            _tripScoutLiveScan.BringToFront();
        }

        private void LayoutTripScoutLiveBell()
        {
            if (_tripScoutLiveBell == null || _tripScoutLiveBell.IsDisposed || _tripScoutToolbarPanel == null)
                return;

            bool live = TripScoutLivePanelEnabled;
            _tripScoutLiveBell.Visible = live;
            if (_tripScoutLiveScan != null && !_tripScoutLiveScan.IsDisposed && !_tripScoutLiveScan.Scanning)
                _tripScoutLiveScan.Visible = false;

            if (!live)
            {
                _tripScoutLiveBell.SetNotificationState(0, false);
                StopTripScoutLiveScan();
                return;
            }

            int padR = _tripScoutToolbarPanel.Padding.Right;
            int clientW = _tripScoutToolbarPanel.ClientSize.Width;
            int liveSwitchW = 0;
            if (tsLivePanelSwitch != null && !tsLivePanelSwitch.IsDisposed)
                liveSwitchW = tsLivePanelSwitch.Width;

            const int bellSize = 32;
            const int scanSize = 22;
            const int gap = 6;
            int xBell = clientW - padR - liveSwitchW - gap - bellSize;
            _tripScoutLiveBell.SetBounds(xBell, 4, bellSize, bellSize);

            if (_tripScoutLiveScan != null && !_tripScoutLiveScan.IsDisposed)
            {
                int xScan = xBell - gap - scanSize;
                _tripScoutLiveScan.SetBounds(xScan, 10, scanSize, scanSize);
            }

            _tripScoutLiveBell.BringToFront();
            _tripScoutLiveScan?.BringToFront();
        }

        private void StartTripScoutLiveScan()
        {
            EnsureTripScoutLiveBell();
            if (_tripScoutLiveScan == null || _tripScoutLiveScan.IsDisposed)
                return;
            _tripScoutLiveScanStartedUtc = DateTime.UtcNow;
            LayoutTripScoutLiveBell();
            _tripScoutLiveScan.Scanning = true;
            _tripScoutLiveScan.BringToFront();
            _tripScoutLiveBell?.BringToFront();
            tsLivePanelSwitch?.BringToFront();
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
