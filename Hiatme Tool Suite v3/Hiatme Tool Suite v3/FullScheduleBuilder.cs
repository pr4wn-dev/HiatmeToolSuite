using Hiatme_Tools;
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
    /// <summary>Thrown when schedule building fails. Use StepName, FilePath, DriverName, TripNumber, RowIndex to locate the problem.</summary>
    public class ScheduleBuilderException : Exception
    {
        public string StepName { get; }
        public string FilePath { get; }
        public string DriverName { get; }
        public string TripNumber { get; }
        public int RowIndex { get; }

        public ScheduleBuilderException(string stepName, string filePath, string driverName, string tripNumber, int rowIndex, Exception inner)
            : base(BuildMessage(stepName, filePath, driverName, tripNumber, rowIndex, inner), inner)
        {
            StepName = stepName ?? "";
            FilePath = filePath ?? "";
            DriverName = driverName ?? "";
            TripNumber = tripNumber ?? "";
            RowIndex = rowIndex;
        }

        private static string BuildMessage(string step, string path, string driver, string trip, int row, Exception inner)
        {
            var sb = new StringBuilder("Schedule builder error.");
            if (!string.IsNullOrEmpty(step)) sb.Append(" Step: ").Append(step);
            if (!string.IsNullOrEmpty(path)) sb.Append(" | File: ").Append(path);
            if (!string.IsNullOrEmpty(driver)) sb.Append(" | Driver/Tab: ").Append(driver);
            if (row > 0) sb.Append(" | Row: ").Append(row);
            if (!string.IsNullOrEmpty(trip)) sb.Append(" | Trip: ").Append(trip);
            sb.AppendLine().AppendLine().Append(inner?.Message ?? "Unknown error.");
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
            await Task.Delay(2000);
        }
        public async Task DownloadMCTrips(DateTime mcdate, MCLoginHandler mcLoginHandler)
        {
            try
            {
                MCTripListDLer = new MCTripDownloader();
                MCTripList = new List<MCDownloadedTrip>();
                await AsyncUpdateLoadingScreen("Checking connections");
                MCTripList = await MCTripListDLer.DownloadTripRecords(mcdate, mcLoginHandler);
                await AsyncUpdateLoadingScreen("Downloading trips");
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
                throw new ScheduleBuilderException("DownloadMCTrips", null, null, null, 0, ex);
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
                    throw new ScheduleBuilderException("BuildTempCsvFiles", null, null, null, 0, new InvalidOperationException("No trips were matched. Check that template CSVs exist for " + NameOfDay + " and match the downloaded trip data."));
                }

                CreateWorkbook();
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
        private void LoadTemplateFiles()
        {
            try
            {
                if (Directory.Exists(AppContext.BaseDirectory + NameOfDay + "\\"))
                {
                    driverTripList = new Dictionary<string, List<MCDownloadedTrip>>();
                    var filePaths = Directory.GetFiles(AppContext.BaseDirectory + NameOfDay, "*.csv");
                    foreach (string s in filePaths)
                    {
                        try
                        {
                            AddTemplateToList(s);
                        }
                        catch (ScheduleBuilderException) { throw; }
                        catch (Exception ex)
                        {
                            throw new ScheduleBuilderException("LoadTemplateFiles", s, Path.GetFileNameWithoutExtension(s), null, 0, ex);
                        }
                    }
                }
                CleanTempFolder();
                BuildTempCsvFiles();
            }
            catch (ScheduleBuilderException) { throw; }
            catch (Exception ex)
            {
                throw new ScheduleBuilderException("LoadTemplateFiles", AppContext.BaseDirectory + NameOfDay, null, null, 0, ex);
            }
        }
        private void BuildTempCsvFiles()
        {
            TripsFound = new List<MCDownloadedTrip>();
            if (driverTripList == null)
            {
                throw new ScheduleBuilderException("BuildTempCsvFiles", null, null, null, 0, new InvalidOperationException("No templates for chosen day. Please create them."));
            }
            try
            {
                foreach (KeyValuePair<string, List<MCDownloadedTrip>> templatetriplist in driverTripList)
                {
                    List<MCDownloadedTrip> confirmedtrips = new List<MCDownloadedTrip>();
                    string driverName = templatetriplist.Key;
                    foreach (MCDownloadedTrip templatetrip in templatetriplist.Value)
                    {
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
                        catch (ScheduleBuilderException) { throw; }
                        catch (Exception ex)
                        {
                            throw new ScheduleBuilderException("BuildTempCsvFiles", null, driverName, templatetrip?.TripNumber, 0, ex);
                        }
                    }
                    try
                    {
                        SaveTripListToCSVFile(confirmedtrips, templatetriplist.Key);
                    }
                    catch (ScheduleBuilderException) { throw; }
                    catch (Exception ex)
                    {
                        throw new ScheduleBuilderException("SaveTripListToCSVFile", null, driverName, null, 0, ex);
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
                throw new ScheduleBuilderException("CreateMReservesCSVFile", null, "Reserves", null, 0, ex);
            }
        }
        private void CleanTempFolder()
        {
            if (Directory.Exists(AppContext.BaseDirectory + "\\Template Temps\\"))
            {
                string[] filePaths = Directory.GetFiles(AppContext.BaseDirectory + "\\Template Temps\\");
                foreach (string filePath in filePaths)
                    File.Delete(filePath);
            }
            else
            {
                Directory.CreateDirectory(AppContext.BaseDirectory + "\\Template Temps");
            }
        }
        public void SaveTripListToCSVFile(List<MCDownloadedTrip> triplist, string filename)
        {
            string fullPath = AppContext.BaseDirectory + "Template Temps\\" + filename + ".csv";
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
                throw new ScheduleBuilderException("SaveTripListToCSVFile", fullPath, filename, null, 0, ex);
            }
        }
        private void AddTemplateToList(string filename)
        {
            if (!filename.Contains("Schedule") && !filename.Contains("Reserves") && !filename.Contains("LGTC"))
            {

                if (File.Exists(filename))
                {
                    //Console.WriteLine("File found for: " + drivername);
                    string actualfilename = Path.GetFileNameWithoutExtension(filename);
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
                throw new ScheduleBuilderException("GetTripListFromCSVFile", filePath, Path.GetFileNameWithoutExtension(filePath), null, 0, ex);
            }

            IDictionary<string, string[]> keyValuePairs = new Dictionary<string, string[]>();
            Regex CSVParser = new Regex(",(?=(?:[^\"]*\"[^\"]*\")*(?![^\"]*\"))");
            int rowcounter = 0;

            for (int row = 0; row < rows.Length; row++)
            {
                int rowIndexOneBased = row + 1;
                string tripNumberForError = null;
                try
                {
                    string[] rowValues = CSVParser.Split(rows[row]);
                    if (rowValues.Length < 14)
                    {
                        throw new ScheduleBuilderException("GetTripListFromCSVFile", filePath, Path.GetFileNameWithoutExtension(filePath), rowValues.Length > 0 ? rowValues[0].Replace("\"", string.Empty) : null, rowIndexOneBased, new InvalidOperationException("Row has only " + rowValues.Length + " columns; expected 14. Check for missing or extra commas."));
                    }
                    if (rowValues[0] == string.Empty)
                        continue;

                    tripNumberForError = rowValues[0].Replace("\"", string.Empty);
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
                    throw new ScheduleBuilderException("GetTripListFromCSVFile", filePath, Path.GetFileNameWithoutExtension(filePath), tripNumberForError, rowIndexOneBased, ex);
                }
            }
            return templatetriplist;
        }



        public async void CreateWorkbook()
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

                if (!Directory.Exists(AppContext.BaseDirectory + "\\Template Temps\\"))
                {
                    HideLoadingScreen();
                    return;
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
                var files = from file in Directory.EnumerateFiles(AppContext.BaseDirectory + "\\Template Temps\\") select file;
                var counter = 1;
                Microsoft.Office.Interop.Excel.Workbook csvWorkbook;
                Microsoft.Office.Interop.Excel.Worksheet worksheetCSV;
                foreach (var file in files)
                {
                    currentFile = file;
                    csvWorkbook = xlApp.Workbooks.Open(file);
                    worksheetCSV = ((Microsoft.Office.Interop.Excel.Worksheet)csvWorkbook.Worksheets[1]);

                    Microsoft.Office.Interop.Excel.Worksheet targetWorksheet = ((Microsoft.Office.Interop.Excel.Worksheet)newWorkbook.Worksheets[counter]);
                    worksheetCSV.Copy(targetWorksheet);
                    counter++;
                    Marshal.ReleaseComObject(worksheetCSV);
                    Marshal.ReleaseComObject(csvWorkbook);
                    Marshal.ReleaseComObject(targetWorksheet);
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
                try { newWorkbook?.Close(false); xlApp?.Quit(); } catch { }

                string step = currentFile ?? "CreateWorkbook (starting Excel)";
                var wrapped = new ScheduleBuilderException("CreateWorkbook", step, null, null, 0, ex);

                if (ex is System.Runtime.InteropServices.COMException comEx && (comEx.HResult == unchecked((int)0x80040154)))
                {
                    MessageBox.Show(
                        "Excel could not be started (Class not registered).\n\n" +
                        "• Make sure Microsoft Excel is installed on this PC.\n" +
                        "• If the app is 32-bit, install 32-bit Office; if 64-bit, install 64-bit Office.\n\n" + wrapped.Message,
                        "Schedule Builder – Excel Not Available",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
                else
                {
                    MessageBox.Show(
                        "Schedule build failed while creating the workbook.\n\n" + wrapped.Message,
                        "Schedule Builder Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
            }
        }



















    }
}
