using Microsoft.Office.Interop.Excel;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data.Entity.Core.Common.CommandTrees.ExpressionBuilder;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Forms.DataVisualization.Charting;
using System.Xml.Linq;
using static System.Data.Entity.Infrastructure.Design.Executor;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>Thrown when loading/splitting the schedule Excel file fails. Use TabName, RowIndex, TripNumber, ColumnOrField to locate the problem.</summary>
    public class ScheduleLoadException : Exception
    {
        public string FilePath { get; }
        public string TabName { get; }
        public int RowIndex { get; }
        public string TripNumber { get; }
        public string ColumnOrField { get; }

        public ScheduleLoadException(string filePath, string tabName, int rowIndex, string tripNumber, string columnOrField, Exception inner)
            : base(BuildMessage(filePath, tabName, rowIndex, tripNumber, columnOrField, inner), inner)
        {
            FilePath = filePath;
            TabName = tabName ?? "";
            RowIndex = rowIndex;
            TripNumber = tripNumber ?? "";
            ColumnOrField = columnOrField ?? "";
        }

        private static string BuildMessage(string path, string tab, int row, string trip, string col, Exception inner)
        {
            var sb = new StringBuilder("Schedule load error.");
            if (!string.IsNullOrEmpty(path)) sb.Append(" File: ").Append(path);
            if (!string.IsNullOrEmpty(tab)) sb.Append(" | Tab: ").Append(tab);
            if (row > 0) sb.Append(" | Row: ").Append(row);
            if (!string.IsNullOrEmpty(trip)) sb.Append(" | Trip: ").Append(trip);
            if (!string.IsNullOrEmpty(col)) sb.Append(" | Column/Field: ").Append(col);
            sb.AppendLine().AppendLine().Append(inner?.Message ?? "Unknown error.");
            return sb.ToString();
        }
    }

    /// <summary>Thrown when analysis fails. Use CheckName, DriverName, TripNumber to locate the problem.</summary>
    public class ScheduleAnalysisException : Exception
    {
        public string CheckName { get; }
        public string DriverName { get; }
        public string TripNumber { get; }

        public ScheduleAnalysisException(string checkName, string driverName, string tripNumber, Exception inner)
            : base(BuildMessage(checkName, driverName, tripNumber, inner), inner)
        {
            CheckName = checkName ?? "";
            DriverName = driverName ?? "";
            TripNumber = tripNumber ?? "";
        }

        private static string BuildMessage(string check, string driver, string trip, Exception inner)
        {
            var sb = new StringBuilder("Analysis error.");
            if (!string.IsNullOrEmpty(check)) sb.Append(" Step: ").Append(check);
            if (!string.IsNullOrEmpty(driver)) sb.Append(" | Driver/Tab: ").Append(driver);
            if (!string.IsNullOrEmpty(trip)) sb.Append(" | Trip: ").Append(trip);
            sb.AppendLine().AppendLine().Append(inner?.Message ?? "Unknown error.");
            return sb.ToString();
        }
    }

    internal class Analyzer
    {
        public List<SubjectGrade> gradeList;

        public delegate void UpdateLoadingScreenHandler(string text);
        public delegate void ShowLoadingScreenHandler();
        public delegate void HideLoadingScreenHandler();

        public event UpdateLoadingScreenHandler UpdateLoadingScreen;
        public event ShowLoadingScreenHandler ShowLoadingScreen;
        public event HideLoadingScreenHandler HideLoadingScreen;
        private bool pregrade { get; set; }
        private MCLoginHandler modivcareloginHandler { get; set; }
        private WRLoginHandler wellrydeloginHandler { get; set; }
        public List<WRDrivers> WRDriverList { get; set; }
        private string scheduledate { get; set; }
        private List<MCDownloadedTrip> modivcareDownloadedTrips { get; set; }
        public List<MCDriverTab> drivertablist { get; set; }
        public List<MCDownloadedTrip> loggedScheduleTrips { get; set; }
        private List<WRDownloadedTrip> wellrydeDownloadedTrips { get; set; }
        public void IntializeAnalyzer(MCLoginHandler mCloginHandler, WRLoginHandler wRLoginHandler)
        {
            modivcareloginHandler = mCloginHandler;
            wellrydeloginHandler = wRLoginHandler;
        }
        public async Task StartAnalysis(string longdatestr, int dayint, int yearint, DateTime mcdate)
        {
            notecount = 0;
            drivertablist = new List<MCDriverTab>();
            WRTripDownloader wrtd = new WRTripDownloader();
            wellrydeDownloadedTrips = new List<WRDownloadedTrip>();
            wellrydeDownloadedTrips = await wrtd.DownloadTripRecords(longdatestr, dayint, yearint, wellrydeloginHandler);

            /*
            if (wellrydeDownloadedTrips == null)
            {
                await wellrydeloginHandler.ResetConnection();
                drivertablist = new List<MCDriverTab>();
                wrtd = new WRTripDownloader();
                wellrydeDownloadedTrips = new List<WRDownloadedTrip>();
                wellrydeDownloadedTrips = await wrtd.DownloadTripRecords(longdatestr, dayint, yearint, wellrydeloginHandler);
            }
            */
            WRDriverList = await wrtd.GetAllDrivers(wellrydeloginHandler);

            MCTripDownloader mctd = new MCTripDownloader();
            modivcareDownloadedTrips = new List<MCDownloadedTrip>();
            modivcareDownloadedTrips = await mctd.DownloadTripRecords(mcdate, modivcareloginHandler);

            loggedScheduleTrips = new List<MCDownloadedTrip>();

            gradeList = new List<SubjectGrade>();

        }
        private async Task AsyncUpdateLoadingScreen(string txt)
        {
            UpdateLoadingScreen(txt);
            await Task.Delay(2000);
        }
        public void AnalyzeTrips(DateTime mcdate)
        {
            try
            {
                RunAnalysisStep("FindWrongDatesInSchedule", () => FindWrongDatesInSchedule(mcdate));
                RunAnalysisStep("CheckForCancels", () => CheckForCancels());
                RunAnalysisStep("FindHiddenTrips", () => FindHiddenTrips());
                RunAnalysisStep("CheckForDuplicateTickets", () => CheckForDuplicateTickets());
                RunAnalysisStep("CompareTimes", () => CompareTimes());
                RunAnalysisStep("CompareAddresses", () => CompareAddresses());
                RunAnalysisStep("CheckForEscorts", () => CheckForEscorts());
                RunAnalysisStep("CheckForKids", () => CheckForKids());
                RunAnalysisStep("CheckForWheelchair", () => CheckForWheelchair());
                RunAnalysisStep("CheckForMassTransit", () => CheckForMassTransit());
                RunAnalysisStep("CheckForServiceDog", () => CheckForServiceDog());
                RunAnalysisStep("CheckForWillCallsOnWrongPage", () => CheckForWillCallsOnWrongPage());
                RunAnalysisStep("CheckForScooter", () => CheckForScooter());
                RunAnalysisStep("CheckForEscortComments", () => CheckForEscortComments());
                RunAnalysisStep("GradeAlertMaintenance", () => GradeAlertMaintenance());
                RunAnalysisStep("GradeNotes", () => GradeNotes());
            }
            catch (ScheduleAnalysisException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new ScheduleAnalysisException("AnalyzeTrips", null, null, ex);
            }
        }

        private void RunAnalysisStep(string stepName, System.Action step)
        {
            try
            {
                step();
            }
            catch (ScheduleAnalysisException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new ScheduleAnalysisException(stepName, null, null, ex);
            }
        }

        private void GradeAlertMaintenance()
        {
            SubjectGrade alertMaintenance = new SubjectGrade();
            alertMaintenance.Subject = "Alert Maintenance";

            bool fail = false;
            foreach (MCDownloadedTrip loggedtrip in loggedScheduleTrips)
            {
                try
                {
                    bool reservecancel = false;
                    if (loggedtrip.DriverNameParsed == "Reserves")
                    {
                        if (loggedtrip.GetAlerts().Contains("Cancelled"))
                        {
                            reservecancel = true;
                        }
                    }

                    if (!reservecancel)
                    {
                        if (loggedtrip.GetAlerts().Contains("Date"))
                        {
                            fail = true;
                        }
                        if (loggedtrip.GetAlerts().Contains("Hidden"))
                        {
                            fail = true;
                        }
                        if (loggedtrip.GetAlerts().Contains("Cancelled"))
                        {
                            fail = true;
                        }
                        if (loggedtrip.GetAlerts().Contains("Dupe"))
                        {
                            fail = true;
                        }
                        if (loggedtrip.GetAlerts().Contains("WC Not in reserves!"))
                        {
                            fail = true;
                        }
                        if (loggedtrip.GetAlerts().Contains("Time"))
                        {
                            fail = true;
                        }
                        if (loggedtrip.GetAlerts().Contains("Address"))
                        {
                            fail = true;
                        }
                    }
                }
                catch (ScheduleAnalysisException) { throw; }
                catch (Exception ex) { throw new ScheduleAnalysisException("GradeAlertMaintenance", loggedtrip?.DriverNameParsed, loggedtrip?.TripNumber, ex); }
            }

            if(fail)
            {
                alertMaintenance.Notes = "- Clean up alerts!";
                alertMaintenance.Grade = "F";
            }
            else
            {
                alertMaintenance.Notes = "- Great job! :)";
                alertMaintenance.Grade = "A+";
            }
            gradeList.Add(alertMaintenance);
        }
        private void FindHiddenTrips()
        {
            if (wellrydeDownloadedTrips.Any() & modivcareDownloadedTrips.Any())
            {
                foreach (MCDownloadedTrip mcdt in modivcareDownloadedTrips)
                {
                    bool tripfound = false;
                    foreach (WRDownloadedTrip wrdt in wellrydeDownloadedTrips)
                    {
                        if (wrdt.TripNumber.Replace(" ", "") == mcdt.TripNumber.Replace(" ", ""))
                        {
                            tripfound = true;
                            break;
                        }
                    }
                    if (!tripfound)
                    {
                        CheckIfTripIsAlreadyLogged(mcdt, "Hidden");
                    }
                }
            }
        }
        private void FindWrongDatesInSchedule(DateTime modivecaredate)
        {
            string datestr = ReturnDateFromMCTime(modivecaredate.ToString());
            if (drivertablist.Any())
            {
                foreach (MCDriverTab dt in drivertablist)
                {
                    foreach (MCDownloadedTrip mcst in dt.scheduledTrips)
                    {
                        try
                        {
                            if (mcst.Date != datestr)
                            {
                                CheckIfTripIsAlreadyLogged(mcst, "Date");
                            }
                        }
                        catch (ScheduleAnalysisException) { throw; }
                        catch (Exception ex) { throw new ScheduleAnalysisException("FindWrongDatesInSchedule", dt.driverName, mcst?.TripNumber, ex); }
                    }
                }
            }
        }
        private void CheckForDuplicateTickets()
        {
            if (drivertablist.Any() & modivcareDownloadedTrips.Any())
            {
                foreach (MCDriverTab dt in drivertablist)
                {
                    foreach (MCDownloadedTrip mcst in dt.scheduledTrips)
                    {
                        try
                        {
                            bool tripscanned = false;
                            foreach (MCDownloadedTrip existingtrip in loggedScheduleTrips)
                            {
                                if (existingtrip.TripNumber.Replace(" ", "") == mcst.TripNumber.Replace(" ", ""))
                                {
                                    tripscanned = true;
                                }
                            }
                            if (tripscanned)
                            {
                                continue;
                            }
                            List<MCDownloadedTrip> dupelist = new List<MCDownloadedTrip>();

                            foreach (MCDriverTab dt2 in drivertablist)
                            {
                                foreach (MCDownloadedTrip mcst2 in dt2.scheduledTrips)
                                {
                                    if (mcst2.TripNumber.Replace(" ", "") == mcst.TripNumber.Replace(" ", ""))
                                    {
                                        dupelist.Add(mcst2);
                                    }
                                }
                            }
                            if (dupelist.Count > 1)
                            {
                                foreach (MCDownloadedTrip dupedtrip in dupelist)
                                {
                                    dupedtrip.Alerts.Add("Dupe");
                                    mcst.Assignable = false;
                                    loggedScheduleTrips.Add(dupedtrip);
                                }
                            }
                        }
                        catch (ScheduleAnalysisException) { throw; }
                        catch (Exception ex) { throw new ScheduleAnalysisException("CheckForDuplicateTickets", dt.driverName, mcst?.TripNumber, ex); }
                    }
                }
            }
        }
        private void CompareTimes()
        {
            foreach (MCDriverTab dt in drivertablist)
            {
                foreach (MCDownloadedTrip mcst in dt.scheduledTrips)
                {
                    try
                    {
                        foreach (MCDownloadedTrip mcdt in modivcareDownloadedTrips)
                        {
                            if (mcdt.TripNumber.Replace(" ","") == mcst.TripNumber.Replace(" ", ""))
                            {
                                if (mcdt.PUTime != mcst.PUTime)
                                {
                                    CheckIfTripIsAlreadyLogged(mcst, "Time");
                                    mcst.Assignable = false;
                                    break;
                                }

                                if (mcdt.DOTime != mcst.DOTime)
                                {
                                    CheckIfTripIsAlreadyLogged(mcst, "Time");
                                    mcst.Assignable = false;
                                    break;
                                }
                            }
                        }
                    }
                    catch (ScheduleAnalysisException) { throw; }
                    catch (Exception ex) { throw new ScheduleAnalysisException("CompareTimes", dt.driverName, mcst?.TripNumber, ex); }
                }
            }
        }
        private void CompareAddresses()
        {
            foreach (MCDriverTab dt in drivertablist)
            {
                foreach (MCDownloadedTrip mcst in dt.scheduledTrips)
                {
                    try
                    {
                        foreach (MCDownloadedTrip mcdt in modivcareDownloadedTrips)
                        {
                            if (mcdt.TripNumber.Replace(" ", "") == mcst.TripNumber.Replace(" ", ""))
                            {
                                if (mcdt.PUStreet != mcst.PUStreet)
                                {
                                    CheckIfTripIsAlreadyLogged(mcst, "Address");
                                    mcst.Assignable = false;
                                    break;
                                }

                                if (mcdt.DOStreet != mcst.DOStreet)
                                {
                                    CheckIfTripIsAlreadyLogged(mcst, "Address");
                                    mcst.Assignable = false;
                                    break;
                                }
                            }
                        }
                    }
                    catch (ScheduleAnalysisException) { throw; }
                    catch (Exception ex) { throw new ScheduleAnalysisException("CompareAddresses", dt.driverName, mcst?.TripNumber, ex); }
                }
            }
        }
        private void CheckForEscorts()
        {
            if (drivertablist.Any() & wellrydeDownloadedTrips.Any())
            {
                foreach (MCDriverTab dt in drivertablist)
                {
                    foreach (MCDownloadedTrip mcst in dt.scheduledTrips)
                    {
                        try
                        {
                            foreach (WRDownloadedTrip wrdt in wellrydeDownloadedTrips)
                            {
                                if (wrdt.TripNumber.Replace(" ", "") == mcst.TripNumber.Replace(" ", ""))
                                {
                                    if (Convert.ToInt16(wrdt.Escorts) > 0)
                                    {
                                        CheckIfTripIsAlreadyLogged(mcst, "Escort");
                                        break;
                                    }
                                }
                            }
                        }
                        catch (ScheduleAnalysisException) { throw; }
                        catch (Exception ex) { throw new ScheduleAnalysisException("CheckForEscorts", dt.driverName, mcst?.TripNumber, ex); }
                    }
                }
            }
        }
        private void CheckForKids()
        {
            if (drivertablist.Any() & modivcareDownloadedTrips.Any())
            {
                foreach (MCDriverTab dt in drivertablist)
                {
                    foreach (MCDownloadedTrip mcst in dt.scheduledTrips)
                    {
                        try
                        {
                            foreach (MCDownloadedTrip mcdt in modivcareDownloadedTrips)
                            {
                                if (mcdt.TripNumber.Replace(" ", "") == mcst.TripNumber.Replace(" ", ""))
                                {
                                    if (IsDOBAKid(mcdt.Age.Replace(" ","")) | DoesCommentsContainKids(mcdt.Comments))
                                    {
                                        CheckIfTripIsAlreadyLogged(mcst, "Child");
                                        break;
                                    }
                                }
                            }
                        }
                        catch (ScheduleAnalysisException) { throw; }
                        catch (Exception ex) { throw new ScheduleAnalysisException("CheckForKids", dt.driverName, mcst?.TripNumber, ex); }
                    }
                }
            }
        }
        private bool IsDOBAKid(string dob)
        {
            string[] dobsubitems = dob.Split('/');
            int today = DateTime.Now.Year;
            //Console.WriteLine("Rider dob: " + dobsubitems[2]);
            if (Int32.Parse(dobsubitems[2]) > (today - 18))
            {
                Console.WriteLine("Rider is a child: " + dobsubitems[2]);
                return true;
            }
            int age = today - Int32.Parse(dobsubitems[2]);
            //Console.WriteLine("rider is: " + age);
            return false;
        }
        private bool DoesCommentsContainKids(string comments)
        {
            if (comments.ToLower().Contains("kid"))
            {
                return true;
            }
            if (comments.ToLower().Contains("kids"))
            {
                return true;
            }
            if (comments.ToLower().Contains("child"))
            {
                return true;
            }
            if (comments.ToLower().Contains("children"))
            {
                return true;
            }
            if (comments.ToLower().Contains("infant"))
            {
                return true;
            }
            if (comments.ToLower().Contains("baby"))
            {
                return true;
            }
            if (comments.ToLower().Contains("toddler"))
            {
                return true;
            }


            return false;
        }
        private void CheckForLBS()
        {
            if (drivertablist.Any() & modivcareDownloadedTrips.Any())
            {
                foreach (MCDriverTab dt in drivertablist)
                {
                    foreach (MCDownloadedTrip mcst in dt.scheduledTrips)
                    {
                        foreach (MCDownloadedTrip mcdt in modivcareDownloadedTrips)
                        {
                            if (mcdt.TripNumber.Replace(" ", "") == mcst.TripNumber.Replace(" ", ""))
                            {
                                if (DoesCommentsContainLBS(mcdt.Comments))
                                {
                                    CheckIfTripIsAlreadyLogged(mcst, "LBS");
                                    break;
                                }
                            }
                        }
                    }
                }
            }
        }
        private bool DoesCommentsContainLBS(string comments)
        {
            if (comments.ToLower().Contains("lbs"))
            {
                return true;
            }
            return false;

        }
        private void CheckForServiceDog()
        {
            if (drivertablist.Any() & modivcareDownloadedTrips.Any())
            {
                foreach (MCDriverTab dt in drivertablist)
                {
                    foreach (MCDownloadedTrip mcst in dt.scheduledTrips)
                    {
                        try
                        {
                            foreach (MCDownloadedTrip mcdt in modivcareDownloadedTrips)
                            {
                                if (mcdt.TripNumber.Replace(" ", "") == mcst.TripNumber.Replace(" ", ""))
                                {
                                    if (DoesCommentsContainServiceDog(mcdt.Comments))
                                    {
                                        CheckIfTripIsAlreadyLogged(mcst, "Service Dog");
                                        break;
                                    }
                                }
                            }
                        }
                        catch (ScheduleAnalysisException) { throw; }
                        catch (Exception ex) { throw new ScheduleAnalysisException("CheckForServiceDog", dt.driverName, mcst?.TripNumber, ex); }
                    }
                }
            }
        }
        private bool DoesCommentsContainServiceDog(string comments)
        {
            if (comments.ToLower().Contains("service dog"))
            {
                return true;
            }
            if (comments.ToLower().Contains("dog"))
            {
                return true;
            }
            if (comments.ToLower().Contains("animal"))
            {
                return true;
            }
            if (comments.ToLower().Contains("pet"))
            {
                return true;
            }
            return false;
        }

        private void CheckForScooter()
        {
            if (drivertablist.Any() & modivcareDownloadedTrips.Any())
            {
                foreach (MCDriverTab dt in drivertablist)
                {
                    foreach (MCDownloadedTrip mcst in dt.scheduledTrips)
                    {
                        try
                        {
                            foreach (MCDownloadedTrip mcdt in modivcareDownloadedTrips)
                            {
                                if (mcdt.TripNumber.Replace(" ", "") == mcst.TripNumber.Replace(" ", ""))
                                {
                                    if (DoesCommentsContainScooter(mcdt.Comments))
                                    {
                                        CheckIfTripIsAlreadyLogged(mcst, "Scooter");
                                        break;
                                    }
                                }
                            }
                        }
                        catch (ScheduleAnalysisException) { throw; }
                        catch (Exception ex) { throw new ScheduleAnalysisException("CheckForScooter", dt.driverName, mcst?.TripNumber, ex); }
                    }
                }
            }
        }
        private bool DoesCommentsContainScooter(string comments)
        {
            if (comments.ToLower().Contains("scooter"))
            {
                return true;
            }

            return false;
        }

        private void CheckForEscortComments()
        {
            if (drivertablist.Any() & modivcareDownloadedTrips.Any())
            {
                foreach (MCDriverTab dt in drivertablist)
                {
                    foreach (MCDownloadedTrip mcst in dt.scheduledTrips)
                    {
                        try
                        {
                            foreach (MCDownloadedTrip mcdt in modivcareDownloadedTrips)
                            {
                                if (mcdt.TripNumber.Replace(" ", "") == mcst.TripNumber.Replace(" ", ""))
                                {
                                    if (DoesCommentsContainEscort(mcdt.Comments))
                                    {
                                        CheckIfTripIsAlreadyLogged(mcst, "Escort");
                                        break;
                                    }
                                }
                            }
                        }
                        catch (ScheduleAnalysisException) { throw; }
                        catch (Exception ex) { throw new ScheduleAnalysisException("CheckForEscortComments", dt.driverName, mcst?.TripNumber, ex); }
                    }
                }
            }
        }
        private bool DoesCommentsContainEscort(string comments)
        {
            if (comments.ToLower().Contains("escort"))
            {
                return true;
            }

            if (comments.ToLower().Contains("escorts"))
            {
                return true;
            }

            return false;
        }
        private void CheckForMassTransit()
        {
            if (drivertablist.Any() & modivcareDownloadedTrips.Any())
            {
                foreach (MCDriverTab dt in drivertablist)
                {
                    foreach (MCDownloadedTrip mcst in dt.scheduledTrips)
                    {
                        try
                        {
                            foreach (MCDownloadedTrip mcdt in modivcareDownloadedTrips)
                            {
                                if (mcdt.TripNumber.Replace(" ", "") == mcst.TripNumber.Replace(" ", ""))
                                {
                                    if (DoesCommentsContainMassTransit(mcdt.Comments))
                                    {
                                        CheckIfTripIsAlreadyLogged(mcst, "Mass Transit");
                                        break;
                                    }
                                }
                            }
                        }
                        catch (ScheduleAnalysisException) { throw; }
                        catch (Exception ex) { throw new ScheduleAnalysisException("CheckForMassTransit", dt.driverName, mcst?.TripNumber, ex); }
                    }
                }
            }
        }
        private bool DoesCommentsContainMassTransit(string comments)
        {
            if (comments.ToLower().Contains("mass transit"))
            {
                return true;
            }
            return false;
        }
        private void CheckForWheelchair()
        {
            if (drivertablist.Any() & modivcareDownloadedTrips.Any())
            {
                foreach (MCDriverTab dt in drivertablist)
                {
                    foreach (MCDownloadedTrip mcst in dt.scheduledTrips)
                    {
                        try
                        {
                            foreach (MCDownloadedTrip mcdt in modivcareDownloadedTrips)
                            {
                                if (mcdt.TripNumber.Replace(" ", "") == mcst.TripNumber.Replace(" ", ""))
                                {
                                    if (DoesCommentsContainWheelchair(mcdt.Comments))
                                    {
                                        CheckIfTripIsAlreadyLogged(mcst, "MWC");
                                        break;
                                    }
                                }
                            }
                        }
                        catch (ScheduleAnalysisException) { throw; }
                        catch (Exception ex) { throw new ScheduleAnalysisException("CheckForWheelchair", dt.driverName, mcst?.TripNumber, ex); }
                    }
                }
            }
        }
        private bool DoesCommentsContainWheelchair(string comments)
        {
            if (comments.ToLower().Contains("mwc"))
            {
                return true;
            }
            if (comments.ToLower().Contains("wheelchair"))
            {
                return true;
            }
            return false;
        }
        private void CheckForCancels()
        {
            if (drivertablist.Any() & modivcareDownloadedTrips.Any())
            {
                foreach (MCDriverTab dt in drivertablist)
                {
                        foreach (MCDownloadedTrip mcst in dt.scheduledTrips)
                        {
                            bool tripfound = false;
                            foreach (MCDownloadedTrip mcdt in modivcareDownloadedTrips)
                            {
                                if (mcdt.TripNumber.Replace(" ", "") == mcst.TripNumber.Replace(" ", ""))
                                {
                                    tripfound = true;
                                }
                            }
                            if (!tripfound)
                            {
                            CheckIfTripIsAlreadyLogged(mcst, "Cancelled");
                            mcst.Assignable = false;
                            pregrade = false;
                        }
                        }
                }
            }
        }
        private void CheckForWillCallsOnWrongPage()
        {
            if (drivertablist.Any())
            {
                foreach (MCDriverTab dt in drivertablist)
                {
                    foreach (MCDownloadedTrip mcst in dt.scheduledTrips)
                    {
                        try
                        {
                            if (dt.driverName != "Reserves")
                            {
                                if (mcst.PUTime != null && mcst.PUTime.Replace(" ", "") == "00:00")
                                {
                                    CheckIfTripIsAlreadyLogged(mcst, "WC Not in reserves!");
                                    mcst.Assignable = false;
                                }
                            }
                        }
                        catch (ScheduleAnalysisException) { throw; }
                        catch (Exception ex) { throw new ScheduleAnalysisException("CheckForWillCallsOnWrongPage", dt.driverName, mcst?.TripNumber, ex); }
                    }
                }
            }
        }
        public int ReturnAlertCount()
        {
            int alertcount = 0;
           
            foreach (MCDownloadedTrip loggedtrp in loggedScheduleTrips)
            {
                alertcount += loggedtrp.GetAlertCount();
            }

            return alertcount;
        }
        private void CheckIfTripIsAlreadyLogged(MCDownloadedTrip trp, string alert)
        {
            bool tripfound = false;
            foreach (MCDownloadedTrip logtrip in loggedScheduleTrips)
            {
                if (logtrip.TripNumber.Replace(" ", "") == trp.TripNumber.Replace(" ", ""))
                {
                    tripfound = true;
                    bool alertfound = false;
                    foreach (string alertflag in logtrip.Alerts)
                    {
                        if (alertflag == alert)
                        {
                            alertfound = true;
                            break;
                        }
                    }
                    if (!alertfound)
                    {
                        logtrip.Alerts.Add(alert);
                    }
                   
                }
            }
            if (!tripfound)
            {
                trp.Alerts.Add(alert);
                loggedScheduleTrips.Add(trp);
            }
        }
        public async Task StartTripAssigning(string longdatestr, int dayint, int yearint, DateTime mcdate)
        {
            await AsyncUpdateLoadingScreen("Unassigning trips");
            await UnassignTrips();
            
            await AssignTrips(longdatestr, dayint, yearint, mcdate);
        }
        public int GetAssignedTripCount()
        {
            int asstripcount = 0;
            foreach (WRDownloadedTrip wrdt in wellrydeDownloadedTrips)
            {
                if (wrdt.Status == "Assigned")
                {
                    asstripcount++;
                }
            }
            return asstripcount;
        }
        public int GetReservedTripCount()
        {
            int restripcount = 0;
            foreach (WRDownloadedTrip wrdt in wellrydeDownloadedTrips)
            {
                if (wrdt.Status == "Reserved")
                {
                    restripcount++;
                }
            }
            return restripcount;
        }
        private async Task UnassignTrips()
        {
            if (drivertablist.Any() & wellrydeDownloadedTrips.Any())
            {
                List<string> unassignabletrips = new List<string>();

                foreach (WRDownloadedTrip wrdt in wellrydeDownloadedTrips)
                {
                    if (wrdt.Status == "Assigned")
                    {
                        unassignabletrips.Add(wrdt.TripUUID);
                    }
                }
                await UnassignTripBatch(unassignabletrips);
            }
        }
        private async Task UnassignTripBatch(List<string> trips)
        {
            //Console.WriteLine("About to submit trips for driver id: " + id);
            string tripformlist = "";
            foreach (string trip in trips)
            {
                tripformlist += trip + ",";

            }
            //Console.WriteLine(tripformlist);

            var formContent = new FormUrlEncodedContent(new[]{
             new KeyValuePair<string, string>("tripUUIDs", tripformlist),
            // new KeyValuePair<string, string>("hasAssigned", "0"),
             new KeyValuePair<string, string>("_csrf", wellrydeloginHandler._CsrfToken),
             });

            HttpResponseMessage res = await wellrydeloginHandler.Client.PostAsync("https://portal.app.wellryde.com/portal//trip/unAssignValidation", formContent);
            var response = await res.Content.ReadAsStringAsync();

            Console.WriteLine(response.Length);
            if (response.Length == 0)
            {
                await wellrydeloginHandler.ResetConnection();
                await UnassignTripBatch(trips);
            }
            else
            {
                await FinalizeUnassignTripBatch(trips);
            }
        }
        private async Task FinalizeUnassignTripBatch(List<string> tripscopy)
        {
            //Console.WriteLine("About to submit trips for driver id: " + id);
            string tripformlist = "";
            foreach (string trip in tripscopy)
            {
                tripformlist += trip + ",";

            }
            //Console.WriteLine(tripformlist);

            var formContent = new FormUrlEncodedContent(new[]{
             new KeyValuePair<string, string>("vizzonIds", tripformlist),
             new KeyValuePair<string, string>("viewName", "trip"),
             new KeyValuePair<string, string>("_csrf", wellrydeloginHandler._CsrfToken),
             });

            HttpResponseMessage res = await wellrydeloginHandler.Client.PostAsync("https://portal.app.wellryde.com/portal/trip/unassign", formContent);
            var response = await res.Content.ReadAsStringAsync();
        }
        private async Task AssignTrips(string longdatestring, int dayintnum, int yearintnum, DateTime mcdatedt)
        {
            if (drivertablist.Any())
            {
                foreach (MCDriverTab dt in drivertablist)
                {
                    foreach (WRDrivers driver in WRDriverList)
                    {
                        if (dt.driverName == SplitDriverName(driver.text))
                        {
                            Console.WriteLine(driver.text + ":" + driver.value);
                            List<string> driverstrips = new List<string>();

                            foreach (MCDownloadedTrip mcst in dt.scheduledTrips)
                            {
                                if (mcst.Assignable)
                                {
                                    Console.WriteLine(mcst.TripNumber + " [alerts: " + mcst.GetAlerts() + "]");

                                    foreach (WRDownloadedTrip wrdt in wellrydeDownloadedTrips)
                                    {
                                        if (wrdt.TripNumber.Replace(" ", "") == mcst.TripNumber.Replace(" ", ""))
                                        {
                                            if (wrdt.Status != "Cancelled")
                                            {
                                                driverstrips.Add(wrdt.TripUUID);
                                                break;
                                            }
                                        }
                                    }
                                }
                            }

                            await AsyncUpdateLoadingScreen("Assigning " + driverstrips.Count + " trips to " + SplitDriverName(driver.text));
                            await SubmitTripBatch(driver.value, driverstrips);
                           
                        }
                    }
                }

                WRTripDownloader wrtd = new WRTripDownloader();
                wellrydeDownloadedTrips = new List<WRDownloadedTrip>();
                await AsyncUpdateLoadingScreen("Refreshing records");
                wellrydeDownloadedTrips = await wrtd.DownloadTripRecords(longdatestring, dayintnum, yearintnum, wellrydeloginHandler);
            }
        }
        private async Task SubmitTripBatch(string id, List<string> trips)
        {
            Console.WriteLine("About to submit trips for driver id: " + id);
            string tripformlist = "";
            foreach (string trip in trips)
            {
                tripformlist += trip + ",";
            }
            //Console.WriteLine(tripformlist);

            //string decodedfilterlist = HttpUtility.UrlDecode("%5B%7B%22sequence%22%3A%221%22%2C%22value%22%3A%22-1%22%7D%2C%7B%22sequence%22%3A%222%22%2C%22value%22%3A%22-1%22%7D%2C%7B%22sequence%22%3A%223%22%2C%22value%22%3A%22-1%22%7D%2C%7B%22sequence%22%3A%224%22%2C%22value%22%3A%22-1%22%7D%2C%7B%22sequence%22%3A%225%22%2C%22value%22%3A%22-1%22%7D%2C%7B%22sequence%22%3A%226%22%2C%22value%22%3A%22-1%22%7D%2C%7B%22sequence%22%3A%227%22%2C%22value%22%3A%22%7B%5C%22specificDate%5C%22%3A%5C%22" + finaldate + "%5C%22%7D%22%7D%2C%7B%22sequence%22%3A%228%22%2C%22value%22%3A%22-1%22%7D%2C%7B%22sequence%22%3A%229%22%2C%22value%22%3A%22-1%22%7D%2C%7B%22sequence%22%3A%2210%22%2C%22value%22%3A%22-1%22%7D%5D");
            //string decodedfilterArgsJson = HttpUtility.UrlDecode("%7B%22fetchColumnInfo%22%3Afalse%7D");
            //string decodedfilterValues = HttpUtility.UrlDecode("%5B%5D");

            var formContent = new FormUrlEncodedContent(new[]{
             new KeyValuePair<string, string>("tripUUIDs", tripformlist),
             new KeyValuePair<string, string>("driverId", id),
             new KeyValuePair<string, string>("hasAssigned", "0"),
             new KeyValuePair<string, string>("_csrf", wellrydeloginHandler._CsrfToken),
             });

            HttpResponseMessage res = await wellrydeloginHandler.Client.PostAsync("https://portal.app.wellryde.com/portal/trip/assignTripDriver", formContent);
            var response = await res.Content.ReadAsStringAsync();
        }







        public async Task PullReserves(string longdatestr, int dayint, int yearint, DateTime mcdate)
        {
            await AsyncUpdateLoadingScreen("Checking connections");
            List<MCDownloadedTrip> resservedtrips = new List<MCDownloadedTrip>();

            WRTripDownloader wrtd = new WRTripDownloader();
            wellrydeDownloadedTrips = new List<WRDownloadedTrip>();
            wellrydeDownloadedTrips = await wrtd.DownloadTripRecords(longdatestr, dayint, yearint, wellrydeloginHandler);

            MCTripDownloader mctd = new MCTripDownloader();
            modivcareDownloadedTrips = new List<MCDownloadedTrip>();
            modivcareDownloadedTrips = await mctd.DownloadTripRecords(mcdate, modivcareloginHandler);
            await AsyncUpdateLoadingScreen("Downloading trips");
            if (wellrydeDownloadedTrips.Any() & modivcareDownloadedTrips.Any())
            {
                foreach (MCDownloadedTrip mcdt in modivcareDownloadedTrips)
                {
                    foreach (WRDownloadedTrip wrdt in wellrydeDownloadedTrips)
                    {
                        if (wrdt.TripNumber.Replace(" ", "") == mcdt.TripNumber.Replace(" ", ""))
                        {
                            if (wrdt.Status == "Reserved")
                            {
                                resservedtrips.Add(mcdt);
                            }
                        }
                    }
                }

                await AsyncUpdateLoadingScreen("Saving to file");
                SaveFinalBatchToFile(resservedtrips);
                await AsyncUpdateLoadingScreen("Finalizing process..");
            }
        }
        public void SaveFinalBatchToFile(List<MCDownloadedTrip> trips)
        {

            Console.WriteLine("Creating Schedule main file...");

            //before your loop
            var csv = new StringBuilder();
            //scheduleTripsAndTemplates.CheckIfScheduleDirectoriesExist();
            //scheduleTripsAndTemplates.CheckIfScheduleDirectoryDateExists(scheduledate);
            //for each trip
            //trips.Reverse();
            foreach (MCDownloadedTrip trip in trips)
            {
                var newLine = string.Format("\"{0}\",\"{1}\",\"{2}\",\"{3}\",\"{4}\",\"{5}\",\"{6}\",\"{7}\",\"{8}\",\"{9}\",\"{10}\",\"{11}\",\"{12}\",\"{13}\"", trip.TripNumber, trip.Date, trip.ClientFullName, trip.PUStreet, trip.PUCity, trip.PUTelephone, trip.PUTime, trip.DOStreet, trip.DOCITY, trip.DOTelephone, trip.DOTime, trip.Age, trip.Miles, trip.Comments);
                csv.AppendLine(newLine);
            }


            Console.WriteLine(csv.ToString());
            try
            {
                string filePath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                File.WriteAllText(AppContext.BaseDirectory + "RemainingTrips" + ".csv", csv.ToString());

                System.Diagnostics.Process.Start(AppContext.BaseDirectory + "RemainingTrips" + ".csv");
                ///OPENING EXCEL FILE LIKE THIS CAUSES COPY + PASTE ISSUES!!
                //var excelApp = new Microsoft.Office.Interop.Excel.Application { Visible = true };
                //Microsoft.Office.Interop.Excel.Workbook ScheduleWorkbook2 = excelApp.Workbooks.Open(filePath + "\\Test Folder\\" + "\\" + "RemainingTrips" + ".csv");
            }
            catch
            {

            }
        }
        public async Task SplitFile(string targetPath, string sourceFile)
        {
            bool isSave;
            Microsoft.Office.Interop.Excel.XlFileFormat fileFormat = Microsoft.Office.Interop.Excel.XlFileFormat.xlOpenXMLWorkbook;

            string exportFormat = "CSV";

            Microsoft.Office.Interop.Excel.Application xlApp = null;
            Microsoft.Office.Interop.Excel.Workbook xlFile = null;
            try
            {
                xlApp = new Microsoft.Office.Interop.Excel.Application();
                try
                {
                    xlFile = xlApp.Workbooks.Open(sourceFile);
                }
                catch (Exception ex)
                {
                    throw new ScheduleLoadException(sourceFile, null, 0, null, "Opening file", ex);
                }

                xlApp.DisplayAlerts = false;
                int TabCount = xlFile.Worksheets.Count;
                int sheetCount = 0;

                for (int i = 1; i <= TabCount; i++)
                {
                    MCDriverTab drivertab = new MCDriverTab();
                    isSave = true;
                    string sheetName = null;
                    try
                    {
                        sheetName = xlFile.Sheets[i].Name;
                    }
                    catch (Exception ex)
                    {
                        throw new ScheduleLoadException(sourceFile, "Sheet index " + i, 0, null, "Reading tab name", ex);
                    }
                    string newFilename = System.IO.Path.Combine(targetPath, sheetName);

                    Microsoft.Office.Interop.Excel.Worksheet tempSheet = xlApp.Worksheets[i];
                    tempSheet.Copy();
                    Microsoft.Office.Interop.Excel.Range xlRange = tempSheet.UsedRange;

                    string[][] rows;
                    try
                    {
                        rows = GetStringArrayFromSheetRange(xlRange.Cells.Value);
                    }
                    catch (Exception ex)
                    {
                        throw new ScheduleLoadException(sourceFile, sheetName, 0, null, "Reading sheet data (UsedRange)", ex);
                    }
                    if (sheetName != "Reserves")
                    {
                        CheckForNotes(rows);
                    }

                    if (rows != null)
                    {
                        int rowIndex = 0;
                        foreach (string[] row in rows)
                        {
                            rowIndex++;
                            if (row.Length == 0) { continue; }
                            if (!CheckIfRowIsGood(row)) { continue; }
                            MCDownloadedTrip drivertrip = new MCDownloadedTrip();
                            drivertrip.DriverNameParsed = sheetName;
                            string tripNumberForError = (row.Length > 0 && row[0] != null) ? row[0].ToString() : "";
                            try
                            {
                                for (int a = 0; a < row.Length; a++)
                                {
                                    string colName = GetColumnNameForIndex(a);
                                    switch (a)
                                    {
                                        case 0:
                                            drivertrip.TripNumber = row[a];
                                            break;
                                        case 1:
                                            drivertrip.Date = SafeReturnDateFromMCTime(row[a], sourceFile, sheetName, rowIndex, tripNumberForError, "Date");
                                            break;
                                        case 2:
                                            drivertrip.ClientFullName = row[a];
                                            break;
                                        case 3:
                                            drivertrip.PUStreet = row[a];
                                            break;
                                        case 4:
                                            drivertrip.PUCity = row[a];
                                            break;
                                        case 5:
                                            drivertrip.PUTelephone = row[a];
                                            break;
                                        case 6:
                                            drivertrip.PUTime = SafeParseTime(row[a], sourceFile, sheetName, rowIndex, tripNumberForError, "PU Time");
                                            break;
                                        case 7:
                                            drivertrip.DOStreet = row[a];
                                            break;
                                        case 8:
                                            drivertrip.DOCITY = row[a];
                                            break;
                                        case 9:
                                            drivertrip.DOTelephone = row[a];
                                            break;
                                        case 10:
                                            drivertrip.DOTime = SafeParseTime(row[a], sourceFile, sheetName, rowIndex, tripNumberForError, "DO Time");
                                            break;
                                        case 11:
                                            drivertrip.Age = row[a];
                                            break;
                                        case 12:
                                            drivertrip.Miles = row[a];
                                            break;
                                        case 13:
                                            drivertrip.Comments = row[a];
                                            break;
                                    }
                                }
                            }
                            catch (ScheduleLoadException)
                            {
                                throw;
                            }
                            catch (Exception ex)
                            {
                                throw new ScheduleLoadException(sourceFile, sheetName, rowIndex, tripNumberForError, "Parsing row", ex);
                            }
                            drivertab.scheduledTrips.Add(drivertrip);
                        }
                    }

                    drivertab.driverName = sheetName;
                    drivertablist.Add(drivertab);

                    Microsoft.Office.Interop.Excel.Workbook tempBook = xlApp.ActiveWorkbook;

                    try
                    {
                        switch (exportFormat)
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
                        string errorMessage = "Error Exporting " + sheetName + System.Environment.NewLine + "Original Message: " + ex.Message;
                        MessageBox.Show(errorMessage, "Error Exporting", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                    }
                }
            }
            finally
            {
                if (xlFile != null)
                {
                    try { xlFile.Close(false); } catch { }
                }
                if (xlApp != null)
                {
                    try { xlApp.Quit(); } catch { }
                    Marshal.ReleaseComObject(xlApp);
                }
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }

        private static string GetColumnNameForIndex(int index)
        {
            string[] names = { "TripNumber", "Date", "ClientFullName", "PUStreet", "PUCity", "PUTelephone", "PU Time", "DOStreet", "DOCITY", "DOTelephone", "DO Time", "Age", "Miles", "Comments" };
            return index >= 0 && index < names.Length ? names[index] : "Column " + (index + 1);
        }

        private string SafeReturnDateFromMCTime(string datestr, string filePath, string tabName, int rowIndex, string tripNumber, string columnName)
        {
            try
            {
                return ReturnDateFromMCTime(datestr);
            }
            catch (Exception ex)
            {
                throw new ScheduleLoadException(filePath, tabName, rowIndex, tripNumber, columnName, ex);
            }
        }

        private static string SafeParseTime(string cellValue, string filePath, string tabName, int rowIndex, string tripNumber, string columnName)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(cellValue)) return "";
                double oa = double.Parse(cellValue, CultureInfo.InvariantCulture);
                DateTime dt = DateTime.FromOADate(oa);
                return dt.ToString("HH:mm");
            }
            catch (Exception ex)
            {
                throw new ScheduleLoadException(filePath, tabName, rowIndex, tripNumber, columnName, ex);
            }
        }
        private bool CheckIfRowIsGood(string[] myrow)
        {
            for (int a = 0; a < myrow.Length; a++)
            {
                switch (a)
                {
                    case 0:
                        if (myrow[a] == null)
                        {
                            return false;
                        }
                        if (!myrow[a].Contains("-"))
                        {
                            return false;
                        }
                        break;
                    case 1:
                        if (myrow[a] == null)
                        {
                            return false;
                        }
                        if (!myrow[a].Contains("/"))
                        {
                            return false;
                        }
                        break;
                }
            }
                return true;
        }
        private int notecount;
        private void CheckForNotes(string[][] allrows)
        {
            if (allrows != null)
            {
                foreach (string[] row in allrows)
                {
                    if (CheckIfRowHasNotes(row)) 
                    {
                        notecount++;
                    } 
                }
            }
        }
        private void GradeNotes()
        {
            SubjectGrade subjectGrade = new SubjectGrade();
            subjectGrade.Subject = "Note Distribution";
            if (notecount >= 5)
            {
                subjectGrade.Grade = "A";
                subjectGrade.Notes = "- Well done!";
            }
            else
            {
                subjectGrade.Grade = "F";
                subjectGrade.Notes = "- Must have at least 5 notes!";
            }
            gradeList.Add(subjectGrade);
            Console.WriteLine("NOTES: " + notecount);
        }
        private bool CheckIfRowHasNotes(string[] myrow)
        {
            for (int a = 0; a < myrow.Length; a++)
            {
                switch (a)
                {
                    case 0:
                        if (myrow[a] == null)
                        {
                            if (myrow[2] != null)
                            {
                                return true;
                            }
                            if (myrow[3] != null)
                            {
                                return true;
                            }
                            if (myrow[4] != null)
                            {
                                return true;
                            }
                            if (myrow[5] != null)
                            {
                                return true;
                            }
                            if (myrow[6] != null)
                            {
                                return true;
                            }
                            if (myrow[7] != null)
                            {
                                return true;
                            }
                        }
                        break;
                    default:
                        break;
                }
            }
            return false;
        }
        private string ReturnDateFromMCTime(string datestr)
        {
            string[] dateparts = datestr.Split(new char[] { ' ' });
            if (dateparts[0] != null)
            {
                return dateparts[0].Replace(" ","");
            }
            else
            {
                return string.Empty;
            }
        }
        private string ReturnDateFromMCDateAndTime(string datestr)
        {
            string[] dateparts = datestr.Split(new char[] { '-' });
            if (dateparts[0] != null)
            {
                return dateparts[0].Replace(" ", "");
            }
            else
            {
                return string.Empty;
            }
        }
        static string[][] GetStringArrayFromSheetRange(Object rangeValues)
        {
            string[][] stringArray = null;

            Array array = rangeValues as Array;
            if (null != array)
            {
                int rank = array.Rank;
                if (rank > 1)
                {
                    int rowCount = array.GetLength(0);
                    int columnCount = array.GetUpperBound(1);

                    stringArray = new string[rowCount][];

                    for (int index = 0; index < rowCount; index++)
                    {
                        stringArray[index] = new string[columnCount];

                        for (int index2 = 0; index2 < columnCount; index2++)
                        {
                            Object obj = array.GetValue(index + 1, index2 + 1);
                            if (null != obj)
                            {
                                string value = obj.ToString();

                                stringArray[index][index2] = value;
                            }
                        }
                    }
                }
            }

            return stringArray;
        }
        public string SplitDriverName(string driverrname)
        {
            string[] namesubitems = driverrname.Split(new string[] { " " }, StringSplitOptions.None);
            string first = namesubitems[0].ToLower();
            string last = namesubitems[namesubitems.Length - 1].ToLower();

            return char.ToUpper(first[0]) + first.Substring(1) + " " + char.ToUpper(last[0]);
        }
















    }
}
