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
                if (TripScoutChangeCount(key) <= 0 && !_tripScoutWillCallTripNos.Contains(key))
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

            ApplyTripScoutFilter(tssearchbox?.Text ?? "");

            if (anchorTripNo.Length == 0)
                return;

            foreach (ListViewItem item in tslv.Items)
            {
                var row = item.Tag as TripScoutListRow;
                if (row == null || row.Kind != TripScoutListRowKind.Trip)
                    continue;
                if (!string.Equals(TripScoutNormalizeTripNo(row.TripNo), anchorTripNo, StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    item.EnsureVisible();
                    tslv.TopItem = item;
                }
                catch
                {
                    // TopItem can throw if the list is mid-layout — ignore.
                }
                break;
            }
        }

        private static string TripScoutListRowTripNo(object tag)
        {
            if (tag is TripScoutListRow row)
                return TripScoutNormalizeTripNo(row.TripNo);
            return "";
        }

        private void TripScoutBindListView(List<WRDownloadedTrip> trips, bool fitColumns = false)
        {
            EnsureTripScoutExpandClick();
            TripScoutPruneExpandedTrips();

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
                    bool hasWillCall = _tripScoutWillCallTripNos.Contains(tripNo);
                    bool hasDetails = changeCount > 0 || hasWillCall;
                    bool expanded = _tripScoutExpandedTripNos.Contains(tripNo);

                    ListViewItem item = TripScoutBuildTripRow(trip, hasDetails, expanded, changeCount);
                    tslv.Items.Add(item);

                    if (!expanded || !hasDetails)
                        continue;

                    if (hasWillCall)
                    {
                        var wc = _tripScoutWillCalls?.FirstOrDefault(w =>
                            w != null
                            && string.Equals(TripScoutNormalizeTripNo(w.TripNo), tripNo, StringComparison.OrdinalIgnoreCase));
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
                tslv.EndUpdate();
                tslv.ResetHotState();
            }

            TripScoutApplyRowHighlights();
            if (fitColumns)
                ScheduleTripScoutColumnFit();
        }

        private static ListViewItem TripScoutBuildTripRow(
            WRDownloadedTrip trip,
            bool hasDetails,
            bool expanded,
            int changeCount)
        {
            string tripNo = trip.TripNumber ?? "";
            string prefix = hasDetails ? (expanded ? "▼ " : "▶ ") : "   ";
            var item = new ListViewItem(prefix + (trip.Status ?? ""));
            item.Tag = TripScoutListRow.ForTrip(trip, hasDetails, expanded);
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
            if (changeCount > 0)
                item.SubItems[1].Text = tripNo + " (" + changeCount + ")";
            return item;
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
