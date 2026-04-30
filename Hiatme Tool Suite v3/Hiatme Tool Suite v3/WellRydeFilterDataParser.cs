using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Parses WellRyde portal filterdata JSON (<c>values</c> = rows of mixed-type cells). Supports the full trip-list grid
    /// (many columns including PU/DO addresses) and the compact broker-style grid.
    /// </summary>
    internal static class WellRydeFilterDataParser
    {
        private const int MinCellsCompact = 8;
        private const int MinCellsTripList = 18;

        /// <summary>
        /// Legacy <c>WRTripDownloader.FormatTripID</c>: portal trip id <c>1-20260430-43039-B</c> becomes <c>1-43039-B</c>
        /// so <see cref="MCDownloadedTrip.TripNumber"/> / schedule rows match in <c>Analyzer.FindHiddenTrips</c> and assign flows.
        /// </summary>
        internal static string FormatTripIdForScheduleMatch(string tripid)
        {
            if (string.IsNullOrWhiteSpace(tripid))
                return "";
            try
            {
                string t = tripid.Replace(" ", "").Trim();
                string[] parts = t.Split(new[] { '-' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 4)
                    return t;
                return parts[0] + "-" + parts[2] + "-" + parts[3];
            }
            catch
            {
                return tripid.Trim();
            }
        }

        public static List<WRDownloadedTrip> ParseTrips(string filterDataJson, out int totalRecords)
        {
            totalRecords = 0;
            var list = new List<WRDownloadedTrip>();
            if (string.IsNullOrWhiteSpace(filterDataJson))
                return list;

            JObject root;
            try
            {
                root = JObject.Parse(filterDataJson);
            }
            catch
            {
                return list;
            }

            totalRecords = root["totalRecords"]?.Value<int?>() ?? 0;
            var values = root["values"] as JArray;
            if (values == null)
                return list;

            foreach (var rowToken in values)
            {
                var row = rowToken as JArray;
                if (row == null)
                    continue;

                WRDownloadedTrip trip;
                if (row.Count >= MinCellsTripList)
                    trip = ParseTripListGridRow(row);
                else if (row.Count >= MinCellsCompact)
                    trip = ParseCompactGridRow(row);
                else
                    continue;

                list.Add(trip);
            }

            return list;
        }

        /// <summary>Trip list / full vizzon.TRIP grid (see filterData column order in portal JSON).</summary>
        private static WRDownloadedTrip ParseTripListGridRow(JArray row)
        {
            string miles = CellString(row[17]);
            string rawTripId = CellString(row[1]);
            return new WRDownloadedTrip
            {
                TripUUID = CellString(row[0]),
                TripNumber = FormatTripIdForScheduleMatch(rawTripId),
                References = CellString(row[2]),
                DriverName = LinkColumnValue(CellString(row[3])),
                Status = CellString(row[5]),
                ClientName = CellString(row[6]),
                ScheduleLocation = CellString(row[7]),
                PUTime = CellString(row[8]),
                DOTime = CellString(row[9]),
                PUStreet = CellString(row[10]),
                PUCity = CellString(row[11]),
                DOStreet = CellString(row[12]),
                DOCITY = CellString(row[13]),
                Escorts = CellString(row[14]),
                ActualPUTime = CellString(row[15]),
                ActualDOTime = CellString(row[16]),
                Miles = miles,
                Age = row.Count > 18 ? CellString(row[18]) : "",
                Price = WellRydeTripPriceCalculator.FromMiles(miles)
            };
        }

        /// <summary>Compact grid (e.g. broker circulation list) with fewer columns.</summary>
        private static WRDownloadedTrip ParseCompactGridRow(JArray row)
        {
            string miles = row.Count > 9 ? CellString(row[9]) : "";
            string rawTripId = CellString(row[1]);
            return new WRDownloadedTrip
            {
                TripUUID = CellString(row[0]),
                TripNumber = FormatTripIdForScheduleMatch(rawTripId),
                References = CellString(row[2]),
                Status = CellString(row[3]),
                PUTime = CellString(row[4]),
                DOTime = "",
                DriverName = LinkColumnValue(CellString(row[6])),
                ClientName = CellString(row[7]),
                Miles = miles,
                Price = WellRydeTripPriceCalculator.FromMiles(miles),
                PUStreet = "",
                PUCity = "",
                DOStreet = "",
                DOCITY = ""
            };
        }

        private static string CellString(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null || token.Type == JTokenType.Undefined)
                return "";
            if (token.Type == JTokenType.String)
                return (((string)token) ?? "").Trim();
            return token.ToString().Trim();
        }

        private static string LinkColumnValue(string cell)
        {
            if (string.IsNullOrWhiteSpace(cell))
                return "";
            try
            {
                var o = JObject.Parse(cell);
                return (o["columnValue"]?.ToString() ?? "").Trim();
            }
            catch
            {
                return "";
            }
        }
    }
}
