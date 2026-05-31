using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// After LOAD TRIPS, fill the office geocode cache so BUILD rarely leaves trips without coordinates.
    /// </summary>
    internal static class SupeyLoadGeocodePrefetch
    {
        public static async Task<(int Submitted, int CachedAlready)> PrefetchTripsAsync(
            IList<MCDownloadedTrip> trips,
            CancellationToken cancellationToken = default)
        {
            if (trips == null || trips.Count == 0 || !HiatmeGeoSettings.UseServer)
                return (0, 0);

            var needNetwork = new List<(string street, string city, string state, string zip)>();
            int cached = 0;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void Consider(string street, string city)
            {
                if (string.IsNullOrWhiteSpace(street) && string.IsNullOrWhiteSpace(city))
                    return;
                string state = "ME";
                string zip = "";
                string key = (street ?? "") + "|" + (city ?? "") + "|" + state;
                if (!seen.Add(key))
                    return;
                if (AddressGeocoder.IsCached(street, city, state, zip, "us"))
                {
                    cached++;
                    return;
                }
                needNetwork.Add((street ?? "", city ?? "", state, zip));
            }

            foreach (var t in trips)
            {
                if (t == null) continue;
                cancellationToken.ThrowIfCancellationRequested();
                Consider(t.PUStreet, t.PUCity);
                Consider(t.DOStreet, t.DOCITY);
            }

            int submitted = 0;
            if (needNetwork.Count > 0)
                submitted = await AddressGeocoder.PrefetchViaServerAsync(needNetwork, cancellationToken)
                    .ConfigureAwait(false);

            return (submitted, cached);
        }
    }
}
