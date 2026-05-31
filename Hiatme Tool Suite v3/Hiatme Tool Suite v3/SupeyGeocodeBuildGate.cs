using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Blocks server BUILD when trip PU/DO addresses are not in the geocode cache yet.
    /// </summary>
    internal static class SupeyGeocodeBuildGate
    {
        public static (bool Ok, string Detail, int MissingEndpoints) CheckTrips(
            IList<MCDownloadedTrip> trips)
        {
            if (trips == null || trips.Count == 0)
                return (true, "No trips loaded.", 0);

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int missing = 0;

            void Consider(string street, string city)
            {
                if (string.IsNullOrWhiteSpace(street) && string.IsNullOrWhiteSpace(city))
                    return;
                string state = "ME";
                string zip = "";
                string key = (street ?? "") + "|" + (city ?? "") + "|" + state;
                if (!seen.Add(key))
                    return;
                if (!AddressGeocoder.IsCached(street, city, state, zip, "us"))
                    missing++;
            }

            foreach (var t in trips)
            {
                if (t == null) continue;
                Consider(t.PUStreet, t.PUCity);
                Consider(t.DOStreet, t.DOCITY);
            }

            if (missing == 0)
                return (true, "All trip addresses are in the geocode cache.", 0);

            return (false,
                missing + " unique trip address(es) are not geocoded yet.\r\n\r\n"
                + "After LOAD TRIPS, wait for the background geocode prefetch to finish "
                + "(status bar), or run BUILD again in a minute.\r\n\r\n"
                + "Server BUILD does not call Nominatim during solve — missing coords go to reserves.",
                missing);
        }

        public static async Task<(bool Ok, string Detail)> EnsureReadyAsync(
            IList<MCDownloadedTrip> trips,
            CancellationToken cancellationToken = default)
        {
            var (ok, detail, missing) = CheckTrips(trips);
            if (ok || !HiatmeGeoSettings.UseServer)
                return (ok, detail);

            if (missing > 0 && missing <= 40)
            {
                try
                {
                    await SupeyLoadGeocodePrefetch.PrefetchTripsAsync(trips, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    // Fall through to re-check.
                }
                var (ok2, detail2, _) = CheckTrips(trips);
                return (ok2, detail2);
            }

            return (ok, detail);
        }
    }
}
