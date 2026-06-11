using System;
using System.IO;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>Desktop export layout: <c>Schedule for {year}\Schedule for {Month} {day} {year}.xlsx</c>.</summary>
    internal static class ScheduleExportPaths
    {
        public static string YearFolderName(int year) => "Schedule for " + year;

        public static string WorkbookFileName(string monthName, int day, int year) =>
            "Schedule for " + monthName + " " + day + " " + year + ".xlsx";

        public static string EnsureDesktopYearFolder(int year)
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            string dir = Path.Combine(desktop, YearFolderName(year));
            Directory.CreateDirectory(dir);
            return dir;
        }

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
