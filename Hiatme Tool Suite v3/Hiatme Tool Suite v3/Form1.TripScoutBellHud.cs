using System;
using System.Drawing;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    partial class Form1
    {
        private TripScoutLiveBellControl _tripScoutLiveBell;

        private void EnsureTripScoutLiveBell()
        {
            if (_tripScoutToolbarPanel == null || _tripScoutToolbarPanel.IsDisposed)
                return;

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
        }

        private void LayoutTripScoutLiveBell()
        {
            if (_tripScoutLiveBell == null || _tripScoutLiveBell.IsDisposed || _tripScoutToolbarPanel == null)
                return;

            bool live = TripScoutLivePanelEnabled;
            _tripScoutLiveBell.Visible = live;
            if (!live)
            {
                _tripScoutLiveBell.SetNotificationState(0, false);
                return;
            }

            int padR = _tripScoutToolbarPanel.Padding.Right;
            int clientW = _tripScoutToolbarPanel.ClientSize.Width;
            int liveSwitchW = 0;
            if (tsLivePanelSwitch != null && !tsLivePanelSwitch.IsDisposed)
                liveSwitchW = tsLivePanelSwitch.Width;

            const int bellSize = 32;
            int x = clientW - padR - liveSwitchW - bellSize - 6;
            _tripScoutLiveBell.SetBounds(x, 4, bellSize, bellSize);
            _tripScoutLiveBell.BringToFront();
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
