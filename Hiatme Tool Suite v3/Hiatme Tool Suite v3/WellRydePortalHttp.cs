using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Globalization;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Shared helpers for WellRyde portal responses (PHP scraper parity: JSON vs HTML, session loss).
    /// </summary>
    public static class WellRydeTripParsing
    {
        /// <summary>Resolve calendar date for WellRyde <c>specificDate</c> (PHP: <c>date('F j, Y')</c>).</summary>
        public static DateTime ResolveTripDate(string longdate, int day, int year)
        {
            if (DateTime.TryParse(longdate, CultureInfo.CurrentCulture, DateTimeStyles.None, out var dt))
                return dt.Date;
            string monthandday = Regex.Match(longdate, @"(?<=^([^,]*,){1})([^,]*)").Value;
            string monthName = Regex.Match(monthandday, @"(?<=^([^ ]* ){1})([^ ]*)").Value;
            if (!string.IsNullOrEmpty(monthName))
            {
                var s = monthName + " " + day + ", " + year;
                if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
                    return dt.Date;
            }
            return new DateTime(year, 1, Math.Max(1, Math.Min(day, 28)));
        }

        /// <summary>
        /// Inner JSON for VTripBilling <c>filterList</c> sequence 2 (stringified in the form). Chrome HAR uses <c>{"period":"0d"}</c> for the current calendar day in the UI;
        /// other days use <c>specificDate</c> (English, invariant) so historical batches match the portal filter.
        /// </summary>
        public static string BuildVtTripBillingDateSlotValueJson(DateTime tripDate)
        {
            if (tripDate.Date == DateTime.Today)
                return JsonConvert.SerializeObject(new { period = "0d" });
            return JsonConvert.SerializeObject(new
            {
                specificDate = tripDate.Date.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture),
            });
        }

        public static bool LooksLikeNonJsonPayload(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
                return true;
            var t = body.TrimStart();
            if (t.Length > 0 && t[0] == '\uFEFF')
                t = t.Substring(1).TrimStart();
            if (t.StartsWith("<", StringComparison.Ordinal))
                return true;
            if (t.StartsWith("<!DOCTYPE", StringComparison.OrdinalIgnoreCase))
                return true;
            return false;
        }

        public static int ParseWellRydeTotalRecords(JToken tr)
        {
            if (tr == null || tr.Type == JTokenType.Null)
                return 0;
            if (tr.Type == JTokenType.Integer || tr.Type == JTokenType.Float)
                return Convert.ToInt32(tr.Value<double>(), CultureInfo.InvariantCulture);
            if (int.TryParse(tr.ToString().Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
                return n;
            return 0;
        }

        /// <summary>Typical WellRyde driver list API returns a JSON array.</summary>
        public static bool LooksLikeJsonArray(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
                return false;
            var t = body.TrimStart();
            return t.StartsWith("[", StringComparison.Ordinal);
        }

        public static bool ResponseIndicatesPortalLogin(HttpResponseMessage res)
        {
            if (res?.RequestMessage?.RequestUri == null)
                return false;
            var u = res.RequestMessage.RequestUri.AbsoluteUri;
            return u.IndexOf("login", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static bool BillSubmitBodyIndicatesSuccess(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
                return false;
            if (LooksLikeNonJsonPayload(body))
                return false;
            return string.Equals(body.Trim(), "SUCCESS", StringComparison.Ordinal);
        }
    }
}
