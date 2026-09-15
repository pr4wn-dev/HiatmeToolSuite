using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Learned early/late room for the Suggest yes/no only. Empty during BUILD.
    /// </summary>
    internal sealed class SuggestRiderWindow
    {
        public string TripNumber { get; set; } = "";
        public string Client { get; set; } = "";
        public double EarlyOkMin { get; set; }
        public double AllowedLateMin { get; set; }
        public bool Hard { get; set; }
    }

    internal static class SuggestRiderWindows
    {
        private static readonly AsyncLocal<List<SuggestRiderWindow>> Held =
            new AsyncLocal<List<SuggestRiderWindow>>();

        public static void Clear() => Held.Value = null;

        public static bool TryGet(MCDownloadedTrip trip, out SuggestRiderWindow win)
        {
            win = null;
            var list = Held.Value;
            if (list == null || trip == null)
                return false;

            string tn = (trip.TripNumber ?? "").Trim();
            string client = (trip.ClientFullName ?? "").Trim();
            if (tn.Length > 0)
            {
                foreach (var row in list)
                {
                    if (string.Equals(row.TripNumber, tn, StringComparison.OrdinalIgnoreCase))
                    {
                        win = row;
                        return true;
                    }
                }
            }
            if (client.Length > 0)
            {
                foreach (var row in list)
                {
                    if (string.Equals(row.Client, client, StringComparison.OrdinalIgnoreCase))
                    {
                        win = row;
                        return true;
                    }
                }
            }
            return false;
        }

        public static double DoLateCap(MCDownloadedTrip trip)
        {
            double cap = SupeyTripTimingPolicy.DoLateCapMinutes(trip);
            if (!TryGet(trip, out var w) || w == null)
                return cap;
            if (w.Hard)
                return 0;
            return Math.Max(cap, w.AllowedLateMin);
        }

        public static async Task LoadAsync(
            IEnumerable<MCDownloadedTrip> trips,
            DateTime? serviceDate,
            CancellationToken token)
        {
            Clear();
            var settings = HiatmeAiSettings.Load();
            if (settings == null || trips == null)
                return;

            var payload = new List<object>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var trip in trips)
            {
                if (trip == null)
                    continue;
                string tn = (trip.TripNumber ?? "").Trim();
                string client = (trip.ClientFullName ?? "").Trim();
                string key = tn.Length > 0 ? tn : client;
                if (key.Length == 0 || !seen.Add(key))
                    continue;
                string puCity = (trip.PUCity ?? "").Trim();
                string doCity = (trip.DOCITY ?? "").Trim();
                string corridor = (puCity.Length > 0 && doCity.Length > 0)
                    ? (puCity + "|" + doCity).ToLowerInvariant()
                    : "";
                payload.Add(new
                {
                    trip_no = tn,
                    trip_number = tn,
                    client,
                    corridor,
                    comments = trip.Comments ?? "",
                });
            }
            if (payload.Count == 0)
                return;

            string sd = serviceDate.HasValue ? serviceDate.Value.ToString("yyyy-MM-dd") : "";
            var rows = await HiatmeAiClient.GetRiderWindowsAsync(
                settings, new { service_date = sd, trips = payload }, token)
                .ConfigureAwait(false);
            if (rows == null || rows.Count == 0)
                return;
            Held.Value = rows;
        }
    }
}
