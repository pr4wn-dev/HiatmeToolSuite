using System;

using System.Text.RegularExpressions;



namespace Hiatme_Tool_Suite_v3

{

    /// <summary>

    /// Shared template ↔ live trip match (same rules as <see cref="FullScheduleBuilder.BuildTempCsvFiles"/>).

    /// Weekday folder only — not ticket #, not template service date column.

    /// </summary>

    internal static class TemplateTripMatchRules

    {

        private static readonly Regex CollapseWs = new Regex(@"\s+", RegexOptions.Compiled);

        private static readonly Regex ZipToken = new Regex(@"\b\d{5}(-\d{4})?\b", RegexOptions.Compiled);

        private static readonly Regex TrailingState = new Regex(

            @"\s+\b(ME|MA|MAINE|NH|NEW\s+HAMPSHIRE)\b\s*$",

            RegexOptions.Compiled | RegexOptions.IgnoreCase);



        public static bool TripsMatch(MCDownloadedTrip template, MCDownloadedTrip live)

        {

            if (template == null || live == null) return false;

            if (ModernMatch(template, live)) return true;

            return LegacyScheduleBuilderMatch(template, live);

        }



        private static bool ModernMatch(MCDownloadedTrip template, MCDownloadedTrip live)

        {

            return ClientEq(template.ClientFullName, live.ClientFullName)

                && StreetEq(template.PUStreet, live.PUStreet)

                && CityEq(template.PUCity, live.PUCity)

                && PuTimeEq(template, live)

                && StreetEq(template.DOStreet, live.DOStreet)

                && CityEq(template.DOCITY, live.DOCITY)

                && DoTimeEq(template, live);

        }



        /// <summary>Byte-for-byte style match used by legacy Schedule Builder before shared rules.</summary>

        private static bool LegacyScheduleBuilderMatch(MCDownloadedTrip template, MCDownloadedTrip live)

        {

            string puTimeT = (template.PUTime ?? "").TrimStart('0');

            string puTimeD = (live.PUTime ?? "").TrimStart('0');

            string doTimeT = (template.DOTime ?? "").TrimStart('0');

            string doTimeD = (live.DOTime ?? "").TrimStart('0');

            return string.Equals(live.ClientFullName, template.ClientFullName, StringComparison.Ordinal)

                && string.Equals(live.PUStreet, template.PUStreet, StringComparison.Ordinal)

                && string.Equals(live.PUCity, template.PUCity, StringComparison.Ordinal)

                && string.Equals(puTimeD, puTimeT, StringComparison.Ordinal)

                && string.Equals(live.DOStreet, template.DOStreet, StringComparison.Ordinal)

                && string.Equals(live.DOCITY, template.DOCITY, StringComparison.Ordinal)

                && string.Equals(doTimeD, doTimeT, StringComparison.Ordinal);

        }



        private static string Norm(string s)

        {

            if (string.IsNullOrWhiteSpace(s)) return "";

            return CollapseWs.Replace(s.Trim(), " ");

        }



        private static bool ClientEq(string a, string b) =>

            string.Equals(Norm(a), Norm(b), StringComparison.OrdinalIgnoreCase);



        private static bool StreetEq(string a, string b)

        {

            if (FieldEq(a, b)) return true;

            return string.Equals(NormStreetKey(a), NormStreetKey(b), StringComparison.Ordinal);

        }



        private static bool CityEq(string a, string b)

        {

            if (FieldEq(a, b)) return true;

            string ka = NormCityKey(a);

            string kb = NormCityKey(b);

            if (ka.Length == 0 && kb.Length == 0) return true;

            if (string.Equals(ka, kb, StringComparison.Ordinal)) return true;

            if (ka.Length >= 4 && kb.Length >= 4

                && (ka.StartsWith(kb, StringComparison.Ordinal) || kb.StartsWith(ka, StringComparison.Ordinal)))

                return true;

            return false;

        }



        private static bool FieldEq(string a, string b) =>

            string.Equals(Norm(a), Norm(b), StringComparison.OrdinalIgnoreCase);



        private static string NormStreetKey(string raw)

        {

            string n = Norm(raw).ToUpperInvariant().Replace(".", "").Replace(",", "");

            n = Regex.Replace(n, @"\bSTREET\b", "ST", RegexOptions.IgnoreCase);

            n = Regex.Replace(n, @"\bROAD\b", "RD", RegexOptions.IgnoreCase);

            n = Regex.Replace(n, @"\bAVENUE\b", "AVE", RegexOptions.IgnoreCase);

            n = Regex.Replace(n, @"\bDRIVE\b", "DR", RegexOptions.IgnoreCase);

            n = Regex.Replace(n, @"\bLANE\b", "LN", RegexOptions.IgnoreCase);

            n = Regex.Replace(n, @"\bCOURT\b", "CT", RegexOptions.IgnoreCase);

            n = Regex.Replace(n, @"\bCIRCLE\b", "CIR", RegexOptions.IgnoreCase);

            return CollapseWs.Replace(n.Trim(), " ");

        }



        private static string NormCityKey(string raw)

        {

            string n = Norm(raw);

            if (n.Length == 0) return "";

            n = ZipToken.Replace(n, "");

            n = TrailingState.Replace(n, "");

            return CollapseWs.Replace(n.Trim(), " ").ToUpperInvariant();

        }



        private static bool PuTimeEq(MCDownloadedTrip template, MCDownloadedTrip live)

        {

            // Live download still shows 00:00 PU while template row may have been updated to a real time.

            if (ScheduleBuilderReserveBuckets.IsWillCallTrip(live))

                return true;

            if (IsWildcardTemplatePu(template?.PUTime))

                return true;

            var tp = SupeyTripTimes.TryParsePU(template);

            var lp = SupeyTripTimes.TryParsePU(live);

            if (tp.HasValue && lp.HasValue)

                return tp.Value == lp.Value;

            return LegacyTimeStringEq(template?.PUTime, live?.PUTime);

        }

        /// <summary>Template will-call rows often keep 00:00 PU when the live trip still has 00:00.</summary>

        private static bool IsWildcardTemplatePu(string raw)

        {

            string n = NormTimeRaw(raw);

            return n.Length == 0 || string.Equals(n, "00:00", StringComparison.OrdinalIgnoreCase);

        }



        /// <summary>

        /// Template CSV DO column matches Excel. Use template trip # for leg when choosing live DO field.

        /// </summary>

        private static bool DoTimeEq(MCDownloadedTrip template, MCDownloadedTrip live)

        {

            // Template CSV / Excel often has 00:00 on return legs — same as legacy Schedule Builder.

            if (IsWildcardTemplateDo(template?.DOTime))

                return true;



            var tp = SupeyTripTimes.TryParse(template?.DOTime);

            var lp = SupeyTripTimes.TryParse(live?.DOTime);

            if (tp.HasValue && lp.HasValue && tp.Value == lp.Value)

                return true;



            // Legacy builder compared appointment column (DOTime) only, not SchedDOTime.

            if (LegacyScheduleBuilderDoTimeEq(template?.DOTime, live?.DOTime))

                return true;



            return LegacyDoStringEq(template?.DOTime, live);

        }



        /// <summary>Template return-leg rows with no real drop deadline (00:00 / blank).</summary>

        private static bool IsWildcardTemplateDo(string raw)

        {

            string n = NormTimeRaw(raw);

            return n.Length == 0 || string.Equals(n, "00:00", StringComparison.OrdinalIgnoreCase);

        }



        private static bool LegacyScheduleBuilderDoTimeEq(string templateDo, string liveDo)

        {

            string t = (templateDo ?? "").TrimStart('0');

            string d = (liveDo ?? "").TrimStart('0');

            if (t.Length == 0 && d.Length == 0) return true;

            return string.Equals(d, t, StringComparison.Ordinal);

        }



        private static char DetectLegSuffix(string tripNumber)

        {

            if (string.IsNullOrEmpty(tripNumber)) return 'B';

            int len = tripNumber.Length;

            if (len >= 2 && tripNumber[len - 2] == '-')

            {

                char c = char.ToUpperInvariant(tripNumber[len - 1]);

                if (c == 'A' || c == 'B' || c == 'C') return c;

            }

            return 'B';

        }



        private static bool LegacyTimeStringEq(string templateRaw, string liveRaw)

        {

            string t = NormTimeRaw(templateRaw);

            string l = NormTimeRaw(liveRaw);

            if (t.Length == 0 && l.Length == 0) return true;

            return string.Equals(t, l, StringComparison.OrdinalIgnoreCase);

        }



        private static bool LegacyDoStringEq(string templateDo, MCDownloadedTrip live)

        {

            string t = NormTimeRaw(templateDo);

            if (t.Length == 0) return true;



            string d = NormTimeRaw(live?.DOTime);

            if (string.Equals(t, d, StringComparison.OrdinalIgnoreCase))

                return true;



            string s = NormTimeRaw(live?.SchedDOTime);

            return string.Equals(t, s, StringComparison.OrdinalIgnoreCase);

        }



        private static string NormTimeRaw(string raw)

        {

            if (string.IsNullOrWhiteSpace(raw)) return "";

            string s = raw.Trim();

            if (s.Equals("00:00", StringComparison.OrdinalIgnoreCase)

                || s.Equals("00:00:00", StringComparison.OrdinalIgnoreCase)

                || s.Equals("12:00 AM", StringComparison.OrdinalIgnoreCase)

                || s.Equals("12:00AM", StringComparison.OrdinalIgnoreCase))

                return "00:00";

            return s.TrimStart('0');

        }

    }

}


