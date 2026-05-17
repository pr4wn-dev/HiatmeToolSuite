using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Categories of soft / hard problems the build pipeline can flag on a per-trip or per-driver
    /// basis. Drives row tints in the preview ListView and the warnings count in the status strip.
    /// </summary>
    internal enum SupeyWarningKind
    {
        /// <summary>PU or DO address could not be geocoded. Trip routed to Reserves.</summary>
        MissingGeo,

        /// <summary>The driver cannot reach the trip's DO before its scheduled appointment time.</summary>
        LateArrival,

        /// <summary>The driver reaches the DO with less than a few minutes of margin (default &lt; 5 min).</summary>
        TightArrival,

        /// <summary>The driver cannot reach the next trip's PU on time given the previous DO + drive.</summary>
        LateNextPickup,

        /// <summary>OSRM routing failed for at least one leg; straight-line fallback was used.</summary>
        StraightLineFallback,

        /// <summary>Algorithm had to relax shift constraints to honor a manual lock (or similar override).</summary>
        OutsideShift,

        /// <summary>Driver's home address could not be geocoded; the driver was excluded from this build.</summary>
        DriverHomeUnresolvable,

        /// <summary>OSRM routing failed and we couldn't even fall back gracefully.</summary>
        RouteFailure,
    }

    /// <summary>
    /// One typed warning attached to a trip, a driver, or the whole build. Empty fields mean
    /// "not applicable". <see cref="Detail"/> is the human-readable explanation shown in the
    /// warnings modal and tooltips.
    /// </summary>
    internal sealed class SupeyWarning
    {
        public SupeyWarningKind Kind { get; }
        public string TripNumber { get; }
        public string DriverName { get; }
        public string Detail { get; }

        public SupeyWarning(SupeyWarningKind kind, string tripNumber, string driverName, string detail)
        {
            Kind = kind;
            TripNumber = tripNumber ?? "";
            DriverName = driverName ?? "";
            Detail = detail ?? "";
        }

        public bool IsHard =>
            Kind == SupeyWarningKind.LateArrival ||
            Kind == SupeyWarningKind.LateNextPickup ||
            Kind == SupeyWarningKind.MissingGeo ||
            Kind == SupeyWarningKind.DriverHomeUnresolvable ||
            Kind == SupeyWarningKind.RouteFailure;
    }

    /// <summary>
    /// Per-trip geographic coordinates. Populated during phase 1 (geocoding) and read by every
    /// subsequent phase. <see cref="MissingPickup"/> / <see cref="MissingDropoff"/> answer "did
    /// Nominatim fail to place this end of the trip?" without forcing callers to inspect the
    /// nullable structs every time.
    /// </summary>
    internal sealed class SupeyTripGeo
    {
        public GeoPoint? Pickup { get; set; }
        public GeoPoint? Dropoff { get; set; }
        public bool MissingPickup => !Pickup.HasValue;
        public bool MissingDropoff => !Dropoff.HasValue;
        public bool Complete => Pickup.HasValue && Dropoff.HasValue;
    }

    /// <summary>
    /// Helpers for the time/string fields on <see cref="MCDownloadedTrip"/>. Modivcare's PU/DO
    /// times come back as free-form strings ("8:30 AM", "08:30", "14:00 ", " 1430 ") so the
    /// algorithm needs forgiving parsing — but it must also be deterministic, because two trips
    /// at the same logical time are required to compare equal.
    /// </summary>
    internal static class SupeyTripTimes
    {
        /// <summary>Parses a Modivcare-style PU/DO time string to a same-day <see cref="TimeSpan"/>.</summary>
        public static TimeSpan? TryParse(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            string s = raw.Trim();

            // Some Modivcare exports drop the colon ("0830", "1430"). Re-insert before parsing.
            if (s.Length == 4 && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                s = s.Substring(0, 2) + ":" + s.Substring(2, 2);
            else if (s.Length == 3 && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                s = s.Substring(0, 1) + ":" + s.Substring(1, 2);

            if (DateTime.TryParseExact(s,
                    new[] { "h:mm tt", "hh:mm tt", "H:mm", "HH:mm", "h:mmtt", "hh:mmtt" },
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var dtExact))
                return dtExact.TimeOfDay;

            if (DateTime.TryParse(s, CultureInfo.CurrentCulture, DateTimeStyles.None, out var dt))
                return dt.TimeOfDay;

            return null;
        }

        public static TimeSpan? TryParsePU(MCDownloadedTrip t) => t == null ? (TimeSpan?)null : TryParse(t.PUTime);
        public static TimeSpan? TryParseDO(MCDownloadedTrip t) => t == null ? (TimeSpan?)null : TryParse(t.DOTime);

        /// <summary>"5:45 PM" — the format we surface in stats / dropdown items.</summary>
        public static string FormatTimeOfDay(TimeSpan? t)
        {
            if (!t.HasValue) return "—";
            var dt = DateTime.Today.Add(t.Value);
            return dt.ToString("h:mm tt", CultureInfo.CurrentCulture);
        }

        /// <summary>"6h 12m" — used by the per-driver stats strip.</summary>
        public static string FormatHoursMinutes(TimeSpan span)
        {
            if (span.Ticks < 0) span = TimeSpan.Zero;
            int h = (int)span.TotalHours;
            int m = span.Minutes;
            if (h == 0) return m + "m";
            return h + "h " + m + "m";
        }

        /// <summary>"6h 12m" from raw seconds.</summary>
        public static string FormatHoursMinutesFromSeconds(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0) seconds = 0;
            return FormatHoursMinutes(TimeSpan.FromSeconds(seconds));
        }

        /// <summary>"14.3 mi" — uses customary US miles since the Modivcare data is mile-denominated.</summary>
        public static string FormatMiles(double meters)
        {
            if (double.IsNaN(meters) || double.IsInfinity(meters) || meters < 0) meters = 0;
            double mi = meters / 1609.344;
            return mi.ToString("0.0", CultureInfo.InvariantCulture) + " mi";
        }
    }

    /// <summary>
    /// 8 dark-mode-readable hues cycled in order so each natural ride-share group gets a stable
    /// color. Index 0 is reserved for "no group / unassigned"; index 1+ map to groups 1..N.
    /// </summary>
    internal static class SupeyGroupPalette
    {
        public static readonly Color[] Colors =
        {
            Color.FromArgb(255, 92, 138),   // pink-red
            Color.FromArgb(79, 195, 247),   // sky blue
            Color.FromArgb(129, 199, 132),  // green
            Color.FromArgb(255, 183, 77),   // amber
            Color.FromArgb(186, 104, 200),  // purple
            Color.FromArgb(77, 208, 225),   // teal
            Color.FromArgb(255, 213, 79),   // yellow
            Color.FromArgb(161, 136, 127),  // taupe
        };

        public static Color For(int groupNumberOneBased)
        {
            if (groupNumberOneBased <= 0) return Color.Gray;
            int idx = (groupNumberOneBased - 1) % Colors.Length;
            return Colors[idx];
        }
    }
}
