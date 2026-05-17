using System;
using System.Globalization;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Persistent record for one driver in the Supey Schedule roster. Captures everything the
    /// algorithm needs to score (driver, group) candidates: a starting/ending location, a hard
    /// passenger capacity, and a working window. Vehicle label is purely cosmetic — appears in
    /// the roster ListView and tooltips so dispatchers can recognize the rig.
    /// </summary>
    /// <remarks>
    /// Persisted as JSON in <c>{AppContext.BaseDirectory}\SupeyDrivers.json</c> via
    /// <see cref="SupeyDriverRosterStore"/>. Hand-edits are tolerated as long as the file remains
    /// valid JSON; corrupt saves are caught by the loader and a <c>.bak</c> from the previous
    /// successful save is kept alongside.
    /// </remarks>
    internal sealed class SupeyDriverProfile
    {
        public string Name { get; set; } = "";
        public string HomeStreet { get; set; } = "";
        public string HomeCity { get; set; } = "";
        public string HomeState { get; set; } = "";
        public string HomeZip { get; set; } = "";

        /// <summary>
        /// WellRyde portal user id (e.g. <c>SEC-W-lHn9ad26_xTsgT5HZhEA</c>) when this row was
        /// imported from WellRyde. Empty for manually-added drivers. Used as a stable dedupe key
        /// when re-pulling the roster (so re-imports merge instead of duplicate) and as the target
        /// id for the future "push edited address back to WellRyde" sync.
        /// </summary>
        public string WellRydeSecId { get; set; } = "";

        /// <summary>
        /// WellRyde username (e.g. <c>jbrown</c>) when imported. Helps users recognize who's who
        /// in the roster ListView and is the human-readable handle used in the portal UI. Optional
        /// — empty for manually-added drivers.
        /// </summary>
        public string WellRydeUsername { get; set; } = "";

        /// <summary>
        /// UTC timestamp of the last successful pull from WellRyde for this driver. Used to show
        /// "synced X minutes ago" in the editor and to gate the future push-back ("you edited
        /// since last sync — push to WellRyde?").
        /// </summary>
        public DateTime? WellRydeSyncedAtUtc { get; set; }

        /// <summary>Hard passenger ceiling at any moment in the day (sedan ~4, van ~6–8).</summary>
        public int CapacityPassengers { get; set; } = 4;

        /// <summary>Cosmetic — display only. Empty is fine.</summary>
        public string VehicleLabel { get; set; } = "";

        /// <summary>"HH:mm" 24-hour. Empty means "no shift start floor".</summary>
        public string ShiftStart { get; set; } = "06:00";

        /// <summary>"HH:mm" 24-hour. Empty means "no shift end ceiling".</summary>
        public string ShiftEnd { get; set; } = "18:00";

        /// <summary>One-line "123 Main St, Dayton OH 45402" used for display + geocode lookups.</summary>
        public string FormatHomeOneLine()
        {
            var street = (HomeStreet ?? "").Trim();
            var city = (HomeCity ?? "").Trim();
            var state = (HomeState ?? "").Trim();
            var zip = (HomeZip ?? "").Trim();

            string locality;
            if (state.Length > 0 && zip.Length > 0) locality = (city + " " + state + " " + zip).Trim();
            else if (state.Length > 0) locality = (city + " " + state).Trim();
            else locality = (city + " " + zip).Trim();
            locality = locality.Trim();

            if (street.Length > 0 && locality.Length > 0) return street + ", " + locality;
            if (street.Length > 0) return street;
            return locality;
        }

        /// <summary>Parses <see cref="ShiftStart"/>; returns null on empty / unparseable.</summary>
        public TimeSpan? ParseShiftStart() => ParseHHmm(ShiftStart);

        /// <summary>Parses <see cref="ShiftEnd"/>; returns null on empty / unparseable.</summary>
        public TimeSpan? ParseShiftEnd() => ParseHHmm(ShiftEnd);

        private static TimeSpan? ParseHHmm(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            var s = raw.Trim();
            // Accept both "8:30" and "08:30"; reject single-digit-only.
            if (TimeSpan.TryParseExact(s, new[] { @"h\:mm", @"hh\:mm", @"H\:mm", @"HH\:mm" },
                    CultureInfo.InvariantCulture, out var ts))
                return ts;
            // Last-ditch — try DateTime parsing so "8:30 AM" still works.
            if (DateTime.TryParse(s, CultureInfo.CurrentCulture, DateTimeStyles.None, out var dt))
                return dt.TimeOfDay;
            return null;
        }
    }
}
