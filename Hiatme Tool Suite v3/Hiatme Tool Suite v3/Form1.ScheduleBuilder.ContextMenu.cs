using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    public partial class Form1
    {
        private ContextMenuStrip _fsTripsCtxMenu;
        private ToolStripMenuItem _fsTripsCtxBanClient;
        private ToolStripMenuItem _fsTripsCtxUnbanClient;
        private ToolStripMenuItem _fsTripsCtxFocusMap;
        private ToolStripMenuItem _fsTripsCtxCopyForAi;
        private ToolStripMenuItem _fsTripsCtxCopyCurrentTab;
        private ToolStripMenuItem _fsTripsCtxCopySelectedTrip;
        private ToolStripMenuItem _fsTripsCtxAutoSortGroup;
        private ToolStripMenuItem _fsTripsCtxGeocodeDriverHome;
        private ToolStripMenuItem _fsTripsCtxEmailDriver;
        private ToolStripMenuItem _fsTripsCtxSuggestDriver;
        private ToolStripMenuItem _fsTripsCtxCutTrip;
        private ToolStripMenuItem _fsTripsCtxDelete;
        private ToolStripMenuItem _fsTripsCtxUndo;
        private ToolStripMenuItem _fsTripsCtxRedo;
        private ToolStripMenuItem _fsTripsCtxPasteTrip;
        private ToolStripMenuItem _fsTripsCtxInsertTripAbove;
        private ToolStripMenuItem _fsTripsCtxInsertTripBelow;
        private ToolStripMenuItem _fsTripsCtxClearCut;
        private ToolStripMenuItem _fsTripsCtxAddRowMenu;
        private ToolStripMenuItem _fsTripsCtxAddRowAbove;
        private ToolStripMenuItem _fsTripsCtxAddRowBelow;
        private ToolStripMenuItem _fsTripsCtxAddNoteMenu;
        private ToolStripMenuItem _fsTripsCtxAddNoteToRow;
        private ToolStripMenuItem _fsTripsCtxAddNoteAbove;
        private ToolStripMenuItem _fsTripsCtxAddNoteBelow;
        private ToolStripMenuItem _fsTripsCtxEditNote;
        private ToolStripMenuItem _fsTripsCtxChangeGroupColor;
        private ToolStripMenuItem _fsTripsCtxResetGroupColor;
        private ToolStripMenuItem _fsTripsCtxRerouteModivcare;
        private ToolStripMenuItem _fsTripsCtxAddToReroutes;
        private ToolStripMenuItem _fsTripsCtxAddToCancels;
        private MCDownloadedTrip _fsTripsCtxTrip;
        private SupeyTripCluster _fsTripsCtxGroup;
        private FsPreviewNoteTag _fsTripsCtxNoteTag;

        private enum FsNotePlacement
        {
            ToRow,
            Above,
            Below,
        }

        private enum FsRowPlacement
        {
            Above,
            Below,
        }

        private static ToolStripMenuItem CreateFsTripsCtxItem(string text)
        {
            return new ToolStripMenuItem(text)
            {
                BackColor = DarkContextMenuRenderer.Background,
                ForeColor = DarkContextMenuRenderer.ForeColor,
            };
        }

        private void BuildFsTripsContextMenu()
        {
            _fsTripsCtxMenu = new ContextMenuStrip
            {
                Renderer = new DarkContextMenuRenderer(),
                BackColor = DarkContextMenuRenderer.Background,
                ForeColor = DarkContextMenuRenderer.ForeColor,
                ShowImageMargin = true,
            };

            _fsTripsCtxBanClient = new ToolStripMenuItem("Ban client")
            {
                BackColor = DarkContextMenuRenderer.Background,
                ForeColor = DarkContextMenuRenderer.ForeColor,
                Image = MenuIconFactory.GetUnassignIcon(),
            };
            _fsTripsCtxBanClient.Click += (s, e) =>
            {
                if (_fsTripsCtxTrip != null)
                    FsBanClientFromTrip(_fsTripsCtxTrip, quietWhenMissing: true);
            };

            _fsTripsCtxUnbanClient = new ToolStripMenuItem("Remove from banned list")
            {
                BackColor = DarkContextMenuRenderer.Background,
                ForeColor = DarkContextMenuRenderer.ForeColor,
                Image = MenuIconFactory.GetAssignIcon(),
            };
            _fsTripsCtxUnbanClient.Click += (s, e) =>
            {
                if (_fsTripsCtxTrip != null)
                    FsUnbanClientFromTrip(_fsTripsCtxTrip);
            };

            _fsTripsCtxFocusMap = new ToolStripMenuItem("Show on map")
            {
                BackColor = DarkContextMenuRenderer.Background,
                ForeColor = DarkContextMenuRenderer.ForeColor,
                Image = MenuIconFactory.GetLocateIcon(),
            };
            _fsTripsCtxFocusMap.Click += (s, e) =>
            {
                if (_fsTripsCtxTrip != null && _fsMap != null && _fsMap.Visible)
                    _fsMap.FocusTrip(_fsTripsCtxTrip);
            };

            _fsTripsCtxCopyForAi = new ToolStripMenuItem("Copy for AI review (Cursor)")
            {
                BackColor = DarkContextMenuRenderer.Background,
                ForeColor = DarkContextMenuRenderer.ForeColor,
            };
            _fsTripsCtxCopyForAi.Click += (s, e) => FsCopyScheduleForAiReviewToClipboard();

            _fsTripsCtxCopyCurrentTab = new ToolStripMenuItem("Copy current tab")
            {
                BackColor = DarkContextMenuRenderer.Background,
                ForeColor = DarkContextMenuRenderer.ForeColor,
            };
            _fsTripsCtxCopyCurrentTab.Click += (s, e) => FsCopyCurrentTabToClipboard();

            _fsTripsCtxCopySelectedTrip = new ToolStripMenuItem("Copy selected trip")
            {
                BackColor = DarkContextMenuRenderer.Background,
                ForeColor = DarkContextMenuRenderer.ForeColor,
            };
            _fsTripsCtxCopySelectedTrip.Click += (s, e) => FsCopySelectedTripToClipboard();

            _fsTripsCtxAutoSortGroup = new ToolStripMenuItem("Auto-sort group for best route efficiency")
            {
                BackColor = DarkContextMenuRenderer.Background,
                ForeColor = DarkContextMenuRenderer.ForeColor,
            };
            _fsTripsCtxAutoSortGroup.Click += (s, e) =>
            {
                if (_fsTripsCtxGroup != null)
                    _ = FsAutoSortGroupForEfficiencyAsync(_fsTripsCtxGroup);
            };

            _fsTripsCtxGeocodeDriverHome = new ToolStripMenuItem("Geocode driver home")
            {
                BackColor = DarkContextMenuRenderer.Background,
                ForeColor = DarkContextMenuRenderer.ForeColor,
            };
            _fsTripsCtxGeocodeDriverHome.Click += (s, e) =>
            {
                _ = FsGeocodeActiveTabDriverHomeAsync();
            };

            _fsTripsCtxEmailDriver = new ToolStripMenuItem("Email schedule to driver…")
            {
                BackColor = DarkContextMenuRenderer.Background,
                ForeColor = DarkContextMenuRenderer.ForeColor,
            };
            _fsTripsCtxEmailDriver.Click += (s, e) => _ = FsEmailActiveDriverFromContextAsync();

            _fsTripsCtxSuggestDriver = new ToolStripMenuItem("Suggest driver for trip…")
            {
                BackColor = DarkContextMenuRenderer.Background,
                ForeColor = DarkContextMenuRenderer.ForeColor,
            };
            _fsTripsCtxSuggestDriver.Click += (s, e) => _ = FsSuggestDriverForTripAsync();

            _fsTripsCtxCutTrip = new ToolStripMenuItem("Cut trip")
            {
                BackColor = DarkContextMenuRenderer.Background,
                ForeColor = DarkContextMenuRenderer.ForeColor,
                ShortcutKeys = Keys.Control | Keys.X,
                ShowShortcutKeys = true,
            };
            _fsTripsCtxCutTrip.Click += (s, e) => FsCutSelectedTrip();

            _fsTripsCtxDelete = new ToolStripMenuItem("Delete")
            {
                BackColor = DarkContextMenuRenderer.Background,
                ForeColor = DarkContextMenuRenderer.ForeColor,
                ShortcutKeys = Keys.Delete,
                ShowShortcutKeys = true,
            };
            _fsTripsCtxDelete.Click += (s, e) => FsDeleteSelection();

            _fsTripsCtxUndo = new ToolStripMenuItem("Undo")
            {
                BackColor = DarkContextMenuRenderer.Background,
                ForeColor = DarkContextMenuRenderer.ForeColor,
                ShortcutKeys = Keys.Control | Keys.Z,
                ShowShortcutKeys = true,
            };
            _fsTripsCtxUndo.Click += (s, e) => FsUndoScheduleEdit();

            _fsTripsCtxRedo = new ToolStripMenuItem("Redo")
            {
                BackColor = DarkContextMenuRenderer.Background,
                ForeColor = DarkContextMenuRenderer.ForeColor,
                ShortcutKeys = Keys.Control | Keys.Y,
                ShowShortcutKeys = true,
            };
            _fsTripsCtxRedo.Click += (s, e) => FsRedoScheduleEdit();

            _fsTripsCtxPasteTrip = new ToolStripMenuItem("Paste trip")
            {
                BackColor = DarkContextMenuRenderer.Background,
                ForeColor = DarkContextMenuRenderer.ForeColor,
                ShortcutKeys = Keys.Control | Keys.V,
                ShowShortcutKeys = true,
            };
            _fsTripsCtxPasteTrip.Click += (s, e) => FsInsertCutTrip(below: false);

            _fsTripsCtxInsertTripAbove = new ToolStripMenuItem("Insert trip above")
            {
                BackColor = DarkContextMenuRenderer.Background,
                ForeColor = DarkContextMenuRenderer.ForeColor,
            };
            _fsTripsCtxInsertTripAbove.Click += (s, e) => FsInsertCutTrip(below: false);

            _fsTripsCtxInsertTripBelow = new ToolStripMenuItem("Insert trip below")
            {
                BackColor = DarkContextMenuRenderer.Background,
                ForeColor = DarkContextMenuRenderer.ForeColor,
            };
            _fsTripsCtxInsertTripBelow.Click += (s, e) => FsInsertCutTrip(below: true);

            _fsTripsCtxClearCut = new ToolStripMenuItem("Clear cut trip")
            {
                BackColor = DarkContextMenuRenderer.Background,
                ForeColor = DarkContextMenuRenderer.ForeColor,
            };
            _fsTripsCtxClearCut.Click += (s, e) =>
            {
                FsClearCutTrip();
                SetScheduleBuilderStatus("Cut trip cleared.");
            };

            _fsTripsCtxEditNote = CreateFsTripsCtxItem("Edit note…");
            _fsTripsCtxEditNote.Click += (s, e) => FsEditNoteFromContext();

            _fsTripsCtxAddNoteToRow = CreateFsTripsCtxItem("Add note to row…");
            _fsTripsCtxAddNoteToRow.Click += (s, e) => FsAddNoteFromContext(FsNotePlacement.ToRow);

            _fsTripsCtxAddNoteAbove = CreateFsTripsCtxItem("Add note above…");
            _fsTripsCtxAddNoteAbove.Click += (s, e) => FsAddNoteFromContext(FsNotePlacement.Above);

            _fsTripsCtxAddNoteBelow = CreateFsTripsCtxItem("Add note below…");
            _fsTripsCtxAddNoteBelow.Click += (s, e) => FsAddNoteFromContext(FsNotePlacement.Below);

            _fsTripsCtxAddNoteMenu = CreateFsTripsCtxItem("Add note");
            _fsTripsCtxAddNoteMenu.DropDownItems.Add(_fsTripsCtxAddNoteToRow);
            _fsTripsCtxAddNoteMenu.DropDownItems.Add(_fsTripsCtxAddNoteAbove);
            _fsTripsCtxAddNoteMenu.DropDownItems.Add(_fsTripsCtxAddNoteBelow);

            _fsTripsCtxAddRowAbove = CreateFsTripsCtxItem("Add row above…");
            _fsTripsCtxAddRowAbove.Click += (s, e) => FsAddRowFromContext(FsRowPlacement.Above);

            _fsTripsCtxAddRowBelow = CreateFsTripsCtxItem("Add row below…");
            _fsTripsCtxAddRowBelow.Click += (s, e) => FsAddRowFromContext(FsRowPlacement.Below);

            _fsTripsCtxAddRowMenu = CreateFsTripsCtxItem("Add row");
            _fsTripsCtxAddRowMenu.DropDownItems.Add(_fsTripsCtxAddRowAbove);
            _fsTripsCtxAddRowMenu.DropDownItems.Add(_fsTripsCtxAddRowBelow);

            _fsTripsCtxChangeGroupColor = new ToolStripMenuItem("Change group color")
            {
                BackColor = DarkContextMenuRenderer.Background,
                ForeColor = DarkContextMenuRenderer.ForeColor,
            };
            _fsTripsCtxChangeGroupColor.Click += (s, e) => FsChangeGroupColorFromContext();

            _fsTripsCtxResetGroupColor = new ToolStripMenuItem("Reset group color to default")
            {
                BackColor = DarkContextMenuRenderer.Background,
                ForeColor = DarkContextMenuRenderer.ForeColor,
            };
            _fsTripsCtxResetGroupColor.Click += (s, e) => FsResetGroupColorFromContext();

            _fsTripsCtxRerouteModivcare = new ToolStripMenuItem("Reroute on Modivcare…")
            {
                BackColor = DarkContextMenuRenderer.Background,
                ForeColor = DarkContextMenuRenderer.ForeColor,
            };
            _fsTripsCtxRerouteModivcare.Click += (s, e) => FsRerouteTripOnModivcareFromContext();

            _fsTripsCtxAddToReroutes = new ToolStripMenuItem("Move to Reroutes section")
            {
                BackColor = DarkContextMenuRenderer.Background,
                ForeColor = DarkContextMenuRenderer.ForeColor,
            };
            _fsTripsCtxAddToReroutes.Click += (s, e) => FsAddTripToReroutesSectionFromContext();

            _fsTripsCtxAddToCancels = new ToolStripMenuItem("Add to Cancels section (no Modivcare)")
            {
                BackColor = DarkContextMenuRenderer.Background,
                ForeColor = DarkContextMenuRenderer.ForeColor,
            };
            _fsTripsCtxAddToCancels.Click += (s, e) => FsAddTripToCancelsSectionFromContext();

            _fsTripsCtxMenu.Items.Add(_fsTripsCtxBanClient);
            _fsTripsCtxMenu.Items.Add(_fsTripsCtxUnbanClient);
            _fsTripsCtxMenu.Items.Add(_fsTripsCtxRerouteModivcare);
            _fsTripsCtxMenu.Items.Add(_fsTripsCtxAddToReroutes);
            _fsTripsCtxMenu.Items.Add(_fsTripsCtxAddToCancels);
            // Suggest driver hidden for now — kept in the menu so it can be re-enabled later.
            _fsTripsCtxSuggestDriver.Visible = false;
            _fsTripsCtxMenu.Items.Add(_fsTripsCtxSuggestDriver);
            _fsTripsCtxMenu.Items.Add(new ToolStripSeparator());
            _fsTripsCtxMenu.Items.Add(_fsTripsCtxCutTrip);
            _fsTripsCtxMenu.Items.Add(_fsTripsCtxDelete);
            _fsTripsCtxMenu.Items.Add(_fsTripsCtxPasteTrip);
            _fsTripsCtxMenu.Items.Add(_fsTripsCtxUndo);
            _fsTripsCtxMenu.Items.Add(_fsTripsCtxRedo);
            _fsTripsCtxMenu.Items.Add(_fsTripsCtxInsertTripAbove);
            _fsTripsCtxMenu.Items.Add(_fsTripsCtxInsertTripBelow);
            _fsTripsCtxMenu.Items.Add(_fsTripsCtxAddRowMenu);
            _fsTripsCtxMenu.Items.Add(_fsTripsCtxClearCut);
            _fsTripsCtxMenu.Items.Add(new ToolStripSeparator());
            _fsTripsCtxMenu.Items.Add(_fsTripsCtxAddNoteMenu);
            _fsTripsCtxMenu.Items.Add(_fsTripsCtxEditNote);
            _fsTripsCtxMenu.Items.Add(_fsTripsCtxChangeGroupColor);
            _fsTripsCtxMenu.Items.Add(_fsTripsCtxResetGroupColor);
            _fsTripsCtxMenu.Items.Add(_fsTripsCtxAutoSortGroup);
            _fsTripsCtxMenu.Items.Add(_fsTripsCtxGeocodeDriverHome);
            _fsTripsCtxMenu.Items.Add(_fsTripsCtxEmailDriver);
            _fsTripsCtxMenu.Items.Add(new ToolStripSeparator());
            _fsTripsCtxMenu.Items.Add(_fsTripsCtxFocusMap);

            // Copy options hidden for now — kept in the menu so they can be re-enabled later.
            var copySeparator = new ToolStripSeparator { Visible = false };
            _fsTripsCtxCopyForAi.Visible = false;
            _fsTripsCtxCopyCurrentTab.Visible = false;
            _fsTripsCtxCopySelectedTrip.Visible = false;
            _fsTripsCtxMenu.Items.Add(copySeparator);
            _fsTripsCtxMenu.Items.Add(_fsTripsCtxCopyForAi);
            _fsTripsCtxMenu.Items.Add(_fsTripsCtxCopyCurrentTab);
            _fsTripsCtxMenu.Items.Add(_fsTripsCtxCopySelectedTrip);
        }

        private async void FsTripsLv_MouseUp_ShowContextMenu(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right || _fsTripsLv == null) return;

            try
            {
                await ScheduleOsrmGate.ProbePreviewServicesAsync(
                    HiatmeAiSettings.Load(), CancellationToken.None).ConfigureAwait(true);
            }
            catch { }

            var hit = _fsTripsLv.HitTest(e.Location);
            _fsTripsCtxHitItem = hit.Item;
            if (_fsTripsCtxHitItem == null && _fsTripsLv.SelectedItems.Count > 0)
                _fsTripsCtxHitItem = _fsTripsLv.SelectedItems[0];

            FsBindContextTagsFromHitItem(_fsTripsCtxHitItem);
            if (_fsTripsCtxHitItem != null)
            {
                _fsTripsCtxHitItem.Selected = true;
                _fsTripsCtxHitItem.Focused = true;
            }

            bool hasTrip = _fsTripsCtxTrip != null;
            bool isBanned = hasTrip && ScheduleBuilderBannedClients.IsBanned(_fsTripsCtxTrip);
            bool isReserves = _fsActiveDriverTab != null
                && _fsActiveDriverTab.Equals("Reserves", StringComparison.OrdinalIgnoreCase);
            bool canSortGroup = _fsHasPreview
                && !isReserves
                && _fsTripsCtxGroup != null
                && _fsTripsCtxGroup.Trips != null
                && _fsTripsCtxGroup.Trips.Count >= 2;

            bool hasBuild = _fsHasPreview && fsbuilder != null;
            bool canMoveTrips = hasBuild && !string.IsNullOrWhiteSpace(_fsActiveDriverTab);

            EnsureFsDriverRosterLoaded();
            var activeDriverProfile = ScheduleBuilderDriverMapRouting.FindProfileForScheduleTab(
                _supeyRoster, _fsActiveDriverTab);
            bool hasHomeAddress = activeDriverProfile != null
                && (!string.IsNullOrWhiteSpace(activeDriverProfile.HomeStreet)
                    || !string.IsNullOrWhiteSpace(activeDriverProfile.HomeCity));
            bool canGeocodeDriverHome = !isReserves && hasHomeAddress && ScheduleOsrmGate.PreviewGeoOk;
            bool canInsertAt = canMoveTrips && FsCanInsertAtContext();
            bool canInsertBlank = canInsertAt && !isReserves;
            bool canInsertCut = canInsertAt && FsHasCutTrip;

            _fsTripsCtxCutTrip.Enabled = hasTrip && canMoveTrips && !FsHasCutTrip;
            bool canDeleteTrip = hasTrip && canMoveTrips;
            bool canDeleteGap = canMoveTrips && !isReserves
                && _fsTripsCtxHitItem?.Tag is FsPreviewGapTag gapCtx
                && !gapCtx.TrailingPad
                && !ScheduleBuilderGapNotes.GapTagHasNoteBar(gapCtx);
            _fsLinesByTab.TryGetValue(_fsActiveDriverTab ?? "", out var deleteLines);
            bool canDeleteNote = canMoveTrips && !isReserves
                && (ScheduleBuilderGroupNotes.IsDeletableNoteRow(_fsTripsCtxHitItem, deleteLines)
                    || (_fsTripsCtxHitItem?.Tag is FsPreviewGapTag gapDel
                        && ScheduleBuilderGapNotes.GapTagHasNoteBar(gapDel)));
            _fsTripsCtxDelete.Enabled = canDeleteTrip || canDeleteGap || canDeleteNote;
            if (canDeleteTrip)
            {
                string num = (_fsTripsCtxTrip.TripNumber ?? "").Trim();
                _fsTripsCtxDelete.Text = string.IsNullOrEmpty(num) ? "Delete trip" : "Delete trip " + num;
            }
            else if (canDeleteNote)
                _fsTripsCtxDelete.Text = "Delete note";
            else if (canDeleteGap)
                _fsTripsCtxDelete.Text = "Delete blank row";
            else
                _fsTripsCtxDelete.Text = "Delete";
            _fsTripsCtxPasteTrip.Enabled = canInsertCut;
            _fsTripsCtxPasteTrip.Text = "Paste trip" + FsCutTripMenuSuffix();
            _fsTripsCtxUndo.Enabled = _fsUndoStack.CanUndo;
            _fsTripsCtxUndo.Text = FsUndoMenuText();
            _fsTripsCtxRedo.Enabled = _fsUndoStack.CanRedo;
            _fsTripsCtxRedo.Text = FsRedoMenuText();
            _fsTripsCtxAddRowAbove.Enabled = canInsertBlank;
            _fsTripsCtxAddRowBelow.Enabled = canInsertBlank;
            _fsTripsCtxAddRowMenu.Enabled = canInsertBlank;
            _fsTripsCtxInsertTripAbove.Visible = FsHasCutTrip;
            _fsTripsCtxInsertTripBelow.Visible = FsHasCutTrip;
            _fsTripsCtxInsertTripAbove.Enabled = canInsertCut;
            _fsTripsCtxInsertTripBelow.Enabled = canInsertCut;
            _fsTripsCtxInsertTripAbove.Text = "Insert trip above" + FsCutTripMenuSuffix();
            _fsTripsCtxInsertTripBelow.Text = "Insert trip below" + FsCutTripMenuSuffix();
            _fsTripsCtxClearCut.Enabled = FsHasCutTrip;

            _fsTripsCtxBanClient.Enabled = hasTrip;
            _fsTripsCtxUnbanClient.Enabled = hasTrip && isBanned;
            bool alreadyRerouted = hasTrip
                && _fsLinesByTab.TryGetValue(_fsActiveDriverTab ?? "", out var rerouteLines)
                && rerouteLines != null
                && ScheduleBuilderReroutedTrips.IsMarked(rerouteLines, _fsTripsCtxTrip);
            _fsTripsCtxRerouteModivcare.Enabled = hasTrip && hasBuild && !alreadyRerouted;
            if (hasTrip && alreadyRerouted)
                _fsTripsCtxRerouteModivcare.Text = "Reroute on Modivcare (already marked)";
            else
                _fsTripsCtxRerouteModivcare.Text = "Reroute on Modivcare…";

            _fsLinesByTab.TryGetValue("Reserves", out var reserveLinesForCtx);
            bool alreadyInReroutes = hasTrip && hasBuild
                && !FsNeedsMoveToReservesReroutes(
                    _fsTripsCtxTrip, _fsActiveDriverTab ?? "", fsbuilder, reserveLinesForCtx);
            _fsTripsCtxAddToReroutes.Enabled = hasTrip && hasBuild && !alreadyInReroutes;
            if (alreadyInReroutes)
                _fsTripsCtxAddToReroutes.Text = "Move to Reroutes section (already there)";
            else if (alreadyRerouted)
                _fsTripsCtxAddToReroutes.Text = "Move to Reroutes section";
            else
                _fsTripsCtxAddToReroutes.Text = "Add to Reroutes section";

            bool alreadyInCancels = hasTrip && hasBuild
                && !FsNeedsMoveToReservesCancels(_fsTripsCtxTrip, _fsActiveDriverTab ?? "", fsbuilder);
            _fsTripsCtxAddToCancels.Enabled = hasTrip && hasBuild && !alreadyInCancels;
            _fsTripsCtxAddToCancels.Text = alreadyInCancels
                ? "Add to Cancels section (already there)"
                : "Add to Cancels section (no Modivcare)";

            bool canSuggestDriver = hasTrip && hasBuild && ScheduleOsrmGate.PreviewRoutingOk;
            _fsTripsCtxSuggestDriver.Enabled = canSuggestDriver;
            if (canSuggestDriver && isReserves)
                _fsTripsCtxSuggestDriver.Text = "Suggest driver for reserve trip…";
            else if (canSuggestDriver)
                _fsTripsCtxSuggestDriver.Text = "Suggest driver for trip…";
            else
                _fsTripsCtxSuggestDriver.Text = "Suggest driver for trip (routing offline)";

            _fsTripsCtxFocusMap.Enabled = hasTrip
                && _fsMap != null
                && _fsMap.Visible
                && ScheduleOsrmGate.PreviewRoutingOk;
            _fsTripsCtxAutoSortGroup.Enabled = canSortGroup && ScheduleOsrmGate.PreviewRoutingOk;
            _fsTripsCtxGeocodeDriverHome.Enabled = canGeocodeDriverHome;
            _fsTripsCtxCopyForAi.Enabled = hasBuild;
            _fsTripsCtxCopyCurrentTab.Enabled = hasBuild;
            _fsTripsCtxCopySelectedTrip.Enabled = hasTrip;
            bool canAddNoteToRow = FsCanAddNoteToRowContext();
            bool canAddNoteAbove = canMoveTrips && !isReserves && canInsertAt;
            bool canAddNoteBelow = canAddNoteAbove;
            _fsTripsCtxAddNoteToRow.Enabled = canAddNoteToRow;
            _fsTripsCtxAddNoteAbove.Enabled = canAddNoteAbove;
            _fsTripsCtxAddNoteBelow.Enabled = canAddNoteBelow;
            _fsTripsCtxAddNoteMenu.Enabled = canAddNoteToRow || canAddNoteAbove;
            var contextItem = FsResolveContextListItem();
            bool canEditNote = canMoveTrips && !isReserves
                && ScheduleBuilderGapNotes.IsEditableNoteRow(
                    contextItem, deleteLines, out _);
            _fsTripsCtxEditNote.Enabled = canEditNote;

            bool canChangeGroupColor = _fsTripsCtxGroup != null
                && !isReserves
                && FsShowGroupColorsEnabled
                && hasBuild;
            _fsTripsCtxChangeGroupColor.Enabled = canChangeGroupColor;
            bool hasColorOverride = canChangeGroupColor
                && _fsLinesByTab.TryGetValue(_fsActiveDriverTab ?? "", out var colorLines)
                && colorLines != null
                && ScheduleBuilderGroupColors.GetOverride(colorLines, _fsTripsCtxGroup.GroupNumber).HasValue;
            _fsTripsCtxResetGroupColor.Enabled = hasColorOverride;
            if (canChangeGroupColor)
            {
                _fsTripsCtxChangeGroupColor.Text = "Change group color — group "
                    + _fsTripsCtxGroup.GroupNumber;
                _fsTripsCtxResetGroupColor.Text = "Reset group color — group "
                    + _fsTripsCtxGroup.GroupNumber;
            }
            else
            {
                _fsTripsCtxChangeGroupColor.Text = "Change group color";
                _fsTripsCtxResetGroupColor.Text = "Reset group color to default";
            }

            if (canSortGroup)
            {
                _fsTripsCtxAutoSortGroup.Text = ScheduleOsrmGate.PreviewRoutingOk
                    ? "Auto-sort group "
                        + _fsTripsCtxGroup.GroupNumber + " for best route efficiency (100%)"
                    : "Auto-sort group (road routing offline)";
            }
            else
            {
                _fsTripsCtxAutoSortGroup.Text = "Auto-sort group for best route efficiency (100%)";
            }

            if (canGeocodeDriverHome && activeDriverProfile != null)
            {
                _fsTripsCtxGeocodeDriverHome.Text = "Geocode driver home — "
                    + (activeDriverProfile.Name ?? _fsActiveDriverTab ?? "driver").Trim();
            }
            else if (!ScheduleOsrmGate.PreviewGeoOk && hasHomeAddress && !isReserves)
            {
                _fsTripsCtxGeocodeDriverHome.Text = "Geocode driver home (office server offline)";
            }
            else
            {
                _fsTripsCtxGeocodeDriverHome.Text = "Geocode driver home";
            }

            bool driverHasEmail = activeDriverProfile != null
                && !string.IsNullOrWhiteSpace(activeDriverProfile.Email);
            bool canEmailDriver = hasBuild && !isReserves && driverHasEmail;
            _fsTripsCtxEmailDriver.Enabled = canEmailDriver;
            if (canEmailDriver && activeDriverProfile != null)
            {
                _fsTripsCtxEmailDriver.Text = "Email schedule to driver — "
                    + (activeDriverProfile.Name ?? _fsActiveDriverTab ?? "driver").Trim();
            }
            else if (hasBuild && !isReserves && activeDriverProfile != null)
            {
                _fsTripsCtxEmailDriver.Text = "Email schedule to driver (no email on roster)";
            }
            else
            {
                _fsTripsCtxEmailDriver.Text = "Email schedule to driver…";
            }

            if (hasTrip)
            {
                string name = (_fsTripsCtxTrip.ClientFullName ?? "").Trim();
                string age = string.IsNullOrWhiteSpace(_fsTripsCtxTrip.Age) ? "" : " · age " + _fsTripsCtxTrip.Age;
                _fsTripsCtxBanClient.Text = isBanned
                    ? "Ban client (already banned)"
                    : "Ban client — " + name + age;
                _fsTripsCtxUnbanClient.Text = "Remove ban — " + name + age;
            }
            else
            {
                _fsTripsCtxBanClient.Text = "Ban client";
                _fsTripsCtxUnbanClient.Text = "Remove from banned list";
            }

            _fsTripsCtxMenu.Show(_fsTripsLv, e.Location);
        }

        private string FsCutTripMenuSuffix()
        {
            if (_fsCutTrip == null) return "";
            string num = (_fsCutTrip.TripNumber ?? "").Trim();
            return string.IsNullOrEmpty(num) ? "" : " (" + num + ")";
        }

        private void FsCopyScheduleForAiReviewToClipboard()
        {
            if (!_fsHasPreview || fsbuilder == null)
            {
                MessageBox.Show(this, "Run BUILD first, then copy for AI review.", "Schedule Builder",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            try
            {
                DateTime date = fsbdatepicker?.Value ?? DateTime.Today;
                string folder = date.DayOfWeek.ToString();
                string text = ScheduleBuilderReviewExport.BuildFull(
                    date, folder, fsbuilder, _fsLinesByTab, _fsActiveDriverTab);
                Clipboard.SetText(text);
                SetScheduleBuilderStatus("Copied full schedule for AI review — paste into Cursor chat.");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not copy to clipboard:\n\n" + ex.Message, "Schedule Builder",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void FsCopyCurrentTabToClipboard()
        {
            if (!_fsHasPreview || fsbuilder == null || string.IsNullOrWhiteSpace(_fsActiveDriverTab))
            {
                MessageBox.Show(this, "Run BUILD and select a driver or Reserves tab first.", "Schedule Builder",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (!_fsLinesByTab.TryGetValue(_fsActiveDriverTab, out var lines))
                lines = new System.Collections.Generic.List<ScheduleBuilderPreviewLine>();
            try
            {
                DateTime date = fsbdatepicker?.Value ?? DateTime.Today;
                string text = ScheduleBuilderReviewExport.BuildTab(date, _fsActiveDriverTab, lines, fsbuilder);
                Clipboard.SetText(text);
                SetScheduleBuilderStatus("Copied \"" + _fsActiveDriverTab + "\" tab to clipboard.");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not copy to clipboard:\n\n" + ex.Message, "Schedule Builder",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void FsCopySelectedTripToClipboard()
        {
            if (_fsTripsCtxTrip == null) return;
            try
            {
                string text = ScheduleBuilderReviewExport.BuildSingleTrip(_fsTripsCtxTrip, _fsActiveDriverTab);
                Clipboard.SetText(text);
                SetScheduleBuilderStatus("Copied trip " + (_fsTripsCtxTrip.TripNumber ?? "") + " to clipboard.");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not copy to clipboard:\n\n" + ex.Message, "Schedule Builder",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private ListViewItem FsResolveContextListItem()
        {
            if (_fsTripsCtxHitItem != null)
                return _fsTripsCtxHitItem;
            if (_fsTripsLv?.SelectedItems.Count > 0)
                return _fsTripsLv.SelectedItems[0];
            return null;
        }

        private void FsBindContextTagsFromHitItem(ListViewItem item)
        {
            _fsTripsCtxTrip = null;
            _fsTripsCtxGroup = null;
            _fsTripsCtxNoteTag = null;
            if (item == null)
                return;

            if (item.Tag is FsPreviewNoteTag noteTag)
            {
                _fsTripsCtxNoteTag = noteTag;
                _fsTripsCtxGroup = noteTag.Group;
            }
            else if (item.Tag is FsPreviewTripTag tripTag)
            {
                _fsTripsCtxTrip = tripTag.Trip;
                _fsTripsCtxGroup = tripTag.Group;
            }
            else if (item.Tag is FsPreviewGapTag)
            {
            }
            else
                _fsTripsCtxTrip = GetFsTripFromListItem(item);
        }

        private void FsAddRowFromContext(FsRowPlacement placement)
        {
            if (FsHasCutTrip || string.IsNullOrWhiteSpace(_fsActiveDriverTab) || !_fsHasPreview)
                return;

            if (_fsActiveDriverTab.Equals("Reserves", StringComparison.OrdinalIgnoreCase))
                return;

            var item = FsResolveContextListItem();
            if (item == null)
            {
                SetScheduleBuilderStatus("Select a row first, then add a blank row.");
                return;
            }

            bool below = placement == FsRowPlacement.Below;
            if (!TryResolveFsInsertBeforeLine(item, below, out int insertBeforeLine))
            {
                SetScheduleBuilderStatus("Could not add a row here — select a trip, gap, or note row.");
                return;
            }

            FsInsertBlankRowAt(insertBeforeLine);
            SetScheduleBuilderStatus(below ? "Blank row added below selection." : "Blank row added above selection.");
        }

        private bool FsCanAddNoteToRowContext()
        {
            if (!_fsHasPreview || fsbuilder == null || string.IsNullOrWhiteSpace(_fsActiveDriverTab))
                return false;

            if (_fsActiveDriverTab.Equals("Reserves", StringComparison.OrdinalIgnoreCase))
                return false;

            var item = FsResolveContextListItem();
            if (item?.Tag is FsPreviewGapTag gap && !ScheduleBuilderGapNotes.GapTagHasNoteBar(gap))
                return FsPreviewLineRef.GetLineIndex(item.Tag) >= 0;

            return false;
        }

        private void FsAddNoteFromContext(FsNotePlacement placement)
        {
            if (string.IsNullOrWhiteSpace(_fsActiveDriverTab) || !_fsHasPreview)
                return;

            if (_fsActiveDriverTab.Equals("Reserves", StringComparison.OrdinalIgnoreCase))
                return;

            var item = FsResolveContextListItem();
            if (item == null)
            {
                SetScheduleBuilderStatus("Select a row first, then add a note.");
                return;
            }

            if (!_fsLinesByTab.TryGetValue(_fsActiveDriverTab, out var lines) || lines == null)
                return;

            string dialogTitle;
            string dialogIntro;
            string undoLabel;
            string statusMessage;

            switch (placement)
            {
                case FsNotePlacement.ToRow:
                    dialogTitle = "Add note to row";
                    dialogIntro = "Turn the selected blank row into a note row. Text is saved in the workbook; row color applies only to this row.";
                    undoLabel = "add note to row";
                    statusMessage = "Note added to selected row.";
                    break;
                case FsNotePlacement.Above:
                    dialogTitle = "Add note above";
                    dialogIntro = "Insert a new note row above the selected row. Text is saved in the workbook; row color applies only to this row.";
                    undoLabel = "add note above";
                    statusMessage = "Note added above selected row.";
                    break;
                default:
                    dialogTitle = "Add note below";
                    dialogIntro = "Insert a new note row below the selected row. Text is saved in the workbook; row color applies only to this row.";
                    undoLabel = "add note below";
                    statusMessage = "Note added below selected row.";
                    break;
            }

            if (placement == FsNotePlacement.ToRow)
            {
                if (!(item.Tag is FsPreviewGapTag gap) || ScheduleBuilderGapNotes.GapTagHasNoteBar(gap))
                {
                    SetScheduleBuilderStatus("Add note to row works on a selected blank row.");
                    return;
                }

                int lineIndex = FsPreviewLineRef.GetLineIndex(item.Tag);
                if (lineIndex < 0 || lineIndex >= lines.Count)
                {
                    SetScheduleBuilderStatus("Could not add a note on this row.");
                    return;
                }

                var toRow = ScheduleGroupNoteForm.Prompt(this, dialogTitle, dialogIntro, "", null);
                if (toRow == null)
                    return;

                if (string.IsNullOrWhiteSpace(toRow.NoteText) && !toRow.NoteRowColor.HasValue)
                    return;

                FsRevealGapsForManualInsert();
                FsPushUndoSnapshot(undoLabel);
                ScheduleBuilderGapNotes.ApplyAt(
                    lines, lineIndex, toRow.NoteText, toRow.NoteRowColor);
            }
            else
            {
                bool below = placement == FsNotePlacement.Below;
                if (!TryResolveFsInsertBeforeLine(item, below, out int insertBeforeLine))
                {
                    SetScheduleBuilderStatus("Could not add a note here — select a trip, gap, or note row.");
                    return;
                }

                var inserted = ScheduleGroupNoteForm.Prompt(this, dialogTitle, dialogIntro, "", null);
                if (inserted == null)
                    return;

                if (string.IsNullOrWhiteSpace(inserted.NoteText) && !inserted.NoteRowColor.HasValue)
                    return;

                FsRevealGapsForManualInsert();
                FsPushUndoSnapshot(undoLabel);
                ScheduleBuilderGapNotes.InsertAt(
                    lines, insertBeforeLine, inserted.NoteText, inserted.NoteRowColor);
            }

            FsCommitPreviewLinesForTab(_fsActiveDriverTab, lines);
            ShowFsTripsForTab(_fsActiveDriverTab);
            SyncFsPreviewCsvsForExport();
            SetScheduleBuilderStatus(statusMessage);
        }

        private void FsEditNoteFromContext()
        {
            if (string.IsNullOrWhiteSpace(_fsActiveDriverTab) || !_fsHasPreview)
                return;

            if (!ScheduleBuilderGapNotes.IsEditableNoteRow(
                    FsResolveContextListItem(),
                    _fsLinesByTab.TryGetValue(_fsActiveDriverTab, out var existing) ? existing : null,
                    out int lineIndex))
            {
                return;
            }

            if (!_fsLinesByTab.TryGetValue(_fsActiveDriverTab, out var lines) || lines == null)
                return;

            string current = "";
            Color? currentColor = null;
            if (_fsTripsCtxHitItem?.Tag is FsPreviewGapTag gapTag)
            {
                current = gapTag.NoteText ?? "";
                currentColor = gapTag.NoteRowColor;
            }
            else if (_fsTripsCtxNoteTag != null)
            {
                current = _fsTripsCtxNoteTag.NoteText ?? "";
                currentColor = _fsTripsCtxNoteTag.NoteRowColor;
            }

            ScheduleBuilderGapNotes.TryReadNoteAt(lines, lineIndex, out current, out currentColor);

            var edited = ScheduleGroupNoteForm.Prompt(
                this,
                "Edit note",
                "Update note text and row color. Row color applies only to this note row.",
                current,
                currentColor);
            if (edited == null)
                return;

            FsPushUndoSnapshot("edit note");
            ScheduleBuilderGapNotes.ApplyAt(
                lines, lineIndex, edited.NoteText, edited.NoteRowColor);
            FsCommitPreviewLinesForTab(_fsActiveDriverTab, lines);
            ShowFsTripsForTab(_fsActiveDriverTab);
            SyncFsPreviewCsvsForExport();

            bool hasContent = !string.IsNullOrWhiteSpace(edited.NoteText)
                || edited.NoteRowColor.HasValue;
            SetScheduleBuilderStatus(hasContent ? "Note saved." : "Note cleared.");
        }

        private void FsChangeGroupColorFromContext()
        {
            if (_fsTripsCtxGroup == null
                || string.IsNullOrWhiteSpace(_fsActiveDriverTab)
                || !_fsHasPreview)
                return;

            int groupNumber = _fsTripsCtxGroup.GroupNumber;
            Color initial = _fsTripsCtxGroup.DisplayColor;
            if (_fsLinesByTab.TryGetValue(_fsActiveDriverTab, out var existing) && existing != null)
            {
                var overrideColor = ScheduleBuilderGroupColors.GetOverride(existing, groupNumber);
                if (overrideColor.HasValue)
                    initial = overrideColor.Value;
            }

            using (var dlg = new ColorDialog())
            {
                dlg.Color = initial;
                dlg.FullOpen = true;
                if (dlg.ShowDialog(this) != DialogResult.OK)
                    return;

                FsApplyGroupColor(groupNumber, _fsTripsCtxGroup, dlg.Color, "change group color");
            }
        }

        private void FsResetGroupColorFromContext()
        {
            if (_fsTripsCtxGroup == null
                || string.IsNullOrWhiteSpace(_fsActiveDriverTab)
                || !_fsHasPreview)
                return;

            FsApplyGroupColor(
                _fsTripsCtxGroup.GroupNumber,
                _fsTripsCtxGroup,
                null,
                "reset group color");
        }

        private void FsApplyGroupColor(
            int groupNumber,
            SupeyTripCluster group,
            Color? color,
            string undoLabel)
        {
            if (!_fsLinesByTab.TryGetValue(_fsActiveDriverTab, out var lines) || lines == null)
                return;

            ScheduleBuilderGroupHeaderReconcile.ReconcileInPlace(lines);
            var groups = ScheduleBuilderPreviewGroups.BuildFromPreviewLines(lines);
            group = FindFsGroupByNumber(groups, groupNumber) ?? group;
            if (group == null)
                return;

            FsPushUndoSnapshot(undoLabel);
            ScheduleBuilderGroupColors.ApplyColor(lines, groups, group, color);
            FsCommitPreviewLinesForTab(_fsActiveDriverTab, lines);
            ShowFsTripsForTab(_fsActiveDriverTab);
            SyncFsPreviewCsvsForExport();
            _ = RefreshFsMapForCurrentTabAsync();

            if (color.HasValue)
                SetScheduleBuilderStatus("Group " + groupNumber + " color updated.");
            else
                SetScheduleBuilderStatus("Group " + groupNumber + " color reset to default.");
        }

        private async Task FsAutoSortGroupForEfficiencyAsync(SupeyTripCluster group)
        {
            if (group?.Trips == null || group.Trips.Count < 2) return;
            if (!ScheduleOsrmGate.PreviewRoutingOk)
            {
                SetScheduleBuilderStatus("Auto-sort unavailable — road routing (OSRM) is offline.");
                return;
            }
            if (string.IsNullOrWhiteSpace(_fsActiveDriverTab)
                || _fsActiveDriverTab.Equals("Reserves", StringComparison.OrdinalIgnoreCase))
                return;

            string tab = _fsActiveDriverTab;
            if (!_fsLinesByTab.TryGetValue(tab, out var lines) || lines == null)
                return;

            int groupNumber = group.GroupNumber;
            MCDownloadedTrip ctxTrip = _fsTripsCtxTrip;

            SetScheduleBuilderStatus("Group " + groupNumber + " · finding best trip order…");

            EnsureFsDriverRosterLoaded();
            var driverProfile = ScheduleBuilderDriverMapRouting.FindProfileForScheduleTab(
                _supeyRoster, tab);
            GeoPoint? homeGeo = null;
            if (driverProfile != null)
            {
                try
                {
                    await HiatmeGeoSettings.RefreshConnectivityAsync(
                        HiatmeAiSettings.Load(), CancellationToken.None).ConfigureAwait(true);
                }
                catch { }

                homeGeo = await ScheduleBuilderDriverMapRouting.ResolveHomeGeoAsync(
                    driverProfile, CancellationToken.None).ConfigureAwait(true);
            }

            var dayPosition = ScheduleBuilderDriverMapRouting.GroupDayPosition.Middle;
            if (_fsGroupsByTab.TryGetValue(tab, out var tabGroups) && tabGroups != null)
            {
                int groupIndex = FindFsGroupIndex(tabGroups, group);
                dayPosition = ScheduleBuilderDriverMapRouting.ResolveDayPosition(
                    groupIndex, tabGroups.Count);
            }

            if (homeGeo.HasValue && !SupeyMapWorkspace.IsValidGeoPoint(homeGeo.Value))
                homeGeo = null;

            var (bestOrder, scorePercent, alreadyOptimal, approx) =
                await ScheduleBuilderMapMileage.FindBestTripOrderAsync(
                    group, homeGeo, dayPosition, CancellationToken.None).ConfigureAwait(true);

            if (bestOrder == null || bestOrder.Count == 0)
            {
                SetScheduleBuilderStatus("Group " + group.GroupNumber
                    + " · could not sort (trips need geocoded PU/DO pins).");
                return;
            }

            if (alreadyOptimal)
            {
                FsSelectGroupInListView(groupNumber);
                FsTripsLv_SelectionChangedUpdateMap();
                SetScheduleBuilderStatus("Group " + groupNumber
                    + " is already at 100% route efficiency.");
                return;
            }

            FsPushUndoSnapshot("auto-sort group");
            if (!ScheduleBuilderPreviewDrag.ApplyTripOrderToGroup(
                    lines, groupNumber, bestOrder))
            {
                SetScheduleBuilderStatus("Group " + groupNumber + " · could not apply sort.");
                return;
            }

            FsCommitPreviewLinesForTab(tab, lines);

            ShowFsTripsForTab(tab);
            await RefreshFsMapForCurrentTabAsync().ConfigureAwait(true);

            if (ctxTrip != null)
                SelectFsTripInListView(ctxTrip);
            else
                FsSelectGroupInListView(groupNumber);
            FsTripsLv_SelectionChangedUpdateMap();

            string scoreText = scorePercent.HasValue
                ? scorePercent.Value.ToString("0") + "% route efficiency"
                : "sorted";
            if (approx)
                scoreText += " (approx)";
            SetScheduleBuilderStatus("Group " + groupNumber + " auto-sorted · " + scoreText + ".");
        }

        private async Task FsGeocodeActiveTabDriverHomeAsync()
        {
            await FsGeocodeDriverHomeAsync(
                ScheduleBuilderDriverMapRouting.FindProfileForScheduleTab(_supeyRoster, _fsActiveDriverTab),
                _fsActiveDriverTab).ConfigureAwait(true);
        }

        internal async Task FsGeocodeDriverHomeAsync(SupeyDriverProfile profile, string tabLabel = null)
        {
            if (profile == null)
            {
                SetScheduleBuilderStatus("No driver roster match"
                    + (string.IsNullOrWhiteSpace(tabLabel) ? "." : " for tab \"" + tabLabel.Trim() + "\"."));
                return;
            }

            if (!ScheduleOsrmGate.PreviewGeoOk)
            {
                SetScheduleBuilderStatus("Geocode unavailable — office AI server is offline.");
                return;
            }

            if (string.IsNullOrWhiteSpace(profile.HomeStreet) && string.IsNullOrWhiteSpace(profile.HomeCity))
            {
                SetScheduleBuilderStatus((profile.Name ?? "Driver") + " has no home address in the roster.");
                return;
            }

            string who = (profile.Name ?? tabLabel ?? "Driver").Trim();
            SetScheduleBuilderStatus("Geocoding home for " + who + "…");

            GeoPoint? geo;
            try
            {
                geo = await ScheduleBuilderDriverMapRouting.ResolveHomeGeoAsync(
                    profile, CancellationToken.None).ConfigureAwait(true);
            }
            catch
            {
                geo = null;
            }

            if (geo.HasValue && SupeyMapWorkspace.IsValidGeoPoint(geo.Value))
            {
                SetScheduleBuilderStatus(who + " · home geocoded.");
                RefreshFsMapIfDriverTabActive();
                return;
            }

            SetScheduleBuilderStatus(who + " · could not geocode home (check address or server).");
        }

        private void FsCommitPreviewLinesForTab(string tab, List<ScheduleBuilderPreviewLine> lines)
        {
            FsTrackReroutedKeysFromLines(lines);

            if (lines != null
                && !tab.Equals("Reserves", StringComparison.OrdinalIgnoreCase))
            {
                ScheduleBuilderTrailingRows.StripTrailingPads(lines);
                lines = ScheduleBuilderGroupHeaderReconcile.Reconcile(lines);
                ScheduleBuilderTrailingRows.EnsureAtEnd(lines);
            }

            _fsLinesByTab[tab] = lines;

            if (fsbuilder?.PreviewDriverLines != null)
            {
                var dict = fsbuilder.PreviewDriverLines as Dictionary<string, List<ScheduleBuilderPreviewLine>>;
                if (dict != null && !tab.Equals("Reserves", StringComparison.OrdinalIgnoreCase))
                    dict[tab] = lines;
            }

            if (tab.Equals("Reserves", StringComparison.OrdinalIgnoreCase))
                fsbuilder?.ApplyPreviewReserveLines(lines);

            if (fsbuilder?.driverTripList != null)
            {
                var trips = new List<MCDownloadedTrip>();
                foreach (var line in lines)
                {
                    if (line?.Kind == ScheduleBuilderPreviewLine.LineKind.Trip && line.Trip != null)
                        trips.Add(line.Trip);
                }
                fsbuilder.driverTripList[tab] = trips;
            }

            FsReapplyReroutedHighlights();
            FsReapplyWellRydeCancelledHighlights();
            if (_fsHasPreview
                && !string.IsNullOrWhiteSpace(_fsActiveDriverTab)
                && tab.Equals(_fsActiveDriverTab, StringComparison.OrdinalIgnoreCase))
            {
                FsSyncReroutedHighlightsFromPreviewLines();
            }
        }
    }

    internal sealed class ScheduleGroupNoteEditResult
    {
        public string NoteText { get; set; } = "";
        public Color? NoteRowColor { get; set; }
    }

    /// <summary>Themed dialog for group notes — text plus optional note-row color (not whole-group color).</summary>
    internal sealed class ScheduleGroupNoteForm : SupeyForm
    {
        private const int DialogWidth = 500;
        private const int ContentWidth = 452;

        private readonly TextBox _noteBox;
        private readonly Panel _swatch;
        private readonly Label _colorLabel;
        private Color? _noteRowColor;

        private ScheduleGroupNoteForm(string title, string introText, string initialNote, Color? initialRowColor)
        {
            _noteRowColor = initialRowColor;

            Text = title ?? "Note";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(DialogWidth, 320);
            MinimumSize = new Size(DialogWidth, 320);
            MaximumSize = new Size(DialogWidth, 320);
            BackColor = SupeyTheme.Surface;

            var footer = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 57,
                BackColor = SupeyTheme.Surface,
            };
            footer.Controls.Add(new Panel
            {
                Dock = DockStyle.Top,
                Height = 1,
                BackColor = SupeyTheme.Divider,
            });
            var footerButtons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                Padding = new Padding(0, 8, 24, 12),
                BackColor = SupeyTheme.Surface,
            };
            var okBtn = new DarkOnAccentMaterialButton
            {
                Text = "SAVE",
                AutoSize = false,
                Type = SupeyMaterialButton.MaterialButtonType.Contained,
                UseAccentColor = true,
                Size = new Size(96, 36),
                DialogResult = DialogResult.OK,
            };
            var cancelBtn = new SupeyMaterialButton
            {
                Text = "CANCEL",
                AutoSize = false,
                Type = SupeyMaterialButton.MaterialButtonType.Text,
                UseAccentColor = false,
                NoAccentTextColor = SupeyTheme.TextSecondary,
                Size = new Size(96, 36),
                Margin = new Padding(0, 0, 8, 0),
                DialogResult = DialogResult.Cancel,
            };
            footerButtons.Controls.Add(okBtn);
            footerButtons.Controls.Add(cancelBtn);
            footer.Controls.Add(footerButtons);

            var body = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(24, 20, 24, 12),
                BackColor = SupeyTheme.Surface,
            };

            var stack = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                BackColor = SupeyTheme.Surface,
            };
            stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 88f));
            stack.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            var introLabel = new Label
            {
                Text = string.IsNullOrWhiteSpace(introText)
                    ? "Note text is saved in the workbook. Row color applies only to this note row."
                    : introText,
                Font = new Font("Segoe UI", 9f),
                AutoSize = true,
                MaximumSize = new Size(ContentWidth, 0),
                Margin = new Padding(0, 0, 0, 12),
                ForeColor = SupeyTheme.TextSecondary,
                BackColor = SupeyTheme.Surface,
            };
            stack.Controls.Add(introLabel, 0, 0);

            var noteHost = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(1),
                BackColor = SupeyTheme.BorderSubtle,
                Margin = new Padding(0, 0, 0, 14),
            };
            _noteBox = new TextBox
            {
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                Dock = DockStyle.Fill,
                Text = initialNote ?? "",
                Font = new Font("Segoe UI", 9.75f),
                BackColor = SupeyTheme.SurfaceElevated,
                ForeColor = SupeyTheme.TextPrimary,
                BorderStyle = BorderStyle.None,
            };
            noteHost.Controls.Add(_noteBox);
            stack.Controls.Add(noteHost, 0, 1);

            var colorCard = BuildColorCard(out _swatch, out _colorLabel);
            colorCard.Dock = DockStyle.Fill;
            stack.Controls.Add(colorCard, 0, 2);

            body.Controls.Add(stack);

            Controls.Add(body);
            Controls.Add(footer);
            AcceptButton = okBtn;
            CancelButton = cancelBtn;

            SupeyDarkScrollBars.Apply(this);
            RefreshSwatch();
        }

        private Panel BuildColorCard(out Panel swatch, out Label colorLabel)
        {
            var card = new Panel
            {
                BackColor = SupeyTheme.SurfaceElevated,
                Padding = new Padding(16, 14, 16, 14),
            };
            card.Paint += (s, e) =>
            {
                var g = e.Graphics;
                var r = card.ClientRectangle;
                r.Width -= 1;
                r.Height -= 1;
                using (var pen = new Pen(SupeyTheme.BorderSubtle))
                    g.DrawRectangle(pen, r);
            };

            var cardStack = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = SupeyTheme.SurfaceElevated,
            };
            cardStack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            cardStack.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var colorCaption = new Label
            {
                Text = "Note row color",
                Font = new Font("Segoe UI Semibold", 9.75f),
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 10),
                ForeColor = SupeyTheme.TextPrimary,
                BackColor = SupeyTheme.SurfaceElevated,
            };
            cardStack.Controls.Add(colorCaption, 0, 0);

            var colorRow = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 4,
                RowCount = 1,
                BackColor = SupeyTheme.SurfaceElevated,
            };
            colorRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 56f));
            colorRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            colorRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            colorRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var swatchPanel = new Panel
            {
                Width = 48,
                Height = 32,
                Margin = new Padding(0, 2, 12, 0),
                BackColor = SupeyTheme.Surface,
            };
            swatchPanel.Paint += (s, e) => SwatchPaint(swatchPanel, e);
            swatch = swatchPanel;

            var statusLabel = new Label
            {
                AutoSize = true,
                Anchor = AnchorStyles.Left | AnchorStyles.Top,
                Margin = new Padding(0, 8, 12, 0),
                Font = new Font("Segoe UI", 9f),
                ForeColor = SupeyTheme.TextSecondary,
                BackColor = SupeyTheme.SurfaceElevated,
            };
            colorLabel = statusLabel;

            var pickBtn = new SupeyMaterialButton
            {
                Text = "PICK COLOR…",
                AutoSize = false,
                Type = SupeyMaterialButton.MaterialButtonType.Outlined,
                Size = new Size(118, 34),
                Margin = new Padding(0, 0, 6, 0),
            };
            pickBtn.Click += (s, e) => PickColor();

            var clearBtn = new SupeyMaterialButton
            {
                Text = "NO COLOR",
                AutoSize = false,
                Type = SupeyMaterialButton.MaterialButtonType.Text,
                UseAccentColor = false,
                NoAccentTextColor = SupeyTheme.TextSecondary,
                Size = new Size(96, 34),
                Margin = new Padding(0, 0, 0, 0),
            };
            clearBtn.Click += (s, e) =>
            {
                _noteRowColor = null;
                RefreshSwatch();
            };

            colorRow.Controls.Add(swatch, 0, 0);
            colorRow.Controls.Add(colorLabel, 1, 0);
            colorRow.Controls.Add(pickBtn, 2, 0);
            colorRow.Controls.Add(clearBtn, 3, 0);
            cardStack.Controls.Add(colorRow, 0, 1);
            card.Controls.Add(cardStack);
            return card;
        }

        private void SwatchPaint(Panel swatch, PaintEventArgs e)
        {
            var g = e.Graphics;
            var r = swatch.ClientRectangle;
            r.Width -= 1;
            r.Height -= 1;

            using (var pen = new Pen(SupeyTheme.BorderSubtle))
                g.DrawRectangle(pen, r);

            if (!_noteRowColor.HasValue)
                return;

            int stripeW = Math.Max(6, r.Width / 5);
            var stripe = new Rectangle(r.X + 1, r.Y + 1, stripeW, r.Height - 1);
            using (var brush = new SolidBrush(_noteRowColor.Value))
                g.FillRectangle(brush, stripe);
        }

        public static ScheduleGroupNoteEditResult Prompt(
            IWin32Window owner,
            string title,
            string introText,
            string initialNote,
            Color? initialRowColor)
        {
            using (var form = new ScheduleGroupNoteForm(title, introText, initialNote, initialRowColor))
            {
                if (form.ShowDialog(owner) != DialogResult.OK)
                    return null;

                return new ScheduleGroupNoteEditResult
                {
                    NoteText = form._noteBox.Text.Trim(),
                    NoteRowColor = form._noteRowColor,
                };
            }
        }

        private void PickColor()
        {
            using (var dlg = new ColorDialog())
            {
                dlg.FullOpen = true;
                dlg.Color = _noteRowColor ?? Color.FromArgb(70, 130, 180);
                if (dlg.ShowDialog(this) != DialogResult.OK)
                    return;

                _noteRowColor = dlg.Color;
                RefreshSwatch();
            }
        }

        private void RefreshSwatch()
        {
            if (_noteRowColor.HasValue)
            {
                _colorLabel.Text = "Colored note row";
                _colorLabel.ForeColor = SupeyTheme.TextPrimary;
            }
            else
            {
                _colorLabel.Text = "Plain row (no color bar)";
                _colorLabel.ForeColor = SupeyTheme.TextSecondary;
            }

            _swatch.Invalidate();
        }
    }
}
