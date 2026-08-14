using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    public partial class Form1
    {
        /// <summary>
        /// Cut clipboard. A list because the trip list is multi-select: cutting has to take
        /// everything highlighted, not just the row the mouse happened to be over.
        /// </summary>
        private readonly List<ScheduleBuilderCutTrip> _fsCutTrips = new List<ScheduleBuilderCutTrip>();

        private ListViewItem _fsTripsCtxHitItem;

        private bool FsHasCutTrip => _fsCutTrips.Count > 0;

        private void FsClearCutTrip()
        {
            _fsCutTrips.Clear();
            FsUpdateCutTripBar();
        }

        private void FsCutSelectedTrip()
        {
            if (string.IsNullOrWhiteSpace(_fsActiveDriverTab) || !_fsHasPreview)
                return;

            string tab = _fsActiveDriverTab;
            if (!_fsLinesByTab.TryGetValue(tab, out var lines) || lines == null)
                return;

            // Whole selection, snapshotted before anything is removed, in the order the rows
            // appear so they paste back in the same order.
            var trips = FsCollectSelectedTrips();
            if (trips.Count == 0)
                return;

            var cut = new List<ScheduleBuilderCutTrip>(trips.Count);
            foreach (var trip in trips)
            {
                cut.Add(new ScheduleBuilderCutTrip
                {
                    Trip = trip,
                    ReserveBand = FsFindTripReserveBand(lines, trip),
                    Rerouted = ScheduleBuilderReroutedTrips.IsMarked(lines, trip),
                });
            }

            FsPushUndoSnapshot(FsTripCountLabel("cut", cut.Count));

            var removed = new List<ScheduleBuilderCutTrip>(cut.Count);
            foreach (var entry in cut)
            {
                if (ScheduleBuilderPreviewDrag.TryRemoveTrip(lines, entry.Trip))
                    removed.Add(entry);
            }

            if (removed.Count == 0)
                return;

            _fsCutTrips.Clear();
            _fsCutTrips.AddRange(removed);
            FsUpdateCutTripBar();
            FsCommitPreviewLinesForTab(tab, lines);
            ShowFsTripsForTab(tab);
            RequestFsMapRefresh();

            if (removed.Count == 1)
            {
                string num = (removed[0].Trip.TripNumber ?? "").Trim();
                SetScheduleBuilderStatus(string.IsNullOrEmpty(num)
                    ? "Trip cut — right-click a row · Paste trip or Insert trip above/below."
                    : "Trip " + num + " cut — right-click a row · Paste trip or Insert trip above/below.");
            }
            else
            {
                SetScheduleBuilderStatus(
                    removed.Count + " trips cut — right-click a row · Paste trips or Insert above/below.");
            }
        }

        private void FsDeleteSelectedTrip()
        {
            if (string.IsNullOrWhiteSpace(_fsActiveDriverTab) || !_fsHasPreview)
                return;

            string tab = _fsActiveDriverTab;
            if (!_fsLinesByTab.TryGetValue(tab, out var lines) || lines == null)
                return;

            var trips = FsCollectSelectedTrips();
            if (trips.Count == 0)
                return;

            FsPushUndoSnapshot(FsTripCountLabel("delete", trips.Count));

            var deleted = new List<MCDownloadedTrip>(trips.Count);
            foreach (var trip in trips)
            {
                if (ScheduleBuilderPreviewDrag.TryRemoveTrip(lines, trip))
                    deleted.Add(trip);
            }

            if (deleted.Count == 0)
            {
                SetScheduleBuilderStatus("Could not delete trip.");
                return;
            }

            foreach (var trip in deleted)
                FsForgetCutTrip(trip);

            FsCommitPreviewLinesForTab(tab, lines);
            ShowFsTripsForTab(tab);
            SyncFsPreviewCsvsForExport();
            RequestFsMapRefresh();

            if (deleted.Count == 1)
            {
                string num = (deleted[0].TripNumber ?? "").Trim();
                SetScheduleBuilderStatus(string.IsNullOrEmpty(num)
                    ? "Trip deleted from schedule."
                    : "Trip " + num + " deleted from schedule.");
            }
            else
            {
                SetScheduleBuilderStatus(deleted.Count + " trips deleted from schedule.");
            }
        }

        /// <summary>Drop a trip from the cut clipboard once it no longer exists to paste.</summary>
        private void FsForgetCutTrip(MCDownloadedTrip trip)
        {
            if (trip == null || _fsCutTrips.Count == 0)
                return;

            for (int i = _fsCutTrips.Count - 1; i >= 0; i--)
            {
                var held = _fsCutTrips[i].Trip;
                if (ReferenceEquals(held, trip) || ScheduleBuilderPreviewDrag.TripEquals(held, trip))
                    _fsCutTrips.RemoveAt(i);
            }

            FsUpdateCutTripBar();
        }

        private static string FsTripCountLabel(string verb, int count)
        {
            return count == 1 ? verb + " trip" : verb + " " + count + " trips";
        }

        private void FsInsertFromContextMenu(bool below)
        {
            if (FsHasCutTrip)
                FsInsertCutTrip(below);
            else
                FsInsertBlankRow(below);
        }

        private void FsInsertCutTrip(bool below)
        {
            if (!FsHasCutTrip || string.IsNullOrWhiteSpace(_fsActiveDriverTab) || !_fsHasPreview)
                return;

            if (!TryResolveFsInsertBeforeLine(_fsTripsCtxHitItem, below, out int insertBeforeLine))
            {
                SetScheduleBuilderStatus("Could not insert here — right-click a trip, gap, or group row.");
                return;
            }

            string tab = _fsActiveDriverTab;

            // Resolve the destination before emptying the clipboard, or a tab with no lines
            // would swallow the whole cut with nothing left to paste.
            if (!_fsLinesByTab.TryGetValue(tab, out var lines) || lines == null)
                return;

            var cut = new List<ScheduleBuilderCutTrip>(_fsCutTrips);
            FsClearCutTrip();

            _fsPreserveRouteChangeBaseline = true;
            FsSnapshotPreMoveGroupMeters(tab, cut[0].Trip, merge: false, mergeTargetTrip: null);

            bool intoReserves = tab.Equals("Reserves", StringComparison.OrdinalIgnoreCase);
            Color? fallbackBand = intoReserves
                ? ScheduleBuilderPreviewDrag.ResolveReserveBandForInsert(lines, insertBeforeLine)
                : (Color?)null;

            FsPushUndoSnapshot(FsTripCountLabel("insert", cut.Count));

            // Walk the insert point forward so the batch lands consecutively in the order it was
            // cut, rather than every trip landing on the same index and reversing the run.
            var inserted = new List<MCDownloadedTrip>(cut.Count);
            int at = insertBeforeLine;
            foreach (var entry in cut)
            {
                int before = lines.Count;
                Color? band = intoReserves ? (entry.ReserveBand ?? fallbackBand) : null;
                ScheduleBuilderPreviewDrag.InsertTripLine(lines, entry.Trip, at, band, entry.Rerouted);

                // InsertTripLine is a no-op when the trip is somehow already on this tab, so only
                // advance when a row was really added.
                if (lines.Count > before)
                {
                    inserted.Add(entry.Trip);
                    at++;
                }
            }

            if (inserted.Count == 0)
            {
                SetScheduleBuilderStatus("Nothing to insert here.");
                return;
            }

            FsCommitPreviewLinesForTab(tab, lines);
            ShowFsTripsForTab(tab);
            SelectFsTripsInListView(inserted);
            _ = FsRefreshAfterTripMoveAsync();

            if (inserted.Count == 1)
            {
                string num = (inserted[0].TripNumber ?? "").Trim();
                SetScheduleBuilderStatus(string.IsNullOrEmpty(num)
                    ? "Trip inserted — map updating…"
                    : "Trip " + num + " inserted — map updating…");
            }
            else
            {
                SetScheduleBuilderStatus(inserted.Count + " trips inserted — map updating…");
            }
        }

        private void FsInsertBlankRowAt(int insertBeforeLine)
        {
            if (string.IsNullOrWhiteSpace(_fsActiveDriverTab) || !_fsHasPreview)
                return;

            if (_fsActiveDriverTab.Equals("Reserves", StringComparison.OrdinalIgnoreCase))
                return;

            string tab = _fsActiveDriverTab;
            if (!_fsLinesByTab.TryGetValue(tab, out var lines) || lines == null)
                return;

            FsRevealGapsForManualInsert();
            FsPushUndoSnapshot("insert blank row");
            ScheduleBuilderPreviewDrag.InsertGapLine(lines, insertBeforeLine);
            FsCommitPreviewLinesForTab(tab, lines);
            ShowFsTripsForTab(tab);
            SyncFsPreviewCsvsForExport();
            RequestFsMapRefresh();
        }

        private void FsInsertBlankRow(bool below)
        {
            if (FsHasCutTrip || string.IsNullOrWhiteSpace(_fsActiveDriverTab) || !_fsHasPreview)
                return;

            if (!TryResolveFsInsertBeforeLine(_fsTripsCtxHitItem, below, out int insertBeforeLine))
            {
                SetScheduleBuilderStatus("Could not insert here — right-click a trip, gap, or group row.");
                return;
            }

            FsInsertBlankRowAt(insertBeforeLine);
            SetScheduleBuilderStatus("Blank row inserted.");
        }

        private void FsDeleteBlankRow()
        {
            if (string.IsNullOrWhiteSpace(_fsActiveDriverTab) || !_fsHasPreview)
                return;

            if (_fsActiveDriverTab.Equals("Reserves", StringComparison.OrdinalIgnoreCase))
                return;

            if (!(_fsTripsCtxHitItem?.Tag is FsPreviewGapTag gapTag) || gapTag.PreviewLineIndex < 0)
            {
                SetScheduleBuilderStatus("Right-click a blank row to delete it.");
                return;
            }

            if (gapTag.TrailingPad)
            {
                SetScheduleBuilderStatus("Extra rows at the bottom stay — pick one to paste or insert a trip.");
                return;
            }

            string tab = _fsActiveDriverTab;
            if (!_fsLinesByTab.TryGetValue(tab, out var lines) || lines == null)
                return;

            int idx = gapTag.PreviewLineIndex;
            if (idx < 0 || idx >= lines.Count
                || lines[idx]?.Kind != ScheduleBuilderPreviewLine.LineKind.Gap)
            {
                SetScheduleBuilderStatus("Could not delete this row.");
                return;
            }

            FsPushUndoSnapshot("delete blank row");
            lines.RemoveAt(idx);
            FsCommitPreviewLinesForTab(tab, lines);
            ShowFsTripsForTab(tab);
            SyncFsPreviewCsvsForExport();
            RequestFsMapRefresh();
            SetScheduleBuilderStatus("Blank row deleted.");
        }

        private void FsDeleteGapNoteRow()
        {
            if (string.IsNullOrWhiteSpace(_fsActiveDriverTab) || !_fsHasPreview)
                return;

            if (_fsActiveDriverTab.Equals("Reserves", StringComparison.OrdinalIgnoreCase))
                return;

            var item = _fsTripsCtxHitItem;
            if (item == null && _fsTripsLv?.SelectedItems.Count > 0)
                item = _fsTripsLv.SelectedItems[0];

            if (!(item?.Tag is FsPreviewGapTag gapTag) || gapTag.PreviewLineIndex < 0)
            {
                SetScheduleBuilderStatus("Select a note row to delete it.");
                return;
            }

            if (!ScheduleBuilderGapNotes.GapTagHasNoteBar(gapTag))
            {
                FsDeleteBlankRow();
                return;
            }

            string tab = _fsActiveDriverTab;
            if (!_fsLinesByTab.TryGetValue(tab, out var lines) || lines == null)
                return;

            int idx = gapTag.PreviewLineIndex;
            if (idx < 0 || idx >= lines.Count
                || lines[idx]?.Kind != ScheduleBuilderPreviewLine.LineKind.Gap)
            {
                SetScheduleBuilderStatus("Could not delete this note row.");
                return;
            }

            FsPushUndoSnapshot("delete note row");
            lines.RemoveAt(idx);
            FsCommitPreviewLinesForTab(tab, lines);
            ShowFsTripsForTab(tab);
            SyncFsPreviewCsvsForExport();
            RequestFsMapRefresh();
            SetScheduleBuilderStatus("Note row deleted.");
        }

        private void FsDeleteNoteRow()
        {
            if (string.IsNullOrWhiteSpace(_fsActiveDriverTab) || !_fsHasPreview)
                return;

            if (_fsActiveDriverTab.Equals("Reserves", StringComparison.OrdinalIgnoreCase))
                return;

            var item = _fsTripsCtxHitItem;
            if (item == null && _fsTripsLv?.SelectedItems.Count > 0)
                item = _fsTripsLv.SelectedItems[0];

            if (!(item?.Tag is FsPreviewNoteTag noteTag) || noteTag.PreviewLineIndex < 0)
            {
                SetScheduleBuilderStatus("Select a note row to delete it.");
                return;
            }

            string tab = _fsActiveDriverTab;
            if (!_fsLinesByTab.TryGetValue(tab, out var lines) || lines == null)
                return;

            if (!ScheduleBuilderGroupNotes.IsDeletableNoteRow(item, lines))
            {
                SetScheduleBuilderStatus("Only saved group notes can be deleted (not automatic group color bars).");
                return;
            }

            int idx = noteTag.PreviewLineIndex;
            FsPushUndoSnapshot("delete note row");
            if (!ScheduleBuilderGroupNotes.TryRemoveNoteRow(lines, idx))
            {
                SetScheduleBuilderStatus("Could not delete this note row.");
                return;
            }

            FsCommitPreviewLinesForTab(tab, lines);
            ShowFsTripsForTab(tab);
            SyncFsPreviewCsvsForExport();
            RequestFsMapRefresh();
            SetScheduleBuilderStatus("Note row deleted.");
        }

        private void FsDeleteSelection()
        {
            var item = FsResolveContextListItem();
            if (item != null)
                _fsTripsCtxHitItem = item;

            if (_fsTripsCtxTrip != null
                && _fsHasPreview
                && !string.IsNullOrWhiteSpace(_fsActiveDriverTab))
            {
                FsDeleteSelectedTrip();
                return;
            }

            if (_fsTripsCtxHitItem?.Tag is FsPreviewNoteTag)
            {
                FsDeleteNoteRow();
                return;
            }

            if (_fsTripsCtxHitItem?.Tag is FsPreviewGapTag gapDelete)
            {
                if (ScheduleBuilderGapNotes.GapTagHasNoteBar(gapDelete))
                    FsDeleteGapNoteRow();
                else
                    FsDeleteBlankRow();
                return;
            }

            SetScheduleBuilderStatus("Nothing selected to delete.");
        }

        private async Task FsRefreshAfterTripMoveAsync()
        {
            await RefreshFsMapForCurrentTabAsync().ConfigureAwait(true);
            FsTripsLv_SelectionChangedUpdateMap();
        }

        /// <summary>Line index in preview lines where insert should occur.</summary>
        private bool TryResolveFsInsertBeforeLine(ListViewItem item, bool below, out int insertBeforeLine)
        {
            insertBeforeLine = 0;
            if (_fsTripsLv == null || string.IsNullOrWhiteSpace(_fsActiveDriverTab))
                return false;

            if (!_fsLinesByTab.TryGetValue(_fsActiveDriverTab, out var lines) || lines == null)
                return false;

            return ScheduleBuilderPreviewDrag.TryResolveInsertLineIndex(lines, item, below, out insertBeforeLine);
        }

        private bool FsCanInsertAtContext()
        {
            if (!_fsHasPreview || string.IsNullOrWhiteSpace(_fsActiveDriverTab))
                return false;
            if (_fsTripsCtxHitItem == null)
                return true;
            if (_fsTripsCtxHitItem.Tag is FsPreviewSectionHeaderTag sectionTag)
                return sectionTag.PreviewLineIndex >= 0;
            return ScheduleBuilderPreviewDrag.TryResolveInsertLineIndex(
                _fsLinesByTab.TryGetValue(_fsActiveDriverTab, out var lines) ? lines : null,
                _fsTripsCtxHitItem,
                below: false,
                out _);
        }

        private static Color? FsFindTripReserveBand(IList<ScheduleBuilderPreviewLine> lines, MCDownloadedTrip trip)
        {
            if (lines == null || trip == null) return null;
            foreach (var line in lines)
            {
                if (line?.Kind != ScheduleBuilderPreviewLine.LineKind.Trip || line.Trip == null)
                    continue;
                if (ReferenceEquals(line.Trip, trip)
                    || string.Equals(line.Trip.TripNumber, trip.TripNumber, StringComparison.OrdinalIgnoreCase))
                    return line.ReserveBandColor;
            }
            return null;
        }

        private void BuildFsCutTripBar(Panel host)
        {
            _fsCutTripBar = new Panel
            {
                Dock = DockStyle.Top,
                Height = 0,
                Visible = false,
                BackColor = SupeyTheme.SurfaceElevated,
                Padding = new Padding(0),
            };

            _fsCutTripBarAccent = new Panel
            {
                Dock = DockStyle.Left,
                Width = 3,
                BackColor = SupeyTheme.AccentPrimary,
            };

            var textHost = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = SupeyTheme.SurfaceElevated,
                Padding = new Padding(10, 6, 10, 6),
            };

            _fsCutTripBarLine1 = new Label
            {
                Dock = DockStyle.Top,
                Height = 20,
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI Semibold", 9.75f),
                ForeColor = SupeyTheme.TextPrimary,
                BackColor = SupeyTheme.SurfaceElevated,
            };

            _fsCutTripBarLine2 = new Label
            {
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 9f),
                ForeColor = SupeyTheme.TextSecondary,
                BackColor = SupeyTheme.SurfaceElevated,
            };

            textHost.Controls.Add(_fsCutTripBarLine2);
            textHost.Controls.Add(_fsCutTripBarLine1);

            _fsCutTripBar.Controls.Add(textHost);
            _fsCutTripBar.Controls.Add(_fsCutTripBarAccent);

            var bottomRule = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 1,
                BackColor = SupeyTheme.Divider,
            };
            _fsCutTripBar.Controls.Add(bottomRule);

            host.Controls.Add(_fsCutTripBar);
            SupeyDarkScrollBars.Apply(_fsCutTripBar);
            FsUpdateCutTripBar();
        }

        private void FsUpdateCutTripBar()
        {
            if (_fsCutTripBar == null)
                return;

            if (_fsCutTrips.Count == 0)
            {
                _fsCutTripBar.Visible = false;
                _fsCutTripBar.Height = 0;
                _fsCutTripBarLine1.Text = string.Empty;
                _fsCutTripBarLine2.Text = string.Empty;
                return;
            }

            if (_fsCutTrips.Count == 1)
            {
                _fsCutTripBarLine1.Text = FsFormatCutTripSummaryLine(_fsCutTrips[0].Trip);
                _fsCutTripBarLine2.Text = FsFormatCutTripAddressLine(_fsCutTrips[0].Trip);
            }
            else
            {
                _fsCutTripBarLine1.Text = "CUT  ·  " + _fsCutTrips.Count + " trips";
                _fsCutTripBarLine2.Text = FsFormatCutTripNumbersLine(_fsCutTrips);
            }

            _fsCutTripBar.Visible = true;
            _fsCutTripBar.Height = FsCutTripBarHeight;
        }

        /// <summary>Trip numbers on the clipboard, trimmed so a big cut still fits one line.</summary>
        private static string FsFormatCutTripNumbersLine(IList<ScheduleBuilderCutTrip> cut)
        {
            const int maxShown = 8;
            var nums = new List<string>();

            foreach (var entry in cut)
            {
                string num = (entry?.Trip?.TripNumber ?? "").Trim();
                nums.Add(num.Length > 0 ? num : "(no trip #)");
                if (nums.Count == maxShown)
                    break;
            }

            string text = string.Join("  ·  ", nums);
            int rest = cut.Count - nums.Count;
            return rest > 0 ? text + "  ·  +" + rest + " more" : text;
        }

        private static string FsFormatCutTripSummaryLine(MCDownloadedTrip trip)
        {
            if (trip == null)
                return "CUT · (trip)";

            var parts = new List<string> { "CUT" };

            string num = (trip.TripNumber ?? "").Trim();
            if (num.Length > 0)
                parts.Add(num);
            else
                parts.Add("(no trip #)");

            string leg = FsFormatCutTripLeg(trip);
            if (leg.Length > 0)
                parts.Add("Leg " + leg);

            string client = (trip.ClientFullName ?? "").Trim();
            if (client.Length == 0)
            {
                client = ((trip.ClientFirstName ?? "") + " " + (trip.ClientLastName ?? "")).Trim();
            }
            if (client.Length > 0)
                parts.Add(client);

            string pu = FormatTimeOnly(trip.PUTime);
            if (!string.IsNullOrWhiteSpace(pu))
                parts.Add("PU " + pu.Trim());

            string dot = FsFormatCutTripDoTime(trip);
            if (!string.IsNullOrWhiteSpace(dot))
                parts.Add("DO " + dot.Trim());

            return string.Join("  ·  ", parts);
        }

        private static string FsFormatCutTripAddressLine(MCDownloadedTrip trip)
        {
            if (trip == null)
                return string.Empty;

            string pu = FsJoinStreetCity(trip.PUStreet, trip.PUCity);
            string dot = FsJoinStreetCity(trip.DOStreet, trip.DOCITY);
            if (pu.Length == 0 && dot.Length == 0)
                return string.Empty;

            if (pu.Length > 0 && dot.Length > 0)
                return "PU: " + pu + "    →    DO: " + dot;
            if (pu.Length > 0)
                return "PU: " + pu;
            return "DO: " + dot;
        }

        private static string FsFormatCutTripLeg(MCDownloadedTrip trip)
        {
            string num = (trip?.TripNumber ?? "").Trim();
            if (num.Length == 0)
                return string.Empty;

            char leg = SupeyScheduleAlgorithm.DetectLegPublic(num);
            if (leg == '\0' || leg == '?')
                return string.Empty;

            return leg.ToString(CultureInfo.InvariantCulture).ToUpperInvariant();
        }

        private static string FsFormatCutTripDoTime(MCDownloadedTrip trip)
        {
            if (trip == null)
                return string.Empty;

            string t = FormatTimeOnly(trip.DOTime);
            if (string.IsNullOrWhiteSpace(t))
                t = FormatTimeOnly(trip.SchedDOTime);
            return t ?? string.Empty;
        }

        private static string FsJoinStreetCity(string street, string city)
        {
            string s = (street ?? "").Trim();
            string c = (city ?? "").Trim();
            if (s.Length > 0 && c.Length > 0)
                return s + ", " + c;
            if (s.Length > 0)
                return s;
            return c;
        }
    }
}
