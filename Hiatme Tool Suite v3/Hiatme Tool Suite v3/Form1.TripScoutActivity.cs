using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    partial class Form1
    {
        private static readonly Color TripScoutChangeRowColor = Color.FromArgb(120, 92, 48);
        private static readonly Color TripScoutWillCallRowColor = Color.FromArgb(88, 112, 188);

        private SupeyButton _tripScoutChangesBtn;
        private SupeyButton _tripScoutWillCallsBtn;

        private string _tripScoutChangesAckHash;
        private double _tripScoutChangesAckTs;
        private string _tripScoutBellAckHash;
        private string _tripScoutChangesServerHash;
        private List<HiatmeAiClient.TripScoutChangeRow> _tripScoutDayChanges
            = new List<HiatmeAiClient.TripScoutChangeRow>();
        private List<HiatmeAiClient.WellRydeBellWillCall> _tripScoutWillCalls
            = new List<HiatmeAiClient.WellRydeBellWillCall>();
        private HiatmeAiClient.WellRydeBellStatus _tripScoutLastBellStatus;
        private readonly HashSet<string> _tripScoutWillCallTripNos =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _tripScoutNewChangeTripNos =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private void EnsureTripScoutActivityToolbar()
        {
            if (_tripScoutActionsHost == null || _tripScoutActionsHost.IsDisposed)
                return;

            if (_tripScoutChangesBtn == null || _tripScoutChangesBtn.IsDisposed)
            {
                _tripScoutChangesBtn = MakeTripScoutActivityButton("Changes", TripScoutChangesBtn_Click);
                _tripScoutWillCallsBtn = MakeTripScoutActivityButton("Will calls", TripScoutWillCallsBtn_Click);
                _tripScoutActivitySep = MakeTripScoutToolbarSeparator("tripScoutActivitySep");

                _tripScoutActionsHost.Controls.Add(_tripScoutActivitySep);
                _tripScoutActionsHost.Controls.Add(_tripScoutChangesBtn);
                _tripScoutActionsHost.Controls.Add(_tripScoutWillCallsBtn);
            }

            EnsureTripScoutExpandClick();
            EnsureTripScoutSimulateToolbar();
            UpdateTripScoutActivityButtons();
        }

        private static SupeyButton MakeTripScoutActivityButton(string text, EventHandler click)
        {
            var btn = new SupeyButton
            {
                Text = text,
                Kind = SupeyButton.Variant.Secondary,
            };
            btn.Click += click;
            return btn;
        }

        internal void TripScoutResetActivityState()
        {
            _tripScoutChangesAckHash = null;
            _tripScoutChangesAckTs = 0;
            _tripScoutBellAckHash = null;
            _tripScoutChangesServerHash = null;
            _tripScoutDayChanges.Clear();
            _tripScoutWillCalls.Clear();
            _tripScoutLastBellStatus = null;
            _tripScoutWillCallTripNos.Clear();
            _tripScoutNewChangeTripNos.Clear();
            TripScoutClearExpandedTrips();
            TripScoutResetChangeAlert();
            UpdateTripScoutActivityButtons();
            TripScoutApplyRowHighlights();
            TripScoutUpdateLiveBellIndicator();
        }

        private void UpdateTripScoutActivityButtons()
        {
            if (_tripScoutChangesBtn == null || _tripScoutChangesBtn.IsDisposed)
                return;

            int newChanges = CountUnackedChanges();
            _tripScoutChangesBtn.Text = newChanges > 0
                ? "Changes (" + newChanges + ")"
                : "Changes";
            _tripScoutChangesBtn.Kind = newChanges > 0
                ? SupeyButton.Variant.Primary
                : SupeyButton.Variant.Secondary;

            int newBell = CountUnackedWillCalls();
            _tripScoutWillCallsBtn.Text = newBell > 0
                ? "Will calls (" + newBell + ")"
                : "Will calls";
            _tripScoutWillCallsBtn.Kind = newBell > 0
                ? SupeyButton.Variant.Primary
                : SupeyButton.Variant.Secondary;

            LayoutTripScoutToolbarControls();
        }

        private int CountUnackedChanges()
        {
            return _tripScoutNewChangeTripNos.Count;
        }

        private int CountUnackedWillCalls()
        {
            if (_tripScoutWillCalls == null || _tripScoutWillCalls.Count == 0)
                return 0;
            if (_tripScoutIsBellAcked())
                return 0;
            return _tripScoutWillCalls.Count;
        }

        private bool _tripScoutIsBellAcked()
        {
            return !string.IsNullOrEmpty(_tripScoutBellAckHash)
                && string.Equals(_tripScoutBellAckHash, _tripScoutLiveBellHash, StringComparison.Ordinal);
        }

        private async Task TripScoutRefreshActivityAsync(
            HiatmeAiSettings settings,
            string serviceDate)
        {
            if (settings == null || string.IsNullOrWhiteSpace(serviceDate))
                return;

            var changesTask = HiatmeAiClient.GetTripScoutDayChangesAsync(
                settings, serviceDate, null, CancellationToken.None);
            var bellTask = HiatmeAiClient.GetWellRydeBellStatusAsync(
                settings, CancellationToken.None);
            await Task.WhenAll(changesTask, bellTask).ConfigureAwait(true);

            ApplyTripScoutChangesPayload(await changesTask.ConfigureAwait(true));

            var bell = await bellTask.ConfigureAwait(true);
            _tripScoutLiveBellHash = bell?.ContentHash ?? _tripScoutLiveBellHash;
            _tripScoutLastBellStatus = bell;
            ApplyTripScoutBellPayload(bell);
            UpdateTripScoutActivityButtons();
            TripScoutApplyRowHighlights();
        }

        internal async Task TripScoutRefreshActivityAfterLivePollAsync(
            HiatmeAiSettings settings,
            string serviceDate)
        {
            try
            {
                await TripScoutRefreshActivityAsync(settings, serviceDate).ConfigureAwait(true);
            }
            catch
            {
                // Activity feed is supplemental; never break the live trip poll.
            }
        }

        private void ApplyTripScoutChangesPayload(HiatmeAiClient.TripScoutDayChanges payload)
        {
            if (payload == null || !payload.Ok || !payload.Available)
                return;

            _tripScoutChangesServerHash = payload.ContentHash ?? "";
            _tripScoutDayChanges = payload.Changes ?? new List<HiatmeAiClient.TripScoutChangeRow>();
            RebuildTripScoutChangesByTrip();
            RebuildTripScoutNewChangeTripNosFromHash();
            TripScoutProcessNewChangeAlerts();
            UpdateTripScoutActivityButtons();
            TripScoutRebindVisibleListPreserveScroll();
        }

        private void RebuildTripScoutNewChangeTripNosFromHash()
        {
            _tripScoutNewChangeTripNos.Clear();
            if (_tripScoutDayChanges == null || _tripScoutDayChanges.Count == 0)
                return;

            foreach (var row in _tripScoutDayChanges)
            {
                if (row == null || string.IsNullOrWhiteSpace(row.TripNo))
                    continue;
                double ts = row.Ts ?? 0;
                if (ts <= _tripScoutChangesAckTs)
                    continue;
                _tripScoutNewChangeTripNos.Add(row.TripNo.Trim());
            }
        }

        private void ApplyTripScoutBellPayload(HiatmeAiClient.WellRydeBellStatus bell)
        {
            _tripScoutWillCallTripNos.Clear();
            _tripScoutWillCalls = bell?.Willcalls ?? new List<HiatmeAiClient.WellRydeBellWillCall>();
            if (bell == null || !bell.Ok || !bell.Available)
                return;

            foreach (var wc in _tripScoutWillCalls)
            {
                if (wc == null || string.IsNullOrWhiteSpace(wc.TripNo))
                    continue;
                _tripScoutWillCallTripNos.Add(wc.TripNo.Trim());
            }
            TripScoutUpdateLiveBellIndicator();
            if (_tripScoutAllTrips != null && _tripScoutAllTrips.Count > 0)
                TripScoutRebindVisibleListPreserveScroll();
        }

        internal string TripScoutPeekBellStatusNote()
        {
            return _tripScoutLastBellStatus == null
                ? ""
                : TripScoutFormatBellStatusNote(_tripScoutLastBellStatus);
        }

        internal string TripScoutPeekChangesStatusNote()
        {
            if (CountUnackedChanges() <= 0)
                return "";
            return "Changes: " + CountUnackedChanges() + " trip(s) — click trip rows (▶) to expand.";
        }

        internal string TripScoutFormatBellStatusNote(HiatmeAiClient.WellRydeBellStatus bell)
        {
            if (bell == null || !bell.Ok || !bell.Available)
                return bell != null && !bell.Ok ? "Bell check failed — " + (bell.Error ?? "unknown") + "." : "";

            ApplyTripScoutBellPayload(bell);
            if (_tripScoutWillCalls.Count == 0)
            {
                if (bell.WillcallCount > 0 && !string.Equals(_tripScoutBellAckHash, bell.ContentHash, StringComparison.Ordinal))
                    return "Bell shows " + bell.WillcallCount + " will-call(s) (open Will calls for details).";
                return "";
            }

            if (_tripScoutIsBellAcked())
                return "";

            var parts = _tripScoutWillCalls
                .Where(w => w != null && !string.IsNullOrWhiteSpace(w.TripNo))
                .Take(3)
                .Select(w =>
                {
                    string rider = string.IsNullOrWhiteSpace(w.Rider) ? "" : " " + w.Rider.Trim();
                    return w.TripNo.Trim() + rider;
                })
                .ToList();
            string summary = string.Join("; ", parts);
            if (_tripScoutWillCalls.Count > 3)
                summary += " +" + (_tripScoutWillCalls.Count - 3) + " more";

            int matched = _tripScoutWillCalls.Count(w =>
                w != null
                && !string.IsNullOrWhiteSpace(w.TripNo)
                && _tripScoutAllTrips != null
                && _tripScoutAllTrips.Any(t =>
                    string.Equals(TripScoutTripKey(t), w.TripNo.Trim(), StringComparison.OrdinalIgnoreCase)));

            return "Bell: " + _tripScoutWillCalls.Count + " will-call ready"
                + (matched > 0 ? " (" + matched + " in list)" : "")
                + " — " + summary + ".";
        }

        private void TripScoutApplyRowHighlights()
        {
            if (tslv == null || tslv.IsDisposed || tslv.Items.Count == 0)
                return;

            foreach (ListViewItem item in tslv.Items)
            {
                if (TripScoutIsDetailRow(item))
                    continue;

                var row = item.Tag as TripScoutListRow;
                var trip = row?.Trip;
                string key = TripScoutTripKey(trip);
                if (_tripScoutWillCallTripNos.Contains(key))
                    item.BackColor = TripScoutWillCallRowColor;
                else if (_tripScoutNewChangeTripNos.Contains(key))
                    item.BackColor = TripScoutChangeRowColor;
                else
                    item.BackColor = Color.Empty;
            }
            tslv.Invalidate(true);
        }

        private void TripScoutChangesBtn_Click(object sender, EventArgs e)
        {
            foreach (string tripNo in _tripScoutNewChangeTripNos.ToList())
                _tripScoutExpandedTripNos.Add(tripNo);
            AckTripScoutChanges();
            TripScoutRebindVisibleListPreserveScroll();
        }

        private void TripScoutWillCallsBtn_Click(object sender, EventArgs e)
        {
            if (_tripScoutWillCalls != null)
            {
                foreach (var wc in _tripScoutWillCalls)
                {
                    if (wc != null && !string.IsNullOrWhiteSpace(wc.TripNo))
                        _tripScoutExpandedTripNos.Add(wc.TripNo.Trim());
                }
            }
            AckTripScoutBell();
            TripScoutRebindVisibleListPreserveScroll();
        }

        private void AckTripScoutChanges()
        {
            _tripScoutChangesAckHash = _tripScoutChangesServerHash;
            _tripScoutChangesAckTs = 0;
            if (_tripScoutDayChanges != null)
            {
                foreach (var row in _tripScoutDayChanges)
                {
                    if (row?.Ts == null)
                        continue;
                    if (row.Ts.Value > _tripScoutChangesAckTs)
                        _tripScoutChangesAckTs = row.Ts.Value;
                }
            }
            _tripScoutNewChangeTripNos.Clear();
            TripScoutClearChangeAlertQueue();
            UpdateTripScoutActivityButtons();
            TripScoutApplyRowHighlights();
        }

        internal void TripScoutKickActivityRefreshAfterLoad()
        {
            var settings = TripScoutAiSettings();
            string serviceDate = TripScoutSelectedServiceDateIso();
            if (settings == null || string.IsNullOrWhiteSpace(settings.BaseUrl))
                return;
            _ = TripScoutRefreshActivityAfterLoadAsync(settings, serviceDate);
        }

        private async Task TripScoutRefreshActivityAfterLoadAsync(
            HiatmeAiSettings settings,
            string serviceDate)
        {
            await TripScoutRefreshActivityAsync(settings, serviceDate).ConfigureAwait(true);
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() =>
                {
                    TripScoutApplyRowHighlights();
                    UpdateTripScoutActivityButtons();
                }));
                return;
            }
            TripScoutApplyRowHighlights();
            UpdateTripScoutActivityButtons();
        }

        private void AckTripScoutBell()
        {
            _tripScoutBellAckHash = _tripScoutLiveBellHash;
            UpdateTripScoutActivityButtons();
            TripScoutUpdateLiveBellIndicator();
        }

        internal void TripScoutSelectTripByNumber(string tripNo)
        {
            if (string.IsNullOrWhiteSpace(tripNo) || tslv == null || tslv.IsDisposed)
                return;

            string key = tripNo.Trim();
            _suppressTripScoutSearch = true;
            try
            {
                if (tssearchbox != null)
                    tssearchbox.Text = key;
            }
            finally
            {
                _suppressTripScoutSearch = false;
            }

            ApplyTripScoutFilter(key);

            foreach (ListViewItem item in tslv.Items)
            {
                var trip = TripScoutListRow.TryGetTrip(item?.Tag);
                if (trip == null)
                    continue;
                if (!string.Equals(TripScoutTripKey(trip), key, StringComparison.OrdinalIgnoreCase))
                    continue;
                item.Selected = true;
                item.Focused = true;
                item.EnsureVisible();
                break;
            }
        }
    }
}
