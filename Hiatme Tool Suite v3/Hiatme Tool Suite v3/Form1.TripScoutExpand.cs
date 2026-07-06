using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    partial class Form1
    {
        private static readonly Color TripScoutChangeDetailBg = Color.FromArgb(58, 58, 62);

        private readonly HashSet<string> _tripScoutExpandedTripNos =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<HiatmeAiClient.TripScoutChangeRow>> _tripScoutChangesByTrip =
            new Dictionary<string, List<HiatmeAiClient.TripScoutChangeRow>>(StringComparer.OrdinalIgnoreCase);
        private bool _tripScoutExpandClickWired;

        internal void EnsureTripScoutExpandClick()
        {
            if (_tripScoutExpandClickWired || tslv == null || tslv.IsDisposed)
                return;
            tslv.MouseUp += TripScoutListView_MouseUp;
            _tripScoutExpandClickWired = true;
        }

        private void TripScoutListView_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || tslv == null || tslv.IsDisposed)
                return;

            var hit = tslv.HitTest(e.Location);
            if (hit?.Item == null)
                return;

            var row = hit.Item.Tag as TripScoutListRow;
            if (row == null || row.Kind != TripScoutListRowKind.Trip)
                return;

            if (!row.HasChanges)
                return;

            TripScoutToggleTripExpanded(row.TripNo);
        }

        internal void TripScoutClearExpandedTrips()
        {
            _tripScoutExpandedTripNos.Clear();
        }

        private static string TripScoutNormalizeTripNo(string tripNo)
        {
            return string.IsNullOrWhiteSpace(tripNo) ? "" : tripNo.Trim();
        }

        private void TripScoutPruneExpandedTrips()
        {
            if (_tripScoutExpandedTripNos.Count == 0)
                return;

            var stale = new List<string>();
            foreach (string key in _tripScoutExpandedTripNos)
            {
                if (TripScoutChangeCount(key) <= 0 && !TripScoutIsWillCallTrip(key))
                    stale.Add(key);
            }

            foreach (string key in stale)
                _tripScoutExpandedTripNos.Remove(key);
        }

        private void RebuildTripScoutChangesByTrip()
        {
            _tripScoutChangesByTrip.Clear();
            if (_tripScoutDayChanges == null)
                return;

            foreach (var row in _tripScoutDayChanges)
            {
                if (row == null || string.IsNullOrWhiteSpace(row.TripNo))
                    continue;
                string key = row.TripNo.Trim();
                if (!_tripScoutChangesByTrip.TryGetValue(key, out var list))
                {
                    list = new List<HiatmeAiClient.TripScoutChangeRow>();
                    _tripScoutChangesByTrip[key] = list;
                }
                list.Add(row);
            }

            foreach (var list in _tripScoutChangesByTrip.Values)
            {
                list.Sort((a, b) =>
                {
                    double ta = a?.Ts ?? 0;
                    double tb = b?.Ts ?? 0;
                    return ta.CompareTo(tb);
                });
            }
        }

        private int TripScoutChangeCount(string tripNo)
        {
            if (string.IsNullOrWhiteSpace(tripNo))
                return 0;
            List<HiatmeAiClient.TripScoutChangeRow> list;
            return _tripScoutChangesByTrip.TryGetValue(tripNo.Trim(), out list) ? list.Count : 0;
        }

        private void TripScoutToggleTripExpanded(string tripNo)
        {
            string key = TripScoutNormalizeTripNo(tripNo);
            if (key.Length == 0)
                return;

            if (_tripScoutExpandedTripNos.Contains(key))
                _tripScoutExpandedTripNos.Remove(key);
            else
                _tripScoutExpandedTripNos.Add(key);

            TripScoutRebindVisibleListPreserveScroll();
        }

        private void TripScoutRebindVisibleListPreserveScroll()
        {
            if (tslv == null || tslv.IsDisposed)
                return;

            string anchorTripNo = TripScoutListRowTripNo(tslv.TopItem?.Tag);
            int anchorIndex = tslv.TopItem?.Index ?? -1;

            SupeyListViewHelpers.SetRedraw(tslv, false);
            try
            {
                ApplyTripScoutFilter(tssearchbox?.Text ?? "", anchorTripNo, anchorIndex);
            }
            finally
            {
                SupeyListViewHelpers.SetRedraw(tslv, true, invalidate: true);
            }
        }

        private void TripScoutRestoreScrollAnchor(string anchorTripNo, int anchorIndex)
        {
            if (tslv == null || tslv.IsDisposed || tslv.Items.Count == 0)
                return;

            ListViewItem top = null;
            if (!string.IsNullOrEmpty(anchorTripNo))
            {
                foreach (ListViewItem item in tslv.Items)
                {
                    var row = item.Tag as TripScoutListRow;
                    if (row == null || row.Kind != TripScoutListRowKind.Trip)
                        continue;
                    if (!string.Equals(TripScoutNormalizeTripNo(row.TripNo), anchorTripNo, StringComparison.OrdinalIgnoreCase))
                        continue;
                    top = item;
                    break;
                }
            }

            if (top == null && anchorIndex >= 0 && anchorIndex < tslv.Items.Count)
                top = tslv.Items[anchorIndex];

            if (top == null)
                return;

            try
            {
                top.EnsureVisible();
                tslv.TopItem = top;
            }
            catch
            {
                // TopItem can throw if the list is mid-layout — ignore.
            }
        }

        /// <summary>
        /// Updates visible trip rows in place. When <paramref name="resyncDetailRows"/> is true,
        /// also rebuilds expanded change/will-call detail lines without clearing the list.
        /// </summary>
        private bool TripScoutSyncVisibleTripListInPlace(bool resyncDetailRows)
        {
            if (tslv == null || tslv.IsDisposed || _tripScoutAllTrips == null || _tripScoutAllTrips.Count == 0)
                return false;

            string trimmed = (tssearchbox?.Text ?? "").Trim();
            List<WRDownloadedTrip> visible;
            if (trimmed.Length == 0)
            {
                visible = _tripScoutAllTrips;
            }
            else
            {
                visible = new List<WRDownloadedTrip>(_tripScoutAllTrips.Count);
                foreach (var trip in _tripScoutAllTrips)
                {
                    if (MatchesTripScoutFilter(trip, trimmed))
                        visible.Add(trip);
                }
            }

            var tripItems = new List<ListViewItem>();
            foreach (ListViewItem item in tslv.Items)
            {
                var row = item.Tag as TripScoutListRow;
                if (row == null)
                    return false;
                if (row.Kind == TripScoutListRowKind.Trip)
                    tripItems.Add(item);
            }

            if (visible.Count != tripItems.Count)
                return false;

            for (int i = 0; i < visible.Count; i++)
            {
                var trip = visible[i];
                if (trip == null)
                    return false;

                string tripNo = TripScoutTripKey(trip);
                var row = tripItems[i].Tag as TripScoutListRow;
                if (row == null
                    || !string.Equals(TripScoutNormalizeTripNo(row.TripNo), TripScoutNormalizeTripNo(tripNo), StringComparison.OrdinalIgnoreCase))
                    return false;

                if (!resyncDetailRows)
                {
                    int changeCount = TripScoutChangeCount(tripNo);
                    bool hasWillCall = TripScoutIsWillCallTrip(tripNo);
                    bool hasDetails = changeCount > 0 || hasWillCall;
                    bool expanded = _tripScoutExpandedTripNos.Contains(tripNo);
                    if (row.HasChanges != hasDetails || row.IsExpanded != expanded)
                        return false;
                }
            }

            ListViewMinWidthEnforcer.SuspendAutoFit(tslv);
            SupeyListViewHelpers.SetRedraw(tslv, false);
            try
            {
                tslv.BeginUpdate();
                for (int i = 0; i < visible.Count; i++)
                {
                    var trip = visible[i];
                    string tripNo = TripScoutTripKey(trip);
                    int changeCount = TripScoutChangeCount(tripNo);
                    bool hasWillCall = TripScoutIsWillCallTrip(tripNo);
                    bool hasDetails = changeCount > 0 || hasWillCall;
                    bool expanded = _tripScoutExpandedTripNos.Contains(tripNo);
                    var tripItem = tripItems[i];
                    TripScoutApplyTripToRow(tripItem, trip, hasDetails, expanded, changeCount);

                    if (!resyncDetailRows)
                        continue;

                    int tripIndex = tslv.Items.IndexOf(tripItem);
                    if (tripIndex < 0)
                        continue;

                    TripScoutRemoveFollowingDetailRows(tripIndex + 1);
                    if (expanded && hasDetails)
                        TripScoutInsertDetailRowsAfterTrip(tripIndex + 1, tripNo, hasWillCall);
                }
            }
            finally
            {
                tslv.EndUpdate();
                SupeyListViewHelpers.SetRedraw(tslv, true, invalidate: true);
                ListViewMinWidthEnforcer.ResumeAutoFit(tslv);
            }

            return true;
        }

        private bool TripScoutTryRefreshVisibleTripRowsInPlace()
            => TripScoutSyncVisibleTripListInPlace(resyncDetailRows: false);

        private void TripScoutRemoveFollowingDetailRows(int index)
        {
            if (tslv == null || tslv.IsDisposed || index < 0)
                return;

            while (index < tslv.Items.Count)
            {
                var row = tslv.Items[index].Tag as TripScoutListRow;
                if (row == null || row.Kind == TripScoutListRowKind.Trip)
                    break;
                tslv.Items.RemoveAt(index);
            }
        }

        private void TripScoutInsertDetailRowsAfterTrip(int insertIndex, string tripNo, bool hasWillCall)
        {
            if (tslv == null || tslv.IsDisposed || insertIndex < 0)
                return;

            var rows = new List<ListViewItem>();
            if (hasWillCall)
            {
                var wc = _tripScoutWillCalls?.FirstOrDefault(w =>
                    w != null && TripScoutTripNosMatch(w.TripNo, tripNo));
                if (wc != null)
                    rows.Add(TripScoutBuildWillCallDetailRow(wc, tripNo));
            }

            if (_tripScoutChangesByTrip.TryGetValue(tripNo, out List<HiatmeAiClient.TripScoutChangeRow> changes))
            {
                foreach (var change in changes)
                    rows.Add(TripScoutBuildChangeDetailRow(change, tripNo));
            }

            for (int i = 0; i < rows.Count; i++)
                tslv.Items.Insert(insertIndex + i, rows[i]);
        }

        private static string TripScoutListRowTripNo(object tag)
        {
            if (tag is TripScoutListRow row)
                return TripScoutNormalizeTripNo(row.TripNo);
            return "";
        }

        private void TripScoutBindListView(
            List<WRDownloadedTrip> trips,
            bool fitColumns = false,
            string scrollAnchorTripNo = null,
            int scrollAnchorIndex = -1)
        {
            EnsureTripScoutExpandClick();
            TripScoutPruneExpandedTrips();

            int[] columnWidths = TripScoutCaptureColumnWidths();
            ListViewMinWidthEnforcer.SuspendAutoFit(tslv);
            SupeyListViewHelpers.SetRedraw(tslv, false);
            try
            {
                tslv.BeginUpdate();
                tslv.Items.Clear();
                if (trips == null || trips.Count == 0)
                    return;

                foreach (WRDownloadedTrip trip in trips)
                {
                    if (trip == null)
                        continue;

                    string tripNo = TripScoutTripKey(trip);
                    int changeCount = TripScoutChangeCount(tripNo);
                    bool hasWillCall = TripScoutIsWillCallTrip(tripNo);
                    bool hasDetails = changeCount > 0 || hasWillCall;
                    bool expanded = _tripScoutExpandedTripNos.Contains(tripNo);

                    ListViewItem item = TripScoutBuildTripRow(trip, hasDetails, expanded, changeCount);
                    tslv.Items.Add(item);

                    if (!expanded || !hasDetails)
                        continue;

                    if (hasWillCall)
                    {
                        var wc = _tripScoutWillCalls?.FirstOrDefault(w =>
                            w != null && TripScoutTripNosMatch(w.TripNo, tripNo));
                        if (wc != null)
                            tslv.Items.Add(TripScoutBuildWillCallDetailRow(wc, tripNo));
                    }

                    if (_tripScoutChangesByTrip.TryGetValue(tripNo, out List<HiatmeAiClient.TripScoutChangeRow> changes))
                    {
                        foreach (var change in changes)
                            tslv.Items.Add(TripScoutBuildChangeDetailRow(change, tripNo));
                    }
                }
            }
            finally
            {
                TripScoutRestoreColumnWidths(columnWidths);
                TripScoutRestoreScrollAnchor(scrollAnchorTripNo, scrollAnchorIndex);
                tslv.EndUpdate();
                tslv.ResetHotState();
                SupeyListViewHelpers.SetRedraw(tslv, true, invalidate: true);
                ListViewMinWidthEnforcer.ResumeAutoFit(tslv);
            }

            TripScoutApplyRowHighlights();
            if (fitColumns)
                ScheduleTripScoutColumnFit();
        }

        private int[] TripScoutCaptureColumnWidths()
        {
            if (tslv == null || tslv.IsDisposed || tslv.Columns.Count == 0)
                return null;

            var widths = new int[tslv.Columns.Count];
            for (int i = 0; i < widths.Length; i++)
                widths[i] = tslv.Columns[i].Width;
            return widths;
        }

        private void TripScoutRestoreColumnWidths(int[] widths)
        {
            if (widths == null || tslv == null || tslv.IsDisposed)
                return;

            for (int i = 0; i < widths.Length && i < tslv.Columns.Count; i++)
            {
                if (tslv.Columns[i].Width != widths[i])
                    tslv.Columns[i].Width = widths[i];
            }
        }

        private static ListViewItem TripScoutBuildTripRow(
            WRDownloadedTrip trip,
            bool hasDetails,
            bool expanded,
            int changeCount)
        {
            var item = new ListViewItem("");
            TripScoutApplyTripToRow(item, trip, hasDetails, expanded, changeCount, createSubItems: true);
            return item;
        }

        private static void TripScoutApplyTripToRow(
            ListViewItem item,
            WRDownloadedTrip trip,
            bool hasDetails,
            bool expanded,
            int changeCount,
            bool createSubItems = false)
        {
            string tripNo = trip.TripNumber ?? "";
            string prefix = hasDetails ? (expanded ? "▼ " : "▶ ") : "   ";
            item.Text = prefix + (trip.Status ?? "");
            item.Tag = TripScoutListRow.ForTrip(trip, hasDetails, expanded);

            // WinForms: column 0 = Text/SubItems[0]; column N = SubItems[N].
            if (createSubItems)
            {
                item.SubItems.Add(tripNo);
                item.SubItems.Add("");
                item.SubItems.Add(trip.ClientName ?? "");
                item.SubItems.Add(trip.DriverName ?? "");
                item.SubItems.Add(FormatTimeOnly(trip.PUTime ?? ""));
                item.SubItems.Add(trip.PUStreet ?? "");
                item.SubItems.Add(trip.PUCity ?? "");
                item.SubItems.Add(FormatTimeOnly(trip.DOTime ?? ""));
                item.SubItems.Add(trip.DOStreet ?? "");
                item.SubItems.Add(trip.DOCITY ?? "");
                item.SubItems.Add(trip.Miles ?? "");
                item.SubItems.Add("$" + (trip.Price ?? ""));
                item.SubItems.Add(trip.References ?? "");
            }
            else
            {
                TripScoutSetColumnText(item, 1, tripNo);
                TripScoutSetColumnText(item, 2, "");
                TripScoutSetColumnText(item, 3, trip.ClientName ?? "");
                TripScoutSetColumnText(item, 4, trip.DriverName ?? "");
                TripScoutSetColumnText(item, 5, FormatTimeOnly(trip.PUTime ?? ""));
                TripScoutSetColumnText(item, 6, trip.PUStreet ?? "");
                TripScoutSetColumnText(item, 7, trip.PUCity ?? "");
                TripScoutSetColumnText(item, 8, FormatTimeOnly(trip.DOTime ?? ""));
                TripScoutSetColumnText(item, 9, trip.DOStreet ?? "");
                TripScoutSetColumnText(item, 10, trip.DOCITY ?? "");
                TripScoutSetColumnText(item, 11, trip.Miles ?? "");
                TripScoutSetColumnText(item, 12, "$" + (trip.Price ?? ""));
                TripScoutSetColumnText(item, 13, trip.References ?? "");
            }

            if (changeCount > 0)
                TripScoutSetColumnText(item, 1, tripNo + " (" + changeCount + ")");
        }

        private static void TripScoutSetColumnText(ListViewItem item, int columnIndex, string text)
        {
            if (item == null || columnIndex <= 0)
                return;

            while (item.SubItems.Count <= columnIndex)
                item.SubItems.Add("");
            item.SubItems[columnIndex].Text = text ?? "";
        }

        private static ListViewItem TripScoutBuildChangeDetailRow(
            HiatmeAiClient.TripScoutChangeRow change,
            string tripNo)
        {
            string when = TripScoutFormatChangeTime(change?.Ts);
            string headline = TripScoutChangeFormat.FormatHeadline(change) ?? "Updated";
            string diff = TripScoutChangeFormat.FormatDiff(change);

            var item = new ListViewItem("      " + when);
            item.Tag = TripScoutListRow.ForChange(change, tripNo);
            item.SubItems.Add("↳ " + headline);
            item.SubItems.Add("");
            item.SubItems.Add(diff);
            item.SubItems.Add("");
            item.SubItems.Add("");
            item.SubItems.Add("");
            item.SubItems.Add("");
            item.SubItems.Add("");
            item.SubItems.Add("");
            item.SubItems.Add("");
            item.SubItems.Add("");
            item.SubItems.Add("");
            item.SubItems.Add("");
            item.BackColor = TripScoutChangeDetailBg;
            item.ForeColor = Color.FromArgb(255, 220, 160);
            return item;
        }

        private static ListViewItem TripScoutBuildWillCallDetailRow(
            HiatmeAiClient.WellRydeBellWillCall willCall,
            string tripNo)
        {
            string rider = string.IsNullOrWhiteSpace(willCall?.Rider) ? "" : " — " + willCall.Rider.Trim();
            var item = new ListViewItem("      Will call ready");
            item.Tag = TripScoutListRow.ForWillCall(willCall, tripNo);
            item.SubItems.Add("");
            item.SubItems.Add("");
            item.SubItems.Add("Ready for pickup" + rider);
            item.SubItems.Add("");
            item.SubItems.Add(willCall?.PuAddr ?? "");
            item.SubItems.Add("");
            item.SubItems.Add("");
            item.SubItems.Add("");
            item.SubItems.Add("");
            item.SubItems.Add("");
            item.SubItems.Add("");
            item.SubItems.Add("");
            item.BackColor = Color.FromArgb(68, 82, 120);
            item.ForeColor = Color.White;
            return item;
        }

        private static string TripScoutFormatChangeTime(double? ts)
        {
            if (!ts.HasValue || ts.Value <= 0)
                return "";
            try
            {
                var dt = DateTimeOffset.FromUnixTimeSeconds((long)ts.Value).LocalDateTime;
                return dt.ToString("h:mm tt", CultureInfo.CurrentCulture);
            }
            catch
            {
                return "";
            }
        }

        internal bool TripScoutIsDetailRow(ListViewItem item)
        {
            var row = item?.Tag as TripScoutListRow;
            return row != null && row.Kind != TripScoutListRowKind.Trip;
        }
    }
}
