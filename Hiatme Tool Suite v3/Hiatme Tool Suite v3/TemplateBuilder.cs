using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    internal class TemplateBuilder
    {
        public delegate void UpdateLoadingScreenHandler(string text);
        public delegate void ShowLoadingScreenHandler();
        public delegate void HideLoadingScreenHandler();

        public event UpdateLoadingScreenHandler UpdateLoadingScreen;
        public event ShowLoadingScreenHandler ShowLoadingScreen;
        public event HideLoadingScreenHandler HideLoadingScreen;

        public IDictionary<string, IDictionary<string, string[]>> driverTripList;
        public string TemplateNameOfDay { get; set; }
        public bool BadScheduleScan { get; set; }
        public string TemplateNameOfFileToLoad { get; set; }

        /// <summary>Weekday folder name from the Templates tab (Monday…Sunday). Used as the authoritative save target.</summary>
        public string TargetWeekdayName { get; set; }

        /// <summary>Day of week inferred from repeated trip dates in the export (when enough samples exist).</summary>
        public string InferredWeekdayFromSchedule { get; private set; }

        /// <summary>True when inferred weekday from dates disagrees with <see cref="TargetWeekdayName"/>.</summary>
        public bool ScheduleWeekdayMismatchWarning { get; private set; }

        private string TemplateDateField { get; set; }
        private readonly List<string> _badScheduleDiagnostics = new List<string>();
        private const int MaxBadScheduleDiagnostics = 30;

        private static string AppBaseDir()
        {
            var b = AppContext.BaseDirectory ?? "";
            return b.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        internal static string GetTemplateTempDirectory() => Path.Combine(AppBaseDir(), "Template Temps");

        internal static string GetDayTemplateDirectory(string dayName)
        {
            if (string.IsNullOrWhiteSpace(dayName))
                return null;
            return Path.Combine(AppBaseDir(), dayName.Trim());
        }

        private async Task AsyncUpdateLoadingScreen(string txt)
        {
            UpdateLoadingScreen?.Invoke(txt);
            await Task.Yield();
        }

        public void StartTemplateBuilder()
        {
            BadScheduleScan = false;
            ScheduleWeekdayMismatchWarning = false;
            InferredWeekdayFromSchedule = null;
            _badScheduleDiagnostics.Clear();
            CreateTempTemplateFiles();
        }

        /// <summary>Deletes all files in <see cref="GetTemplateTempDirectory"/> (working folder only, not weekday folders).</summary>
        public void ClearTemplateWorkingFolder()
        {
            try
            {
                var dir = GetTemplateTempDirectory();
                if (!Directory.Exists(dir))
                    return;
                foreach (var f in Directory.EnumerateFiles(dir))
                {
                    try
                    {
                        File.Delete(f);
                    }
                    catch
                    {
                        /* ignore single-file failures */
                    }
                }
            }
            catch
            {
                // ignore
            }
        }

        public string FormatBadScheduleUserMessage()
        {
            var sb = new StringBuilder();
            sb.AppendLine("The schedule could not be used as templates.");
            sb.AppendLine();
            sb.AppendLine("Problems found (tab name = Excel sheet name = exported .csv file):");
            sb.AppendLine();
            if (_badScheduleDiagnostics.Count == 0)
            {
                sb.AppendLine("(No row details were recorded — see trip date column 12 / \"L\" in each driver tab.)");
            }
            else
            {
                foreach (var line in _badScheduleDiagnostics)
                    sb.AppendLine("• " + line);
                if (_badScheduleDiagnostics.Count >= MaxBadScheduleDiagnostics)
                    sb.AppendLine("• … (additional rows omitted)");
            }
            sb.AppendLine();
            sb.AppendLine("How to fix:");
            sb.AppendLine("• Each driver tab should look like the normal Modivcare schedule export.");
            sb.AppendLine("• Trip date must be in column 12 and look like a date with slashes (e.g. 5/13/2026).");
            sb.AppendLine("• Remove stray text or numbers in that column that are not dates.");
            sb.AppendLine("• Tab names must be valid Windows file names (no \\ / : * ? \" < > |).");
            sb.AppendLine();
            sb.AppendLine("Fix the workbook, save it, then click Add Template again.");
            return sb.ToString();
        }

        private void CreateTempTemplateFiles()
        {
            var tempDir = GetTemplateTempDirectory();
            if (Directory.Exists(tempDir))
            {
                foreach (var filePath in Directory.GetFiles(tempDir))
                {
                    try
                    {
                        File.Delete(filePath);
                    }
                    catch
                    {
                        /* continue */
                    }
                }

                SplitFile(tempDir + Path.DirectorySeparatorChar, TemplateNameOfFileToLoad);
            }
            else
            {
                Directory.CreateDirectory(tempDir);
                SplitFile(tempDir + Path.DirectorySeparatorChar, TemplateNameOfFileToLoad);
            }
        }

        private void SplitFile(string targetPath, string sourceFile)
        {
            if (string.IsNullOrWhiteSpace(sourceFile) || !File.Exists(sourceFile))
            {
                MessageBox.Show(
                    "The selected file could not be found or the path is empty.\n\nPick the schedule file again.",
                    "Template builder",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            Microsoft.Office.Interop.Excel.Application xlApp = null;
            Microsoft.Office.Interop.Excel.Workbook xlFile = null;
            var exportErrors = new List<string>();

            try
            {
                var fileFormat = Microsoft.Office.Interop.Excel.XlFileFormat.xlCSV;
                const string exportFormat = "CSV";

                xlApp = new Microsoft.Office.Interop.Excel.Application();
                xlApp.DisplayAlerts = false;
                xlFile = xlApp.Workbooks.Open(sourceFile);
                int tabCount = xlFile.Worksheets.Count;

                for (int i = 1; i <= tabCount; i++)
                {
                    string sheetName = xlFile.Sheets[i].Name;
                    if (ContainsInvalidFileNameChar(sheetName))
                    {
                        exportErrors.Add(
                            $"Tab \"{sheetName}\": rename the sheet — it contains characters that cannot be used in a file name ( \\ / : * ? \" < > | ).");
                        continue;
                    }

                    string newFilename = Path.Combine(targetPath, sheetName);
                    Microsoft.Office.Interop.Excel.Worksheet tempSheet = null;
                    Microsoft.Office.Interop.Excel.Workbook tempBook = null;
                    try
                    {
                        tempSheet = xlApp.Worksheets[i];
                        tempSheet.Copy();
                        tempBook = xlApp.ActiveWorkbook;

                        if (exportFormat == "CSV")
                        {
                            newFilename += ".csv";
                            fileFormat = Microsoft.Office.Interop.Excel.XlFileFormat.xlCSV;
                        }

                        tempBook.SaveAs(newFilename, fileFormat);
                    }
                    catch (Exception ex)
                    {
                        exportErrors.Add($"Tab \"{sheetName}\": could not export CSV — {ex.Message}");
                    }
                    finally
                    {
                        if (tempBook != null)
                        {
                            try
                            {
                                tempBook.Close(false);
                            }
                            catch
                            {
                                /* ignore */
                            }

                            Marshal.ReleaseComObject(tempBook);
                        }

                        if (tempSheet != null)
                            Marshal.ReleaseComObject(tempSheet);
                    }
                }

                if (exportErrors.Count > 0)
                {
                    var eb = new StringBuilder();
                    eb.AppendLine("One or more tabs could not be exported to CSV:");
                    eb.AppendLine();
                    foreach (var err in exportErrors.Take(12))
                        eb.AppendLine("• " + err);
                    if (exportErrors.Count > 12)
                        eb.AppendLine("• … (" + (exportErrors.Count - 12) + " more)");
                    eb.AppendLine();
                    eb.AppendLine("Fix tab names or close anything locking those files, then try Add Template again.");
                    MessageBox.Show(eb.ToString(), "Excel export", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Excel could not process this workbook.\n\n" + ex.Message +
                    "\n\nMake sure Microsoft Excel is installed and the file is not open in another program, then try again.",
                    "Template builder",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                if (xlFile != null)
                {
                    try
                    {
                        xlFile.Close(false);
                    }
                    catch
                    {
                        /* ignore */
                    }

                    try
                    {
                        Marshal.ReleaseComObject(xlFile);
                    }
                    catch
                    {
                        /* ignore */
                    }

                    xlFile = null;
                }

                if (xlApp != null)
                {
                    try
                    {
                        xlApp.Quit();
                    }
                    catch
                    {
                        /* ignore */
                    }

                    try
                    {
                        Marshal.ReleaseComObject(xlApp);
                    }
                    catch
                    {
                        /* ignore */
                    }

                    xlApp = null;
                }

                GC.Collect();
                GC.WaitForPendingFinalizers();
            }

            CheckTemplateFilesLines();
        }

        private static bool ContainsInvalidFileNameChar(string name)
        {
            if (string.IsNullOrEmpty(name))
                return true;
            return name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0;
        }

        private void CheckTemplateFilesLines()
        {
            datecounter = 0;
            InferredWeekdayFromSchedule = null;
            ScheduleWeekdayMismatchWarning = false;

            var tempDir = GetTemplateTempDirectory();
            if (!Directory.Exists(tempDir))
                return;

            var filePaths = Directory.GetFiles(tempDir, "*.csv");
            if (filePaths.Length == 0)
            {
                RecordBadSchedule(
                    "No driver CSV files were created in the working folder. " +
                    "Usually this means every Excel tab failed to export (bad tab names, invalid characters in the sheet name, or Excel could not save). " +
                    "Fix the issues in the export message (if any), then try Add Template again.");
                TemplateNameOfDay = null;
                return;
            }

            foreach (string s in filePaths)
                GetTripListFromCSVFile(s, false, recordLayoutDiagnostics: true);

            if (datecounter > 30 && !string.IsNullOrWhiteSpace(TemplateDateField) && TemplateDateField.Contains("/"))
            {
                try
                {
                    string[] datesections = TemplateDateField.Split('/');
                    if (datesections.Length >= 3)
                    {
                        var dateValue = new DateTime(
                            int.Parse(datesections[2], CultureInfo.InvariantCulture),
                            int.Parse(datesections[0], CultureInfo.InvariantCulture),
                            int.Parse(datesections[1], CultureInfo.InvariantCulture));
                        InferredWeekdayFromSchedule = dateValue.DayOfWeek.ToString();
                    }
                }
                catch
                {
                    InferredWeekdayFromSchedule = null;
                }
            }

            if (!string.IsNullOrWhiteSpace(TargetWeekdayName))
            {
                TemplateNameOfDay = TargetWeekdayName.Trim();
                if (!string.IsNullOrEmpty(InferredWeekdayFromSchedule) &&
                    !string.Equals(InferredWeekdayFromSchedule, TemplateNameOfDay, StringComparison.OrdinalIgnoreCase))
                    ScheduleWeekdayMismatchWarning = true;
            }
            else if (datecounter > 30 && !string.IsNullOrEmpty(InferredWeekdayFromSchedule))
            {
                TemplateNameOfDay = InferredWeekdayFromSchedule;
            }
            else
            {
                TemplateNameOfDay = null;
            }
        }

        private void RecordBadSchedule(string detail)
        {
            BadScheduleScan = true;
            if (_badScheduleDiagnostics.Count < MaxBadScheduleDiagnostics)
                _badScheduleDiagnostics.Add(detail);
        }

        private IDictionary<string, string[]> GetTripListFromCSVFile(string filePath, bool checkdates, bool recordLayoutDiagnostics = false)
        {
            var keyValuePairs = new Dictionary<string, string[]>();
            string tabLabel = Path.GetFileNameWithoutExtension(filePath) ?? filePath;

            string[] rows;
            try
            {
                rows = File.ReadAllLines(filePath);
            }
            catch (Exception ex)
            {
                if (recordLayoutDiagnostics)
                    RecordBadSchedule($"File \"{tabLabel}\": could not read CSV — {ex.Message}");
                return keyValuePairs;
            }

            int rowcounter = 0;
            var csvParser = new Regex(",(?=(?:[^\"]*\"[^\"]*\")*(?![^\"]*\"))");

            for (int row = 0; row < rows.Length; row++)
            {
                string[] rowValues = csvParser.Split(rows[row]);

                if (rowValues.Length == 0 || string.IsNullOrEmpty(rowValues[0]))
                    continue;

                if (recordLayoutDiagnostics && row > 0 && rowValues.Length < 14)
                {
                    RecordBadSchedule(
                        $"Tab \"{tabLabel}\", spreadsheet row {row + 1}: only {rowValues.Length} column(s) were read (need 14). " +
                        "Check for a missing comma, an extra line break inside quotes, or a blank row in the middle of the block.");
                }

                for (int i = 0; i < rowValues.Length && i < 14; i++)
                    rowValues[i] = rowValues[i].Replace("\"", string.Empty);

                if (recordLayoutDiagnostics && rowValues.Length >= 14)
                {
                    string[] rowForValidate = rowValues.Length == 14 ? rowValues : rowValues.Take(14).ToArray();
                    if (!TripTemplateCsvValidator.IsLikelyHeaderRow(rowForValidate))
                    {
                        foreach (var issue in TripTemplateCsvValidator.ValidateTripRow(rowForValidate))
                            RecordBadSchedule(issue.FormatForUser(tabLabel, row + 1));
                    }
                }

                for (int i = 0; i < 3 && i < rowValues.Length; i++)
                {
                    if (checkdates)
                    {
                    }

                    try
                    {
                        if (CheckForTripDate(rowValues[i]))
                        {
                        }
                    }
                    catch
                    {
                        continue;
                    }
                }

                keyValuePairs.Add("row" + rowcounter, rowValues);
                rowcounter++;
            }

            return keyValuePairs;
        }

        int datecounter;

        private bool CheckForTripDate(string date)
        {
            if (date != null && date.Contains("/"))
            {
                if (TemplateDateField == date)
                    datecounter++;
                TemplateDateField = date;
                return true;
            }

            return false;
        }

        /// <summary>Shows replace confirmation, moves files into the weekday folder, then clears the temp folder.</summary>
        public async Task<bool> TryRunReplaceTemplatesDialogAsync(IWin32Window owner)
        {
            if (string.IsNullOrWhiteSpace(TemplateNameOfDay))
            {
                MessageBox.Show(
                    owner,
                    "Could not determine which weekday folder to use.\n\n" +
                    "Pick Monday–Sunday in the day list on the Templates tab, then run Add Template again.\n" +
                    "If the day is already selected, the schedule may not contain enough repeating trip dates to double-check the weekday.",
                    "Templates",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                ClearTemplateWorkingFolder();
                return false;
            }

            string dayDir = GetDayTemplateDirectory(TemplateNameOfDay);
            string msg =
                "Replace all saved template CSVs for " + TemplateNameOfDay + "?\n\n" +
                "This will permanently delete every file in:\n" + dayDir + "\n\n" +
                "…and replace them with the driver CSV files from Template Temps (one file per Excel tab).\n\n" +
                "Other weekdays (Tuesday, Wednesday, …) are not changed.\n\n" +
                "Continue?";

            var dialogResult = MessageBox.Show(owner, msg, "Confirm template replace", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (dialogResult != DialogResult.Yes)
            {
                await AsyncUpdateLoadingScreen("Cancelling…");
                ClearTemplateWorkingFolder();
                return false;
            }

            await AsyncUpdateLoadingScreen("Saving templates…");
            try
            {
                MoveTemplateFilesToDayFolder(TemplateNameOfDay);
            }
            catch (Exception ex)
            {
                ClearTemplateWorkingFolder();
                MessageBox.Show(
                    owner,
                    "Could not finish moving template files.\n\n" + ex.Message +
                    "\n\nClose Excel or any program that might have one of these CSV files open, then try again.",
                    "Templates",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return false;
            }

            ClearTemplateWorkingFolderIncludingSkipped();
            return true;
        }

        private void MoveTemplateFilesToDayFolder(string nameoffolder)
        {
            var tempDir = GetTemplateTempDirectory();
            if (!Directory.Exists(tempDir))
                throw new IOException("Working folder is missing: " + tempDir);

            string dayDir = GetDayTemplateDirectory(nameoffolder);
            if (string.IsNullOrEmpty(dayDir))
                throw new InvalidOperationException("Invalid folder name.");

            if (Directory.Exists(dayDir))
            {
                foreach (var filePath in Directory.GetFiles(dayDir))
                    File.Delete(filePath);
            }
            else
            {
                Directory.CreateDirectory(dayDir);
            }

            foreach (var file in Directory.EnumerateFiles(tempDir))
            {
                if (CheckForBadTemplateNames(file))
                    continue;

                string destFile = Path.Combine(dayDir, Path.GetFileName(file));
                if (File.Exists(destFile))
                    File.Delete(destFile);
                File.Move(file, destFile);
            }
        }

        /// <summary>Removes leftover files in temp (e.g. skipped tab exports) after a successful move.</summary>
        private void ClearTemplateWorkingFolderIncludingSkipped()
        {
            ClearTemplateWorkingFolder();
        }

        private bool CheckForBadTemplateNames(string name)
        {
            string fn = Path.GetFileName(name) ?? "";
            if (fn.IndexOf("Reserves", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (fn.IndexOf("Schedule", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (fn.IndexOf("LGTC", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            return false;
        }

        public void GetAvailibleTemplatesForDay(string dayname)
        {
            driverTripList = new Dictionary<string, IDictionary<string, string[]>>();
            string dayDir = GetDayTemplateDirectory(dayname);
            if (dayDir != null && Directory.Exists(dayDir))
            {
                var filePaths = Directory.GetFiles(dayDir, "*.csv");
                foreach (string filesname in filePaths)
                    AddTemplatesToList(filesname, dayname);
            }
        }

        private void AddTemplatesToList(string filename, string nameofday)
        {
            if (filename.IndexOf("Schedule", StringComparison.OrdinalIgnoreCase) >= 0 ||
                filename.IndexOf("Reserves", StringComparison.OrdinalIgnoreCase) >= 0 ||
                filename.IndexOf("LGTC", StringComparison.OrdinalIgnoreCase) >= 0)
                return;

            if (!File.Exists(filename))
                return;

            string actualfilename = Path.GetFileNameWithoutExtension(filename);
            if (driverTripList.ContainsKey(actualfilename))
                return;

            driverTripList.Add(actualfilename, GetTripListFromCSVFile(filename, false, recordLayoutDiagnostics: false));
        }
    }
}
