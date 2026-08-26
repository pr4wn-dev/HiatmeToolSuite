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
        private ToolStripMenuItem _fsTripsCtxAddRowAbove;
        private ToolStripMenuItem _fsTripsCtxAddRowBelow;
        private ToolStripMenuItem _fsTripsCtxAddNoteToRow;
        private ToolStripMenuItem _fsTripsCtxAddNoteAbove;
        private ToolStripMenuItem _fsTripsCtxAddNoteBelow;
        private ToolStripMenuItem _fsTripsCtxEditNote;
        private ToolStripMenuItem _fsTripsCtxChangeGroupColor;
        private ToolStripMenuItem _fsTripsCtxResetGroupColor;
        private ToolStripMenuItem _fsTripsCtxRerouteModivcare;
        private ToolStripMenuItem _fsTripsCtxAddToReroutes;
        private ToolStripMenuItem _fsTripsCtxAddToCancels;
        private ToolStripMenuItem _fsTripsCtxMoveMenu;
        private ToolStripMenuItem _fsTripsCtxTripStatusMenu;
        private ToolStripMenuItem _fsTripsCtxNotesRowsMenu;
        private ToolStripMenuItem _fsTripsCtxAddRowMenu;
        private ToolStripMenuItem _fsTripsCtxGroupMenu;
        private ToolStripMenuItem _fsTripsCtxDriverMenu;
        private ToolStripMenuItem _fsTripsCtxClientMenu;
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

        private static ToolStripMenuItem CreateFsTripsCtxItem(string text, Image image = null)
        {
            var item = new ToolStripMenuItem(text)
            {
                BackColor = DarkContextMenuRenderer.Background,
                ForeColor = DarkContextMenuRenderer.ForeColor,
                Image = image,
                ImageScaling = ToolStripItemImageScaling.None,
                ImageAlign = ContentAlignment.MiddleLeft,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(0, DarkContextMenuRenderer.RowPadV, 0, DarkContextMenuRenderer.RowPadV),
                Margin = Padding.Empty,
            };
            return item;
        }

        private static void StyleFsTripsCtxStrip(ToolStrip strip)
        {
            if (strip == null)
                return;

            strip.Renderer = new DarkContextMenuRenderer();
            strip.BackColor = DarkContextMenuRenderer.Background;
            strip.ForeColor = DarkContextMenuRenderer.ForeColor;
            strip.Font = SupeyTheme.BodyFont;
            strip.Padding = new Padding(0, 4, 0, 4);
            strip.ImageScalingSize = new Size(DarkContextMenuRenderer.IconSize, DarkContextMenuRenderer.IconSize);

            if (strip is ToolStripDropDownMenu menu)
            {
                menu.ShowImageMargin = false;
                menu.ShowCheckMargin = false;
                menu.DropShadowEnabled = true;
            }
        }

        private static void ConfigureFsTripsCtxSubmenuDropDown(ToolStripMenuItem parent)
        {
            if (parent == null)
                return;

            StyleFsTripsCtxStrip(parent.DropDown);
        }

        private static ToolStripMenuItem CreateFsTripsCtxSubmenu(string text, Image image, params ToolStripItem[] children)
        {
            var menu = CreateFsTripsCtxItem(text, image);
            if (children != null)
            {
                foreach (var child in children)
                {
                    if (child != null)
                        menu.DropDownItems.Add(child);
                }
            }

            ConfigureFsTripsCtxSubmenuDropDown(menu);
            return menu;
        }

        private static void FsTripsCtxClearNestedShortcutKeys(ToolStripMenuItem item)
        {
            if (item == null)
                return;
            item.ShortcutKeys = Keys.None;
            item.ShowShortcutKeys = false;
        }

        private void BuildFsTripsContextMenu()
        {
            _fsTripsCtxMenu = new ContextMenuStrip();
            StyleFsTripsCtxStrip(_fsTripsCtxMenu);
            _fsTripsCtxMenu.Opening += FsTripsCtxMenu_Opening;

            _fsTripsCtxBanClient = CreateFsTripsCtxItem("Ban client", SupeyMenuGlyphs.Ban);
            _fsTripsCtxBanClient.Click += (s, e) =>
            {
                if (_fsTripsCtxTrip != null)
                    FsBanClientFromTrip(_fsTripsCtxTrip, quietWhenMissing: true);
            };

            _fsTripsCtxUnbanClient = CreateFsTripsCtxItem("Remove from banned list", SupeyMenuGlyphs.CircleCheck);
            _fsTripsCtxUnbanClient.Click += (s, e) =>
            {
                if (_fsTripsCtxTrip != null)
                    FsUnbanClientFromTrip(_fsTripsCtxTrip);
            };

            _fsTripsCtxFocusMap = CreateFsTripsCtxItem("Show on map", SupeyMenuGlyphs.Map);
            _fsTripsCtxFocusMap.Click += (s, e) =>
            {
                if (_fsTripsCtxTrip == null || _fsMap == null)
                    return;
                if (!FsMapIsShownToUser())
                    ShowFsMap();
                _fsMap.FocusTrip(_fsTripsCtxTrip);
            };

            _fsTripsCtxCopyForAi = CreateFsTripsCtxItem("Copy for AI review (Cursor)");
            _fsTripsCtxCopyForAi.Click += (s, e) => FsCopyScheduleForAiReviewToClipboard();

            _fsTripsCtxCopyCurrentTab = CreateFsTripsCtxItem("Copy current tab");
            _fsTripsCtxCopyCurrentTab.Click += (s, e) => FsCopyCurrentTabToClipboard();

            _fsTripsCtxCopySelectedTrip = CreateFsTripsCtxItem("Copy selected trip");
            _fsTripsCtxCopySelectedTrip.Click += (s, e) => FsCopySelectedTripToClipboard();

            _fsTripsCtxAutoSortGroup = CreateFsTripsCtxItem(
                "Auto-sort group for best route efficiency",
                SupeyMenuGlyphs.Sort);
            _fsTripsCtxAutoSortGroup.Click += (s, e) =>
            {
                if (_fsTripsCtxGroup != null)
                    _ = FsAutoSortGroupForEfficiencyAsync(_fsTripsCtxGroup);
            };

            _fsTripsCtxGeocodeDriverHome = CreateFsTripsCtxItem("Geocode driver home", SupeyMenuGlyphs.Home);
            _fsTripsCtxGeocodeDriverHome.Click += (s, e) =>
            {
                _ = FsGeocodeActiveTabDriverHomeAsync();
            };

            _fsTripsCtxEmailDriver = CreateFsTripsCtxItem("Email schedule to driver…", SupeyMenuGlyphs.Mail);
            _fsTripsCtxEmailDriver.Click += (s, e) => _ = FsEmailActiveDriverFromContextAsync();

            _fsTripsCtxSuggestDriver = CreateFsTripsCtxItem("Suggest driver for trip…", SupeyMenuGlyphs.Person);
            _fsTripsCtxSuggestDriver.Click += (s, e) => _ = FsSuggestDriverForTripAsync();

            _fsTripsCtxCutTrip = CreateFsTripsCtxItem("Cut trip", SupeyMenuGlyphs.Cut);
            _fsTripsCtxCutTrip.Click += (s, e) => FsCutSelectedTrip();

            _fsTripsCtxDelete = CreateFsTripsCtxItem("Delete", SupeyMenuGlyphs.Trash);
            _fsTripsCtxDelete.Click += (s, e) => FsDeleteSelection();

            _fsTripsCtxUndo = CreateFsTripsCtxItem("Undo", SupeyMenuGlyphs.Undo);
            _fsTripsCtxUndo.Click += (s, e) => FsUndoScheduleEdit();

            _fsTripsCtxRedo = CreateFsTripsCtxItem("Redo", SupeyMenuGlyphs.Redo);
            _fsTripsCtxRedo.Click += (s, e) => FsRedoScheduleEdit();

            _fsTripsCtxPasteTrip = CreateFsTripsCtxItem("Paste trip", SupeyMenuGlyphs.Paste);
            _fsTripsCtxPasteTrip.Click += (s, e) => FsInsertCutTrip(below: false);

            FsTripsCtxClearNestedShortcutKeys(_fsTripsCtxCutTrip);
            FsTripsCtxClearNestedShortcutKeys(_fsTripsCtxDelete);
            FsTripsCtxClearNestedShortcutKeys(_fsTripsCtxUndo);
            FsTripsCtxClearNestedShortcutKeys(_fsTripsCtxRedo);
            FsTripsCtxClearNestedShortcutKeys(_fsTripsCtxPasteTrip);

            _fsTripsCtxInsertTripAbove = CreateFsTripsCtxItem("Insert trip above", SupeyMenuGlyphs.InsertAbove);
            _fsTripsCtxInsertTripAbove.Click += (s, e) => FsInsertCutTrip(below: false);

            _fsTripsCtxInsertTripBelow = CreateFsTripsCtxItem("Insert trip below", SupeyMenuGlyphs.InsertBelow);
            _fsTripsCtxInsertTripBelow.Click += (s, e) => FsInsertCutTrip(below: true);

            _fsTripsCtxClearCut = CreateFsTripsCtxItem("Clear cut trip", SupeyMenuGlyphs.Broom);
            _fsTripsCtxClearCut.Click += (s, e) =>
            {
                FsClearCutTrip();
                SetScheduleBuilderStatus("Cut trip cleared.");
            };

            _fsTripsCtxEditNote = CreateFsTripsCtxItem("Edit note…", SupeyMenuGlyphs.Pencil);
            _fsTripsCtxEditNote.Click += (s, e) => FsEditNoteFromContext();

            _fsTripsCtxAddNoteToRow = CreateFsTripsCtxItem("To this row…", SupeyMenuGlyphs.Note);
            _fsTripsCtxAddNoteToRow.Click += (s, e) => FsAddNoteFromContext(FsNotePlacement.ToRow);

            _fsTripsCtxAddNoteAbove = CreateFsTripsCtxItem("Above…", SupeyMenuGlyphs.InsertAbove);
            _fsTripsCtxAddNoteAbove.Click += (s, e) => FsAddNoteFromContext(FsNotePlacement.Above);

            _fsTripsCtxAddNoteBelow = CreateFsTripsCtxItem("Below…", SupeyMenuGlyphs.InsertBelow);
            _fsTripsCtxAddNoteBelow.Click += (s, e) => FsAddNoteFromContext(FsNotePlacement.Below);

            _fsTripsCtxAddRowAbove = CreateFsTripsCtxItem("Above", SupeyMenuGlyphs.RowAbove);
            _fsTripsCtxAddRowAbove.Click += (s, e) => FsAddRowFromContext(FsRowPlacement.Above);

            _fsTripsCtxAddRowBelow = CreateFsTripsCtxItem("Below", SupeyMenuGlyphs.RowBelow);
            _fsTripsCtxAddRowBelow.Click += (s, e) => FsAddRowFromContext(FsRowPlacement.Below);

            _fsTripsCtxChangeGroupColor = CreateFsTripsCtxItem("Change group color", SupeyMenuGlyphs.Palette);
            _fsTripsCtxChangeGroupColor.Click += (s, e) => FsChangeGroupColorFromContext();

            _fsTripsCtxResetGroupColor = CreateFsTripsCtxItem("Reset group color to default", SupeyMenuGlyphs.Revert);
            _fsTripsCtxResetGroupColor.Click += (s, e) => FsResetGroupColorFromContext();

            _fsTripsCtxRerouteModivcare = CreateFsTripsCtxItem("Reroute on Modivcare…", SupeyMenuGlyphs.Reroute);
            _fsTripsCtxRerouteModivcare.Click += (s, e) => FsRerouteTripOnModivcareFromContext();

            _fsTripsCtxAddToReroutes = CreateFsTripsCtxItem("Move to Reroutes section", SupeyMenuGlyphs.Tray);
            _fsTripsCtxAddToReroutes.Click += (s, e) => FsAddTripToReroutesSectionFromContext();

            _fsTripsCtxAddToCancels = CreateFsTripsCtxItem("Add to Cancels section (no Modivcare)", SupeyMenuGlyphs.CircleX);
            _fsTripsCtxAddToCancels.Click += (s, e) => FsAddTripToCancelsSectionFromContext();

            _fsTripsCtxMoveMenu = CreateFsTripsCtxSubmenu(
                "Move trips",
                SupeyMenuGlyphs.Move,
                _fsTripsCtxCutTrip,
                _fsTripsCtxPasteTrip,
                _fsTripsCtxInsertTripAbove,
                _fsTripsCtxInsertTripBelow,
                _fsTripsCtxClearCut);

            _fsTripsCtxTripStatusMenu = CreateFsTripsCtxSubmenu(
                "Reroute trip",
                SupeyMenuGlyphs.Reroute,
                _fsTripsCtxRerouteModivcare,
                _fsTripsCtxAddToReroutes,
                _fsTripsCtxAddToCancels);

            _fsTripsCtxNotesRowsMenu = CreateFsTripsCtxSubmenu(
                "Add note",
                SupeyMenuGlyphs.Note,
                _fsTripsCtxAddNoteToRow,
                _fsTripsCtxAddNoteAbove,
                _fsTripsCtxAddNoteBelow,
                new ToolStripSeparator(),
                _fsTripsCtxEditNote);

            _fsTripsCtxAddRowMenu = CreateFsTripsCtxSubmenu(
                "Add blank row",
                SupeyMenuGlyphs.Rows,
                _fsTripsCtxAddRowAbove,
                _fsTripsCtxAddRowBelow);

            _fsTripsCtxGroupMenu = CreateFsTripsCtxSubmenu(
                "Color group",
                SupeyMenuGlyphs.Palette,
                _fsTripsCtxChangeGroupColor,
                _fsTripsCtxResetGroupColor);

            _fsTripsCtxDriverMenu = CreateFsTripsCtxSubmenu(
                "Email driver",
                SupeyMenuGlyphs.Mail,
                _fsTripsCtxEmailDriver,
                _fsTripsCtxGeocodeDriverHome);

            _fsTripsCtxClientMenu = CreateFsTripsCtxSubmenu(
                "Ban client",
                SupeyMenuGlyphs.Ban,
                _fsTripsCtxBanClient,
                _fsTripsCtxUnbanClient);

            // Every label is verb + object; one-shot actions stay flat instead of hiding in a flyout.
            _fsTripsCtxMenu.Items.Add(_fsTripsCtxFocusMap);
            _fsTripsCtxMenu.Items.Add(_fsTripsCtxSuggestDriver);
            _fsTripsCtxMenu.Items.Add(new ToolStripSeparator());
            _fsTripsCtxMenu.Items.Add(_fsTripsCtxMoveMenu);
            _fsTripsCtxMenu.Items.Add(_fsTripsCtxDelete);
            _fsTripsCtxMenu.Items.Add(_fsTripsCtxUndo);
            _fsTripsCtxMenu.Items.Add(_fsTripsCtxRedo);
            _fsTripsCtxMenu.Items.Add(new ToolStripSeparator());
            _fsTripsCtxMenu.Items.Add(_fsTripsCtxTripStatusMenu);
            _fsTripsCtxMenu.Items.Add(new ToolStripSeparator());
            _fsTripsCtxMenu.Items.Add(_fsTripsCtxNotesRowsMenu);
            _fsTripsCtxMenu.Items.Add(_fsTripsCtxAddRowMenu);
            _fsTripsCtxMenu.Items.Add(_fsTripsCtxGroupMenu);
            _fsTripsCtxMenu.Items.Add(_fsTripsCtxAutoSortGroup);
            _fsTripsCtxMenu.Items.Add(new ToolStripSeparator());
            _fsTripsCtxMenu.Items.Add(_fsTripsCtxDriverMenu);
            _fsTripsCtxMenu.Items.Add(_fsTripsCtxClientMenu);

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

        private void FsKickPreviewServicesProbeInBackground()
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await ScheduleOsrmGate.ProbePreviewServicesAsync(
                        HiatmeAiSettings.Load(), CancellationToken.None).ConfigureAwait(false);
                }
                catch { /* desk preview */ }
            });
        }

        private void FsTripsCtxMenu_Opening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            FsPrepareTripsContextMenuState();
        }

        private void FsTripsLv_MouseUp_ShowContextMenu(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right || _fsTripsLv == null) return;

            FsKickPreviewServicesProbeInBackground();

            var hit = _fsTripsLv.HitTest(e.Location);
            _fsTripsCtxHitItem = hit.Item;
            if (_fsTripsCtxHitItem == null && _fsTripsLv.SelectedItems.Count > 0)
                _fsTripsCtxHitItem = _fsTripsLv.SelectedItems[0];

            if (_fsTripsCtxHitItem != null)
            {
                // Right-clicking inside a selection keeps it, so cut and delete can act on the
                // whole batch. Right-clicking outside one replaces it, the way Explorer does,
                // rather than quietly widening what the next command will touch.
                if (!_fsTripsCtxHitItem.Selected)
                    _fsTripsLv.SelectedItems.Clear();

                _fsTripsCtxHitItem.Selected = true;
                _fsTripsCtxHitItem.Focused = true;
            }

            _fsTripsCtxMenu.Show(_fsTripsLv, e.Location);
        }

        private void FsPrepareTripsContextMenuState()
        {
            FsBindContextTagsFromHitItem(_fsTripsCtxHitItem);

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

            // Every trip-row command acts on the whole highlighted batch, so the labels have to
            // say so — otherwise "Cut trip" on a five-row selection looks like it only took one.
            int selectedTripCount = FsCollectSelectedTrips().Count;
            string selectedTripNoun = selectedTripCount > 1
                ? selectedTripCount + " trips"
                : "trip";

            _fsTripsCtxCutTrip.Enabled = hasTrip && canMoveTrips && !FsHasCutTrip;
            _fsTripsCtxCutTrip.Text = "Cut " + selectedTripNoun;
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
                _fsTripsCtxDelete.Text = selectedTripCount > 1
                    ? "Delete " + selectedTripNoun
                    : (string.IsNullOrEmpty(num) ? "Delete trip" : "Delete trip " + num);
            }
            else if (canDeleteNote)
                _fsTripsCtxDelete.Text = "Delete note";
            else if (canDeleteGap)
                _fsTripsCtxDelete.Text = "Delete blank row";
            else
                _fsTripsCtxDelete.Text = "Delete";
            _fsTripsCtxPasteTrip.Enabled = canInsertCut;
            _fsTripsCtxPasteTrip.Text = "Paste " + FsCutTripMenuNoun();
            _fsTripsCtxUndo.Enabled = _fsUndoStack.CanUndo;
            _fsTripsCtxUndo.Text = FsUndoMenuText();
            _fsTripsCtxRedo.Enabled = _fsUndoStack.CanRedo;
            _fsTripsCtxRedo.Text = FsRedoMenuText();
            _fsTripsCtxAddRowAbove.Enabled = canInsertBlank;
            _fsTripsCtxAddRowBelow.Enabled = canInsertBlank;
            _fsTripsCtxInsertTripAbove.Visible = FsHasCutTrip;
            _fsTripsCtxInsertTripBelow.Visible = FsHasCutTrip;
            _fsTripsCtxInsertTripAbove.Enabled = canInsertCut;
            _fsTripsCtxInsertTripBelow.Enabled = canInsertCut;
            _fsTripsCtxInsertTripAbove.Text = "Insert " + FsCutTripMenuNoun() + " above";
            _fsTripsCtxInsertTripBelow.Text = "Insert " + FsCutTripMenuNoun() + " below";
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

            bool canAddAnySelectedToReroutes = false;
            if (hasTrip && hasBuild && _fsTripsLv != null)
            {
                foreach (ListViewItem item in _fsTripsLv.SelectedItems)
                {
                    if (item?.Tag is FsPreviewTripTag tag
                        && tag.Trip != null
                        && FsNeedsMoveToReservesReroutes(
                            tag.Trip, _fsActiveDriverTab ?? "", fsbuilder, reserveLinesForCtx))
                    {
                        canAddAnySelectedToReroutes = true;
                        break;
                    }
                }
            }
            if (!canAddAnySelectedToReroutes && hasTrip && hasBuild && !alreadyInReroutes)
                canAddAnySelectedToReroutes = true;

            _fsTripsCtxAddToReroutes.Enabled = canAddAnySelectedToReroutes;
            if (alreadyInReroutes && selectedTripCount <= 1)
                _fsTripsCtxAddToReroutes.Text = "Move to Reroutes section (already there)";
            else if (selectedTripCount > 1)
                _fsTripsCtxAddToReroutes.Text = "Add " + selectedTripCount + " trips to Reroutes section";
            else if (alreadyRerouted)
                _fsTripsCtxAddToReroutes.Text = "Move to Reroutes section";
            else
                _fsTripsCtxAddToReroutes.Text = "Add to Reroutes section";

            bool alreadyInCancels = hasTrip && hasBuild
                && !FsNeedsMoveToReservesCancels(_fsTripsCtxTrip, _fsActiveDriverTab ?? "", fsbuilder);
            bool canAddAnySelectedToCancels = false;
            if (hasTrip && hasBuild && _fsTripsLv != null)
            {
                foreach (ListViewItem item in _fsTripsLv.SelectedItems)
                {
                    if (item?.Tag is FsPreviewTripTag tag
                        && tag.Trip != null
                        && FsNeedsMoveToReservesCancels(tag.Trip, _fsActiveDriverTab ?? "", fsbuilder))
                    {
                        canAddAnySelectedToCancels = true;
                        break;
                    }
                }
            }
            if (!canAddAnySelectedToCancels && hasTrip && hasBuild && !alreadyInCancels)
                canAddAnySelectedToCancels = true;

            _fsTripsCtxAddToCancels.Enabled = canAddAnySelectedToCancels;
            _fsTripsCtxAddToCancels.ToolTipText = "Moves the trip to Cancels without touching Modivcare.";
            if (alreadyInCancels && selectedTripCount <= 1)
                _fsTripsCtxAddToCancels.Text = "Add to Cancels section (already there)";
            else if (selectedTripCount > 1)
                _fsTripsCtxAddToCancels.Text = "Add " + selectedTripCount + " trips to Cancels section";
            else
                _fsTripsCtxAddToCancels.Text = "Add to Cancels section";

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
            // The parent already reads "Color group 3", so repeating the group here just makes the
            // flyout wide enough to cover the schedule behind it.
            _fsTripsCtxChangeGroupColor.Text = "Pick color…";
            _fsTripsCtxResetGroupColor.Text = "Reset to default";

            if (canSortGroup)
            {
                _fsTripsCtxAutoSortGroup.Text = ScheduleOsrmGate.PreviewRoutingOk
                    ? "Sort group " + _fsTripsCtxGroup.GroupNumber
                    : "Sort group (routing offline)";
            }
            else
            {
                _fsTripsCtxAutoSortGroup.Text = "Sort group";
            }

            _fsTripsCtxGeocodeDriverHome.Text =
                !ScheduleOsrmGate.PreviewGeoOk && hasHomeAddress && !isReserves
                    ? "Geocode home (server offline)"
                    : "Geocode home address";

            bool driverHasEmail = activeDriverProfile != null
                && !string.IsNullOrWhiteSpace(activeDriverProfile.Email);
            bool canEmailDriver = hasBuild && !isReserves && driverHasEmail;
            _fsTripsCtxEmailDriver.Enabled = canEmailDriver;
            _fsTripsCtxEmailDriver.Text =
                !canEmailDriver && hasBuild && !isReserves && activeDriverProfile != null
                    ? "Send schedule (no email on roster)"
                    : "Send schedule…";

            // The client's name belongs on the parent row, where it is visible before the flyout
            // opens; the two actions inside then stay short and the same width.
            _fsTripsCtxBanClient.Text = isBanned ? "Add to banned list (already banned)" : "Add to banned list";
            _fsTripsCtxUnbanClient.Text = "Remove from banned list";

            if (hasTrip)
            {
                string name = (_fsTripsCtxTrip.ClientFullName ?? "").Trim();
                string age = string.IsNullOrWhiteSpace(_fsTripsCtxTrip.Age) ? "" : " · age " + _fsTripsCtxTrip.Age;
                _fsTripsCtxClientMenu.Text = name.Length == 0
                    ? "Ban client"
                    : "Ban client — " + name + age;
            }
            else
            {
                _fsTripsCtxClientMenu.Text = "Ban client";
            }

            _fsTripsCtxGroupMenu.Text = _fsTripsCtxGroup != null && !isReserves
                ? "Color group " + _fsTripsCtxGroup.GroupNumber
                : "Color group";

            _fsTripsCtxDriverMenu.Text = activeDriverProfile != null && hasBuild && !isReserves
                ? "Email driver — " + (activeDriverProfile.Name ?? _fsActiveDriverTab ?? "driver").Trim()
                : "Email driver";
        }

        /// <summary>What a paste or insert would place: "trip 10001", or "3 trips" for a batch.</summary>
        private string FsCutTripMenuNoun()
        {
            if (_fsCutTrips.Count > 1)
                return _fsCutTrips.Count + " trips";

            string num = _fsCutTrips.Count == 1
                ? (_fsCutTrips[0].Trip?.TripNumber ?? "").Trim()
                : "";
            return num.Length == 0 ? "trip" : "trip " + num;
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

                var toRow = ScheduleGroupNoteForm.Prompt(this, dialogTitle, dialogIntro, "", null, false, null);
                if (toRow == null)
                    return;

                if (string.IsNullOrWhiteSpace(toRow.NoteText) && !toRow.NoteRowColor.HasValue)
                    return;

                FsRevealGapsForManualInsert();
                FsPushUndoSnapshot(undoLabel);
                ScheduleBuilderGapNotes.ApplyAt(
                    lines, lineIndex, toRow.NoteText, toRow.NoteRowColor, toRow.CenterTextInRow, toRow.NoteTextColor);
            }
            else
            {
                bool below = placement == FsNotePlacement.Below;
                if (!TryResolveFsInsertBeforeLine(item, below, out int insertBeforeLine))
                {
                    SetScheduleBuilderStatus("Could not add a note here — select a trip, gap, or note row.");
                    return;
                }

                var inserted = ScheduleGroupNoteForm.Prompt(this, dialogTitle, dialogIntro, "", null, false, null);
                if (inserted == null)
                    return;

                if (string.IsNullOrWhiteSpace(inserted.NoteText) && !inserted.NoteRowColor.HasValue)
                    return;

                FsRevealGapsForManualInsert();
                FsPushUndoSnapshot(undoLabel);
                ScheduleBuilderGapNotes.InsertAt(
                    lines, insertBeforeLine, inserted.NoteText, inserted.NoteRowColor, inserted.CenterTextInRow, inserted.NoteTextColor);
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

            var item = FsResolveContextListItem();
            if (!_fsLinesByTab.TryGetValue(_fsActiveDriverTab, out var lines) || lines == null)
                return;

            if (item?.Tag is FsPreviewNoteTag groupNoteTag && groupNoteTag.Group != null)
            {
                int groupNumber = groupNoteTag.Group.GroupNumber;
                string current = groupNoteTag.NoteText ?? "";
                Color? currentColor = groupNoteTag.NoteRowColor;
                bool currentCenter = groupNoteTag.NoteTextCentered;
                Color? currentTextColor = groupNoteTag.NoteTextColor;
                ScheduleBuilderGroupNotes.TryReadNote(
                    lines, groupNumber, out current, out currentColor, out currentCenter, out currentTextColor);

                var edited = ScheduleGroupNoteForm.Prompt(
                    this,
                    "Edit group note",
                    "Group note text is saved in the workbook. Pick a row color to override the group color on this row only.",
                    current,
                    currentColor,
                    currentCenter,
                    currentTextColor);
                if (edited == null)
                    return;

                _fsGroupsByTab.TryGetValue(_fsActiveDriverTab, out var groups);
                FsPushUndoSnapshot("edit group note");
                ScheduleBuilderGroupNotes.ApplyNote(
                    lines,
                    groups,
                    groupNoteTag.Group,
                    edited.NoteText,
                    edited.NoteRowColor,
                    edited.CenterTextInRow,
                    edited.NoteTextColor);
                FsCommitPreviewLinesForTab(_fsActiveDriverTab, lines);
                ShowFsTripsForTab(_fsActiveDriverTab);
                SyncFsPreviewCsvsForExport();

                bool hasContent = !string.IsNullOrWhiteSpace(edited.NoteText)
                    || edited.NoteRowColor.HasValue
                    || edited.CenterTextInRow
                    || edited.NoteTextColor.HasValue;
                SetScheduleBuilderStatus(hasContent ? "Group note saved." : "Group note cleared.");
                return;
            }

            if (!ScheduleBuilderGapNotes.IsEditableNoteRow(item, lines, out int lineIndex))
                return;

            string gapCurrent = "";
            Color? gapColor = null;
            bool gapCenter = false;
            Color? gapTextColor = null;
            if (item?.Tag is FsPreviewGapTag gapTag)
            {
                gapCurrent = gapTag.NoteText ?? "";
                gapColor = gapTag.NoteRowColor;
                gapCenter = gapTag.NoteTextCentered;
                gapTextColor = gapTag.NoteTextColor;
            }

            ScheduleBuilderGapNotes.TryReadNoteAt(lines, lineIndex, out gapCurrent, out gapColor, out gapCenter, out gapTextColor);

            var gapEdited = ScheduleGroupNoteForm.Prompt(
                this,
                "Edit note",
                "Update note text, row color, and font color. Row color applies only to this note row.",
                gapCurrent,
                gapColor,
                gapCenter,
                gapTextColor);
            if (gapEdited == null)
                return;

            FsPushUndoSnapshot("edit note");
            ScheduleBuilderGapNotes.ApplyAt(
                lines, lineIndex, gapEdited.NoteText, gapEdited.NoteRowColor, gapEdited.CenterTextInRow, gapEdited.NoteTextColor);
            FsCommitPreviewLinesForTab(_fsActiveDriverTab, lines);
            ShowFsTripsForTab(_fsActiveDriverTab);
            SyncFsPreviewCsvsForExport();

            bool gapHasContent = !string.IsNullOrWhiteSpace(gapEdited.NoteText)
                || gapEdited.NoteRowColor.HasValue;
            SetScheduleBuilderStatus(gapHasContent ? "Note saved." : "Note cleared.");
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
            RequestFsMapRefresh();

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

            // Map routes often have pins in the geocode cache while the cluster arrays are empty
            // (list rebuild / cache restore). Fill those first so auto-sort matches the map.
            group = FsResolveLiveGroup(group) ?? group;
            bool endpointsReady = await ScheduleBuilderMapMileage.EnsureGroupEndpointsAsync(
                group,
                _fsMapPickupByTrip,
                _fsMapDropoffByTrip,
                CancellationToken.None).ConfigureAwait(true);
            if (!endpointsReady)
            {
                SetScheduleBuilderStatus("Group " + group.GroupNumber
                    + " · could not sort (trips need geocoded PU/DO pins).");
                return;
            }

            var (bestOrder, scorePercent, alreadyOptimal, approx) =
                await ScheduleBuilderMapMileage.FindBestTripOrderAsync(
                    group, homeGeo, dayPosition, CancellationToken.None).ConfigureAwait(true);

            if (bestOrder == null || bestOrder.Count == 0)
            {
                SetScheduleBuilderStatus("Group " + group.GroupNumber
                    + " · could not sort (routing failed — check OSRM).");
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

            SetFsLinesByTabEntry(tab, lines);

            if (fsbuilder?.PreviewDriverLines != null)
            {
                var dict = fsbuilder.PreviewDriverLines as Dictionary<string, List<ScheduleBuilderPreviewLine>>;
                if (dict != null && !tab.Equals("Reserves", StringComparison.OrdinalIgnoreCase))
                    dict[tab] = lines;
            }

            if (tab.Equals("Reserves", StringComparison.OrdinalIgnoreCase))
            {
                ScheduleBuilderReserveBuckets.ReassignBandsAndRefreshSectionCounts(lines);
                fsbuilder?.ApplyPreviewReserveLines(lines);
            }

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
        public bool CenterTextInRow { get; set; }
        public Color? NoteTextColor { get; set; }
    }

    /// <summary>Themed dialog for notes — text, optional row color, optional font color.</summary>
    internal sealed class ScheduleGroupNoteForm : SupeyForm
    {
        private const int DialogWidth = 500;
        private const int ContentWidth = 452;
        private const int DialogHeight = 468;

        private readonly TextBox _noteBox;
        private readonly Panel _swatch;
        private readonly Label _colorLabel;
        private readonly Panel _textSwatch;
        private readonly Label _textColorLabel;
        private readonly CheckBox _centerCheck;
        private Color? _noteRowColor;
        private Color? _noteTextColor;

        private ScheduleGroupNoteForm(
            string title,
            string introText,
            string initialNote,
            Color? initialRowColor,
            bool initialCenterText,
            Color? initialTextColor)
        {
            _noteRowColor = initialRowColor;
            _noteTextColor = initialTextColor;

            Text = title ?? "Note";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(DialogWidth, DialogHeight);
            MinimumSize = new Size(DialogWidth, DialogHeight);
            MaximumSize = new Size(DialogWidth, DialogHeight);
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
                RowCount = 5,
                BackColor = SupeyTheme.Surface,
            };
            stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 72f));
            stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            stack.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            var introLabel = new Label
            {
                Text = string.IsNullOrWhiteSpace(introText)
                    ? "Note text is saved in the workbook. Row color and font color apply only to this note row."
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
                Margin = new Padding(0, 0, 0, 10),
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

            var colorCard = BuildColorCard(
                "Note row color",
                "PICK COLOR…",
                "NO COLOR",
                () => _noteRowColor,
                c => { _noteRowColor = c; RefreshRowSwatch(); },
                out _swatch,
                out _colorLabel,
                fullSwatchFill: false);
            colorCard.Dock = DockStyle.Top;
            colorCard.Margin = new Padding(0, 0, 0, 8);
            stack.Controls.Add(colorCard, 0, 2);

            var textColorCard = BuildColorCard(
                "Font color",
                "PICK COLOR…",
                "DEFAULT",
                () => _noteTextColor,
                c => { _noteTextColor = c; RefreshTextSwatch(); },
                out _textSwatch,
                out _textColorLabel,
                fullSwatchFill: true);
            textColorCard.Dock = DockStyle.Top;
            textColorCard.Margin = new Padding(0, 0, 0, 4);
            stack.Controls.Add(textColorCard, 0, 3);

            _centerCheck = new CheckBox
            {
                Text = "Center text in row",
                AutoSize = true,
                Checked = initialCenterText,
                Margin = new Padding(0, 8, 0, 0),
                Font = new Font("Segoe UI", 9f),
                ForeColor = SupeyTheme.TextPrimary,
                BackColor = SupeyTheme.Surface,
            };
            stack.Controls.Add(_centerCheck, 0, 4);

            body.Controls.Add(stack);

            Controls.Add(body);
            Controls.Add(footer);
            AcceptButton = okBtn;
            CancelButton = cancelBtn;

            SupeyDarkScrollBars.Apply(this);
            RefreshRowSwatch();
            RefreshTextSwatch();
        }

        private Panel BuildColorCard(
            string caption,
            string pickText,
            string clearText,
            Func<Color?> getColor,
            Action<Color?> setColor,
            out Panel swatch,
            out Label colorLabel,
            bool fullSwatchFill)
        {
            var card = new Panel
            {
                BackColor = SupeyTheme.SurfaceElevated,
                Padding = new Padding(16, 12, 16, 12),
                Height = 86,
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
                Text = caption,
                Font = new Font("Segoe UI Semibold", 9.75f),
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 8),
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
            swatchPanel.Paint += (s, e) => SwatchPaint(swatchPanel, e, getColor(), fullSwatchFill);
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
                Text = pickText,
                AutoSize = false,
                Type = SupeyMaterialButton.MaterialButtonType.Outlined,
                Size = new Size(118, 34),
                Margin = new Padding(0, 0, 6, 0),
            };
            pickBtn.Click += (s, e) =>
            {
                using (var dlg = new ColorDialog())
                {
                    dlg.FullOpen = true;
                    dlg.Color = getColor() ?? Color.FromArgb(70, 130, 180);
                    if (dlg.ShowDialog(this) != DialogResult.OK)
                        return;
                    setColor(dlg.Color);
                }
            };

            var clearBtn = new SupeyMaterialButton
            {
                Text = clearText,
                AutoSize = false,
                Type = SupeyMaterialButton.MaterialButtonType.Text,
                UseAccentColor = false,
                NoAccentTextColor = SupeyTheme.TextSecondary,
                Size = new Size(96, 34),
                Margin = new Padding(0, 0, 0, 0),
            };
            clearBtn.Click += (s, e) => setColor(null);

            colorRow.Controls.Add(swatch, 0, 0);
            colorRow.Controls.Add(colorLabel, 1, 0);
            colorRow.Controls.Add(pickBtn, 2, 0);
            colorRow.Controls.Add(clearBtn, 3, 0);
            cardStack.Controls.Add(colorRow, 0, 1);
            card.Controls.Add(cardStack);
            return card;
        }

        private static void SwatchPaint(Panel swatch, PaintEventArgs e, Color? color, bool fullFill)
        {
            var g = e.Graphics;
            var r = swatch.ClientRectangle;
            r.Width -= 1;
            r.Height -= 1;

            using (var pen = new Pen(SupeyTheme.BorderSubtle))
                g.DrawRectangle(pen, r);

            if (!color.HasValue)
                return;

            if (fullFill)
            {
                var fill = new Rectangle(r.X + 1, r.Y + 1, r.Width - 1, r.Height - 1);
                using (var brush = new SolidBrush(color.Value))
                    g.FillRectangle(brush, fill);
                return;
            }

            int stripeW = Math.Max(6, r.Width / 5);
            var stripe = new Rectangle(r.X + 1, r.Y + 1, stripeW, r.Height - 1);
            using (var brush = new SolidBrush(color.Value))
                g.FillRectangle(brush, stripe);
        }

        public static ScheduleGroupNoteEditResult Prompt(
            IWin32Window owner,
            string title,
            string introText,
            string initialNote,
            Color? initialRowColor,
            bool initialCenterText = false,
            Color? initialTextColor = null)
        {
            using (var form = new ScheduleGroupNoteForm(
                title, introText, initialNote, initialRowColor, initialCenterText, initialTextColor))
            {
                if (form.ShowDialog(owner) != DialogResult.OK)
                    return null;

                return new ScheduleGroupNoteEditResult
                {
                    NoteText = form._noteBox.Text.Trim(),
                    NoteRowColor = form._noteRowColor,
                    CenterTextInRow = form._centerCheck.Checked,
                    NoteTextColor = form._noteTextColor,
                };
            }
        }

        private void RefreshRowSwatch()
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

        private void RefreshTextSwatch()
        {
            if (_noteTextColor.HasValue)
            {
                _textColorLabel.Text = "Custom font color";
                _textColorLabel.ForeColor = SupeyTheme.TextPrimary;
            }
            else
            {
                _textColorLabel.Text = "Auto (contrast / default)";
                _textColorLabel.ForeColor = SupeyTheme.TextSecondary;
            }

            _textSwatch.Invalidate();
        }
    }
}
