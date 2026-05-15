using Microsoft.Office.Interop.Excel;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.UI;
using System.Windows.Forms;
using static MaterialSkin.MaterialSkinManager;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>Thrown when schedule building fails. Use StepName, FilePath, DriverName, TripNumber, RowIndex, ColumnOrField to locate the problem.</summary>
    public class ScheduleBuilderException : Exception
    {
        public string StepName { get; }
        public string FilePath { get; }
        public string DriverName { get; }
        public string TripNumber { get; }
        public int RowIndex { get; }
        /// <summary>Column letter, field name, or other pinpoint (e.g. "Column L (trip date)").</summary>
        public string ColumnOrField { get; }

        public ScheduleBuilderException(string stepName, string filePath, string driverName, string tripNumber, int rowIndex, Exception inner, string columnOrField = null)
            : base(BuildMessage(stepName, filePath, driverName, tripNumber, rowIndex, columnOrField, inner), inner)
        {
            StepName = stepName ?? "";
            FilePath = filePath ?? "";
            DriverName = driverName ?? "";
            TripNumber = tripNumber ?? "";
            RowIndex = rowIndex;
            ColumnOrField = columnOrField ?? "";
        }

        private static string BuildMessage(string step, string path, string driver, string trip, int row, string columnOrField, Exception inner)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Schedule builder could not finish.");
            sb.AppendLine();
            if (!string.IsNullOrEmpty(step))
                sb.AppendLine("Step: " + step);
            if (!string.IsNullOrEmpty(path))
                sb.AppendLine("File: " + path);
            if (!string.IsNullOrEmpty(driver))
                sb.AppendLine("Excel tab / driver template: " + driver);
            if (row > 0)
                sb.AppendLine("Line in template CSV (1-based; includes header if present): " + row);
            if (!string.IsNullOrEmpty(trip))
                sb.AppendLine("Trip #: " + trip);
            if (!string.IsNullOrEmpty(columnOrField) && columnOrField != "—")
                sb.AppendLine("Column / field: " + columnOrField);
            sb.AppendLine();
            sb.AppendLine(inner?.Message ?? "Unknown error.");
            return sb.ToString();
        }
    }

    internal class FullScheduleBuilder
    {
        public delegate void UpdateLoadingScreenHandler(string text);
        public delegate void ShowLoadingScreenHandler();
        public delegate void HideLoadingScreenHandler();

        public event UpdateLoadingScreenHandler UpdateLoadingScreen;
        public event ShowLoadingScreenHandler ShowLoadingScreen;
        public event HideLoadingScreenHandler HideLoadingScreen;

        public MCTripDownloader MCTripListDLer;
        public List<MCDownloadedTrip> MCTripList { get; set; }
        public List<MCDownloadedTrip> TripsFound { get; set; }
        public string NameOfDay { get; set; }
        public string Day { get; set; }
        public string NameOfMonth { get; set; }
        public string Month { get; set; }
        public string Year { get; set; }

        public IDictionary<string, List<MCDownloadedTrip>> driverTripList;
        public FullScheduleBuilder(string dayname, string daynumber, string monthname, string monthnumber, string year)
        {
            NameOfDay = dayname;
            Day = daynumber;
            NameOfMonth = monthname;
            Month = monthnumber;
            Year = year;
        }
        private async Task AsyncUpdateLoadingScreen(string txt)
        {
            UpdateLoadingScreen(txt);
            await Task.Yield();
        }
        public async Task DownloadMCTrips(DateTime mcdate, MCLoginHandler mcLoginHandler)
        {
            try
            {
                MCTripListDLer = new MCTripDownloader();
                MCTripList = new List<MCDownloadedTrip>();
                await AsyncUpdateLoadingScreen("Checking connections");
                await AsyncUpdateLoadingScreen("Downloading trips…");
                MCTripList = await MCTripListDLer.DownloadTripRecords(mcdate, mcLoginHandler);
                if (MCTripList != null)
                {
                    foreach (MCDownloadedTrip mcrtr in MCTripList)
                    {
                        Console.WriteLine(mcrtr.Date + ": " + mcrtr.ClientFullName + " " + mcrtr.TripNumber);
                    }
                }
                Console.WriteLine("finished gathering trips!");
            }
            catch (Exception ex)
            {
                throw new ScheduleBuilderException(
                    "DownloadMCTrips",
                    null,
                    null,
                    null,
                    0,
                    new InvalidOperationException(
                        "Could not download trips from Modivcare for the selected date.\n\n" +
                        "Check that you are signed in, the service date is correct, and your network connection is stable.\n\n" +
                        "Original error: " + ex.Message,
                        ex),
                    "—");
            }
        }
        public async Task BuildFullSchedule(DateTime modcdate, MCLoginHandler modcLoginHandler)
        {
            try
            {
                await DownloadMCTrips(modcdate, modcLoginHandler);

                if (MCTripList == null || !MCTripList.Any())
                {
                    throw new ScheduleBuilderException("DownloadMCTrips", null, null, null, 0, new InvalidOperationException("No trips were downloaded. Check your Modivcare connection and date."));
                }

                await AsyncUpdateLoadingScreen("Loading template files");
                LoadTemplateFiles();

                if (TripsFound == null || !TripsFound.Any())
                {
                    var dayDir = Path.Combine(AppContext.BaseDirectory, NameOfDay);
                    throw new ScheduleBuilderException(
                        "BuildTempCsvFiles",
                        dayDir,
                        NameOfDay,
                        null,
                        0,
                        new InvalidOperationException(
                            "No Modivcare trips matched your template rows for " + NameOfDay + ".\n\n" +
                            "Common causes:\n" +
                            "• Service date on the schedule builder does not match the date embedded in template trips.\n" +
                            "• Client name, PU/DO street or city, or PU/DO times differ slightly between template and download.\n" +
                            "• Template is for a different weekday than the trips you downloaded.\n\n" +
                            "Template folder used:\n" + dayDir),
                        "Match uses: client name, PU street & city, DO street & city, PU time, DO time (leading zeros ignored).");
                }

                await CreateWorkbookAsync();
            }
            catch (ScheduleBuilderException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new ScheduleBuilderException("BuildFullSchedule", null, null, null, 0, ex);
            }
        }
        private const string CsvColumnLegend =
            "Each trip row must have 14 values in order: A Trip#, B Date, C Client name, D PU street, E PU city, F PU phone, G PU time, H DO street, I DO city, J DO phone, K DO time, L Age, M Miles, N Comments.";

        private void LoadTemplateFiles()
        {
            var dayDir = Path.Combine(AppContext.BaseDirectory, NameOfDay);
            try
            {
                if (!Directory.Exists(dayDir))
                {
                    throw new ScheduleBuilderException(
                        "LoadTemplateFiles",
                        dayDir,
                        NameOfDay,
                        null,
                        0,
                        new DirectoryNotFoundException(
                            "No template folder was found for " + NameOfDay + ".\n\n" +
                            "Expected folder:\n" + dayDir + "\n\n" +
                            "On the Templates tab, add templates for this weekday (one CSV per driver tab), then run the schedule builder again."),
                        "—");
                }

                driverTripList = new Dictionary<string, List<MCDownloadedTrip>>();
                var filePaths = Directory.GetFiles(dayDir, "*.csv");
                if (filePaths.Length == 0)
                {
                    throw new ScheduleBuilderException(
                        "LoadTemplateFiles",
                        dayDir,
                        NameOfDay,
                        null,
                        0,
                        new InvalidOperationException(
                            "The folder for " + NameOfDay + " exists but contains no .csv template files.\n\n" +
                            "Add driver templates on the Templates tab, or confirm you chose the correct weekday on the schedule builder."),
                        "—");
                }

                foreach (string s in filePaths)
                {
                    try
                    {
                        AddTemplateToList(s);
                    }
                    catch (ScheduleBuilderException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        throw new ScheduleBuilderException(
                            "LoadTemplateFiles",
                            s,
                            Path.GetFileNameWithoutExtension(s),
                            null,
                            0,
                            ex,
                            "While reading this template CSV");
                    }
                }

                if (driverTripList.Count == 0)
                {
                    throw new ScheduleBuilderException(
                        "LoadTemplateFiles",
                        dayDir,
                        NameOfDay,
                        null,
                        0,
                        new InvalidOperationException(
                            "No driver template CSVs were loaded (only special files such as Reserves/Schedule/LGTC may have been present).\n\n" +
                            "Check that each driver has a .csv file in:\n" + dayDir),
                        "—");
                }

                CleanTempFolder();
                BuildTempCsvFiles();
            }
            catch (ScheduleBuilderException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new ScheduleBuilderException("LoadTemplateFiles", dayDir, NameOfDay, null, 0, ex, "—");
            }
        }
        private void BuildTempCsvFiles()
        {
            TripsFound = new List<MCDownloadedTrip>();
            if (driverTripList == null)
            {
                throw new ScheduleBuilderException("BuildTempCsvFiles", null, null, null, 0, new InvalidOperationException("No templates for chosen day. Create templates on the Templates tab first."), "—");
            }
            try
            {
                foreach (KeyValuePair<string, List<MCDownloadedTrip>> templatetriplist in driverTripList)
                {
                    List<MCDownloadedTrip> confirmedtrips = new List<MCDownloadedTrip>();
                    string driverName = templatetriplist.Key;
                    int templateTripIndex = 0;
                    foreach (MCDownloadedTrip templatetrip in templatetriplist.Value)
                    {
                        templateTripIndex++;
                        try
                        {
                            foreach (MCDownloadedTrip mcdownloadedtrip in MCTripList)
                            {
                                string puTimeT = (templatetrip.PUTime ?? "").TrimStart(new char[] { '0' });
                                string puTimeD = (mcdownloadedtrip.PUTime ?? "").TrimStart(new char[] { '0' });
                                string doTimeT = (templatetrip.DOTime ?? "").TrimStart(new char[] { '0' });
                                string doTimeD = (mcdownloadedtrip.DOTime ?? "").TrimStart(new char[] { '0' });
                                if ((mcdownloadedtrip.ClientFullName == templatetrip.ClientFullName) && (mcdownloadedtrip.PUStreet == templatetrip.PUStreet) && (mcdownloadedtrip.PUCity == templatetrip.PUCity) && (puTimeD == puTimeT)
                                     && (mcdownloadedtrip.DOStreet == templatetrip.DOStreet) && (mcdownloadedtrip.DOCITY == templatetrip.DOCITY) && (doTimeD == doTimeT))
                                {
                                    confirmedtrips.Add(mcdownloadedtrip);
                                    TripsFound.Add(mcdownloadedtrip);
                                }
                            }
                        }
                        catch (ScheduleBuilderException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            throw new ScheduleBuilderException(
                                "BuildTempCsvFiles.MatchTemplateTrip",
                                null,
                                driverName,
                                templatetrip?.TripNumber,
                                0,
                                ex,
                                "Template trip #" + templateTripIndex + " for this driver (compare to row in " + driverName + ".csv)");
                        }
                    }
                    try
                    {
                        SaveTripListToCSVFile(confirmedtrips, templatetriplist.Key);
                    }
                    catch (ScheduleBuilderException) { throw; }
                    catch (Exception ex)
                    {
                        throw new ScheduleBuilderException(
                            "SaveTripListToCSVFile",
                            null,
                            driverName,
                            null,
                            0,
                            new IOException("Could not write CSV for driver tab \"" + driverName + "\".\n\n" + ex.Message, ex),
                            "Working folder: " + TemplateBuilder.GetTemplateTempDirectory());
                    }
                }
                CreateMReservesCSVFile();
            }
            catch (ScheduleBuilderException) { throw; }
            catch (Exception ex)
            {
                throw new ScheduleBuilderException("BuildTempCsvFiles", null, null, null, 0, ex);
            }
        }
        private void CreateMReservesCSVFile()
        {
            try
            {
                List<MCDownloadedTrip> reservetrips = new List<MCDownloadedTrip>();
                foreach (MCDownloadedTrip dledmctrip in MCTripList)
                {
                    bool tripfound = false;
                    foreach (MCDownloadedTrip foundtrip in TripsFound)
                    {
                        if (foundtrip.TripNumber == dledmctrip.TripNumber)
                        {
                            tripfound = true;
                        }
                    }
                    if (!tripfound)
                    {
                        reservetrips.Add(dledmctrip);
                    }
                }
                SaveTripListToCSVFile(reservetrips, "Reserves");
            }
            catch (ScheduleBuilderException) { throw; }
            catch (Exception ex)
            {
                throw new ScheduleBuilderException(
                    "CreateMReservesCSVFile",
                    Path.Combine(TemplateBuilder.GetTemplateTempDirectory(), "Reserves.csv"),
                    "Reserves",
                    null,
                    0,
                    ex,
                    "Trips in the Modivcare download that did not match any template row");
            }
        }
        private void CleanTempFolder()
        {
            var dir = TemplateBuilder.GetTemplateTempDirectory();
            if (Directory.Exists(dir))
            {
                foreach (var filePath in Directory.GetFiles(dir))
                    File.Delete(filePath);
            }
            else
            {
                Directory.CreateDirectory(dir);
            }
        }
        public void SaveTripListToCSVFile(List<MCDownloadedTrip> triplist, string filename)
        {
            string fullPath = Path.Combine(TemplateBuilder.GetTemplateTempDirectory(), filename + ".csv");
            try
            {
                var csv = new StringBuilder();
                foreach (MCDownloadedTrip trip in triplist)
                {
                    var newLine = string.Format("\"{0}\",\"{1}\",\"{2}\",\"{3}\",\"{4}\",\"{5}\",\"{6}\",\"{7}\",\"{8}\",\"{9}\",\"{10}\",\"{11}\",\"{12}\",\"{13}\"", trip.TripNumber ?? "", trip.Date ?? "", trip.ClientFullName ?? "", trip.PUStreet ?? "", trip.PUCity ?? "", trip.PUTelephone ?? "", trip.PUTime ?? "", trip.DOStreet ?? "", trip.DOCITY ?? "", trip.DOTelephone ?? "", trip.DOTime ?? "", trip.Age ?? "", trip.Miles ?? "", trip.Comments ?? "");
                    csv.AppendLine(newLine);
                }
                File.WriteAllText(fullPath, csv.ToString());
            }
            catch (Exception ex)
            {
                throw new ScheduleBuilderException(
                    "SaveTripListToCSVFile",
                    fullPath,
                    filename,
                    null,
                    0,
                    new IOException("Could not write the CSV for this driver tab.\n\n" + ex.Message, ex),
                    "Output file: " + Path.GetFileName(fullPath));
            }
        }
        private void AddTemplateToList(string filename)
        {
            if (!filename.Contains("Schedule") && !filename.Contains("Reserves") && !filename.Contains("LGTC"))
            {

                if (File.Exists(filename))
                {
                    string actualfilename = Path.GetFileNameWithoutExtension(filename);
                    if (driverTripList.ContainsKey(actualfilename))
                        return;
                    driverTripList.Add(actualfilename, GetTripListFromCSVFile(filename, false));
                    //Console.WriteLine("List added: " + actualfilename);
                }
                else
                {
                    //Console.WriteLine("No file found for: " + drivername);
                }
            }
        }
        private List<MCDownloadedTrip> GetTripListFromCSVFile(string filePath, bool checkdates)
        {
            var templatetriplist = new List<MCDownloadedTrip>();
            string[] rows;
            try
            {
                rows = File.ReadAllLines(filePath);
            }
            catch (Exception ex)
            {
                throw new ScheduleBuilderException(
                    "GetTripListFromCSVFile",
                    filePath,
                    Path.GetFileNameWithoutExtension(filePath),
                    null,
                    0,
                    new IOException("Could not read the template file.\n\n" + ex.Message, ex),
                    "Open the CSV outside the app to verify it is not locked or corrupt.");
            }

            if (rows == null || rows.Length == 0)
                return templatetriplist;

            bool anyNonBlankLine = false;
            foreach (var line in rows)
            {
                if (!string.IsNullOrWhiteSpace(line))
                {
                    anyNonBlankLine = true;
                    break;
                }
            }

            if (!anyNonBlankLine)
                return templatetriplist;

            IDictionary<string, string[]> keyValuePairs = new Dictionary<string, string[]>();
            Regex CSVParser = new Regex(",(?=(?:[^\"]*\"[^\"]*\")*(?![^\"]*\"))");
            int rowcounter = 0;

            for (int row = 0; row < rows.Length; row++)
            {
                int rowIndexOneBased = row + 1;
                string tripNumberForError = null;
                try
                {
                    if (string.IsNullOrWhiteSpace(rows[row]))
                        continue;

                    string[] rowValues = CSVParser.Split(rows[row]);
                    string firstCell = rowValues.Length > 0 ? rowValues[0].Replace("\"", string.Empty).Trim() : string.Empty;

                    if (rowValues.Length < 14)
                    {
                        // Blank Excel tabs export as CSV with one empty field or a single short line — not a real trip row.
                        if (string.IsNullOrEmpty(firstCell) && IsAllCellsWhitespace(rowValues))
                            continue;

                        string tab = Path.GetFileNameWithoutExtension(filePath) ?? "template";
                        throw new ScheduleBuilderException(
                            "GetTripListFromCSVFile",
                            filePath,
                            tab,
                            rowValues.Length > 0 ? rowValues[0].Replace("\"", string.Empty) : null,
                            rowIndexOneBased,
                            new InvalidOperationException(
                                "This line has " + rowValues.Length + " comma-separated value(s) but the schedule format needs 14.\n\n" +
                                CsvColumnLegend + "\n\n" +
                                "Fix line " + rowIndexOneBased + " in " + tab + ".csv (often a comma inside a field without quotes, or a line that is not a full trip)."),
                            "Found " + rowValues.Length + " column(s); need 14 (A through N).");
                    }

                    string[] rowForValidate = rowValues.Length == 14 ? rowValues : rowValues.Take(14).ToArray();
                    if (TripTemplateCsvValidator.IsLikelyHeaderRow(rowForValidate))
                        continue;

                    if (rowValues[0] == string.Empty)
                        continue;

                    tripNumberForError = rowValues[0].Replace("\"", string.Empty);
                    string tabName = Path.GetFileNameWithoutExtension(filePath) ?? "template";
                    var cellIssues = TripTemplateCsvValidator.ValidateTripRow(rowForValidate);
                    if (cellIssues != null && cellIssues.Count > 0)
                    {
                        var firstIssue = cellIssues[0];
                        var detail = new StringBuilder();
                        foreach (var issue in cellIssues)
                            detail.AppendLine(issue.FormatForUser(tabName, rowIndexOneBased));
                        throw new ScheduleBuilderException(
                            "GetTripListFromCSVFile",
                            filePath,
                            tabName,
                            tripNumberForError,
                            rowIndexOneBased,
                            new InvalidOperationException(detail.ToString().TrimEnd()),
                            firstIssue.ColumnLetter + " — " + firstIssue.FieldLabel);
                    }

                    MCDownloadedTrip mCTrip = new MCDownloadedTrip();
                    mCTrip.TripNumber = rowValues[0].Replace("\"", string.Empty);
                    mCTrip.Date = rowValues[1].Replace("\"", string.Empty);
                    mCTrip.ClientFullName = rowValues[2].Replace("\"", string.Empty);
                    mCTrip.PUStreet = rowValues[3].Replace("\"", string.Empty);
                    mCTrip.PUCity = rowValues[4].Replace("\"", string.Empty);
                    mCTrip.PUTelephone = rowValues[5].Replace("\"", string.Empty);
                    mCTrip.PUTime = rowValues[6].Replace("\"", string.Empty);
                    mCTrip.DOStreet = rowValues[7].Replace("\"", string.Empty);
                    mCTrip.DOCITY = rowValues[8].Replace("\"", string.Empty);
                    mCTrip.DOTelephone = rowValues[9].Replace("\"", string.Empty);
                    mCTrip.DOTime = rowValues[10].Replace("\"", string.Empty);
                    mCTrip.Age = rowValues[11].Replace("\"", string.Empty);
                    mCTrip.Miles = rowValues[12].Replace("\"", string.Empty);
                    mCTrip.Comments = rowValues[13].Replace("\"", string.Empty);

                    templatetriplist.Add(mCTrip);
                    keyValuePairs.Add("row" + rowcounter.ToString(), rowValues);
                    rowcounter++;
                }
                catch (ScheduleBuilderException) { throw; }
                catch (Exception ex)
                {
                    throw new ScheduleBuilderException(
                        "GetTripListFromCSVFile",
                        filePath,
                        Path.GetFileNameWithoutExtension(filePath),
                        tripNumberForError,
                        rowIndexOneBased,
                        ex,
                        "Parsing trip columns A–N from this line");
                }
            }

            return templatetriplist;
        }

        private static bool IsAllCellsWhitespace(string[] rowValues)
        {
            if (rowValues == null || rowValues.Length == 0)
                return true;
            foreach (var c in rowValues)
            {
                if (!string.IsNullOrWhiteSpace((c ?? "").Replace("\"", string.Empty)))
                    return false;
            }

            return true;
        }



        public async Task CreateWorkbookAsync()
        {
            object misValue = System.Reflection.Missing.Value;
            Microsoft.Office.Interop.Excel.Application xlApp = null;
            Microsoft.Office.Interop.Excel.Workbook newWorkbook = null;
            string currentFile = null;

            try
            {
                await AsyncUpdateLoadingScreen("Starting Excel");
                xlApp = new Microsoft.Office.Interop.Excel.Application { Visible = false };
                newWorkbook = xlApp.Workbooks.Add();

                var tempDir = TemplateBuilder.GetTemplateTempDirectory();
                if (!Directory.Exists(tempDir))
                {
                    HideLoadingScreen();
                    throw new ScheduleBuilderException(
                        "CreateWorkbook",
                        tempDir,
                        null,
                        null,
                        0,
                        new DirectoryNotFoundException(
                            "The working folder for schedule CSV files does not exist:\n" + tempDir + "\n\n" +
                            "Run the schedule builder again from the start."),
                        "—");
                }

                await AsyncUpdateLoadingScreen("Choose a location to save schedule");
                SaveFileDialog saveDlg = new SaveFileDialog();
                saveDlg.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                saveDlg.Filter = "Excel files (*.xlsx)|*.xlsx";
                saveDlg.FilterIndex = 0;
                saveDlg.RestoreDirectory = true;
                saveDlg.Title = "Export Excel File To";
                saveDlg.FileName = "Schedule for " + NameOfMonth + " " + Day + " " + Year + ".xlsx";

                if (saveDlg.ShowDialog() != DialogResult.OK)
                {
                    await AsyncUpdateLoadingScreen("Cancelling process..");
                    try { newWorkbook?.Close(false); xlApp?.Quit(); } catch { }
                    HideLoadingScreen();
                    return;
                }

                await AsyncUpdateLoadingScreen("Building workbook");
                var fileList = Directory.EnumerateFiles(TemplateBuilder.GetTemplateTempDirectory()).ToList();
                if (fileList.Count == 0)
                {
                    throw new ScheduleBuilderException(
                        "CreateWorkbook",
                        tempDir,
                        null,
                        null,
                        0,
                        new InvalidOperationException(
                            "There are no CSV files in the working folder to put into the new workbook.\n\n" +
                            "Try building the schedule again; if this keeps happening, check that template matching produced files in:\n" + tempDir),
                        "—");
                }

                var counter = 1;
                foreach (var file in fileList)
                {
                    string tabName = Path.GetFileNameWithoutExtension(file) ?? file;
                    currentFile = file;
                    Microsoft.Office.Interop.Excel.Workbook csvWorkbook = null;
                    Microsoft.Office.Interop.Excel.Worksheet worksheetCSV = null;
                    Microsoft.Office.Interop.Excel.Worksheet targetWorksheet = null;
                    try
                    {
                        csvWorkbook = xlApp.Workbooks.Open(file);
                        worksheetCSV = (Microsoft.Office.Interop.Excel.Worksheet)csvWorkbook.Worksheets[1];
                        targetWorksheet = (Microsoft.Office.Interop.Excel.Worksheet)newWorkbook.Worksheets[counter];
                        worksheetCSV.Copy(targetWorksheet);
                        counter++;
                    }
                    catch (Exception ex)
                    {
                        throw new ScheduleBuilderException(
                            "CreateWorkbook.ImportCsv",
                            file,
                            tabName,
                            null,
                            counter,
                            new InvalidOperationException(
                                "Excel could not import this driver CSV into the new workbook.\n\n" +
                                "Close the file if it is open elsewhere, check the CSV is not corrupt, then try again.\n\n" +
                                "Detail: " + ex.Message,
                                ex),
                            "Workbook sheet index " + counter + " (each template CSV becomes one sheet tab named like the CSV file)");
                    }
                    finally
                    {
                        if (worksheetCSV != null)
                        {
                            try
                            {
                                Marshal.ReleaseComObject(worksheetCSV);
                            }
                            catch
                            {
                                /* ignore */
                            }
                        }

                        if (csvWorkbook != null)
                        {
                            try
                            {
                                Marshal.ReleaseComObject(csvWorkbook);
                            }
                            catch
                            {
                                /* ignore */
                            }
                        }

                        if (targetWorksheet != null)
                        {
                            try
                            {
                                Marshal.ReleaseComObject(targetWorksheet);
                            }
                            catch
                            {
                                /* ignore */
                            }
                        }
                    }
                }

                currentFile = "(deleting empty sheets)";
                for (int i = xlApp.ActiveWorkbook.Worksheets.Count; i > 0; i--)
                {
                    Worksheet wkSheet = (Worksheet)xlApp.ActiveWorkbook.Worksheets[i];
                    if (wkSheet.Name == "Sheet1")
                    {
                        wkSheet.Delete();
                    }
                    Marshal.ReleaseComObject(wkSheet);
                }
                xlApp.DisplayAlerts = false;

                string path = saveDlg.FileName;
                currentFile = "(saving workbook)";
                newWorkbook.SaveAs(path, Microsoft.Office.Interop.Excel.XlFileFormat.xlWorkbookDefault, Type.Missing, Type.Missing, false, false, XlSaveAsAccessMode.xlNoChange, XlSaveConflictResolution.xlLocalSessionChanges, Type.Missing, Type.Missing);
                newWorkbook.Close(true, misValue, misValue);

                xlApp.Quit();
                Marshal.ReleaseComObject(newWorkbook);
                Marshal.ReleaseComObject(xlApp);

                System.Diagnostics.Process.Start(path);
                await AsyncUpdateLoadingScreen("Finalizing process..");
                HideLoadingScreen();
            }
            catch (ScheduleBuilderException)
            {
                HideLoadingScreen();
                try { newWorkbook?.Close(false); xlApp?.Quit(); } catch { }
                throw;
            }
            catch (Exception ex)
            {
                HideLoadingScreen();
                try
                {
                    newWorkbook?.Close(false);
                    xlApp?.Quit();
                }
                catch
                {
                    /* ignore */
                }

                string tabHint = null;
                try
                {
                    if (!string.IsNullOrEmpty(currentFile) && File.Exists(currentFile))
                        tabHint = Path.GetFileNameWithoutExtension(currentFile);
                }
                catch
                {
                    /* ignore */
                }

                Exception inner = ex;
                if (ex is System.Runtime.InteropServices.COMException comEx &&
                    comEx.HResult == unchecked((int)0x80040154))
                {
                    inner = new InvalidOperationException(
                        "Excel could not be started (Class not registered).\n\n" +
                        "• Install Microsoft Excel on this PC.\n" +
                        "• Use the same bitness as this app (32-bit Office for 32-bit app, 64-bit for 64-bit).\n\n" +
                        "Original error: " + ex.Message,
                        ex);
                }

                throw new ScheduleBuilderException(
                    "CreateWorkbook",
                    currentFile ?? "(unknown step)",
                    tabHint,
                    null,
                    0,
                    inner,
                    "Building or saving the final .xlsx from the CSV files in Template Temps");
            }
        }



















    }
}
