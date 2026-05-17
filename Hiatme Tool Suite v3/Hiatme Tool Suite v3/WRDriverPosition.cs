using System;
using System.Globalization;
using Newtonsoft.Json;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Single driver row from <c>/portal/avl/avlinitiate</c> (AVL = Automatic Vehicle Location).
    /// JSON property names match the portal payload exactly so deserialization is direct.
    /// </summary>
    internal sealed class WRDriverPosition
    {
        [JsonProperty("driverid")]
        public string DriverId { get; set; }

        [JsonProperty("drivername")]
        public string DriverName { get; set; }

        [JsonProperty("lat")]
        public double Latitude { get; set; }

        [JsonProperty("long")]
        public double Longitude { get; set; }

        [JsonProperty("bearing")]
        public double Bearing { get; set; }

        [JsonProperty("speed")]
        public double Speed { get; set; }

        [JsonProperty("delayTime")]
        public double DelayTime { get; set; }

        [JsonProperty("EtaCode")]
        public string EtaCode { get; set; }

        [JsonProperty("EtaCalcDTTM")]
        public string EtaCalcDateTimeRaw { get; set; }

        [JsonProperty("LastConnectedDTTM")]
        public string LastConnectedDateTimeRaw { get; set; }

        [JsonProperty("reportedDttm")]
        public string ReportedDateTimeRaw { get; set; }

        [JsonProperty("isExternalLoad")]
        public bool IsExternalLoad { get; set; }

        [JsonProperty("tpName")]
        public string TransportProviderName { get; set; }

        [JsonProperty("transportationType")]
        public string TransportationType { get; set; }

        [JsonProperty("vehicleName")]
        public string VehicleName { get; set; }

        [JsonProperty("vehicleType")]
        public string VehicleType { get; set; }

        /// <summary>True when both lat/long are non-zero finite numbers.</summary>
        [JsonIgnore]
        public bool HasValidLocation =>
            !double.IsNaN(Latitude) && !double.IsNaN(Longitude) &&
            !double.IsInfinity(Latitude) && !double.IsInfinity(Longitude) &&
            (Math.Abs(Latitude) > 0.0001 || Math.Abs(Longitude) > 0.0001);

        /// <summary>Best-effort parse of <see cref="ReportedDateTimeRaw"/> (e.g. <c>"05/10/2025 6:33:52 PM GMT-03:00"</c>) to a local <see cref="DateTime"/>.</summary>
        public DateTime? GetReportedLocalTime() => TryParsePortalDateTime(ReportedDateTimeRaw);

        /// <summary>Best-effort parse of <see cref="LastConnectedDateTimeRaw"/> to a local <see cref="DateTime"/>.</summary>
        public DateTime? GetLastConnectedLocalTime() => TryParsePortalDateTime(LastConnectedDateTimeRaw);

        private static DateTime? TryParsePortalDateTime(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            string trimmed = raw.Trim();
            int gmtIdx = trimmed.IndexOf(" GMT", StringComparison.OrdinalIgnoreCase);
            string datePart = gmtIdx > 0 ? trimmed.Substring(0, gmtIdx).Trim() : trimmed;
            string offsetPart = gmtIdx > 0 ? trimmed.Substring(gmtIdx + 4).Trim() : null;

            if (DateTime.TryParseExact(datePart,
                new[] { "MM/dd/yyyy h:mm:ss tt", "MM/dd/yyyy hh:mm:ss tt", "M/d/yyyy h:mm:ss tt" },
                CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out DateTime dtUtc))
            {
                if (!string.IsNullOrEmpty(offsetPart) && TryParseHourOffset(offsetPart, out TimeSpan offset))
                {
                    DateTime asUtc = dtUtc - offset;
                    return asUtc.ToLocalTime();
                }
                return dtUtc.ToLocalTime();
            }
            if (DateTime.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out DateTime dtLocal))
                return dtLocal;
            return null;
        }

        private static bool TryParseHourOffset(string s, out TimeSpan offset)
        {
            offset = TimeSpan.Zero;
            if (string.IsNullOrWhiteSpace(s)) return false;
            s = s.Trim();
            int sign = 1;
            if (s.StartsWith("+")) s = s.Substring(1);
            else if (s.StartsWith("-")) { sign = -1; s = s.Substring(1); }
            int colon = s.IndexOf(':');
            int hours, minutes = 0;
            if (colon >= 0)
            {
                if (!int.TryParse(s.Substring(0, colon), NumberStyles.Integer, CultureInfo.InvariantCulture, out hours)) return false;
                int.TryParse(s.Substring(colon + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out minutes);
            }
            else if (!int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out hours))
                return false;
            offset = new TimeSpan(sign * hours, sign * minutes, 0);
            return true;
        }
    }
}
