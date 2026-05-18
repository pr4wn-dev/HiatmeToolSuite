using System;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Hand-placed coords for clinics/addresses Nominatim often misses on Modivcare strings.
    /// Checked before network geocoding for trip PU/DO.
    /// </summary>
    internal static class SupeyKnownFacilities
    {
        private sealed class Entry
        {
            public string StreetContains;
            public string CityContains;
            public double Lat;
            public double Lng;
        }

        private static readonly Entry[] Entries =
        {
            new Entry { StreetContains = "WELLNESS", CityContains = "TOPSHAM", Lat = 43.9230, Lng = -69.9751 },
            new Entry { StreetContains = "1 WELLNESS", CityContains = "TOPSHAM", Lat = 43.9230, Lng = -69.9751 },
            new Entry { StreetContains = "GROVE", CityContains = "LISBON FALLS", Lat = 44.0065, Lng = -70.0608 },
            new Entry { StreetContains = "OLD SUMNER", CityContains = "MECHANIC FALLS", Lat = 44.0880, Lng = -70.4240 },
            new Entry { StreetContains = "HILL ST", CityContains = "SOUTH PARIS", Lat = 44.2250, Lng = -70.5150 },
            new Entry { StreetContains = "HILL ST COMMONS", CityContains = "SOUTH PARIS", Lat = 44.2250, Lng = -70.5150 },
            new Entry { StreetContains = "FALCON", CityContains = "LEWISTON", Lat = 44.0955, Lng = -70.1920 },
            new Entry { StreetContains = "MINOT AVE", CityContains = "AUBURN", Lat = 44.0860, Lng = -70.2310 },
            new Entry { StreetContains = "1512 MINOT", CityContains = "AUBURN", Lat = 44.0860, Lng = -70.2310 },
            new Entry { StreetContains = "589 MINOT", CityContains = "AUBURN", Lat = 44.0845, Lng = -70.2285 },
            new Entry { StreetContains = "646 MAIN", CityContains = "LEWISTON", Lat = 44.0995, Lng = -70.2145 },
            new Entry { StreetContains = "618 MAIN", CityContains = "LEWISTON", Lat = 44.0980, Lng = -70.2180 },
            new Entry { StreetContains = "23 CROSS", CityContains = "AUBURN", Lat = 44.0978, Lng = -70.2265 },
            new Entry { StreetContains = "CROSS ST", CityContains = "AUBURN", Lat = 44.0978, Lng = -70.2265 },
            new Entry { StreetContains = "MANLEY", CityContains = "AUBURN", Lat = 44.0815, Lng = -70.2405 },
            new Entry { StreetContains = "100 MANLEY", CityContains = "AUBURN", Lat = 44.0815, Lng = -70.2405 },
            new Entry { StreetContains = "EAST AVE", CityContains = "LEWISTON", Lat = 44.1045, Lng = -70.2045 },
            new Entry { StreetContains = "20 EAST", CityContains = "LEWISTON", Lat = 44.1045, Lng = -70.2045 },
            new Entry { StreetContains = "STRAWBERRY", CityContains = "LEWISTON", Lat = 44.1065, Lng = -70.2110 },
            new Entry { StreetContains = "80 STRAWBERRY", CityContains = "LEWISTON", Lat = 44.1065, Lng = -70.2110 },
        };

        public static bool TryResolve(string street, string city, out GeoPoint point)
        {
            point = default(GeoPoint);
            string s = Normalize(street);
            string c = Normalize(city);
            if (s.Length == 0 && c.Length == 0) return false;

            foreach (var e in Entries)
            {
                if (c.IndexOf(e.CityContains, StringComparison.Ordinal) < 0) continue;
                if (s.IndexOf(e.StreetContains, StringComparison.Ordinal) < 0) continue;
                point = new GeoPoint(e.Lat, e.Lng);
                return true;
            }
            return false;
        }

        private static string Normalize(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            return value.Trim().ToUpperInvariant()
                .Replace(".", "")
                .Replace(",", "");
        }
    }
}
