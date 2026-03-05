using Hiatme_Tools;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        private string TemplateDateField { get; set; }
        private async Task AsyncUpdateLoadingScreen(string txt)
        {
            UpdateLoadingScreen(txt);
            await Task.Delay(2000);
        }
        public void StartTemplateBuilder()
        {
            //check base dir for temp folder to add csv files to
            BadScheduleScan = false;
            CreateTempTemplateFiles();
        }

        private void CreateTempTemplateFiles()
        {
            if (Directory.Exists(AppContext.BaseDirectory + "\\Template Temps\\"))
            {
                string[] filePaths = Directory.GetFiles(AppContext.BaseDirectory + "\\Template Temps\\");
                foreach (string filePath in filePaths)
                    File.Delete(filePath);

                //template temp folder found
                //MessageBox.Show("Temp folder found");
                SplitFile(AppContext.BaseDirectory + "Template Temps\\", TemplateNameOfFileToLoad);
            }
            else
            {
                Directory.CreateDirectory(AppContext.BaseDirectory + "\\Template Temps");
                SplitFile(AppContext.BaseDirectory + "Template Temps\\", TemplateNameOfFileToLoad);
            }
        }

        private void SplitFile(string targetPath, string sourceFile)
        {
            //Console.WriteLine(sourceFile);
            Microsoft.Office.Interop.Excel.XlFileFormat fileFormat = Microsoft.Office.Interop.Excel.XlFileFormat.xlOpenXMLWorkbook;

            string exportFormat = "";
            //if (cboExcel.Checked) //set the output format
            //exportFormat = "XLSX";
            //else if (cboCsv.Checked)
            exportFormat = "CSV";

            Microsoft.Office.Interop.Excel.Application xlApp = new Microsoft.Office.Interop.Excel.Application(); //object for controlling Excel
            Microsoft.Office.Interop.Excel.Workbook xlFile = xlApp.Workbooks.Open(sourceFile); //open the source file

            xlApp.DisplayAlerts = false; //override Excel save dialog message
            int TabCount = xlFile.Worksheets.Count; //total count of the tabs in the file

            int sheetCount = 0; //this will be used to output the number of exported sheets
            for (int i = 1; i <= TabCount; i++) //for each sheet in the workbook...
            {
                string sheetName = xlFile.Sheets[i].Name;
                string newFilename = System.IO.Path.Combine(targetPath, sheetName); //set the filename with full path, but no extension

                //toolStripStatus.Text = "Exporting: " + sheetName; //update the status bar
                Microsoft.Office.Interop.Excel.Worksheet tempSheet = xlApp.Worksheets[i]; //Current tab will be saved to this in a new workbook
                tempSheet.Copy();
                Microsoft.Office.Interop.Excel.Workbook tempBook = xlApp.ActiveWorkbook;

                try
                {
                    switch (exportFormat) //if the file does NOT exist OR if does and the the user wants to overwrite it, do the export and increase the sheetCount by 1
                    {
                        case "CSV":
                            newFilename += ".csv";
                            fileFormat = Microsoft.Office.Interop.Excel.XlFileFormat.xlCSV;
                            break;
                        case "XLSX":
                            newFilename += ".xlsx";
                            fileFormat = Microsoft.Office.Interop.Excel.XlFileFormat.xlOpenXMLWorkbook;
                            break;
                    }


                    tempBook.SaveAs(newFilename, fileFormat);
                    tempBook.Close(false);
                    sheetCount++;


                }
                catch (Exception ex)
                {
                    //toolStripStatus.Text = "Error!";
                    string errorMessage = "Error Exporting " + sheetName + System.Environment.NewLine + "Original Message: " + ex.Message;
                    MessageBox.Show(errorMessage, "Error Exporting", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                    //toolStripStatus.Text = "Ready";
                }
            }

            xlFile.Close(false);
            GC.Collect();
            GC.WaitForFullGCComplete();
            GC.Collect();
            GC.WaitForFullGCComplete();

            CheckTemplateFilesLines();
            //MessageBox.Show("Well done!");
        }

        private void CheckTemplateFilesLines()
        {
            datecounter = 0;
            var filePaths = Directory.GetFiles(AppContext.BaseDirectory + "\\Template Temps", "*.csv");
            foreach (string s in filePaths)
            {
                GetTripListFromCSVFile(s, false);
            }
            //check for dates and get the day name of template to replace.
            if (datecounter > 30)
            {
                Console.WriteLine("Matching dates found " + datecounter.ToString() + ". Enough to proceed. (> 30");
                //get dayname from template date so we can save templates for that day of the week
                string[] datesections = TemplateDateField.Split('/');
                DateTime dateValue = new DateTime(Int32.Parse(datesections[2]), Int32.Parse(datesections[0]), Int32.Parse(datesections[1]));
                //Console.WriteLine(dateValue.DayOfWeek);
                TemplateNameOfDay = dateValue.DayOfWeek.ToString();
                //ReplaceTemplatesChoiceDialog(dateValue.DayOfWeek.ToString());
            }

        }
        private IDictionary<string, string[]> GetTripListFromCSVFile(string filePath, bool checkdates)
        {
            IDictionary<string, string[]> keyValuePairs = new Dictionary<string, string[]>();

            //reading all the lines(rows) from the file.
            string[] rows = File.ReadAllLines(filePath);

            int rowcounter = 0;
            //Creating row for each line.
            for (int row = 0; row < rows.Length; row++)
            {

                Regex CSVParser = new Regex(",(?=(?:[^\"]*\"[^\"]*\")*(?![^\"]*\"))");

                string[] rowValues = CSVParser.Split(rows[row]);



                if (rowValues[0] == string.Empty)
                {
                    //blank line. move to next
                    continue;
                }

                for (int i = 0; i < rowValues.Length; i++)
                {
                    if (i < 13)
                    {
                        rowValues[i] = rowValues[i].Replace("\"", string.Empty);
                        //Console.WriteLine(rowValues[i]);
                    }
                    if (i == 11)
                    {
                        if (BadScheduleScanner(rowValues[i]))
                        {
                            BadScheduleScan = true;
                        }
                    }
                }

                for (int i = 0; i < 3; i++)
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

                keyValuePairs.Add("row" + rowcounter.ToString(), rowValues);
                rowcounter++;
            }
            return keyValuePairs;
        }
        private bool BadScheduleScanner(string date)
        {
            //Console.WriteLine(date);
            if (!date.Contains("/") && !date.Equals("") && !date.Equals(","))
            {
                return true;
            }
            return false;
        }
        public async void ReplaceTemplatesChoiceDialog()
        {
            DialogResult dialogResult = MessageBox.Show("You are about to replace the templates for " + TemplateNameOfDay, "Are you sure?", MessageBoxButtons.YesNo);
            if (dialogResult == DialogResult.Yes)
            {
                //check if directory exists for day and move files from temp to day folder
                await AsyncUpdateLoadingScreen("Finalizing process..");
                MoveTemplateFilesToDayFolder(TemplateNameOfDay);
                
            }
            else if (dialogResult == DialogResult.No)
            {
                //do nothing
                await AsyncUpdateLoadingScreen("Cancelling process..");
            }
        }
        private void MoveTemplateFilesToDayFolder(string nameoffolder)
        {
            //check if temp dir exists
            if (Directory.Exists(AppContext.BaseDirectory + "\\Template Temps\\"))
            {
                //check if day folder exists
                if (Directory.Exists(AppContext.BaseDirectory + "\\" + nameoffolder + "\\"))
                {
                    //both directories exist. delete files in template day and move files over.
                    string[] filePaths = Directory.GetFiles(AppContext.BaseDirectory + "\\" + nameoffolder);
                    foreach (string filePath in filePaths)
                    File.Delete(filePath);
                    try
                    {
                        foreach (var file in Directory.EnumerateFiles(AppContext.BaseDirectory + "\\Template Temps\\"))
                        {

                            if (CheckForBadTemplateNames(file))
                            {

;
                            }
                            else
                            {
                                string destFile = Path.Combine(AppContext.BaseDirectory + "\\" + nameoffolder, Path.GetFileName(file));
                                if (File.Exists(destFile))
                                {
                                    File.Delete(destFile);
                                    File.Move(file, destFile);
                                }
                                else
                                {
                                    File.Move(file, destFile);
                                }
                            }


                        }
                    }
                    catch (Exception e)
                    {
                        MessageBox.Show(e.ToString());
                    }
                }
                else
                {
                    //if it doesnt exist create it then move files
                    Directory.CreateDirectory(AppContext.BaseDirectory + "\\" + nameoffolder);
                    try
                    {
                        foreach (var file in Directory.EnumerateFiles(AppContext.BaseDirectory + "\\Template Temps\\"))
                        {
                            string destFile = Path.Combine(AppContext.BaseDirectory + "\\" + nameoffolder, Path.GetFileName(file));
                            //if (!File.Exists(destFile))
                            File.Move(file, destFile);

                        }
                    }
                    catch (Exception e)
                    {
                        MessageBox.Show(e.ToString());
                    }
                }




                    //string[] filePaths = Directory.GetFiles(AppContext.BaseDirectory + "\\Template Temps\\");
                    //foreach (string filePath in filePaths)
                    //File.Delete(filePath);

                    //template temp folder found
                    //MessageBox.Show("Temp folder found");

                }

        }
        private bool CheckForBadTemplateNames(string name)
        {
            if (Path.GetFileName(name).Contains("Reserves"))
            {
                return true;
            }
            if (Path.GetFileName(name).Contains("Schedule"))
            {
                return true;
            }
            if (Path.GetFileName(name).Contains("LGTC"))
            {
                return true;
            }
            return false;
        }
        int datecounter;
        private bool CheckForTripDate(string date)
        {
            if (date.Contains("/"))
            {
                //Console.WriteLine("Trip date possibly found: " + date);
                if (TemplateDateField == date)
                {
                    datecounter++;
                }
                TemplateDateField = date;
                return true;
            }
            return false;
        }


        public void GetAvailibleTemplatesForDay(string dayname)
        {
                driverTripList = new Dictionary<string, IDictionary<string, string[]>>();
                if (Directory.Exists(AppContext.BaseDirectory + "\\" + dayname + "\\"))
                {
                    //return a list of driver template names
                    var filePaths = Directory.GetFiles(AppContext.BaseDirectory + "\\" + dayname, "*.csv");
                    foreach (string filesname in filePaths)
                    {
                        AddTemplatesToList(filesname, dayname);
                        //Console.WriteLine(filesname);
                    }
                }
   
                //Console.WriteLine(driverTripList.Count.ToString());
            
                
            

        }
        private void AddTemplatesToList(string filename, string nameofday)
        {
            //IDictionary<string, string[]> drivertriprows = new Dictionary<string, string[]>();
            //string path = AppContext.BaseDirectory + "\\" + nameofday + "\\" + filename;
            if (!filename.Contains("Schedule") && !filename.Contains("Reserves") && !filename.Contains("LGTC"))
            {

            if (File.Exists(filename))
            {
                //Console.WriteLine("File found for: " + drivername);
                string actualfilename = Path.GetFileNameWithoutExtension(filename);
                driverTripList.Add(actualfilename, GetTripListFromCSVFile(filename, false));
                //Console.WriteLine("Template added: " + actualfilename);
            }
            else
            {
                //Console.WriteLine("No file found for: " + drivername);
            }

            }
        }
    }
}
