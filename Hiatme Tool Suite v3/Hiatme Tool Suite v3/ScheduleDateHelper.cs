using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>Resolves schedule calendar dates from UI long-date strings (no external services).</summary>
    internal static class ScheduleDateHelper
    {
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
    }
}
