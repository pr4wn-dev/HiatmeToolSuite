using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    partial class Form1
    {
        private SupeyButton _tripScoutTestChangeBtn;
        private int _tripScoutSimulateTripIndex;
        private int _tripScoutSimulateScenarioIndex;

        private void EnsureTripScoutSimulateToolbar()
        {
            if (_tripScoutToolbarRightFlow == null || _tripScoutToolbarRightFlow.IsDisposed)
                return;

            if (_tripScoutTestChangeBtn != null && !_tripScoutTestChangeBtn.IsDisposed)
                return;

            _tripScoutTestChangeBtn = MakeTripScoutActivityButton("Test change", TripScoutTestChangeBtn_Click);
            _tripScoutTestChangeBtn.Size = new System.Drawing.Size(108, 30);
            _tripScoutTestChangeBtn.Kind = SupeyButton.Variant.Secondary;

            _tripScoutToolbarRightFlow.Controls.Add(MakeFsToolbarSeparator());
            _tripScoutToolbarRightFlow.Controls.Add(_tripScoutTestChangeBtn);
        }

        private async void TripScoutTestChangeBtn_Click(object sender, EventArgs e)
        {
            bool useServer = (ModifierKeys & Keys.Control) == Keys.Control;
            if (useServer)
            {
                await TripScoutSimulateChangeViaServerAsync().ConfigureAwait(true);
                return;
            }

            TripScoutSimulateChangeLocally();
        }

        internal void TripScoutSimulateChangeLocally()
        {
            if (_tripScoutAllTrips == null || _tripScoutAllTrips.Count == 0)
            {
                if (tsstatuslbl != null)
                    tsstatuslbl.Text = "Status: Load trips first, then click Test change.";
                return;
            }

            int tripIdx = _tripScoutSimulateTripIndex % _tripScoutAllTrips.Count;
            _tripScoutSimulateTripIndex++;
            var trip = _tripScoutAllTrips[tripIdx];
            if (trip == null)
                return;

            var row = TripScoutBuildSimulatedChangeRow(trip, _tripScoutSimulateScenarioIndex);
            _tripScoutSimulateScenarioIndex = (_tripScoutSimulateScenarioIndex + 1) % 5;

            if (_tripScoutDayChanges == null)
                _tripScoutDayChanges = new List<HiatmeAiClient.TripScoutChangeRow>();
            _tripScoutDayChanges.Insert(0, row);

            _tripScoutChangesServerHash = TripScoutComputeLocalChangesHash();
            RebuildTripScoutChangesByTrip();
            RebuildTripScoutNewChangeTripNosFromHash();
            TripScoutProcessNewChangeAlerts();
            UpdateTripScoutActivityButtons();
            TripScoutApplyRowHighlights();

            if (tsstatuslbl != null)
            {
                tsstatuslbl.Text = "Status: Simulated — trip "
                    + (trip.TripNumber ?? "?")
                    + ": "
                    + TripScoutChangeFormat.FormatDiff(row)
                    + ". (Ctrl+Test change writes to server journal.)";
            }
        }

        private async Task TripScoutSimulateChangeViaServerAsync()
        {
            if (_tripScoutAllTrips == null || _tripScoutAllTrips.Count == 0)
            {
                if (tsstatuslbl != null)
                    tsstatuslbl.Text = "Status: Load trips first, then Ctrl+click Test change.";
                return;
            }

            var settings = TripScoutAiSettings();
            if (settings == null || string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                if (tsstatuslbl != null)
                    tsstatuslbl.Text = "Status: AI server URL not configured for server simulate.";
                return;
            }

            int tripIdx = _tripScoutSimulateTripIndex % _tripScoutAllTrips.Count;
            var trip = _tripScoutAllTrips[tripIdx];
            string[] scenarios = { "driver_swap", "cancelled", "sched_pu", "sched_do", "address" };
            string scenario = scenarios[_tripScoutSimulateScenarioIndex % scenarios.Length];
            _tripScoutSimulateTripIndex++;
            _tripScoutSimulateScenarioIndex++;

            if (tsstatuslbl != null)
                tsstatuslbl.Text = "Status: Injecting simulated change on server…";

            var result = await HiatmeAiClient.SimulateTripScoutChangeAsync(
                settings,
                TripScoutSelectedServiceDateIso(),
                trip?.TripNumber ?? "",
                scenario,
                trip?.ClientName ?? "",
                trip?.DriverName ?? "",
                CancellationToken.None).ConfigureAwait(true);

            if (result == null || !result.Ok)
            {
                if (tsstatuslbl != null)
                    tsstatuslbl.Text = "Status: Server simulate failed — "
                        + (result?.Error ?? "unknown")
                        + ". Set HIATME_ALLOW_SIMULATE=1 on the panel, or use Test change without Ctrl.";
                return;
            }

            ApplyTripScoutChangesPayload(result);
            TripScoutApplyRowHighlights();
            if (tsstatuslbl != null && result.Changes != null && result.Changes.Count > 0)
            {
                tsstatuslbl.Text = "Status: Server simulated — trip "
                    + (trip?.TripNumber ?? "?")
                    + ": "
                    + TripScoutChangeFormat.FormatDiff(result.Changes[0]);
            }
        }

        private static HiatmeAiClient.TripScoutChangeRow TripScoutBuildSimulatedChangeRow(
            WRDownloadedTrip trip,
            int scenarioIndex)
        {
            string tripNo = (trip.TripNumber ?? "").Trim();
            string client = (trip.ClientName ?? "").Trim();
            string driver = (trip.DriverName ?? "").Trim();
            if (driver.Length == 0)
                driver = "Unassigned";

            string statusBefore = string.IsNullOrWhiteSpace(trip.Status) ? "Scheduled" : trip.Status.Trim();
            string puBefore = FormatTimeOnly(trip.PUTime ?? "") ?? "9:00 AM";
            string doBefore = FormatTimeOnly(trip.DOTime ?? "") ?? "3:00 PM";
            string puAddrBefore = TripScoutJoinAddr(trip.PUStreet, trip.PUCity);
            if (puAddrBefore.Length == 0)
                puAddrBefore = "100 Main St";

            var fields = new List<HiatmeAiClient.TripScoutChangeFieldRow>();
            var tags = new List<string> { "updated" };

            switch (scenarioIndex % 5)
            {
                case 1:
                    fields.Add(new HiatmeAiClient.TripScoutChangeFieldRow
                    {
                        Field = "status",
                        Before = statusBefore,
                        After = "Cancelled",
                    });
                    tags.Add("status_changed");
                    tags.Add("cancelled");
                    break;
                case 2:
                    fields.Add(new HiatmeAiClient.TripScoutChangeFieldRow
                    {
                        Field = "sched_pu_iso",
                        Before = "2026-01-01T" + PuDisplayToIsoTime(puBefore),
                        After = "2026-01-01T09:30:00",
                    });
                    tags.Add("time_changed");
                    break;
                case 3:
                    fields.Add(new HiatmeAiClient.TripScoutChangeFieldRow
                    {
                        Field = "sched_do_iso",
                        Before = "2026-01-01T" + PuDisplayToIsoTime(doBefore),
                        After = "2026-01-01T15:45:00",
                    });
                    tags.Add("time_changed");
                    break;
                case 4:
                    fields.Add(new HiatmeAiClient.TripScoutChangeFieldRow
                    {
                        Field = "pu_address",
                        Before = puAddrBefore,
                        After = "200 Oak Ave",
                    });
                    tags.Add("address_changed");
                    break;
                default:
                    fields.Add(new HiatmeAiClient.TripScoutChangeFieldRow
                    {
                        Field = "driver",
                        Before = driver,
                        After = "Sim Driver " + ((scenarioIndex % 3) + 1),
                    });
                    tags.Add("driver_changed");
                    break;
            }

            var row = new HiatmeAiClient.TripScoutChangeRow
            {
                Ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                ServiceDate = "",
                TripNo = tripNo,
                Client = client,
                Driver = driver,
                Kind = "updated",
                Tags = tags,
                Fields = fields,
            };
            row.Summary = TripScoutChangeFormat.FormatDiff(row);
            return row;
        }

        private static string TripScoutJoinAddr(string street, string city)
        {
            street = (street ?? "").Trim();
            city = (city ?? "").Trim();
            if (street.Length > 0 && city.Length > 0)
                return street + ", " + city;
            return street.Length > 0 ? street : city;
        }

        private static string PuDisplayToIsoTime(string display)
        {
            if (DateTime.TryParse(display, CultureInfo.CurrentCulture, DateTimeStyles.None, out var dt))
                return dt.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            return "09:00:00";
        }

        private string TripScoutComputeLocalChangesHash()
        {
            if (_tripScoutDayChanges == null || _tripScoutDayChanges.Count == 0)
                return "local-sim-empty";

            var parts = _tripScoutDayChanges
                .Where(r => r != null)
                .Select(r =>
                    (r.TripNo ?? "") + "|"
                    + (r.Ts?.ToString(CultureInfo.InvariantCulture) ?? "") + "|"
                    + TripScoutChangeFormat.FormatDiff(r))
                .OrderBy(s => s, StringComparer.Ordinal);
            return "local-sim-" + string.Join(";", parts).GetHashCode().ToString(CultureInfo.InvariantCulture);
        }
    }
}
