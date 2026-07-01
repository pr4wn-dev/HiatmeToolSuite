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
        public List<WRDrivers> WRDriverList { get; set; }
        private string scheduledate { get; set; }
        private List<MCDownloadedTrip> modivcareDownloadedTrips { get; set; }
        public List<MCDriverTab> drivertablist { get; set; }
        public List<MCDownloadedTrip> loggedScheduleTrips { get; set; }
        private List<WRDownloadedTrip> wellrydeDownloadedTrips { get; set; }
        public int LastModivcareDownloadCount { get; private set; }
        public int LastModivcareRequestedIdCount { get; private set; }
        public int LastWellRydeDownloadCount { get; private set; }
        /// <summary>Optional portal session for schedule analysis (hidden trips, escorts, assign UUIDs, reserves).</summary>
        private WellRydePortalSession _wellRydePortalSession;

        /// <summary>
        /// When false, <see cref="UnassignTripBatch"/> and <see cref="SubmitTripBatch"/> do not POST to WellRyde; the UI only refreshes the trip list.
        /// </summary>
        public static bool WellRydePortalAssignAndUnassignCallsServer { get; set; } = true;

        /// <summary>Set before <see cref="StartAnalysis"/> / <see cref="PullReserves"/> / <see cref="StartTripAssigning"/> to load WellRyde trips and drivers; pass null for MC-only.</summary>
        public void SetWellRydePortalSession(WellRydePortalSession session)
        {
            _wellRydePortalSession = session;
        }

        public void IntializeAnalyzer(MCLoginHandler mCloginHandler)
        {
            modivcareloginHandler = mCloginHandler;
        }

        private async Task TryLoadWellRydeTripsAndDriversAsync(DateTime tripDate)
        {
            if (_wellRydePortalSession == null)
                return;

            wellrydeDownloadedTrips = new List<WRDownloadedTrip>();
            WRDriverList = new List<WRDrivers>();

            try
            {
                await AsyncUpdateLoadingScreen("Downloading WellRyde trips");
                var nu = await _wellRydePortalSession.GetPortalNuAsync();
                if (!nu.IsSuccess)
                    return;

                var fd = await _wellRydePortalSession.PostTripFilterDataAsync(tripDate,
                    maxResults: WellRydePortalSession.DefaultTripFilterMaxResult).ConfigureAwait(false);
                if (!fd.IsSuccess)
                    return;

                wellrydeDownloadedTrips = WellRydeFilterDataParser.ParseTrips(fd.JsonBody, out _);
                WRDriverList = await _wellRydePortalSession.GetAllDriversForTripAssignmentAsync().ConfigureAwait(false)
                    ?? new List<WRDrivers>();
            }
            catch
            {
                wellrydeDownloadedTrips = new List<WRDownloadedTrip>();
                WRDriverList = new List<WRDrivers>();
            }
        }

        public async Task StartAnalysis(string longdatestr, int dayint, int yearint, DateTime mcdate)
        {
            _ = ScheduleDateHelper.ResolveTripDate(longdatestr, dayint, yearint);
            notecount = 0;
            drivertablist = new List<MCDriverTab>();
            wellrydeDownloadedTrips = new List<WRDownloadedTrip>();
            WRDriverList = new List<WRDrivers>();

            MCTripDownloader mctd = new MCTripDownloader();
            modivcareDownloadedTrips = new List<MCDownloadedTrip>();
            modivcareDownloadedTrips = await mctd.DownloadTripRecords(mcdate, modivcareloginHandler);
            LastModivcareDownloadCount = modivcareDownloadedTrips?.Count ?? 0;
            LastModivcareRequestedIdCount = mctd.LastRequestedTripIdCount;

            await TryLoadWellRydeTripsAndDriversAsync(mcdate);
            LastWellRydeDownloadCount = wellrydeDownloadedTrips?.Count ?? 0;

            loggedScheduleTrips = new List<MCDownloadedTrip>();

            gradeList = new List<SubjectGrade>();

        }
        private async Task AsyncUpdateLoadingScreen(string txt)
        {
            UpdateLoadingScreen(txt);
            await Task.Yield();
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

        /// <summary>
        /// Populate <see cref="MCDownloadedTrip.Alerts"/> on schedule-builder preview trips using the
        /// same checks as <see cref="AnalyzeTrips"/> (no grading UI).
        /// </summary>
        public async Task ApplyAlertsToScheduleBuilderAsync(FullScheduleBuilder builder, DateTime serviceDate)
        {
            if (builder == null)
                throw new ArgumentNullException(nameof(builder));

            modivcareDownloadedTrips = builder.MCTripList ?? new List<MCDownloadedTrip>();
            LastModivcareDownloadCount = modivcareDownloadedTrips.Count;

            await TryLoadWellRydeTripsAndDriversAsync(serviceDate).ConfigureAwait(false);
            LastWellRydeDownloadCount = wellrydeDownloadedTrips?.Count ?? 0;

            loggedScheduleTrips = new List<MCDownloadedTrip>();
            drivertablist = BuildDriverTabListFromScheduleBuilder(builder);
            ClearAlertsOnScheduledTrips(drivertablist);

            gradeList = new List<SubjectGrade>();
            AnalyzeTrips(serviceDate);
        }

        private static List<MCDriverTab> BuildDriverTabListFromScheduleBuilder(FullScheduleBuilder builder)
        {
            var tabs = new List<MCDriverTab>();
            IReadOnlyDictionary<string, List<ScheduleBuilderPreviewLine>> preview = builder.PreviewDriverLines;
            if (preview == null)
                return tabs;

            foreach (KeyValuePair<string, List<ScheduleBuilderPreviewLine>> kv in preview)
            {
                if (string.IsNullOrWhiteSpace(kv.Key))
                    continue;

                var tab = new MCDriverTab { driverName = kv.Key };
                IList<ScheduleBuilderPreviewLine> lines = kv.Value;
                if (lines != null)
                {
                    for (int i = 0; i < lines.Count; i++)
                    {
                        ScheduleBuilderPreviewLine line = lines[i];
                        if (line?.Kind != ScheduleBuilderPreviewLine.LineKind.Trip || line.Trip == null)
                            continue;

                        line.Trip.DriverNameParsed = kv.Key;
                        tab.scheduledTrips.Add(line.Trip);
                    }
                }

                if (tab.scheduledTrips.Count > 0)
                    tabs.Add(tab);
            }

            return tabs;
        }

        private static void ClearAlertsOnScheduledTrips(IEnumerable<MCDriverTab> tabs)
        {
            if (tabs == null)
                return;

            foreach (MCDriverTab tab in tabs)
            {
                if (tab?.scheduledTrips == null)
                    continue;

                foreach (MCDownloadedTrip trip in tab.scheduledTrips)
                {
                    if (trip == null)
                        continue;
                    trip.Alerts = new List<string>();
                }
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

        /// <summary>Matches legacy behavior: WellRyde portal ids were shortened with <see cref="WellRydeFilterDataParser.FormatTripIdForScheduleMatch"/>; Modivcare may use either form.</summary>
        private static bool WellRydeTripNumberMatchesMc(string wrTripNumber, string mcTripNumber)
        {
            string a = WellRydeFilterDataParser.FormatTripIdForScheduleMatch((wrTripNumber ?? "").Replace(" ", ""));
            string b = WellRydeFilterDataParser.FormatTripIdForScheduleMatch((mcTripNumber ?? "").Replace(" ", ""));
            return string.Equals(a, b, StringComparison.Ordinal);
        }

        private static bool ScheduleTripNumberMatches(string tripA, string tripB) =>
            WellRydeTripNumberMatchesMc(tripA, tripB);

        private WRDownloadedTrip FindWellRydeRowForScheduleTrip(MCDownloadedTrip scheduleTrip)
        {
            if (wellrydeDownloadedTrips == null)
                return null;
            foreach (WRDownloadedTrip wrdt in wellrydeDownloadedTrips)
            {
                if (WellRydeTripNumberMatchesMc(wrdt.TripNumber, scheduleTrip.TripNumber))
                    return wrdt;
            }
            return null;
        }

        private static bool IsWellRydeCancelledStatus(string status)
        {
            if (string.IsNullOrWhiteSpace(status))
                return false;
            return status.Equals("Cancelled", StringComparison.OrdinalIgnoreCase)
                || status.Equals("Suspended", StringComparison.OrdinalIgnoreCase);
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
                        if (WellRydeTripNumberMatchesMc(wrdt.TripNumber, mcdt.TripNumber))
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
            if (!drivertablist.Any())
                return;
            foreach (MCDriverTab dt in drivertablist)
            {
                foreach (MCDownloadedTrip mcst in dt.scheduledTrips)
                {
                    try
                    {
                        // Legacy compared raw strings; Excel/CSV dates often differ from picker ToString() (e.g. 04/29 vs 4/29).
                        if (!ScheduleTripDateMatchesPicker(mcst.Date, modivecaredate))
                            CheckIfTripIsAlreadyLogged(mcst, "Date");
                    }
                    catch (ScheduleAnalysisException) { throw; }
                    catch (Exception ex) { throw new ScheduleAnalysisException("FindWrongDatesInSchedule", dt.driverName, mcst?.TripNumber, ex); }
                }
            }
        }

        /// <summary>True if the schedule row date is the same calendar day as the analysis date picker.</summary>
        private bool ScheduleTripDateMatchesPicker(string mcScheduleDateField, DateTime pickerDateTime)
        {
            if (string.IsNullOrWhiteSpace(mcScheduleDateField))
                return false;
            string s = mcScheduleDateField.Trim();
            DateTime parsed;
            if (DateTime.TryParse(s, CultureInfo.CurrentCulture, DateTimeStyles.None, out parsed))
                return parsed.Date == pickerDateTime.Date;
            if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
                return parsed.Date == pickerDateTime.Date;
            if (double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out double oa))
            {
                try
                {
                    return DateTime.FromOADate(oa).Date == pickerDateTime.Date;
                }
                catch
                {
                    // ignore
                }
            }

            string expectedFromPicker = ReturnDateFromMCTime(pickerDateTime.ToString());
            return string.Equals(s, expectedFromPicker, StringComparison.OrdinalIgnoreCase)
                || string.Equals(ReturnDateFromMCTime(s), expectedFromPicker, StringComparison.OrdinalIgnoreCase);
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
        private static bool ScheduleTimesMatch(string mcDownloadTime, string scheduleTime)
        {
            string a = NormalizeScheduleCompareTime(mcDownloadTime);
            string b = NormalizeScheduleCompareTime(scheduleTime);
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeScheduleCompareTime(string raw)
        {
            string n = TripTemplateCsvValidator.NormalizeTimeField(raw ?? "");
            if (string.IsNullOrWhiteSpace(n))
                return "";

            var span = SupeyTripTimes.TryParse(n);
            if (span.HasValue)
                return DateTime.Today.Add(span.Value).ToString("HH:mm");

            if (DateTime.TryParse(n, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsedInvariant))
                return parsedInvariant.ToString("HH:mm");
            if (DateTime.TryParse(n, CultureInfo.CurrentCulture, DateTimeStyles.None, out DateTime parsedLocal))
                return parsedLocal.ToString("HH:mm");

            return n.Trim();
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
                            if (ScheduleTripNumberMatches(mcdt.TripNumber, mcst.TripNumber))
                            {
                                if (!ScheduleTimesMatch(mcdt.PUTime, mcst.PUTime))
                                {
                                    CheckIfTripIsAlreadyLogged(mcst, "Time");
                                    mcst.Assignable = false;
                                    break;
                                }

                                if (!ScheduleTimesMatch(mcdt.DOTime, mcst.DOTime))
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
                            if (ScheduleTripNumberMatches(mcdt.TripNumber, mcst.TripNumber))
                            {
                                // Older Schedule Builder versions pad address cells with trailing
                                // spaces; normalize whitespace/case so only real address changes flag.
                                if (!AddressesMatch(mcdt.PUStreet, mcst.PUStreet))
                                {
                                    CheckIfTripIsAlreadyLogged(mcst, "Address");
                                    mcst.Assignable = false;
                                    break;
                                }

                                if (!AddressesMatch(mcdt.DOStreet, mcst.DOStreet))
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

        private static bool AddressesMatch(string a, string b) =>
            string.Equals(NormalizeAddressForCompare(a), NormalizeAddressForCompare(b), StringComparison.OrdinalIgnoreCase);

        private static string NormalizeAddressForCompare(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";
            return System.Text.RegularExpressions.Regex.Replace(value.Trim(), @"\s+", " ");
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
                                if (WellRydeTripNumberMatchesMc(wrdt.TripNumber, mcst.TripNumber))
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
        /// <summary>
        /// Schedule trips missing from the Modivcare download are cleanup candidates. When WellRyde still
        /// shows the trip as active, skip the alert — MC often omits future-day rows while WR does not.
        /// </summary>
        private void CheckForCancels()
        {
            if (!drivertablist.Any())
                return;

            bool mcHasRows = modivcareDownloadedTrips != null && modivcareDownloadedTrips.Any();
            bool wrHasRows = wellrydeDownloadedTrips != null && wellrydeDownloadedTrips.Any();
            if (!mcHasRows && !wrHasRows)
                return;

            foreach (MCDriverTab dt in drivertablist)
            {
                foreach (MCDownloadedTrip mcst in dt.scheduledTrips)
                {
                    if (mcHasRows)
                    {
                        bool tripfound = false;
                        foreach (MCDownloadedTrip mcdt in modivcareDownloadedTrips)
                        {
                            if (ScheduleTripNumberMatches(mcdt.TripNumber, mcst.TripNumber))
                            {
                                tripfound = true;
                                break;
                            }
                        }
                        if (tripfound)
                            continue;
                    }

                    WRDownloadedTrip wrRow = FindWellRydeRowForScheduleTrip(mcst);
                    if (wrRow != null)
                    {
                        if (!IsWellRydeCancelledStatus(wrRow.Status))
                            continue;
                    }
                    else if (!mcHasRows)
                    {
                        // MC download failed/empty and WR has no row — cannot confirm a cancel.
                        continue;
                    }

                    CheckIfTripIsAlreadyLogged(mcst, "Cancelled");
                    mcst.Assignable = false;
                    pregrade = false;
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
            await TryLoadWellRydeTripsAndDriversAsync(mcdate);
            if (!WellRydePortalAssignAndUnassignCallsServer)
                await AsyncUpdateLoadingScreen("WellRyde: assign/unassign from this app is not connected to the portal yet");
            else
                await AsyncUpdateLoadingScreen("Unassigning trips on WellRyde");
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

        /// <summary>
        /// Same matching rules as <see cref="AssignTrips"/>: assignable MC trips per driver tab with a non-cancelled WellRyde row (trip number match).
        /// Total UUID slots that would be sent in assign batches (same WR trip may appear more than once if the schedule repeats a trip number).
        /// </summary>
        public int GetPlannedWellRydeAssignSlotCount()
        {
            if (drivertablist == null || !drivertablist.Any())
                return 0;
            if (WRDriverList == null || !WRDriverList.Any() || wellrydeDownloadedTrips == null || !wellrydeDownloadedTrips.Any())
                return 0;

            int total = 0;
            foreach (MCDriverTab dt in drivertablist)
            {
                foreach (WRDrivers driver in WRDriverList)
                {
                    if (dt.driverName != SplitDriverName(driver.text))
                        continue;
                    total += BuildTripUuidListForAssignToDriver(dt).Count;
                }
            }

            return total;
        }

        private List<string> BuildTripUuidListForAssignToDriver(MCDriverTab dt)
        {
            var driverstrips = new List<string>();
            if (wellrydeDownloadedTrips == null)
                return driverstrips;
            foreach (MCDownloadedTrip mcst in dt.scheduledTrips)
            {
                if (!mcst.Assignable)
                    continue;
                foreach (WRDownloadedTrip wrdt in wellrydeDownloadedTrips)
                {
                    if (WellRydeTripNumberMatchesMc(wrdt.TripNumber, mcst.TripNumber) && wrdt.Status != "Cancelled")
                    {
                        driverstrips.Add(wrdt.TripUUID);
                        break;
                    }
                }
            }

            return driverstrips;
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
        private async Task UnassignTripBatch(List<string> trips, int attempt = 0)
        {
            if (trips == null || trips.Count == 0)
                return;
            if (!WellRydePortalAssignAndUnassignCallsServer)
            {
                Console.WriteLine("UnassignTripBatch: skipped (WellRydePortalAssignAndUnassignCallsServer is false).");
                return;
            }
            if (_wellRydePortalSession == null)
            {
                Console.WriteLine("UnassignTripBatch: no WellRyde portal session.");
                return;
            }

            var result = await _wellRydePortalSession.PostUnassignTripsAsync(trips).ConfigureAwait(false);
            if (!result.IsSuccess)
                Console.WriteLine("UnassignTripBatch failed: " + result.ErrorMessage +
                    (string.IsNullOrEmpty(result.ResponseBody) ? "" : " | " + result.ResponseBody));
        }

        private Task FinalizeUnassignTripBatch(List<string> tripscopy, int attempt = 0)
        {
            return Task.CompletedTask;
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
                            List<string> driverstrips = BuildTripUuidListForAssignToDriver(dt);
                            foreach (MCDownloadedTrip mcst in dt.scheduledTrips)
                            {
                                if (mcst.Assignable)
                                    Console.WriteLine(mcst.TripNumber + " [alerts: " + mcst.GetAlerts() + "]");
                            }

                            if (WellRydePortalAssignAndUnassignCallsServer)
                                await AsyncUpdateLoadingScreen("Assigning " + driverstrips.Count + " trips to " + SplitDriverName(driver.text));
                            else
                                await AsyncUpdateLoadingScreen("Would assign " + driverstrips.Count + " trips to " + SplitDriverName(driver.text) + " (not sent to WellRyde)");
                            await SubmitTripBatch(driver.value, driverstrips);
                           
                        }
                    }
                }

                await AsyncUpdateLoadingScreen("Refreshing WellRyde trip list");
                await TryLoadWellRydeTripsAndDriversAsync(mcdatedt);
            }
        }
        private async Task SubmitTripBatch(string id, List<string> trips, int attempt = 0)
        {
            if (trips == null || trips.Count == 0)
                return;
            if (!WellRydePortalAssignAndUnassignCallsServer)
            {
                Console.WriteLine("SubmitTripBatch: skipped (flag false) for driver id: " + id);
                return;
            }
            if (_wellRydePortalSession == null)
            {
                Console.WriteLine("SubmitTripBatch: no WellRyde portal session for driver id: " + id);
                return;
            }

            var result = await _wellRydePortalSession.PostAssignTripsToDriverAsync(id, trips).ConfigureAwait(false);
            if (!result.IsSuccess)
                Console.WriteLine("SubmitTripBatch failed for " + id + ": " + result.ErrorMessage +
                    (string.IsNullOrEmpty(result.ResponseBody) ? "" : " | " + result.ResponseBody));
        }







        public async Task PullReserves(string longdatestr, int dayint, int yearint, DateTime mcdate)
        {
            await AsyncUpdateLoadingScreen("Checking connections");
            List<MCDownloadedTrip> resservedtrips = new List<MCDownloadedTrip>();

            wellrydeDownloadedTrips = new List<WRDownloadedTrip>();

            MCTripDownloader mctd = new MCTripDownloader();
            modivcareDownloadedTrips = new List<MCDownloadedTrip>();
            modivcareDownloadedTrips = await mctd.DownloadTripRecords(mcdate, modivcareloginHandler);
            await AsyncUpdateLoadingScreen("Downloading trips");
            await TryLoadWellRydeTripsAndDriversAsync(mcdate);
            if (wellrydeDownloadedTrips.Any() & modivcareDownloadedTrips.Any())
            {
                foreach (MCDownloadedTrip mcdt in modivcareDownloadedTrips)
                {
                    foreach (WRDownloadedTrip wrdt in wellrydeDownloadedTrips)
                    {
                        if (WellRydeTripNumberMatchesMc(wrdt.TripNumber, mcdt.TripNumber))
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
            string ext = Path.GetExtension(sourceFile) ?? "";
            if (ext.Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
            {
                await SplitFileWithNativeXlsxReader(targetPath, sourceFile);
                return;
            }

            bool isSave;
            Microsoft.Office.Interop.Excel.XlFileFormat fileFormat = Microsoft.Office.Interop.Excel.XlFileFormat.xlOpenXMLWorkbook;

            string exportFormat = "CSV";

            Microsoft.Office.Interop.Excel.Application xlApp = null;
            Microsoft.Office.Interop.Excel.Workbook xlFile = null;
            try
            {
                try
                {
                    xlApp = new Microsoft.Office.Interop.Excel.Application();
                }
                catch (COMException ex) when ((uint)ex.ErrorCode == 0x80040154u)
                {
                    throw new ScheduleLoadException(
                        sourceFile,
                        null,
                        0,
                        null,
                        "Excel COM startup",
                        new InvalidOperationException(
                            "Microsoft Excel is not installed/registered on this machine (Class not registered: 0x80040154). " +
                            "Analyzer schedule splitting currently requires desktop Excel. " +
                            "Install or repair Microsoft Office/Excel, then try again.",
                            ex));
                }
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
                        AppendTripsFromRows(drivertab, sheetName, rows, sourceFile);
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
            catch (ScheduleLoadException)
            {
                throw;
            }
            catch (COMException ex) when ((uint)ex.ErrorCode == 0x80040154u)
            {
                throw new ScheduleLoadException(
                    sourceFile,
                    null,
                    0,
                    null,
                    "Excel COM",
                    new InvalidOperationException(
                        "Microsoft Excel is not installed/registered on this machine (Class not registered: 0x80040154). " +
                        "Analyzer schedule splitting currently requires desktop Excel. " +
                        "Install or repair Microsoft Office/Excel, then try again.",
                        ex));
            }
            catch (Exception ex)
            {
                throw new ScheduleLoadException(sourceFile, null, 0, null, "SplitFile", ex);
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

        private async Task SplitFileWithNativeXlsxReader(string targetPath, string sourceFile)
        {
            await Task.Yield();
            var sheetRows = ScheduleBuilderXlsxReader.ReadWorkbookSheets(sourceFile);
            foreach (var sheet in sheetRows)
            {
                string sheetName = sheet.Tab ?? "";
                if (string.IsNullOrWhiteSpace(sheetName))
                    continue;

                var drivertab = new MCDriverTab();
                string[][] rows = NormalizeRows(sheet.Rows);
                if (sheetName != "Reserves")
                    CheckForNotes(rows);

                if (rows != null)
                    AppendTripsFromRows(drivertab, sheetName, rows, sourceFile);

                drivertab.driverName = sheetName;
                drivertablist.Add(drivertab);

                try
                {
                    WriteRowsToCsv(targetPath, sheetName, rows);
                }
                catch (Exception ex)
                {
                    string errorMessage = "Error Exporting " + sheetName + System.Environment.NewLine + "Original Message: " + ex.Message;
                    MessageBox.Show(errorMessage, "Error Exporting", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                }
            }
        }

        private static string[][] NormalizeRows(List<List<string>> rows)
        {
            if (rows == null || rows.Count == 0)
                return new string[0][];

            var normalized = new string[rows.Count][];
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i] ?? new List<string>();
                normalized[i] = row.Select(v => string.IsNullOrWhiteSpace(v) ? null : v).ToArray();
            }
            return normalized;
        }

        private static string CsvQuote(string value)
        {
            return "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";
        }

        private static void WriteRowsToCsv(string targetPath, string sheetName, string[][] rows)
        {
            string newFilename = System.IO.Path.Combine(targetPath, sheetName) + ".csv";
            var sb = new StringBuilder();
            if (rows != null)
            {
                foreach (var row in rows)
                {
                    if (row == null || row.Length == 0)
                    {
                        sb.AppendLine();
                        continue;
                    }
                    sb.AppendLine(string.Join(",", row.Select(CsvQuote)));
                }
            }
            File.WriteAllText(newFilename, sb.ToString(), Encoding.UTF8);
        }

        private void AppendTripsFromRows(MCDriverTab drivertab, string sheetName, string[][] rows, string sourceFile)
        {
            int rowIndex = 0;
            foreach (string[] row in rows)
            {
                rowIndex++;
                if (row == null || row.Length == 0) { continue; }
                if (!CheckIfRowIsGood(row)) { continue; }
                MCDownloadedTrip drivertrip = new MCDownloadedTrip();
                drivertrip.DriverNameParsed = sheetName;
                string tripNumberForError = (row.Length > 0 && row[0] != null) ? row[0].ToString() : "";
                try
                {
                    for (int a = 0; a < row.Length; a++)
                    {
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
                string raw = cellValue.Trim();
                if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double oa))
                {
                    DateTime dt = DateTime.FromOADate(oa);
                    return dt.ToString("HH:mm");
                }

                if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsedInvariant))
                    return parsedInvariant.ToString("HH:mm");
                if (DateTime.TryParse(raw, CultureInfo.CurrentCulture, DateTimeStyles.None, out DateTime parsedLocal))
                    return parsedLocal.ToString("HH:mm");

                var span = SupeyTripTimes.TryParse(raw);
                if (span.HasValue)
                    return DateTime.Today.Add(span.Value).ToString("HH:mm");

                throw new FormatException("Unrecognized time format: \"" + raw + "\"");
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
            if (myrow == null || myrow.Length == 0)
                return false;

            if (TripTemplateCsvValidator.IsLikelyHeaderRow(myrow))
                return false;

            if (CheckIfRowIsGood(myrow))
                return false;

            // Column A labels (Schedule Builder export) and legacy rows with text in C–N.
            if (TripTemplateCsvValidator.IsDispatcherNoteRow(myrow))
                return true;

            if (TripTemplateCsvValidator.IsTemplateInstructionRow(myrow))
                return true;

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
