using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Office.Interop.Excel;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Orchestration helpers for the Supey Schedule tab. Two responsibilities:
    /// <list type="number">
    ///   <item>Downloading Modivcare trips for a service date — same flow as the legacy
    ///   FullScheduleBuilder, just lifted into a reusable helper that doesn't own any UI.</item>
    ///   <item>Emitting the result as a Modivcare-format Excel workbook (one tab per driver +
    ///   Reserves) — same 14-column CSV intermediates the legacy builder uses, so downstream
    ///   tools that ingest those workbooks keep working.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// This intentionally mirrors <see cref="FullScheduleBuilder"/>'s CSV + Excel layout so the
    /// two schedule-builder tools produce interchangeable workbooks; if downstream tools (the
    /// Schedule scanner, billing, etc.) were updated for the legacy format they'll work here too.
    /// </remarks>
    internal static class SupeyScheduleBuilder
    {
        /// <summary>Tab folder used for the per-driver / Reserves CSV intermediates (same one the legacy builder uses).</summary>
        public static string GetTempDirectory() => TemplateBuilder.GetTemplateTempDirectory();

        /// <summary>
        /// Downloads Modivcare trips for the given service date using a shared, already signed-in
        /// <see cref="MCLoginHandler"/>. Surfaces a friendly <see cref="ScheduleBuilderException"/>
        /// when the download fails so the caller's MessageBox can be useful.
        /// </summary>
        public static async Task<List<MCDownloadedTrip>> DownloadTripsAsync(DateTime serviceDate, MCLoginHandler login)
        {
            try
            {
                var dler = new MCTripDownloader();
                var trips = await dler.DownloadTripRecords(serviceDate, login).ConfigureAwait(false);
                return trips ?? new List<MCDownloadedTrip>();
            }
            catch (Exception ex)
            {
                throw new ScheduleBuilderException(
                    "DownloadMCTrips", null, null, null, 0,
                    new InvalidOperationException(
                        "Could not download trips from Modivcare for the selected date.\n\n" +
                        "Check that you are signed in, the date is correct, and your network connection is stable.\n\n" +
                        "Original error: " + ex.Message, ex), "—");
            }
        }

        /// <summary>
        /// Writes the result to the temp folder as 14-column CSVs (one per driver tab + a
        /// Reserves CSV), then opens Excel, imports each CSV as a worksheet, prompts the user
        /// for a save location, and opens the resulting workbook. Mirrors
        /// <see cref="FullScheduleBuilder.CreateWorkbookAsync"/>.
        /// </summary>
        public static async Task SaveWorkbookAsync(SupeyScheduleResult result, IWin32Window owner)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));

            EnsureCleanTempFolder();

            // Build per-driver CSVs in load order so the resulting workbook tab order matches the
            // roster.
            foreach (var plan in result.DriverPlans)
            {
                var trips = new List<MCDownloadedTrip>();
                foreach (var g in plan.Groups)
                    trips.AddRange(g.Trips);
                SaveTripListToCsv(trips, SafeFileName(plan.Driver?.Name ?? "Driver"));
            }
            SaveTripListToCsv(result.Reserves, "Reserves");

            string defaultFileName = "Schedule for " +
                result.ServiceDate.ToString("MMMM") + " " + result.ServiceDate.Day + " " + result.ServiceDate.Year + ".xlsx";

            using (var dlg = new SaveFileDialog
            {
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                Filter = "Excel files (*.xlsx)|*.xlsx",
                FilterIndex = 0,
                RestoreDirectory = true,
                Title = "Export Excel File To",
                FileName = defaultFileName,
            })
            {
                if (dlg.ShowDialog(owner) != DialogResult.OK)
                    return;

                await Task.Run(() => AssembleWorkbook(dlg.FileName)).ConfigureAwait(false);

                try { System.Diagnostics.Process.Start(dlg.FileName); }
                catch { /* failing to open is non-fatal — user can open from Explorer */ }
            }
        }

        private static void EnsureCleanTempFolder()
        {
            string dir = GetTempDirectory();
            if (Directory.Exists(dir))
            {
                foreach (var f in Directory.GetFiles(dir))
                    try { File.Delete(f); } catch { }
            }
            else
            {
                Directory.CreateDirectory(dir);
            }
        }

        private static void SaveTripListToCsv(IList<MCDownloadedTrip> trips, string fileBase)
        {
            string fullPath = Path.Combine(GetTempDirectory(), fileBase + ".csv");
            var csv = new StringBuilder();
            if (trips != null)
            {
                foreach (var t in trips)
                {
                    csv.AppendLine(string.Format("\"{0}\",\"{1}\",\"{2}\",\"{3}\",\"{4}\",\"{5}\",\"{6}\",\"{7}\",\"{8}\",\"{9}\",\"{10}\",\"{11}\",\"{12}\",\"{13}\"",
                        t.TripNumber ?? "", t.Date ?? "", t.ClientFullName ?? "",
                        t.PUStreet ?? "", t.PUCity ?? "", t.PUTelephone ?? "", t.PUTime ?? "",
                        t.DOStreet ?? "", t.DOCITY ?? "", t.DOTelephone ?? "", t.DOTime ?? "",
                        t.Age ?? "", t.Miles ?? "", t.Comments ?? ""));
                }
            }
            File.WriteAllText(fullPath, csv.ToString());
        }

        private static string SafeFileName(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "Driver";
            string s = raw.Trim();
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s;
        }

        private static void AssembleWorkbook(string savePath)
        {
            object misValue = System.Reflection.Missing.Value;
            Microsoft.Office.Interop.Excel.Application xlApp = null;
            Workbook newWorkbook = null;
            try
            {
                xlApp = new Microsoft.Office.Interop.Excel.Application { Visible = false, DisplayAlerts = false };
                newWorkbook = xlApp.Workbooks.Add();

                var fileList = Directory.EnumerateFiles(GetTempDirectory()).ToList();
                int counter = 1;
                foreach (var file in fileList)
                {
                    Workbook csvBook = null;
                    Worksheet src = null;
                    Worksheet dst = null;
                    try
                    {
                        csvBook = xlApp.Workbooks.Open(file);
                        src = (Worksheet)csvBook.Worksheets[1];
                        dst = (Worksheet)newWorkbook.Worksheets[counter];
                        src.Copy(dst);
                        counter++;
                    }
                    finally
                    {
                        if (src != null) try { Marshal.ReleaseComObject(src); } catch { }
                        if (csvBook != null) try { csvBook.Close(false); Marshal.ReleaseComObject(csvBook); } catch { }
                        if (dst != null) try { Marshal.ReleaseComObject(dst); } catch { }
                    }
                }

                // Strip the auto-created blank Sheet1 placeholders.
                for (int i = xlApp.ActiveWorkbook.Worksheets.Count; i > 0; i--)
                {
                    var sheet = (Worksheet)xlApp.ActiveWorkbook.Worksheets[i];
                    if (sheet.Name == "Sheet1") sheet.Delete();
                    Marshal.ReleaseComObject(sheet);
                }

                newWorkbook.SaveAs(savePath, XlFileFormat.xlWorkbookDefault, Type.Missing, Type.Missing,
                    false, false, XlSaveAsAccessMode.xlNoChange, XlSaveConflictResolution.xlLocalSessionChanges,
                    Type.Missing, Type.Missing);
                newWorkbook.Close(true, misValue, misValue);
                xlApp.Quit();
                Marshal.ReleaseComObject(newWorkbook);
                newWorkbook = null;
                Marshal.ReleaseComObject(xlApp);
                xlApp = null;
            }
            finally
            {
                try { newWorkbook?.Close(false); } catch { }
                try { xlApp?.Quit(); } catch { }
                if (newWorkbook != null) try { Marshal.ReleaseComObject(newWorkbook); } catch { }
                if (xlApp != null) try { Marshal.ReleaseComObject(xlApp); } catch { }
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }
    }
}
