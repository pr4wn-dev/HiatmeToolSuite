using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    public partial class Form1
    {
        private ContextMenuStrip _ldTripCtxMenu;
        private ToolStripMenuItem _ldTripCtxAssign;
        private ToolStripMenuItem _ldTripCtxUnassign;

        private void EnsureLateDriversTripContextMenu()
        {
            if (_ldTripCtxMenu != null && !_ldTripCtxMenu.IsDisposed)
                return;

            _ldTripCtxMenu = new ContextMenuStrip
            {
                Renderer = new DarkContextMenuRenderer(),
                BackColor = DarkContextMenuRenderer.Background,
                ForeColor = DarkContextMenuRenderer.ForeColor,
                ShowImageMargin = true,
            };

            _ldTripCtxAssign = new ToolStripMenuItem("Assign to driver...")
            {
                BackColor = DarkContextMenuRenderer.Background,
                ForeColor = DarkContextMenuRenderer.ForeColor,
                Image = MenuIconFactory.GetAssignIcon(),
            };
            _ldTripCtxAssign.Click += async (s, e) => await LateDriversTripCtxAssignAsync();

            _ldTripCtxUnassign = new ToolStripMenuItem("Unassign trip")
            {
                BackColor = DarkContextMenuRenderer.Background,
                ForeColor = DarkContextMenuRenderer.ForeColor,
                Image = MenuIconFactory.GetUnassignIcon(),
            };
            _ldTripCtxUnassign.Click += async (s, e) => await LateDriversTripCtxUnassignAsync();

            _ldTripCtxMenu.Items.Add(_ldTripCtxAssign);
            _ldTripCtxMenu.Items.Add(_ldTripCtxUnassign);
            _ldTripCtxMenu.Opening += LateDriversTripContextMenu_Opening;
        }

        private void WireLateDriversTripContextMenu(SupeyListView lv)
        {
            if (lv == null)
                return;
            EnsureLateDriversTripContextMenu();
            lv.MouseUp -= LateDriversTripLv_MouseUp_ShowContextMenu;
            lv.MouseUp += LateDriversTripLv_MouseUp_ShowContextMenu;
        }

        private void LateDriversTripLv_MouseUp_ShowContextMenu(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right || ldTripLv == null || ldTripLv.IsDisposed)
                return;

            EnsureLateDriversTripContextMenu();

            var hit = ldTripLv.HitTest(e.Location);
            if (hit.Item != null)
            {
                hit.Item.Selected = true;
                hit.Item.Focused = true;
            }

            LateDriversTripContextMenu_RefreshEnabled();
            _ldTripCtxMenu.Show(ldTripLv, e.Location);
        }

        private void LateDriversTripContextMenu_Opening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            LateDriversTripContextMenu_RefreshEnabled();
        }

        private void LateDriversTripContextMenu_RefreshEnabled()
        {
            var targets = GetSelectedLateDriversWrMutationTargets(includeCompleted: true);
            bool hasPortalId = targets.Count > 0;
            bool locked = hasPortalId && targets.Exists(LateDriversWrMutationTargetIsCompleted);
            bool finished = locked && targets.TrueForAll(t => LateDriversWrTripLooksFinished(t.WrRow, t.ListTag));
            string lockNote = finished ? "completed" : "started";

            bool canMutate = hasPortalId && !locked;
            if (_ldTripCtxAssign != null)
            {
                _ldTripCtxAssign.Enabled = canMutate;
                if (locked)
                    _ldTripCtxAssign.Text = "Assign to driver… (" + lockNote + ")";
                else if (targets.Count > 1)
                    _ldTripCtxAssign.Text = "Assign " + targets.Count + " trips to driver...";
                else
                    _ldTripCtxAssign.Text = "Assign to driver...";
            }
            if (_ldTripCtxUnassign != null)
            {
                _ldTripCtxUnassign.Enabled = canMutate;
                if (locked)
                    _ldTripCtxUnassign.Text = "Unassign trip (" + lockNote + ")";
                else if (targets.Count > 1)
                    _ldTripCtxUnassign.Text = "Unassign " + targets.Count + " trips";
                else
                    _ldTripCtxUnassign.Text = "Unassign trip";
            }
        }

        private sealed class LateDriversWrMutationTarget
        {
            public string TripNo;
            public string TripUuid;
            public string DriverName;
            public HiatmeAiClient.TripScoutServerTripRow WrRow;
            public LateDriversTripRowTag ListTag;
        }

        private static bool LateDriversWrMutationTargetIsCompleted(LateDriversWrMutationTarget t)
        {
            if (t == null)
                return false;
            return LateDriversWrTripLooksCompleted(t.WrRow, t.ListTag);
        }

        private static bool LateDriversWrTripLooksFinished(
            HiatmeAiClient.TripScoutServerTripRow wr,
            LateDriversTripRowTag tag)
        {
            string status = (wr?.Status ?? tag?.StatusDisplay ?? "").Trim();
            if (string.Equals(status, "Completed", StringComparison.OrdinalIgnoreCase)
                || string.Equals(status, "Billed", StringComparison.OrdinalIgnoreCase)
                || string.Equals(status, "Dropoff Completed", StringComparison.OrdinalIgnoreCase))
                return true;
            if (wr != null && !string.IsNullOrWhiteSpace(wr.ActualDoIso))
                return true;
            return LateDriversDisplayTimeLooksSet(tag?.ActualDoDisplay);
        }

        /// <summary>
        /// Block assign/unassign once the trip is past scheduled/assigned on WellRyde
        /// (pickup arrived or later, including completed/billed).
        /// </summary>
        private static bool LateDriversWrTripLooksCompleted(
            HiatmeAiClient.TripScoutServerTripRow wr,
            LateDriversTripRowTag tag)
        {
            if (LateDriversWrTripLooksFinished(wr, tag))
                return true;

            string status = (wr?.Status ?? tag?.StatusDisplay ?? "").Trim();
            if (status.Length > 0)
            {
                // Already started / in motion
                if (string.Equals(status, "In Progress", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(status, "Pickup Arrived", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(status, "Pickup Departed", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(status, "Dropoff Arrived", StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            // Actual PU means the trip has left the "not started" window.
            if (wr != null && !string.IsNullOrWhiteSpace(wr.ActualPuIso))
                return true;

            if (LateDriversDisplayTimeLooksSet(tag?.ActualPuDisplay))
                return true;

            return false;
        }

        private static bool LateDriversDisplayTimeLooksSet(string display)
        {
            string ado = (display ?? "").Trim();
            if (ado.Length == 0)
                return false;
            if (string.Equals(ado, "—", StringComparison.Ordinal)
                || string.Equals(ado, "-", StringComparison.Ordinal)
                || string.Equals(ado, "n/a", StringComparison.OrdinalIgnoreCase)
                || string.Equals(ado, "will call", StringComparison.OrdinalIgnoreCase)
                || string.Equals(ado, "0:00", StringComparison.OrdinalIgnoreCase)
                || string.Equals(ado, "00:00", StringComparison.OrdinalIgnoreCase))
                return false;
            return true;
        }

        private List<LateDriversWrMutationTarget> GetSelectedLateDriversWrMutationTargets(
            bool includeCompleted = false)
        {
            var list = new List<LateDriversWrMutationTarget>();
            if (ldTripLv == null || ldTripLv.IsDisposed || ldTripLv.SelectedItems.Count == 0)
                return list;

            var seenUuid = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ListViewItem item in ldTripLv.SelectedItems)
            {
                if (!(item.Tag is LateDriversTripRowTag tag) || tag.IsChangeDetail || tag.IsGroupHeader)
                    continue;

                string tripNo = (tag.TripNo ?? "").Trim();
                if (string.IsNullOrEmpty(tripNo) && tag.ScheduleTrip != null)
                    tripNo = (tag.ScheduleTrip.TripNumber ?? "").Trim();
                if (string.IsNullOrEmpty(tripNo) && tag.HabitEvent != null)
                    tripNo = (tag.HabitEvent.TripNo ?? "").Trim();
                if (string.IsNullOrEmpty(tripNo))
                    continue;

                var wr = FindLateDriversWrTrip(tripNo);
                string uuid = (wr?.TripUuid ?? "").Trim();
                if (string.IsNullOrEmpty(uuid) || !seenUuid.Add(uuid))
                    continue;

                var target = new LateDriversWrMutationTarget
                {
                    TripNo = tripNo,
                    TripUuid = uuid,
                    DriverName = (wr?.Driver ?? tag.DriverDisplay ?? "").Trim(),
                    WrRow = wr,
                    ListTag = tag,
                };
                if (!includeCompleted && LateDriversWrMutationTargetIsCompleted(target))
                    continue;
                list.Add(target);
            }
            return list;
        }

        private async System.Threading.Tasks.Task LateDriversTripCtxAssignAsync()
        {
            var targets = GetSelectedLateDriversWrMutationTargets(includeCompleted: false);
            if (targets.Count == 0)
            {
                var all = GetSelectedLateDriversWrMutationTargets(includeCompleted: true);
                if (all.Count > 0 && all.TrueForAll(LateDriversWrMutationTargetIsCompleted))
                {
                    bool finished = all.TrueForAll(t => LateDriversWrTripLooksFinished(t.WrRow, t.ListTag));
                    SetLateDriversStatus(finished
                        ? "Status: Cannot assign — trip is already completed on WellRyde."
                        : "Status: Cannot assign — trip already started on WellRyde.");
                }
                else
                    SetLateDriversStatus("Status: Assign needs a WellRyde trip with a portal id — refresh live/day first.");
                return;
            }

            if (!await EnsureWellRydePortalSessionForBillingAsync() || _wellRydeSession == null)
            {
                SupeyMessageForm.Show(this, "Driver Habits",
                    "WellRyde portal session is not available.",
                    SupeyMessageKind.Warning,
                    headline: "Cannot assign");
                return;
            }

            ShowLoadingGif();
            try
            {
                if (_tripScoutDriverRoster == null || _tripScoutDriverRoster.Count == 0)
                {
                    await SetLoadingGifLabel("Loading WellRyde driver list…");
                    try
                    {
                        _tripScoutDriverRoster = await _wellRydeSession
                            .GetAllDriversForTripAssignmentAsync()
                            .ConfigureAwait(true);
                    }
                    catch (Exception ex)
                    {
                        SupeyMessageForm.Show(this, "Driver Habits",
                            "Failed to load drivers: " + ex.Message,
                            SupeyMessageKind.Warning,
                            headline: "Cannot assign");
                        return;
                    }
                }

                if (_tripScoutDriverRoster == null || _tripScoutDriverRoster.Count == 0)
                {
                    SupeyMessageForm.Show(this, "Driver Habits",
                        "WellRyde returned no drivers.",
                        SupeyMessageKind.Warning,
                        headline: "Cannot assign");
                    return;
                }
            }
            finally
            {
                hidegiftimer.Start();
            }

            string contextLine = targets.Count == 1
                ? "Assigning trip " + targets[0].TripNo
                : "Assigning " + targets.Count + " trips";

            WRDrivers picked;
            using (var dlg = new DriverPickerForm(_tripScoutDriverRoster, contextLine))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK)
                    return;
                picked = dlg.SelectedDriver;
            }
            if (picked == null)
                return;

            var uuids = new List<string>(targets.Count);
            foreach (var t in targets)
                uuids.Add(t.TripUuid);

            ShowLoadingGif();
            try
            {
                await SetLoadingGifLabel(
                    "Assigning " + uuids.Count + " trip" + (uuids.Count == 1 ? "" : "s")
                    + " to " + (picked.text ?? "driver") + "…");
                SetLateDriversStatus(
                    "Status: Assigning " + uuids.Count + " trip" + (uuids.Count == 1 ? "" : "s")
                    + " to " + (picked.text ?? "driver") + " on WellRyde…");

                WellRydePortalTripMutationResult result;
                try
                {
                    result = await _wellRydeSession
                        .PostAssignTripsToDriverAsync(picked.value, uuids)
                        .ConfigureAwait(true);
                }
                catch (Exception ex)
                {
                    SupeyMessageForm.Show(this, "Driver Habits",
                        "Assign failed: " + ex.Message,
                        SupeyMessageKind.Warning,
                        headline: "Assign failed");
                    SetLateDriversStatus("Status: Assign failed — " + ex.Message);
                    return;
                }

                if (!result.IsSuccess)
                {
                    string err = result.ErrorMessage ?? "(no error message)";
                    SupeyMessageForm.Show(this, "Driver Habits",
                        "WellRyde rejected the assignment.\n\n" + err,
                        SupeyMessageKind.Warning,
                        headline: "Assign rejected");
                    SetLateDriversStatus("Status: Assign rejected — " + err);
                    return;
                }

                string newDriverName = picked.text ?? "";
                foreach (var t in targets)
                {
                    if (t.WrRow != null)
                        t.WrRow.Driver = newDriverName;
                    if (t.ListTag != null)
                        t.ListTag.DriverDisplay = newDriverName;
                }

                // Force a WR re-pull so habits / Reserved tile match portal.
                string sd = LateDriversSelectedServiceDateIso();
                var settings = LateDriversAiSettings();
                if (settings != null && !string.IsNullOrWhiteSpace(sd))
                {
                    SetLateDriversStatus("Status: Assigned — refreshing WellRyde trips…");
                    await EnsureLateDriversWrTripsAsync(settings, sd, forceRefresh: true)
                        .ConfigureAwait(true);
                }
                BindLateDriversTripPane();
                SetLateDriversStatus(
                    "Status: Assigned " + uuids.Count + " trip" + (uuids.Count == 1 ? "" : "s")
                    + " to " + newDriverName + ".");
            }
            finally
            {
                hidegiftimer.Start();
            }
        }

        private async System.Threading.Tasks.Task LateDriversTripCtxUnassignAsync()
        {
            var targets = GetSelectedLateDriversWrMutationTargets(includeCompleted: false);
            if (targets.Count == 0)
            {
                var all = GetSelectedLateDriversWrMutationTargets(includeCompleted: true);
                if (all.Count > 0 && all.TrueForAll(LateDriversWrMutationTargetIsCompleted))
                {
                    bool finished = all.TrueForAll(t => LateDriversWrTripLooksFinished(t.WrRow, t.ListTag));
                    SetLateDriversStatus(finished
                        ? "Status: Cannot unassign — trip is already completed on WellRyde."
                        : "Status: Cannot unassign — trip already started on WellRyde.");
                }
                else
                    SetLateDriversStatus("Status: Unassign needs a WellRyde trip with a portal id — refresh live/day first.");
                return;
            }

            string heading = targets.Count == 1 ? "Unassign this trip?" : "Unassign these trips?";
            string body = targets.Count == 1
                ? "Unassign trip " + targets[0].TripNo
                    + " from "
                    + (string.IsNullOrWhiteSpace(targets[0].DriverName) ? "(no driver)" : targets[0].DriverName)
                    + " on WellRyde?"
                : "Unassign " + targets.Count + " trips from their current drivers on WellRyde?";

            if (SupeyMessageDialog.Confirm(
                    this,
                    SupeyMessageDialog.Kind.Warning,
                    "Driver Habits",
                    heading,
                    body,
                    yesText: "Unassign",
                    noText: "Cancel") != DialogResult.Yes)
                return;

            if (!await EnsureWellRydePortalSessionForBillingAsync() || _wellRydeSession == null)
            {
                SupeyMessageForm.Show(this, "Driver Habits",
                    "WellRyde portal session is not available.",
                    SupeyMessageKind.Warning,
                    headline: "Cannot unassign");
                return;
            }

            var uuids = new List<string>(targets.Count);
            foreach (var t in targets)
                uuids.Add(t.TripUuid);

            ShowLoadingGif();
            try
            {
                await SetLoadingGifLabel(
                    "Unassigning " + uuids.Count + " trip" + (uuids.Count == 1 ? "" : "s") + "…");
                SetLateDriversStatus(
                    "Status: Unassigning " + uuids.Count + " trip" + (uuids.Count == 1 ? "" : "s")
                    + " on WellRyde…");

                WellRydePortalTripMutationResult result;
                try
                {
                    result = await _wellRydeSession.PostUnassignTripsAsync(uuids).ConfigureAwait(true);
                }
                catch (Exception ex)
                {
                    SupeyMessageForm.Show(this, "Driver Habits",
                        "Unassign failed: " + ex.Message,
                        SupeyMessageKind.Warning,
                        headline: "Unassign failed");
                    SetLateDriversStatus("Status: Unassign failed — " + ex.Message);
                    return;
                }

                if (!result.IsSuccess)
                {
                    string err = result.ErrorMessage ?? "(no error message)";
                    SupeyMessageForm.Show(this, "Driver Habits",
                        "WellRyde rejected the unassign.\n\n" + err,
                        SupeyMessageKind.Warning,
                        headline: "Unassign rejected");
                    SetLateDriversStatus("Status: Unassign rejected — " + err);
                    return;
                }

                foreach (var t in targets)
                {
                    if (t.WrRow != null)
                        t.WrRow.Driver = "";
                    if (t.ListTag != null)
                        t.ListTag.DriverDisplay = "";
                }

                string sd = LateDriversSelectedServiceDateIso();
                var settings = LateDriversAiSettings();
                if (settings != null && !string.IsNullOrWhiteSpace(sd))
                {
                    SetLateDriversStatus("Status: Unassigned — refreshing WellRyde trips…");
                    await EnsureLateDriversWrTripsAsync(settings, sd, forceRefresh: true)
                        .ConfigureAwait(true);
                }
                BindLateDriversTripPane();
                SetLateDriversStatus(
                    "Status: Unassigned " + uuids.Count + " trip" + (uuids.Count == 1 ? "" : "s") + ".");
            }
            finally
            {
                hidegiftimer.Start();
            }
        }
    }
}
