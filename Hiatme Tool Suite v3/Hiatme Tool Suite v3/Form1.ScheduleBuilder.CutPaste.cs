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
        private MCDownloadedTrip _fsCutTrip;
        private Color? _fsCutTripReserveBand;
        private ListViewItem _fsTripsCtxHitItem;

        private bool FsHasCutTrip => _fsCutTrip != null;

        private void FsClearCutTrip()
        {
            _fsCutTrip = null;
            _fsCutTripReserveBand = null;
            _fsCutTripRerouted = false;
            FsUpdateCutTripBar();
        }

        private void FsCutSelectedTrip()
        {
            if (_fsTripsCtxTrip == null || string.IsNullOrWhiteSpace(_fsActiveDriverTab) || !_fsHasPreview)
                return;

            string tab = _fsActiveDriverTab;
            if (!_fsLinesByTab.TryGetValue(tab, out var lines) || lines == null)
                return;

            _fsCutTripReserveBand = FsFindTripReserveBand(lines, _fsTripsCtxTrip);
            _fsCutTripRerouted = ScheduleBuilderReroutedTrips.IsMarked(lines, _fsTripsCtxTrip);
            FsPushUndoSnapshot("cut trip");
            if (!ScheduleBuilderPreviewDrag.TryRemoveTrip(lines, _fsTripsCtxTrip))
                return;

            _fsCutTrip = _fsTripsCtxTrip;
            FsUpdateCutTripBar();
            FsCommitPreviewLinesForTab(tab, lines);
            ShowFsTripsForTab(tab);
            _ = RefreshFsMapForCurrentTabAsync();

            string num = (_fsCutTrip.TripNumber ?? "").Trim();
            SetScheduleBuilderStatus(string.IsNullOrEmpty(num)
                ? "Trip cut — right-click a row · Insert above or Insert below."
                : "Trip " + num + " cut — right-click a row · Insert above or Insert below.");
        }

        private void FsDeleteSelectedTrip()
        {
            if (_fsTripsCtxTrip == null || string.IsNullOrWhiteSpace(_fsActiveDriverTab) || !_fsHasPreview)
                return;

            string tab = _fsActiveDriverTab;
            if (!_fsLinesByTab.TryGetValue(tab, out var lines) || lines == null)
                return;

            MCDownloadedTrip trip = _fsTripsCtxTrip;
            string num = (trip.TripNumber ?? "").Trim();

            FsPushUndoSnapshot("delete trip");
            if (!ScheduleBuilderPreviewDrag.TryRemoveTrip(lines, trip))
            {
                SetScheduleBuilderStatus("Could not delete trip.");
                return;
            }

            if (FsHasCutTrip
                && (_fsCutTrip != null)
                && (ReferenceEquals(_fsCutTrip, trip)
                    || ScheduleBuilderPreviewDrag.TripEquals(_fsCutTrip, trip)))
            {
                FsClearCutTrip();
            }

            FsCommitPreviewLinesForTab(tab, lines);
            ShowFsTripsForTab(tab);
            SyncFsPreviewCsvsForExport();
            _ = RefreshFsMapForCurrentTabAsync();

            SetScheduleBuilderStatus(string.IsNullOrEmpty(num)
                ? "Trip deleted from schedule."
                : "Trip " + num + " deleted from schedule.");
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
            if (_fsCutTrip == null || string.IsNullOrWhiteSpace(_fsActiveDriverTab) || !_fsHasPreview)
                return;

            if (!TryResolveFsInsertBeforeLine(_fsTripsCtxHitItem, below, out int insertBeforeLine))
            {
                SetScheduleBuilderStatus("Could not insert here — right-click a trip, gap, or group row.");
                return;
            }

            string tab = _fsActiveDriverTab;
            MCDownloadedTrip trip = _fsCutTrip;
            Color? cutReserveBand = _fsCutTripReserveBand;
            bool cutRerouted = _fsCutTripRerouted;
            FsClearCutTrip();

            _fsPreserveRouteChangeBaseline = true;
            FsSnapshotPreMoveGroupMeters(tab, trip, merge: false, mergeTargetTrip: null);

            if (!_fsLinesByTab.TryGetValue(tab, out var lines) || lines == null)
                return;

            Color? reserveBand = null;
            if (tab.Equals("Reserves", StringComparison.OrdinalIgnoreCase))
            {
                reserveBand = cutReserveBand
                    ?? ScheduleBuilderPreviewDrag.ResolveReserveBandForInsert(lines, insertBeforeLine);
            }

            FsPushUndoSnapshot("insert trip");
            ScheduleBuilderPreviewDrag.InsertTripLine(lines, trip, insertBeforeLine, reserveBand, cutRerouted);

            FsCommitPreviewLinesForTab(tab, lines);
            ShowFsTripsForTab(tab);
            SelectFsTripInListView(trip);
            _ = FsRefreshAfterTripMoveAsync();

            string num = (trip.TripNumber ?? "").Trim();
            SetScheduleBuilderStatus(string.IsNullOrEmpty(num)
                ? "Trip inserted — map updating…"
                : "Trip " + num + " inserted — map updating…");
        }

        private void FsInsertBlankRow(bool below)
        {
            if (FsHasCutTrip || string.IsNullOrWhiteSpace(_fsActiveDriverTab) || !_fsHasPreview)
                return;

            if (_fsActiveDriverTab.Equals("Reserves", StringComparison.OrdinalIgnoreCase))
                return;

            if (!TryResolveFsInsertBeforeLine(_fsTripsCtxHitItem, below, out int insertBeforeLine))
            {
                SetScheduleBuilderStatus("Could not insert here — right-click a trip, gap, or group row.");
                return;
            }

            string tab = _fsActiveDriverTab;
            if (!_fsLinesByTab.TryGetValue(tab, out var lines) || lines == null)
                return;

            FsRevealGapsForManualInsert();
            FsPushUndoSnapshot("insert blank row");
            ScheduleBuilderPreviewDrag.InsertGapLine(lines, insertBeforeLine);
            // Keep consecutive spacers — collapse would undo insert-from-gap immediately.
            FsCommitPreviewLinesForTab(tab, lines);
            ShowFsTripsForTab(tab);
            SyncFsPreviewCsvsForExport();
            _ = RefreshFsMapForCurrentTabAsync();

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
            _ = RefreshFsMapForCurrentTabAsync();
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
            _ = RefreshFsMapForCurrentTabAsync();
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
            _ = RefreshFsMapForCurrentTabAsync();
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

            if (_fsCutTrip == null)
            {
                _fsCutTripBar.Visible = false;
                _fsCutTripBar.Height = 0;
                _fsCutTripBarLine1.Text = string.Empty;
                _fsCutTripBarLine2.Text = string.Empty;
                return;
            }

            _fsCutTripBarLine1.Text = FsFormatCutTripSummaryLine(_fsCutTrip);
            _fsCutTripBarLine2.Text = FsFormatCutTripAddressLine(_fsCutTrip);
            _fsCutTripBar.Visible = true;
            _fsCutTripBar.Height = FsCutTripBarHeight;
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
