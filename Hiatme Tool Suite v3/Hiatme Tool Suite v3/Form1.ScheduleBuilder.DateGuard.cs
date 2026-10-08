using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    partial class Form1
    {
        private bool _fsLoadInFlight;
        private bool _fsSuppressDatePickerChanged;

        /// <summary>Service date of trips currently on screen (not the date picker when it diverges).</summary>
        private DateTime FsBoardServiceDate()
        {
            if (_fsHasPreview && fsbuilder != null)
            {
                try { return fsbuilder.ServiceDate.Date; }
                catch { /* fall through */ }
            }

            return fsbdatepicker?.Value.Date ?? DateTime.Today;
        }

        private bool FsScheduleDatePickerMismatch()
        {
            if (!_fsHasPreview || fsbdatepicker == null || fsbdatepicker.IsDisposed)
                return false;
            return fsbdatepicker.Value.Date != FsBoardServiceDate();
        }

        /// <summary>
        /// Export/save filenames follow the picker only when it matches the loaded board.
        /// </summary>
        private void FsSyncServiceDateFromPickerIfAligned()
        {
            if (fsbuilder == null || fsbdatepicker == null || fsbdatepicker.IsDisposed)
                return;
            if (FsScheduleDatePickerMismatch())
                return;
            fsbuilder.ApplyServiceDate(fsbdatepicker.Value);
        }

        private void FsNoteLoadedBoardDate(DateTime serviceDate)
        {
            _fsSuppressDatePickerChanged = true;
            try
            {
                if (fsbdatepicker != null && !fsbdatepicker.IsDisposed)
                    fsbdatepicker.Value = serviceDate.Date;
            }
            finally
            {
                _fsSuppressDatePickerChanged = false;
            }

            FsUpdateScheduleDateMismatchUi();
        }

        private void fsbdatepicker_ValueChanged(object sender, EventArgs e)
        {
            if (_fsSuppressDatePickerChanged)
                return;
            FsUpdateScheduleDateMismatchUi();
        }

        private void FsUpdateScheduleDateMismatchUi()
        {
            if (_fsToolbarStatusLbl == null || _fsToolbarStatusLbl.IsDisposed)
                return;

            bool mismatch = FsScheduleDatePickerMismatch();
            if (fsbdatepicker != null && !fsbdatepicker.IsDisposed)
            {
                fsbdatepicker.BorderColor = mismatch
                    ? Color.FromArgb(220, 120, 40)
                    : SupeyTheme.BorderSubtle;
            }

            if (!mismatch)
            {
                if (fsbdatepicker != null && !fsbdatepicker.IsDisposed)
                    fsbdatepicker.BorderColor = SupeyTheme.BorderSubtle;
                return;
            }

            FsAppendDateMismatchToStatus();
        }

        private bool FsConfirmLoadServiceDate(DateTime pickerDay, DateTime? fileDay)
        {
            if (!fileDay.HasValue)
                return true;
            if (fileDay.Value.Date == pickerDay.Date)
                return true;

            return SupeyMessageDialog.Confirm(
                       this,
                       SupeyMessageDialog.Kind.Warning,
                       "Schedule Builder",
                       "Different service date",
                       "This file is for "
                       + fileDay.Value.ToString("dddd, MMMM d, yyyy", CultureInfo.InvariantCulture)
                       + ", but the date picker is "
                       + pickerDay.ToString("dddd, MMMM d, yyyy", CultureInfo.InvariantCulture)
                       + ".\n\nLoad this file anyway?",
                       "Load file",
                       "Cancel")
                   == DialogResult.Yes;
        }

        private void FsAppendDateMismatchToStatus()
        {
            if (_fsToolbarStatusLbl == null || _fsToolbarStatusLbl.IsDisposed)
                return;
            if (!FsScheduleDatePickerMismatch())
                return;

            string board = FsBoardServiceDate().ToString("dddd, MMM d", CultureInfo.InvariantCulture);
            string pick = fsbdatepicker?.Value.Date.ToString("dddd, MMM d", CultureInfo.InvariantCulture)
                ?? board;
            string suffix = "On screen: " + board + " — picker: " + pick + ". Click LOAD to switch days.";
            string cur = _fsToolbarStatusLbl.Text ?? "";
            if (cur.IndexOf("On screen:", StringComparison.OrdinalIgnoreCase) < 0)
                _fsToolbarStatusLbl.Text = string.IsNullOrWhiteSpace(cur) ? suffix : cur + " — " + suffix;
            _fsToolbarStatusLbl.ForeColor = Color.FromArgb(220, 120, 40);
            if (fsbdatepicker != null && !fsbdatepicker.IsDisposed)
                fsbdatepicker.BorderColor = Color.FromArgb(220, 120, 40);
        }

        private bool FsBlockSaveWhenDatePickerMismatch(string actionLabel)
        {
            if (!FsScheduleDatePickerMismatch())
                return false;

            DateTime board = FsBoardServiceDate();
            DateTime pick = fsbdatepicker?.Value.Date ?? board;
            SupeyMessageDialog.ShowWarning(
                this,
                "Schedule Builder",
                "Date picker does not match the schedule on screen",
                "The board still shows "
                + board.ToString("dddd, MMMM d, yyyy", CultureInfo.InvariantCulture)
                + ", but the date picker is "
                + pick.ToString("dddd, MMMM d, yyyy", CultureInfo.InvariantCulture)
                + ".\n\nClick LOAD for the day you want before "
                + (actionLabel ?? "saving")
                + ".");
            FsUpdateScheduleDateMismatchUi();
            return true;
        }
    }
}
