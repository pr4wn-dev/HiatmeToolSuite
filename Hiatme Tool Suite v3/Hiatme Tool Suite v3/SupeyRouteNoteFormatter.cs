using System;
using System.Collections.Generic;
using System.Text;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>Plain-language pickup/drop instructions for Supey schedule group headers.</summary>
    internal static class SupeyRouteNoteFormatter
    {
        public static string Format(SupeyTripCluster g)
        {
            if (g == null || g.Trips.Count == 0) return "";

            var puStops = DescribePickupStops(g);
            var doStops = DescribeDropoffStops(g);
            var sb = new StringBuilder();

            if (g.RiderCount == 1)
            {
                sb.Append("Pick up ").Append(puStops[0]).Append(". Drop ").Append(doStops[0]).Append('.');
                return sb.ToString();
            }

            if (SharesSinglePickupAddress(g))
            {
                string place = ShortPlace(g.Trips[0].PUStreet, g.Trips[0].PUCity);
                sb.Append("Pick up all ").Append(g.RiderCount).Append(" riders");
                if (!string.IsNullOrEmpty(place)) sb.Append(" at ").Append(place);
                sb.Append(" (").Append(SupeyTripTimes.FormatTimeOfDay(g.EarliestPickup));
                if (g.LatestPickup != g.EarliestPickup)
                    sb.Append("–").Append(SupeyTripTimes.FormatTimeOfDay(g.LatestPickup));
                sb.Append("). ");
            }
            else
            {
                sb.Append("Pick up: ").Append(string.Join(" → ", puStops)).Append(". ");
            }

            if (g.RiderCount >= 2 && (g.LatestPickup - g.EarliestPickup).TotalMinutes >= 12)
                sb.Append("Riders ride together between pickups. ");

            sb.Append("Drop: ").Append(string.Join(" → ", doStops)).Append('.');
            return sb.ToString();
        }

        private static List<string> DescribePickupStops(SupeyTripCluster g)
        {
            var list = new List<string>();
            int n = g.PickupOrder.Count > 0 ? g.PickupOrder.Count : g.Trips.Count;
            for (int step = 0; step < n; step++)
            {
                int idx = g.PickupOrder.Count > step ? g.PickupOrder[step] : step;
                list.Add(DescribePickup(g.Trips[idx]));
            }
            return list;
        }

        private static List<string> DescribeDropoffStops(SupeyTripCluster g)
        {
            var list = new List<string>();
            for (int step = 0; step < g.DropoffOrder.Count; step++)
            {
                int idx = g.DropoffOrder[step];
                list.Add(DescribeDropoff(g.Trips[idx]));
            }
            if (list.Count == 0 && g.Trips.Count == 1)
                list.Add(DescribeDropoff(g.Trips[0]));
            return list;
        }

        private static string DescribePickup(MCDownloadedTrip t)
        {
            string name = ShortClient(t);
            string place = ShortPlace(t.PUStreet, t.PUCity);
            string time = SupeyTripTimes.FormatTimeOfDay(SupeyTripTimes.TryParsePU(t));
            var sb = new StringBuilder(name);
            if (!string.IsNullOrEmpty(place)) sb.Append(" (").Append(place).Append(')');
            if (time != "—") sb.Append(' ').Append(time);
            return sb.ToString();
        }

        private static string DescribeDropoff(MCDownloadedTrip t)
        {
            string name = ShortClient(t);
            string place = ShortPlace(t.DOStreet, t.DOCITY);
            var appt = SupeyTripTimes.TryParseDO(t);
            var sb = new StringBuilder(name);
            if (!string.IsNullOrEmpty(place)) sb.Append(" @ ").Append(place);
            if (appt.HasValue) sb.Append(' ').Append(SupeyTripTimes.FormatTimeOfDay(appt)).Append(" appt");
            return sb.ToString();
        }

        private static string ShortClient(MCDownloadedTrip t)
        {
            if (t == null) return "rider";
            if (!string.IsNullOrWhiteSpace(t.ClientLastName))
                return t.ClientLastName.Trim();
            string full = (t.ClientFullName ?? "").Trim();
            if (full.Length == 0) return "rider";
            int comma = full.IndexOf(',');
            if (comma > 0) return full.Substring(0, comma).Trim();
            int space = full.IndexOf(' ');
            return space > 0 ? full.Substring(0, space) : full;
        }

        private static string ShortPlace(string street, string city)
        {
            string c = (city ?? "").Trim();
            if (!string.IsNullOrEmpty(c)) return c;
            string s = (street ?? "").Trim();
            if (s.Length > 28) s = s.Substring(0, 28) + "…";
            return s;
        }

        private static bool SharesSinglePickupAddress(SupeyTripCluster g)
        {
            if (g == null || g.Trips.Count <= 1) return true;
            string key = PickupAddressKey(g.Trips[0]);
            for (int i = 1; i < g.Trips.Count; i++)
            {
                if (!string.Equals(key, PickupAddressKey(g.Trips[i]), StringComparison.OrdinalIgnoreCase))
                    return false;
            }
            return true;
        }

        private static string PickupAddressKey(MCDownloadedTrip t)
        {
            var s = (t?.PUStreet ?? "").Trim().ToUpperInvariant();
            var c = (t?.PUCity ?? "").Trim().ToUpperInvariant();
            if (s.Length > 30) s = s.Substring(0, 30);
            return s + "|" + c;
        }
    }
}
