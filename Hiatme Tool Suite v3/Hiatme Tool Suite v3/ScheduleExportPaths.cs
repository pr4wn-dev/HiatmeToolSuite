using System;
using System.IO;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Desktop export layout:
    /// <c>Desktop\SCHEDULES FOR {year}\Schedule for {Month} {day} {year}.xlsx</c>.
    /// </summary>
    internal static class ScheduleExportPaths
    {
        public static string YearFolderName(int year) => "SCHEDULES FOR " + year;

        private static string LegacyYearFolderName(int year) => "Schedule for " + year;

        public static string WorkbookFileName(string monthName, int day, int year) =>
            "Schedule for " + monthName + " " + day + " " + year + ".xlsx";

        /// <summary>Current user's Desktop (OneDrive redirect when configured).</summary>
        public static string GetUserDesktopPath() =>
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

        /// <summary>
        /// Prefer <see cref="YearFolderName"/>; use legacy <c>Schedule for {year}</c> when that is
        /// the folder that already exists on this machine.
        /// </summary>
        public static string ResolveDesktopYearFolder(int year, bool createIfMissing = false)
        {
            string desktop = GetUserDesktopPath();
            string preferred = Path.Combine(desktop, YearFolderName(year));
            if (Directory.Exists(preferred))
                return preferred;

            string legacy = Path.Combine(desktop, LegacyYearFolderName(year));
            if (Directory.Exists(legacy))
                return legacy;

            if (createIfMissing)
                Directory.CreateDirectory(preferred);
            return preferred;
        }

        public static string EnsureDesktopYearFolder(int year) =>
            ResolveDesktopYearFolder(year, createIfMissing: true);

        public static void GetDefaultWorkbookSaveLocation(
            string monthName,
            string dayText,
            string yearText,
            out string yearFolder,
            out string fileName,
            out string fullPath)
        {
            int day = int.TryParse(dayText, out var d) ? d : DateTime.Now.Day;
            int year = int.TryParse(yearText, out var y) ? y : DateTime.Now.Year;
            if (string.IsNullOrWhiteSpace(monthName))
                monthName = DateTime.Now.ToString("MMMM");
            else
                monthName = monthName.Trim();

            yearFolder = EnsureDesktopYearFolder(year);
            fileName = WorkbookFileName(monthName, day, year);
            fullPath = Path.Combine(yearFolder, fileName);
        }

        public static void GetDefaultWorkbookSaveLocation(
            DateTime serviceDate,
            out string yearFolder,
            out string fileName,
            out string fullPath)
        {
            GetDefaultWorkbookSaveLocation(
                serviceDate.ToString("MMMM"),
                serviceDate.Day.ToString(),
                serviceDate.Year.ToString(),
                out yearFolder,
                out fileName,
                out fullPath);
        }
    }
}
