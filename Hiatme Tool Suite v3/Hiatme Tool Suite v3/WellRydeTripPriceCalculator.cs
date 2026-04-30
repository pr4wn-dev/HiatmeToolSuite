using System;
using System.Globalization;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Mileage-based trip price table ported from legacy <c>WRTripDownloader</c> (WellRyde billing list).
    /// </summary>
    internal static class WellRydeTripPriceCalculator
    {
        private const decimal Tier1MaxMiles = 3;
        private const decimal Tier2MaxMiles = 6;
        private const decimal Tier3MaxMiles = 10;

        private const string Price0To3 = "11.25";
        private const string Price4To6 = "23.07";
        private const string Price7To10 = "28.21";

        /// <summary>First mile in the sliding segment (mile 11) matches legacy chart base.</summary>
        private const decimal Mile11BasePrice = 30.21m;

        private const decimal PerMileOver10 = 2m;

        /// <summary>Returns a price string suitable for <see cref="WRDownloadedTrip.Price"/> (no currency symbol).</summary>
        public static string FromMiles(string milesRaw)
        {
            if (string.IsNullOrWhiteSpace(milesRaw))
                return "0";

            if (!decimal.TryParse(milesRaw.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var tempMiles))
            {
                if (!decimal.TryParse(milesRaw.Trim(), NumberStyles.Any, CultureInfo.CurrentCulture, out tempMiles))
                    return "0";
            }

            int milesInt = (int)decimal.Round(tempMiles, MidpointRounding.AwayFromZero);
            if (milesInt < 1)
                milesInt = 1;

            try
            {
                if (milesInt <= Tier1MaxMiles)
                    return Price0To3;
                if (milesInt <= Tier2MaxMiles)
                    return Price4To6;
                if (milesInt <= Tier3MaxMiles)
                    return Price7To10;

                // Legacy chart: mile 11 = 30.21, then +2 per whole mile.
                decimal price = Mile11BasePrice + (milesInt - 11) * PerMileOver10;
                return price.ToString(CultureInfo.InvariantCulture);
            }
            catch
            {
                return "0";
            }
        }
    }
}
