using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Modal list of Trip Scout server activity (filtered trip changes or will-call bell alerts).
    /// Double-click a row to jump to that trip in the main Trip Scout list.
    /// </summary>
    internal sealed class TripScoutActivityForm : SupeyForm
    {
        public enum ActivityMode
        {
            Changes,
            WillCalls,
        }

        private readonly ActivityMode _mode;
        private readonly ListView _list;
        private readonly Label _hint;

        public string SelectedTripNo { get; private set; }

        public TripScoutActivityForm(
            ActivityMode mode,
            string serviceDate,
            IList<HiatmeAiClient.TripScoutChangeRow> changes,
            IList<HiatmeAiClient.WellRydeBellWillCall> willcalls)
        {
            _mode = mode;
            Text = mode == ActivityMode.Changes
                ? "Trip Scout — changes (" + serviceDate + ")"
                : "Trip Scout — will calls ready";
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(720, 420);
            Size = new Size(920, 520);

            _hint = new Label
            {
                AutoSize = false,
                Dock = DockStyle.Top,
                Height = 36,
                Padding = new Padding(12, 10, 12, 0),
                ForeColor = SupeyTheme.TextSecondary,
                BackColor = SupeyTheme.SurfaceHeader,
                Font = SupeyTheme.CaptionFont,
                Text = mode == ActivityMode.Changes
                    ? "Significant WellRyde changes for this day (routine assign/unassign hidden). Double-click a row to locate the trip."
                    : "Will-call activations from the WellRyde bell. Double-click a row to locate the trip.",
            };

            _list = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                HideSelection = false,
                BorderStyle = BorderStyle.None,
                BackColor = SupeyTheme.ListBody,
                ForeColor = SupeyTheme.ListText,
                Font = SupeyTheme.BodyFont,
            };
            SupeyListViewHelpers.EnableDoubleBufferRecursively(_list);
            ListViewMinWidthEnforcer.Attach(_list);

            if (mode == ActivityMode.Changes)
            {
                _list.Columns.Add("Time", 72);
                _list.Columns.Add("Trip #", 160);
                _list.Columns.Add("Client", 140);
                _list.Columns.Add("Change", 480);
                PopulateChanges(changes);
            }
            else
            {
                _list.Columns.Add("Trip #", 160);
                _list.Columns.Add("Rider", 160);
                _list.Columns.Add("Pickup", 320);
                _list.Columns.Add("When", 120);
                PopulateWillCalls(willcalls);
            }

            _list.DoubleClick += (_, __) => AcceptSelection();
            _list.KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.Handled = true;
                    AcceptSelection();
                }
            };

            var host = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(12, 8, 12, 12),
                BackColor = SupeyTheme.SurfaceElevated,
            };
            host.Controls.Add(_list);
            host.Controls.Add(_hint);

            Controls.Add(host);
            SupeyDarkScrollBars.Apply(this);
        }

        private void PopulateChanges(IList<HiatmeAiClient.TripScoutChangeRow> changes)
        {
            _list.Items.Clear();
            if (changes == null)
                return;

            foreach (var row in changes)
            {
                if (row == null || string.IsNullOrWhiteSpace(row.TripNo))
                    continue;
                var item = new ListViewItem(FormatTs(row.Ts));
                item.SubItems.Add(row.TripNo.Trim());
                item.SubItems.Add(row.Client ?? "");
                item.SubItems.Add(row.Summary ?? "");
                item.Tag = row.TripNo.Trim();
                _list.Items.Add(item);
            }
        }

        private void PopulateWillCalls(IList<HiatmeAiClient.WellRydeBellWillCall> willcalls)
        {
            _list.Items.Clear();
            if (willcalls == null)
                return;

            foreach (var row in willcalls)
            {
                if (row == null || string.IsNullOrWhiteSpace(row.TripNo))
                    continue;
                var item = new ListViewItem(row.TripNo.Trim());
                item.SubItems.Add(row.Rider ?? "");
                item.SubItems.Add(row.PuAddr ?? "");
                item.SubItems.Add(FormatIso(row.CreatedAt));
                item.Tag = row.TripNo.Trim();
                _list.Items.Add(item);
            }
        }

        private void AcceptSelection()
        {
            if (_list.SelectedItems.Count == 0)
                return;
            SelectedTripNo = _list.SelectedItems[0].Tag as string;
            if (string.IsNullOrWhiteSpace(SelectedTripNo))
                return;
            DialogResult = DialogResult.OK;
            Close();
        }

        private static string FormatTs(double? ts)
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

        private static string FormatIso(string iso)
        {
            if (string.IsNullOrWhiteSpace(iso))
                return "";
            if (DateTime.TryParse(
                    iso,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out var dt))
            {
                return dt.ToLocalTime().ToString("h:mm tt", CultureInfo.CurrentCulture);
            }
            return iso;
        }
    }
}
