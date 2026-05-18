using MaterialSkin.Controls;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using System.Web.UI.WebControls;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    internal class MCTimeCorrection
    {
        private MaterialComboBox MaterialComboBox;

        public MCBatchRecords mcBatchRecords;

        private MCTripDownloader mctripdler;

        public HttpContent testformContent { get; private set; }
        public HttpContent dummyformContent { get; private set; }

        public MCCalculateAccuracies MCCalc { get; private set; }

        public TimeCorrectionLoadMode LoadMode { get; set; } = TimeCorrectionLoadMode.StandardScoreboard;

        /// <summary>When set (e.g. by Form1 during LOAD), receives human-readable progress for the loading overlay.</summary>
        public Func<string, Task> ReportProgressAsync { get; set; }

        /// <summary>TripActuals HTML from the last per-trip open before submit (late-reason hidden fields).</summary>
        private string _tripActualsFormHtml;

        public MCTimeCorrection(MaterialComboBox cb)
        {
            mcBatchRecords = new MCBatchRecords();
            mctripdler = new MCTripDownloader();
            MaterialComboBox = new MaterialComboBox();
            MaterialComboBox = cb;
        }

        //Finding Batches
        public async Task GetBatchLinks(MCLoginHandler mCLogin, bool IDs)
        {
            // GetWithAuthRetryAsync handles dead-cookie case: reconnect + replay the GET (tokenless, safe to retry).
            HttpResponseMessage resmsg = await mCLogin.GetWithAuthRetryAsync("https://transportationco.logisticare.com/ProcessATMBatches.aspx");
            var response = await resmsg.Content.ReadAsStringAsync();

            try
            {
                if (MCLoginHandler.IsAuthRedirect(resmsg))
                {
                    // Helper already attempted reconnect; if we still landed on login.aspx (e.g. bad cached creds),
                    // give up gracefully so caller shows an empty result rather than parsing the login page as data.
                    return;
                }
                mCLogin.GrabTokens(response);
                if (IDs == true)
                {
                    GrabBatchIDs(response);
                }
            }
            catch
            {
                Console.WriteLine("There was a problem retrieving location.");
            }
        }
        public async Task GetBatchPage(MCLoginHandler mcloginhandler, string batchtoken, bool returnbatch)
        {
            mcloginhandler.UpdateTripActualsHeaders("https://transportationco.logisticare.com/ProcessATMBatches.aspx");
           
            var formContent = new MyFormUrlEncodedContent(new[]{
                new KeyValuePair<string, string>("__EVENTTARGET", batchtoken),
                new KeyValuePair<string, string>("__EVENTARGUMENT", ""),
                new KeyValuePair<string, string>("__VIEWSTATE", mcloginhandler.ViewStateToken),
                new KeyValuePair<string, string>("__VIEWSTATEGENERATOR", mcloginhandler.ViewStateGeneratorToken),
                new KeyValuePair<string, string>("__SCROLLPOSITIONX", "0"),
                new KeyValuePair<string, string>("__SCROLLPOSITIONY", "0"),
                new KeyValuePair<string, string>("__VIEWSTATEENCRYPTED", ""),
                new KeyValuePair<string, string>("__EVENTVALIDATION", mcloginhandler.EventValidationToken),
             });

            HttpResponseMessage res = await mcloginhandler.Client.PostAsync("https://transportationco.logisticare.com/ProcessATMBatches.aspx", formContent);
            var response = await res.Content.ReadAsStringAsync();

            // Mid-flow session expiry: bail out hard so the outer Form1 handler can reconnect + restart the op.
            // We can't retry inline because ViewState/EventValidation tokens captured above belong to the dead session.
            if (MCLoginHandler.IsAuthRedirect(res))
                throw new ModivcareSessionExpiredException();

            mcloginhandler.GrabTokens(response);

            if (returnbatch){
                mcBatchRecords.MCBatchTrips = new List<MCBatchTripRecord>();
                await LoadBatch(response, mcloginhandler);
            }
            else{
                //await LoadBatch(response, mcloginhandler);
                await RefreshBatch(response, mcloginhandler);
                //await GetDriverPageForSubit(mcloginhandler, triprcd);
            }
        }

        private async Task LoadBatch(string resp, MCLoginHandler loginhandler)
        {
            try{
               
                string response = resp;

                //NOT grabbing tokens correctly FIX!
                //loginhandler.GrabTokens(response);
                //loginhandler.GrabViewStateToken(response);
                //loginhandler.GrabTokensSeptViewState(response);
                mcBatchRecords.MCBatchTrips.Clear();
                mcBatchRecords.MCBatchAdditionalInfo.Clear();
                //get bulk content surrounding trips
                string bulkcontentbegintag = "ctl00_cphMainContent_gvOpenBatchTrips";
                string bulkcontentendtag = "ctl00_cphMainContent_msgDONextDay";
                string bulkcontent = GetContentBulkRegex(response, bulkcontentbegintag, bulkcontentendtag);
                
                //clean up the bulk content
                string bulkcontenttrimmedbegintag = "Attention</th>";
                string bulkcontenttrimmedendtag = "</table>";
                string bulkcontenttrimmed = GetContentBulkRegex(bulkcontent, bulkcontenttrimmedbegintag, bulkcontenttrimmedendtag);
               
                //start seperating trips into a list
                List<string> tmpbatchtriplist = new List<string>();
                foreach (Match match in GetStrBetweenTags(bulkcontenttrimmed, "<td>", "</td>")){
                        tmpbatchtriplist.Add(match.Value);
                }

                //finish cleaning and seperating trips and add to triprecords
                foreach (string tmptrip in tmpbatchtriplist){
                        //Console.WriteLine(tmptrip);
                        MCBatchTripRecord triprcd = new MCBatchTripRecord();
                        string[] subs = tmptrip.Split(new[] { @"</td><td>", "</td>" }, StringSplitOptions.RemoveEmptyEntries);

                        //index 0 contains trip token and tripid/date
                        //set trip token first
                        triprcd.TripToken = GetContentBulkRegex(subs[0], "doPostBack(&#39;", "&#39;,&#39;&#39;)");

                        string stringwitherrors = GetContentBulkRegex(subs[0], "doPostBack(&#39;", ">");
                    triprcd.TripErrors = ParseModivcarePortalErrorFlag(stringwitherrors);
                        //set trip id
                        string tmptripid = GetContentBulkRegex(subs[0], "\">", "</a>");
                        triprcd.TripFull = tmptripid;
                        string[] datetripid = tmptripid.Split(new[] { @"-", "" }, StringSplitOptions.RemoveEmptyEntries);
                        triprcd.Trip = "1-" + datetripid[1] + "-" + datetripid[2];

                        triprcd.Date = datetripid[0];
                        bool founddate = false;

                        foreach (MCBatchAdditionalInfo batchDetails in mcBatchRecords.MCBatchAdditionalInfo)
                    {
                            if (batchDetails.MCBatchDate.Contains(triprcd.Date)){
                                founddate = true;
                            }
                        }

                        if(founddate == false){
                            MCBatchAdditionalInfo newbatchdetails = new MCBatchAdditionalInfo();
                            newbatchdetails.MCBatchDate = triprcd.Date;
                            mcBatchRecords.MCBatchAdditionalInfo.Add(newbatchdetails);
                        }


                        triprcd.Driver = subs[1];
                        triprcd.PUTime = subs[2];
                        triprcd.DOTime = subs[3];
                        triprcd.RiderCallTime = subs[4];
                        triprcd.Vehicle = subs[5];
                        triprcd.SignatureReceived = subs[6];
                        triprcd.CoPay = subs[7];
                        triprcd.BilledAmount = subs[8];
                        triprcd.RequiresAttention = subs[9];

                        mcBatchRecords.MCBatchTrips.Add(triprcd);
                }

                foreach (MCBatchAdditionalInfo batchDetails in mcBatchRecords.MCBatchAdditionalInfo)
                {
                    //Console.WriteLine("Batch Date: " + batchDetails.MCBatchDate);
                }

            }catch{
                // Do Nothing: Assume that timeout represents no match.
                Console.WriteLine("HERE IS OUR PROBLEM IN LOAD BATCH.");
                //loginhandler.Connected = false;
            }
          
        }
        private async Task RefreshBatch(string resp, MCLoginHandler loginhandler)
        {
            //Console.WriteLine("boo");
            try
            {
                string response = resp;

                //NOT grabbing tokens correctly FIX!
                //loginhandler.GrabTokens(response);

                //get bulk content surrounding trips
                string bulkcontentbegintag = "ctl00_cphMainContent_gvOpenBatchTrips";
                string bulkcontentendtag = "ctl00_cphMainContent_msgDONextDay";
                string bulkcontent = GetContentBulkRegex(response, bulkcontentbegintag, bulkcontentendtag);

                //clean up the bulk content
                string bulkcontenttrimmedbegintag = "Attention</th>";
                string bulkcontenttrimmedendtag = "</table>";
                string bulkcontenttrimmed = GetContentBulkRegex(bulkcontent, bulkcontenttrimmedbegintag, bulkcontenttrimmedendtag);

                //start seperating trips into a list
                List<string> tmpbatchtriplist = new List<string>();
                foreach (Match match in GetStrBetweenTags(bulkcontenttrimmed, "<td>", "</td>"))
                {
                    tmpbatchtriplist.Add(match.Value);
                }

                //finish cleaning and seperating trips and add to triprecords
                foreach (string tmptrip in tmpbatchtriplist)
                {
                    //Console.WriteLine(tmptrip);
                    MCBatchTripRecord triprcd = new MCBatchTripRecord();
                    string[] subs = tmptrip.Split(new[] { @"</td><td>", "</td>" }, StringSplitOptions.RemoveEmptyEntries);

                    //index 0 contains batchtoken and tripid/date
                    //set batchtoken first
                    triprcd.TripToken = GetContentBulkRegex(subs[0], "doPostBack(&#39;", "&#39;,&#39;&#39;)");

                    string stringwitherrors = GetContentBulkRegex(subs[0], "doPostBack(&#39;", ">");

                    //set trip id
                    string tmptripid = GetContentBulkRegex(subs[0], "\">", "</a>");
                    triprcd.TripFull = tmptripid;
                    string[] datetripid = tmptripid.Split(new[] { @"-", "" }, StringSplitOptions.RemoveEmptyEntries);
                    triprcd.Trip = datetripid[1] + "-" + datetripid[2];

                    triprcd.Date = datetripid[0];
                    triprcd.Driver = subs[1];
                    triprcd.PUTime = subs[2];
                    triprcd.DOTime = subs[3];
                    triprcd.RiderCallTime = subs[4];
                    triprcd.Vehicle = subs[5];
                    triprcd.SignatureReceived = subs[6];
                    triprcd.CoPay = subs[7];
                    triprcd.BilledAmount = subs[8];
                    triprcd.RequiresAttention = subs[9];

                    //mcBatchRecords.MCBatchTrips.Add(triprcd);
                    foreach (MCBatchTripRecord trp in mcBatchRecords.MCBatchTrips)
                    {
                        if (trp.TripFull == triprcd.TripFull)
                        {
                            //Console.WriteLine(trp.TripFull);
                            trp.TripToken = triprcd.TripToken;
                            trp.TripErrors = ParseModivcarePortalErrorFlag(stringwitherrors);
                        }
                    }
                }

                        }
            catch
            {
                // Do Nothing: Assume that timeout represents no match.
                Console.WriteLine("HERE IS OUR PROBLEM IN RELOAD BATCH.");
                //loginhandler.Connected = false;
            }

        }
        //get main chunk of data needed with a content for spliting into real values



        /// <summary>Loads batch trips and per-date Modivcare (and optional WellRyde) downloads for correction.</summary>
        /// <param name="wellRydePortalSession">When non-null, loads that date's trips from the WellRyde portal for WR-aware correction logic.</param>
        public async Task InitializeCorrections(MCLoginHandler mcLoginHandler, MCBatchLink mylink,
            WellRydePortalSession wellRydePortalSession = null)
        {
            mcBatchRecords = new MCBatchRecords(); //  <---------NEW

            await ReportProgress("Downloading batch trips…");
            await GetBatchPage(mcLoginHandler, mylink.BatchLinkToken, true);

            await ReportProgress("Loading portal driver and vehicle lists…");
            foreach (MCBatchAdditionalInfo addinfo in mcBatchRecords.MCBatchAdditionalInfo)
            {
                MCBatchTripRecord sampleTrip = null;
                foreach (MCBatchTripRecord trprcd in mcBatchRecords.MCBatchTrips)
                {
                    if (trprcd.Date == addinfo.MCBatchDate && !string.IsNullOrWhiteSpace(trprcd.TripToken))
                    {
                        sampleTrip = trprcd;
                        break;
                    }
                }

                await LoadPortalEligibleListsFromSampleTrip(mcLoginHandler, sampleTrip, addinfo.MCBatchDate);
            }

            tempbatchdetaillist = new List<MCBatchAdditionalInfo>();

            foreach (MCBatchAdditionalInfo addinfo in mcBatchRecords.MCBatchAdditionalInfo)
            {
                DateTime tripDate = GetParsedDate(addinfo);
                string dateLabel = tripDate.ToString("d", CultureInfo.CurrentCulture);

                if (IsModivcareTripDownloadDateSupported(tripDate))
                {
                    await ReportProgress("Downloading Modivcare trips for " + dateLabel + "…");
                    await GetModivcareTripsForBatchDates(mcLoginHandler, tripDate, addinfo);
                }
                else
                {
                    addinfo.mcDownloadedTrips = new List<MCDownloadedTrip>();
                    Console.WriteLine("MCTimeCorrection: skipping Modivcare download for " +
                        dateLabel +
                        " (outside the 8-day past / 30-day future trip-download window).");
                    if (!tempbatchdetaillist.Contains(addinfo))
                        tempbatchdetaillist.Add(addinfo);
                }

                await ReportProgress("Downloading WellRyde trips for " + dateLabel + "…");
                await LoadWellRydeTripsForBatchDateAsync(wellRydePortalSession, tripDate, addinfo);
                EnsureScheduledTimesForBatchDate(addinfo);
                if (!tempbatchdetaillist.Contains(addinfo))
                    tempbatchdetaillist.Add(addinfo);

                await ReportProgress("Applying timing rules for " + dateLabel + "…");
                if (LoadMode == TimeCorrectionLoadMode.DataOnly)
                    ApplyDataOnlyTripDefaults(mcBatchRecords.MCBatchTrips, addinfo);
                else if (LoadMode == TimeCorrectionLoadMode.Lenient)
                    LenientCorrectModivcareTimes(mcBatchRecords.MCBatchTrips, addinfo);
                else
                    RevampedCorrectModivcareTimes(mcBatchRecords.MCBatchTrips, addinfo);

                if (UsesPortalRedStandardOverlay())
                    ApplyPortalRedStandardPass(mcBatchRecords.MCBatchTrips, addinfo);
            }
            mcBatchRecords.MCBatchAdditionalInfo = tempbatchdetaillist;

            await ReportProgress("Validating drivers and vehicles…");
            foreach (MCBatchAdditionalInfo batchInfo in mcBatchRecords.MCBatchAdditionalInfo)
            {
                FillMissingDriversAndVehicles(batchInfo);
                ValidateAssignmentsAgainstPortalLists(batchInfo);
                FinalizeDataQualityFixable(batchInfo);
                LoadAssignedVehicles(batchInfo);
                AdjustVehicles(batchInfo);
            }
            SetVehiclesToTrips();
            EnsurePortalRedTripsAreFixable(mcBatchRecords.MCBatchTrips);
        }

        private async Task ReportProgress(string message)
        {
            if (ReportProgressAsync == null || string.IsNullOrEmpty(message))
                return;
            try
            {
                await ReportProgressAsync(message).ConfigureAwait(false);
            }
            catch
            {
                // Overlay updates must not abort batch load.
            }
        }

        public TimeCorrectionAccuracySnapshot CalculateAccuracySnapshot()
        {
            var snapshot = new TimeCorrectionAccuracySnapshot();
            if (mcBatchRecords?.MCBatchTrips == null || mcBatchRecords.MCBatchAdditionalInfo == null)
                return snapshot;

            MCCalc = new MCCalculateAccuracies();

            foreach (MCBatchAdditionalInfo addinfo in mcBatchRecords.MCBatchAdditionalInfo)
            {
                if (addinfo.MCDrivers != null)
                {
                    foreach (MCDriver driver in addinfo.MCDrivers)
                    {
                        if (driver == null)
                            continue;
                        driver.Accuracies = 0;
                        driver.Inaccuracies = 0;
                        driver.Triplegs = 0;
                    }
                }

                MCCalc.CalculateAccuracies(mcBatchRecords.MCBatchTrips, addinfo);
            }

            List<MCDriver> driverstemp = MCCalc.ReturnAccuracies(mcBatchRecords.MCBatchAdditionalInfo);
            int totalLegs = 0;
            int accurateLegs = 0;
            foreach (MCDriver driver in driverstemp)
            {
                if (driver == null || driver.Triplegs == 0)
                    continue;

                driver.AccuracyPercent = Math.Round((double)driver.Accuracies / driver.Triplegs * 100);
                snapshot.Drivers.Add(driver);
                totalLegs += driver.Triplegs;
                accurateLegs += driver.Accuracies;
            }

            snapshot.TotalLegs = totalLegs;
            snapshot.AccurateLegs = accurateLegs;
            snapshot.CompanyAccuracyPercent = totalLegs > 0
                ? Math.Round(accurateLegs * 100.0 / totalLegs)
                : 0;

            var dateLabels = new List<string>();
            foreach (MCBatchAdditionalInfo addinfo in mcBatchRecords.MCBatchAdditionalInfo)
            {
                if (!string.IsNullOrWhiteSpace(addinfo.MCBatchDate) &&
                    !dateLabels.Contains(addinfo.MCBatchDate))
                    dateLabels.Add(addinfo.MCBatchDate);
            }
            snapshot.ServiceDateLabel = string.Join(", ", dateLabels);

            foreach (MCBatchTripRecord trip in mcBatchRecords.MCBatchTrips)
            {
                snapshot.TotalTrips++;
                if (string.Equals(trip.Status, "Passed", StringComparison.OrdinalIgnoreCase))
                    snapshot.PassedTrips++;
                else if (string.Equals(trip.Status, "Failed", StringComparison.OrdinalIgnoreCase))
                    snapshot.FailedTrips++;
                else if (string.Equals(trip.Status, "Fixable", StringComparison.OrdinalIgnoreCase))
                    snapshot.FixableTrips++;
            }

            return snapshot;
        }

        public List<MCDriver> CalculateAccuracies() => CalculateAccuracySnapshot().Drivers;

        public void GetBatchTripStatusCounts(out int fixable, out int passed, out int failed, out int total,
            out string serviceDateLabel)
        {
            fixable = 0;
            passed = 0;
            failed = 0;
            total = 0;
            serviceDateLabel = "";
            if (mcBatchRecords?.MCBatchTrips == null)
                return;

            var dateLabels = new List<string>();
            foreach (MCBatchTripRecord trip in mcBatchRecords.MCBatchTrips)
            {
                total++;
                if (string.Equals(trip.Status, "Passed", StringComparison.OrdinalIgnoreCase))
                    passed++;
                else if (string.Equals(trip.Status, "Failed", StringComparison.OrdinalIgnoreCase))
                    failed++;
                else if (string.Equals(trip.Status, "Fixable", StringComparison.OrdinalIgnoreCase))
                    fixable++;

                if (!string.IsNullOrWhiteSpace(trip.Date) && !dateLabels.Contains(trip.Date))
                    dateLabels.Add(trip.Date);
            }

            serviceDateLabel = string.Join(", ", dateLabels);
        }

        List<MCBatchAdditionalInfo> tempbatchdetaillist;
        private async Task GetModivcareTripsForBatchDates(MCLoginHandler mcrLoginHandler, DateTime mcdate, MCBatchAdditionalInfo additionalInfo)
        {
                MCBatchAdditionalInfo mcbd = additionalInfo;

            //download modivcare trips for date and store them in the batch details
            Console.WriteLine("gathering trips for batch..");
            mcbd.mcDownloadedTrips = new List<MCDownloadedTrip>();
            mcbd.mcDownloadedTrips = await mctripdler.DownloadTripRecords(mcdate, mcrLoginHandler);

            if (mctripdler.InvalidDate)
            {
                Console.WriteLine("MCTimeCorrection: Modivcare trip download calendar rejected " +
                    mcdate.ToString("d", CultureInfo.CurrentCulture) + ".");
            }

            if (mcbd.mcDownloadedTrips != null)
            {
                foreach (MCDownloadedTrip mcrtr in mcbd.mcDownloadedTrips) 
                {
                    foreach (MCBatchTripRecord mcbtr in mcBatchRecords.MCBatchTrips)
                    {                    
                        if (mcbtr.Trip.Replace(" ","") == mcrtr.TripNumber.Replace(" ", ""))
                        {
                            mcbtr.ScheduledPUTime = NormalizeBatchTime(mcrtr.PUTime);
                            mcbtr.ScheduledDOTime = NormalizeBatchTime(mcrtr.DOTime);
                        }
                    }
                }
                if (!tempbatchdetaillist.Contains(mcbd))
                    tempbatchdetaillist.Add(mcbd);
            }
            Console.WriteLine("finished gathering trips!");
        }
        /// <summary>Loads <see cref="MCBatchAdditionalInfo.wrDownloadedTrips"/> from <c>POST /portal/filterdata</c> for the batch date.</summary>
        private static async Task LoadWellRydeTripsForBatchDateAsync(WellRydePortalSession session, DateTime tripDate,
            MCBatchAdditionalInfo additionalInfo)
        {
            additionalInfo.wrDownloadedTrips = new List<WRDownloadedTrip>();
            if (session == null)
                return;

            try
            {
                var fd = await session.PostTripFilterDataAsync(tripDate,
                    maxResults: WellRydePortalSession.DefaultTripFilterMaxResult).ConfigureAwait(false);
                if (!fd.IsSuccess)
                {
                    Console.WriteLine("MCTimeCorrection: WellRyde filterdata failed for " + tripDate.ToString("d", CultureInfo.CurrentCulture) +
                        ": " + (fd.ErrorMessage ?? "unknown error."));
                    return;
                }

                additionalInfo.wrDownloadedTrips =
                    WellRydeFilterDataParser.ParseTrips(fd.JsonBody, out _) ?? new List<WRDownloadedTrip>();
            }
            catch (Exception ex)
            {
                Console.WriteLine("MCTimeCorrection: WellRyde load failed: " + ex.Message);
            }
        }
        private void CorrectModivcareTimes(List<MCBatchTripRecord> batchtrips, MCBatchAdditionalInfo dledtripsaddinfo)
        {
            //Console.WriteLine(dledtripsaddinfo.MCBatchDate);




            Random r = new Random();
            int rInt;

            foreach (MCBatchTripRecord trprcd in batchtrips)
            {
                if (trprcd.Date == dledtripsaddinfo.MCBatchDate)
                {

                    //Console.WriteLine(trprcd.ScheduledPUTime);
                    DateTime schedputime = DateTime.ParseExact(trprcd.ScheduledPUTime, "HH:mm", CultureInfo.InvariantCulture);
                    DateTime scheddotime = DateTime.ParseExact(trprcd.ScheduledDOTime, "HH:mm", CultureInfo.InvariantCulture);
                    DateTime driverputime = DateTime.ParseExact(trprcd.PUTime, "HH:mm", CultureInfo.InvariantCulture);
                    DateTime driverdotime = DateTime.ParseExact(trprcd.DOTime, "HH:mm", CultureInfo.InvariantCulture);

                    if (trprcd.RiderCallTime.Contains("nbsp"))
                    {

                    }
                    else
                    {
                        //Console.WriteLine(trprcd.RiderCallTime);

                        schedputime = DateTime.ParseExact(trprcd.RiderCallTime, "HH:mm", CultureInfo.InvariantCulture);
                        if (trprcd.PUTime != trprcd.RiderCallTime)
                        {
                            trprcd.Alerts = trprcd.Alerts + " RCT";
                            trprcd.Status = "Fixable";
                        }

                    }

                    decimal miles = 0;
                    if (dledtripsaddinfo.mcDownloadedTrips.Any() & dledtripsaddinfo.mcDownloadedTrips != null)
                    {
                        foreach (MCDownloadedTrip mcdledtrip in dledtripsaddinfo.mcDownloadedTrips)
                        {
                            if (mcdledtrip.TripNumber.Replace(" ", "") == trprcd.Trip.Replace(" ", ""))
                            {
                                miles = Convert.ToDecimal(mcdledtrip.Miles);
                            }
                        }
                    }






                  /*
                                if (dledtripsaddinfo.wrDownloadedTrips.Any() & dledtripsaddinfo.wrDownloadedTrips != null)
                                {
                                    foreach (WRDownloadedTrip wrdledtrip in dledtripsaddinfo.wrDownloadedTrips)
                                    {
                                        if (wrdledtrip.TripNumber.Replace(" ", "") == trprcd.Trip.Replace(" ", ""))
                                        {
                                            scheddotime = schedputime + new TimeSpan(0, (int)(10 + Convert.ToDecimal(wrdledtrip.Miles)), 0);
                                        }
                                    }
                                }


                                if (scheddotime.TimeOfDay.Ticks == 0)
                                {
                                    Console.WriteLine(scheddotime);
                                    continue;
                                }




                                if (dledtripsaddinfo.mcDownloadedTrips.Any() & dledtripsaddinfo.mcDownloadedTrips != null)
                                {
                                    foreach (MCDownloadedTrip mcdledtrip in dledtripsaddinfo.mcDownloadedTrips)
                                    {
                                        if (mcdledtrip.TripNumber.Replace(" ", "") == trprcd.Trip.Replace(" ", ""))
                                        {
                                            //Console.WriteLine(mcdledtrip.DOTime);
                                            if (mcdledtrip.DOTime.Replace(" ", "") == "00:00")
                                            {
                                                if (McTripTimingRules.PuLateMinutesOk(trprcd.Trip, driverputime, schedputime))//driver time is good
                                                {
                                                    trprcd.SuggestedPUTime = trprcd.PUTime;
                                                    if (driverdotime == (driverputime + new TimeSpan(0, (int)(10 + Convert.ToDecimal(mcdledtrip.Miles)), 0)))
                                                    {
                                                        trprcd.SuggestedDOTime = trprcd.DOTime;
                                                    }
                                                    else
                                                    {
                                                        //trprcd.Alerts = trprcd.Alerts + " NDO";
                                                        scheddotime = schedputime + new TimeSpan(0, (int)(10 + Convert.ToDecimal(mcdledtrip.Miles)), 0);
                                                        //trprcd.Status = "Fixable";
                                                    }
                                                }
                                                else
                                                {
                                                    trprcd.Alerts = trprcd.Alerts + " NDO";
                                                    rInt = r.Next(0, 30);
                                                    schedputime = schedputime + new TimeSpan(0, rInt, 0);
                                                    trprcd.Status = "Fixable";
                                                    if (driverdotime == (driverputime + new TimeSpan(0, (int)(10 + Convert.ToDecimal(mcdledtrip.Miles)), 0)))//driver time is good
                                                    {
                                                        trprcd.SuggestedDOTime = trprcd.DOTime;
                                                    }
                                                    else
                                                    {
                                                        trprcd.Alerts = trprcd.Alerts + " NDO";
                                                        scheddotime = schedputime + new TimeSpan(0, (int)(10 + Convert.ToDecimal(mcdledtrip.Miles)), 0);
                                                        trprcd.Status = "Fixable";
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }

                                if (trprcd.SuggestedPUTime != null && trprcd.SuggestedDOTime != null)
                                {


                                if (trprcd.ScheduledDOTime.Replace(" ", "") == "00:00")
                                {
                                    if (trprcd.PUTime.Replace(" ", "") == trprcd.SuggestedPUTime.Replace(" ", "") & trprcd.DOTime.Replace(" ", "") == trprcd.SuggestedDOTime.Replace(" ", ""))
                                    {
                                        continue;
                                    }
                                    else
                                    {
                                        trprcd.Alerts = trprcd.Alerts + " NDO";
                                        trprcd.Status = "Fixable";
                                        //continue;
                                    }
                                }
                                }

                                */





                    int putimediff = DateTime.Compare(driverputime, schedputime);
                    int dotimediff = DateTime.Compare(driverdotime, scheddotime);

                    if (trprcd.Trip.Contains("A"))
                    {
                        //Console.WriteLine(trprcd.Trip + " : trip is an A leg. " + schedputime);


                        if ((scheddotime - schedputime).TotalMinutes >= 60)
                        {
                            switch (putimediff)
                            {
                                case 0://times are same
                                    trprcd.SuggestedPUTime = trprcd.PUTime;
                                    break;
                                case -1://driver is early
                                    if (McTripTimingRules.PuEarlyMinutesOk(trprcd.Trip, driverputime, schedputime))//driver time is good
                                    {
                                        trprcd.SuggestedPUTime = trprcd.PUTime;
                                    }
                                    else
                                    {
                                        trprcd.Alerts = trprcd.Alerts + " EPU";
                                        rInt = r.Next(0, 30);
                                        schedputime = schedputime - new TimeSpan(0, rInt, 0);
                                        trprcd.SuggestedPUTime = schedputime.ToString("HH:mm");
                                        trprcd.Status = "Fixable";
                                    }
                                    break; //up to 30 minutes early
                                case 1://driver is late
                                    if (McTripTimingRules.PuLateMinutesOk(trprcd.Trip, driverputime, schedputime))//driver time is good
                                    {
                                        trprcd.SuggestedPUTime = trprcd.PUTime;
                                    }
                                    else
                                    {
                                        trprcd.Alerts = trprcd.Alerts + " LPU";
                                        rInt = r.Next(0, 15);
                                        schedputime = schedputime + new TimeSpan(0, rInt, 0);
                                        trprcd.SuggestedPUTime = schedputime.ToString("HH:mm");
                                        trprcd.Status = "Fixable";
                                    }
                                    break;//up to 15 minutes late
                            }
                            switch (dotimediff)
                            {
                                case 0://times are same
                                    trprcd.SuggestedDOTime = trprcd.DOTime;
                                    break;
                                case -1://driver is early
                                    if ((scheddotime - driverdotime).TotalMinutes <= 30)//driver time is good
                                    {
                                        trprcd.SuggestedDOTime = trprcd.DOTime;
                                    }
                                    else
                                    {
                                        trprcd.Alerts = trprcd.Alerts + " EDO";
                                        rInt = r.Next(0, 30);
                                        scheddotime = scheddotime - new TimeSpan(0, rInt, 0);
                                        trprcd.SuggestedDOTime = scheddotime.ToString("HH:mm");
                                        trprcd.Status = "Fixable";
                                    }
                                    break;//up to 30 minutes early
                                case 1://driver is late
                                    trprcd.Alerts = trprcd.Alerts + " LDO";
                                    rInt = r.Next(0, 30);
                                    scheddotime = scheddotime - new TimeSpan(0, rInt, 0);
                                    trprcd.SuggestedDOTime = scheddotime.ToString("HH:mm");
                                    trprcd.Status = "Fixable";
                                    break;//cant be late
                            }
                            continue;
                        }
                        if ((scheddotime - schedputime).TotalMinutes >= 45 & (scheddotime - schedputime).TotalMinutes < 60)
                        {
                            switch (putimediff)
                            {
                                case 0://times are same
                                    trprcd.SuggestedPUTime = trprcd.PUTime;
                                    break;
                                case -1://driver is early
                                    if (McTripTimingRules.PuEarlyMinutesOk(trprcd.Trip, driverputime, schedputime))//driver time is good
                                    {
                                        trprcd.SuggestedPUTime = trprcd.PUTime;
                                    }
                                    else
                                    {
                                        trprcd.Alerts = trprcd.Alerts + " EPU";
                                        rInt = r.Next(0, 30);
                                        schedputime = schedputime - new TimeSpan(0, rInt, 0);
                                        trprcd.SuggestedPUTime = schedputime.ToString("HH:mm");
                                        trprcd.Status = "Fixable";
                                    }
                                    break; //up to 30 minutes early
                                case 1://driver is late
                                    if (McTripTimingRules.PuLateMinutesOk(trprcd.Trip, driverputime, schedputime))//driver time is good
                                    {
                                        trprcd.SuggestedPUTime = trprcd.PUTime;
                                    }
                                    else
                                    {
                                        trprcd.Alerts = trprcd.Alerts + " LPU";
                                        rInt = r.Next(0, 15);
                                        schedputime = schedputime + new TimeSpan(0, rInt, 0);
                                        trprcd.SuggestedPUTime = schedputime.ToString("HH:mm");
                                        trprcd.Status = "Fixable";
                                    }
                                    break;//up to 10 minutes late
                            }
                            switch (dotimediff)
                            {
                                case 0://times are same
                                    trprcd.SuggestedDOTime = trprcd.DOTime;
                                    break;
                                case -1://driver is early
                                    if ((scheddotime - driverdotime).TotalMinutes <= 20)//driver time is good
                                    {
                                        trprcd.SuggestedDOTime = trprcd.DOTime;
                                    }
                                    else
                                    {
                                        trprcd.Alerts = trprcd.Alerts + " EDO";
                                        rInt = r.Next(0, 20);
                                        scheddotime = scheddotime - new TimeSpan(0, rInt, 0);
                                        trprcd.SuggestedDOTime = scheddotime.ToString("HH:mm");
                                        trprcd.Status = "Fixable";
                                    }
                                    break;//up to 20 minutes early
                                case 1://driver is late
                                    trprcd.Alerts = trprcd.Alerts + " LDO";
                                    rInt = r.Next(0, 20);
                                    scheddotime = scheddotime - new TimeSpan(0, rInt, 0);
                                    trprcd.SuggestedDOTime = scheddotime.ToString("HH:mm");
                                    trprcd.Status = "Fixable";
                                    break;//cant be late
                            }
                            continue;
                        }
                        if ((scheddotime - schedputime).TotalMinutes >= 30 & (scheddotime - schedputime).TotalMinutes < 45)
                        {
                            switch (putimediff)
                            {
                                case 0://times are same
                                    trprcd.SuggestedPUTime = trprcd.PUTime;
                                    break;
                                case -1://driver is early
                                    if (McTripTimingRules.PuEarlyMinutesOk(trprcd.Trip, driverputime, schedputime))//driver time is good
                                    {
                                        trprcd.SuggestedPUTime = trprcd.PUTime;
                                    }
                                    else
                                    {
                                        trprcd.Alerts = trprcd.Alerts + " EPU";
                                        rInt = r.Next(0, 30);
                                        schedputime = schedputime - new TimeSpan(0, rInt, 0);
                                        trprcd.SuggestedPUTime = schedputime.ToString("HH:mm");
                                        trprcd.Status = "Fixable";
                                    }
                                    break; //up to 30 minutes early
                                case 1://driver is late
                                    if (McTripTimingRules.PuLateMinutesOk(trprcd.Trip, driverputime, schedputime))//driver time is good
                                    {
                                        trprcd.SuggestedPUTime = trprcd.PUTime;
                                    }
                                    else
                                    {
                                        trprcd.Alerts = trprcd.Alerts + " LPU";
                                        rInt = r.Next(0, 10);
                                        schedputime = schedputime + new TimeSpan(0, rInt, 0);
                                        trprcd.SuggestedPUTime = schedputime.ToString("HH:mm");
                                        trprcd.Status = "Fixable";
                                    }
                                    break;//up to 10 minutes late
                            }
                            switch (dotimediff)
                            {
                                case 0://times are same
                                    trprcd.SuggestedDOTime = trprcd.DOTime;
                                    break;
                                case -1://driver is early
                                    if ((scheddotime - driverdotime).TotalMinutes <= 15)//driver time is good
                                    {
                                        trprcd.SuggestedDOTime = trprcd.DOTime;
                                    }
                                    else
                                    {
                                        trprcd.Alerts = trprcd.Alerts + " EDO";
                                        rInt = r.Next(0, 15);
                                        scheddotime = scheddotime - new TimeSpan(0, rInt, 0);
                                        trprcd.SuggestedDOTime = scheddotime.ToString("HH:mm");
                                        trprcd.Status = "Fixable";
                                    }
                                    break;//up to 15 minutes early
                                case 1://driver is late
                                    trprcd.Alerts = trprcd.Alerts + " LDO";
                                    rInt = r.Next(0, 15);
                                    scheddotime = scheddotime - new TimeSpan(0, rInt, 0);
                                    trprcd.SuggestedDOTime = scheddotime.ToString("HH:mm");
                                    trprcd.Status = "Fixable";
                                    break;//cant be late
                            }
                            continue;
                        }
                        if ((scheddotime - schedputime).TotalMinutes >= 15 & (scheddotime - schedputime).TotalMinutes < 30)
                        {
                            switch (putimediff)
                            {
                                case 0://times are same
                                    trprcd.SuggestedPUTime = trprcd.PUTime;
                                    break;
                                case -1://driver is early
                                    if (McTripTimingRules.PuEarlyMinutesOk(trprcd.Trip, driverputime, schedputime))//driver time is good
                                    {
                                        trprcd.SuggestedPUTime = trprcd.PUTime;
                                    }
                                    else
                                    {
                                        trprcd.Alerts = trprcd.Alerts + " EPU";
                                        rInt = r.Next(0, 30);
                                        schedputime = schedputime - new TimeSpan(0, rInt, 0);
                                        trprcd.SuggestedPUTime = schedputime.ToString("HH:mm");
                                        trprcd.Status = "Fixable";
                                    }
                                    break; //up to 30 minutes early
                                case 1://driver is late
                                    if (McTripTimingRules.PuLateMinutesOk(trprcd.Trip, driverputime, schedputime))//driver time is good
                                    {
                                        trprcd.SuggestedPUTime = trprcd.PUTime;
                                    }
                                    else
                                    {
                                        trprcd.Alerts = trprcd.Alerts + " LPU";
                                        rInt = r.Next(0, 5);
                                        schedputime = schedputime + new TimeSpan(0, rInt, 0);
                                        trprcd.SuggestedPUTime = schedputime.ToString("HH:mm");
                                        trprcd.Status = "Fixable";
                                    }
                                    break;//up to 5 minutes late
                            }
                            switch (dotimediff)
                            {
                                case 0://times are same
                                    trprcd.SuggestedDOTime = trprcd.DOTime;
                                    break;
                                case -1://driver is early
                                    if ((scheddotime - driverdotime).TotalMinutes <= 5)//driver time is good
                                    {
                                        trprcd.SuggestedDOTime = trprcd.DOTime;
                                    }
                                    else
                                    {
                                        trprcd.Alerts = trprcd.Alerts + " EDO";
                                        rInt = r.Next(0, 5);
                                        scheddotime = scheddotime - new TimeSpan(0, rInt, 0);
                                        trprcd.SuggestedDOTime = scheddotime.ToString("HH:mm");
                                        trprcd.Status = "Fixable";
                                    }
                                    break;//up to 5 minutes early
                                case 1://driver is late
                                    trprcd.Alerts = trprcd.Alerts + " LDO";
                                    rInt = r.Next(0, 5);
                                    scheddotime = scheddotime - new TimeSpan(0, rInt, 0);
                                    trprcd.SuggestedDOTime = scheddotime.ToString("HH:mm");
                                    trprcd.Status = "Fixable";

                                    break;//cant be late
                            }
                            continue;
                        }
                        if ((scheddotime - schedputime).TotalMinutes < 15)
                        {
                            switch (putimediff)
                            {
                                case 0://times are same
                                    trprcd.SuggestedPUTime = trprcd.PUTime;
                                    break;
                                case -1://driver is early
                                    if (McTripTimingRules.PuEarlyMinutesOk(trprcd.Trip, driverputime, schedputime))//driver time is good
                                    {
                                        trprcd.SuggestedPUTime = trprcd.PUTime;
                                    }
                                    else
                                    {
                                        trprcd.Alerts = trprcd.Alerts + " EPU";
                                        rInt = r.Next(0, 30);
                                        schedputime = schedputime - new TimeSpan(0, rInt, 0);
                                        trprcd.SuggestedPUTime = schedputime.ToString("HH:mm");
                                        trprcd.Status = "Fixable";
                                    }
                                    break; //up to 30 minutes early
                                case 1://driver is late
                                    trprcd.Alerts = trprcd.Alerts + " LPU";
                                    trprcd.SuggestedPUTime = schedputime.ToString("HH:mm");
                                    trprcd.Status = "Fixable";
                                    break;//cant be late
                            }
                            switch (dotimediff)
                            {
                                case 0://times are same
                                    trprcd.SuggestedDOTime = trprcd.DOTime;
                                    break;
                                case -1://driver is early
                                    trprcd.Alerts = trprcd.Alerts + " EDO";
                                    trprcd.SuggestedDOTime = scheddotime.ToString("HH:mm");
                                    trprcd.Status = "Fixable";
                                    break;//up to 5 minutes early
                                case 1://driver is late
                                    trprcd.Alerts = trprcd.Alerts + " LDO";
                                    trprcd.SuggestedDOTime = scheddotime.ToString("HH:mm");
                                    trprcd.Status = "Fixable";
                                    break;//cant be late
                            }
                            continue;
                        }
                    }
                    if (trprcd.Trip.Contains("B"))
                    {
                        //Console.WriteLine(trprcd.Trip + " : trip is an B leg");

                        // returns <0 since d1 is earlier than d2
                        if (scheddotime.TimeOfDay.Ticks == 0)
                        {
                            switch (putimediff)
                            {
                                case 0://times are same
                                    trprcd.SuggestedPUTime = trprcd.PUTime;
                                    break;
                                case -1://driver is early. cant be early.
                                    trprcd.Alerts = trprcd.Alerts + " EPU";
                                    rInt = r.Next(0, 30);
                                    schedputime = schedputime + new TimeSpan(0, rInt, 0);
                                    trprcd.SuggestedPUTime = schedputime.ToString("HH:mm");
                                    trprcd.Status = "Fixable";
                                    break; 
                                case 1://driver is late
                                    if (McTripTimingRules.PuLateMinutesOk(trprcd.Trip, driverputime, schedputime))//driver time is good
                                    {
                                        trprcd.SuggestedPUTime = trprcd.PUTime;
                                    }
                                    else
                                    {
                                        trprcd.Alerts = trprcd.Alerts + " LPU";
                                        rInt = r.Next(0, 30);
                                        schedputime = schedputime + new TimeSpan(0, rInt, 0);
                                        trprcd.SuggestedPUTime = schedputime.ToString("HH:mm");
                                        trprcd.Status = "Fixable";
                                    }
                                    break;//up to 30 minutes late
                                }

                            double triptotalmins = (driverdotime - driverputime).TotalMinutes;
                            double milemins = (int)(10 + miles);


                            if (triptotalmins >= milemins)
                            {
                                trprcd.SuggestedDOTime = trprcd.DOTime;
                            }
                            else
                            {
                                trprcd.Alerts = trprcd.Alerts + " EDO";
                                scheddotime = driverputime + new TimeSpan(0, (int)milemins, 0);
                                trprcd.SuggestedDOTime = scheddotime.ToString("HH:mm");
                                trprcd.Status = "Fixable";
                            }

                            Console.WriteLine(scheddotime);
                            continue;
                        }

                        if ((scheddotime - schedputime).TotalMinutes >= 60)
                        {
                            switch (putimediff)
                            {
                                case 0://times are same
                                    trprcd.SuggestedPUTime = trprcd.PUTime;
                                    break;
                                case -1://driver is early
                                    trprcd.Alerts = trprcd.Alerts + " EPU";
                                    rInt = r.Next(0, 30);
                                    schedputime = schedputime + new TimeSpan(0, rInt, 0);
                                    trprcd.SuggestedPUTime = schedputime.ToString("HH:mm");
                                    trprcd.Status = "Fixable";
                                    break; //up to 30 minutes late
                                case 1://driver is late
                                    if (McTripTimingRules.PuLateMinutesOk(trprcd.Trip, driverputime, schedputime))//driver time is good
                                    {
                                        trprcd.SuggestedPUTime = trprcd.PUTime;
                                    }
                                    else
                                    {
                                        trprcd.Alerts = trprcd.Alerts + " LPU";
                                        rInt = r.Next(0, 25);
                                        schedputime = schedputime + new TimeSpan(0, rInt, 0);
                                        trprcd.SuggestedPUTime = schedputime.ToString("HH:mm");
                                        trprcd.Status = "Fixable";
                                    }
                                    break;//up to 25 minutes late
                            }
                            switch (dotimediff)
                            {
                                case 0://times are same
                                    trprcd.SuggestedDOTime = trprcd.DOTime;
                                    break;
                                case -1://driver is early
                                    if ((scheddotime - driverdotime).TotalMinutes <= 25)//driver time is good
                                    {
                                        trprcd.SuggestedDOTime = trprcd.DOTime;
                                    }
                                    else
                                    {
                                        trprcd.Alerts = trprcd.Alerts + " EDO";
                                        rInt = r.Next(0, 25);
                                        scheddotime = scheddotime - new TimeSpan(0, rInt, 0);
                                        trprcd.SuggestedDOTime = scheddotime.ToString("HH:mm");
                                        trprcd.Status = "Fixable";
                                    }
                                    break;//up to 25 minutes early
                                case 1://driver is late
                                    trprcd.Alerts = trprcd.Alerts + " LDO";
                                    rInt = r.Next(0, 25);
                                    scheddotime = scheddotime - new TimeSpan(0, rInt, 0);
                                    trprcd.SuggestedDOTime = scheddotime.ToString("HH:mm");
                                    trprcd.Status = "Fixable";
                                    break;//cant be late
                            }
                            continue;
                        }
                        if ((scheddotime - schedputime).TotalMinutes >= 45 & (scheddotime - schedputime).TotalMinutes < 60)
                        {
                            switch (putimediff)
                            {
                                case 0://times are same
                                    trprcd.SuggestedPUTime = trprcd.PUTime;
                                    break;
                                case -1://driver is early
                                    trprcd.Alerts = trprcd.Alerts + " EPU";
                                    rInt = r.Next(0, 20);
                                    schedputime = schedputime + new TimeSpan(0, rInt, 0);
                                    trprcd.SuggestedPUTime = schedputime.ToString("HH:mm");
                                    trprcd.Status = "Fixable";
                                    break; //cant be early
                                case 1://driver is late
                                    if (McTripTimingRules.PuLateMinutesOk(trprcd.Trip, driverputime, schedputime))//driver time is good
                                    {
                                        trprcd.SuggestedPUTime = trprcd.PUTime;
                                    }
                                    else
                                    {
                                        trprcd.Alerts = trprcd.Alerts + " LPU";
                                        rInt = r.Next(0, 20);
                                        schedputime = schedputime + new TimeSpan(0, rInt, 0);
                                        trprcd.SuggestedPUTime = schedputime.ToString("HH:mm");
                                        trprcd.Status = "Fixable";
                                    }
                                    break;//up to 20 minutes late
                            }
                            switch (dotimediff)
                            {
                                case 0://times are same
                                    trprcd.SuggestedDOTime = trprcd.DOTime;
                                    break;
                                case -1://driver is early
                                    if ((scheddotime - driverdotime).TotalMinutes <= 20)//driver time is good
                                    {
                                        trprcd.SuggestedDOTime = trprcd.DOTime;
                                    }
                                    else
                                    {
                                        trprcd.Alerts = trprcd.Alerts + " EDO";
                                        rInt = r.Next(0, 20);
                                        scheddotime = scheddotime - new TimeSpan(0, rInt, 0);
                                        trprcd.SuggestedDOTime = scheddotime.ToString("HH:mm");
                                        trprcd.Status = "Fixable";
                                    }
                                    break;//up to 20 minutes early
                                case 1://driver is late
                                    trprcd.Alerts = trprcd.Alerts + " LDO";
                                    rInt = r.Next(0, 20);
                                    scheddotime = scheddotime - new TimeSpan(0, rInt, 0);
                                    trprcd.SuggestedDOTime = scheddotime.ToString("HH:mm");
                                    trprcd.Status = "Fixable";
                                    break;//cant be late
                            }
                            continue;
                        }
                        if ((scheddotime - schedputime).TotalMinutes >= 30 & (scheddotime - schedputime).TotalMinutes < 45)
                        {
                            switch (putimediff)
                            {
                                case 0://times are same
                                    trprcd.SuggestedPUTime = trprcd.PUTime;
                                    break;
                                case -1://driver is early
                                    trprcd.Alerts = trprcd.Alerts + " EPU";
                                    rInt = r.Next(0, 10);
                                    schedputime = schedputime + new TimeSpan(0, rInt, 0);
                                    trprcd.SuggestedPUTime = schedputime.ToString("HH:mm");
                                    trprcd.Status = "Fixable";
                                    break; //up to 10 minutes late
                                case 1://driver is late
                                    if (McTripTimingRules.PuLateMinutesOk(trprcd.Trip, driverputime, schedputime))//driver time is good
                                    {
                                        trprcd.SuggestedPUTime = trprcd.PUTime;
                                    }
                                    else
                                    {
                                        trprcd.Alerts = trprcd.Alerts + " LPU";
                                        rInt = r.Next(0, 10);
                                        schedputime = schedputime + new TimeSpan(0, rInt, 0);
                                        trprcd.SuggestedPUTime = schedputime.ToString("HH:mm");
                                        trprcd.Status = "Fixable";
                                    }
                                    break;//up to 10 minutes late
                            }
                            switch (dotimediff)
                            {
                                case 0://times are same
                                    trprcd.SuggestedDOTime = trprcd.DOTime;
                                    break;
                                case -1://driver is early
                                    if ((scheddotime - driverdotime).TotalMinutes <= 15)//driver time is good
                                    {
                                        trprcd.SuggestedDOTime = trprcd.DOTime;
                                    }
                                    else
                                    {
                                        trprcd.Alerts = trprcd.Alerts + " EDO";
                                        rInt = r.Next(0, 15);
                                        scheddotime = scheddotime - new TimeSpan(0, rInt, 0);
                                        trprcd.SuggestedDOTime = scheddotime.ToString("HH:mm");
                                        trprcd.Status = "Fixable";
                                    }
                                    break;//up to 15 minutes early
                                case 1://driver is late
                                    trprcd.Alerts = trprcd.Alerts + " LDO";
                                    rInt = r.Next(0, 15);
                                    scheddotime = scheddotime - new TimeSpan(0, rInt, 0);
                                    trprcd.SuggestedDOTime = scheddotime.ToString("HH:mm");
                                    trprcd.Status = "Fixable";
                                    break;//cant be late
                            }
                            continue;
                        }
                        if ((scheddotime - schedputime).TotalMinutes >= 15 & (scheddotime - schedputime).TotalMinutes < 30)
                        {
                            switch (putimediff)
                            {
                                case 0://times are same
                                    trprcd.SuggestedPUTime = trprcd.PUTime;
                                    break;
                                case -1://driver is early
                                    trprcd.Alerts = trprcd.Alerts + " EPU";
                                    rInt = r.Next(0, 5);
                                    schedputime = schedputime + new TimeSpan(0, rInt, 0);
                                    trprcd.SuggestedPUTime = schedputime.ToString("HH:mm");
                                    trprcd.Status = "Fixable";
                                    break; //up to 30 minutes early
                                case 1://driver is late
                                    if (McTripTimingRules.PuLateMinutesOk(trprcd.Trip, driverputime, schedputime))//driver time is good
                                    {
                                        trprcd.SuggestedPUTime = trprcd.PUTime;
                                    }
                                    else
                                    {
                                        trprcd.Alerts = trprcd.Alerts + " LPU";
                                        rInt = r.Next(0, 5);
                                        schedputime = schedputime + new TimeSpan(0, rInt, 0);
                                        trprcd.SuggestedPUTime = schedputime.ToString("HH:mm");
                                        trprcd.Status = "Fixable";
                                    }
                                    break;//up to 5 minutes late
                            }
                            switch (dotimediff)
                            {
                                case 0://times are same
                                    trprcd.SuggestedDOTime = trprcd.DOTime;
                                    break;
                                case -1://driver is early
                                    if ((scheddotime - driverdotime).TotalMinutes <= 5)//driver time is good
                                    {
                                        trprcd.SuggestedDOTime = trprcd.DOTime;
                                    }
                                    else
                                    {
                                        trprcd.Alerts = trprcd.Alerts + " EDO";
                                        rInt = r.Next(0, 5);
                                        scheddotime = scheddotime - new TimeSpan(0, rInt, 0);
                                        trprcd.SuggestedDOTime = scheddotime.ToString("HH:mm");
                                        trprcd.Status = "Fixable";
                                    }
                                    break;//up to 5 minutes early
                                case 1://driver is late
                                    trprcd.Alerts = trprcd.Alerts + " LDO";
                                    rInt = r.Next(0, 5);
                                    scheddotime = scheddotime - new TimeSpan(0, rInt, 0);
                                    trprcd.SuggestedDOTime = scheddotime.ToString("HH:mm");
                                    trprcd.Status = "Fixable";

                                    break;//cant be late
                            }
                            continue;
                        }
                        if ((scheddotime - schedputime).TotalMinutes < 15)
                        {
                            switch (putimediff)
                            {
                                case 0://times are same
                                    trprcd.SuggestedPUTime = trprcd.PUTime;
                                    break;
                                case -1://driver is early
                                    trprcd.Alerts = trprcd.Alerts + " EPU";
                                    trprcd.SuggestedPUTime = schedputime.ToString("HH:mm");
                                    trprcd.Status = "Fixable";
                                    break;
                                case 1://driver is late
                                    trprcd.Alerts = trprcd.Alerts + " LPU";
                                    trprcd.SuggestedPUTime = schedputime.ToString("HH:mm");
                                    trprcd.Status = "Fixable";
                                    break;//cant be late
                            }
                            switch (dotimediff)
                            {
                                case 0://times are same
                                    trprcd.SuggestedDOTime = trprcd.DOTime;
                                    break;
                                case -1://driver is early
                                    trprcd.Alerts = trprcd.Alerts + " EDO";
                                    trprcd.SuggestedDOTime = scheddotime.ToString("HH:mm");
                                    trprcd.Status = "Fixable";
                                    break;
                                case 1://driver is late
                                    trprcd.Alerts = trprcd.Alerts + " LDO";
                                    trprcd.SuggestedDOTime = scheddotime.ToString("HH:mm");
                                    trprcd.Status = "Fixable";
                                    break;//cant be late
                            }
                            continue;
                        }
                    }
                    if (trprcd.Trip.Contains("C"))
                    {
                        //Console.WriteLine(trprcd.Trip + " : trip is an C leg");
                        // returns <0 since d1 is earlier than d2



                        if ((scheddotime - schedputime).TotalMinutes >= 60)
                        {
                            switch (putimediff)
                            {
                                case 0://times are same
                                    trprcd.SuggestedPUTime = trprcd.PUTime;
                                    break;
                                case -1://driver is early
                                    trprcd.Alerts = trprcd.Alerts + " EPU";
                                    rInt = r.Next(0, 30);
                                    schedputime = schedputime + new TimeSpan(0, rInt, 0);
                                    trprcd.SuggestedPUTime = schedputime.ToString("HH:mm");
                                    trprcd.Status = "Fixable";
                                    break; //up to 30 minutes late
                                case 1://driver is late
                                    if (McTripTimingRules.PuLateMinutesOk(trprcd.Trip, driverputime, schedputime))//driver time is good
                                    {
                                        trprcd.SuggestedPUTime = trprcd.PUTime;
                                    }
                                    else
                                    {
                                        trprcd.Alerts = trprcd.Alerts + " LPU";
                                        rInt = r.Next(0, 25);
                                        schedputime = schedputime + new TimeSpan(0, rInt, 0);
                                        trprcd.SuggestedPUTime = schedputime.ToString("HH:mm");
                                        trprcd.Status = "Fixable";
                                    }
                                    break;//up to 25 minutes late
                            }
                            if (scheddotime.ToString().Contains("00:00"))
                            {
                                trprcd.SuggestedDOTime = trprcd.DOTime;
                                continue;
                            }
                            switch (dotimediff)
                            {
                                case 0://times are same
                                    trprcd.SuggestedDOTime = trprcd.DOTime;
                                    break;
                                case -1://driver is early
                                    if ((scheddotime - driverdotime).TotalMinutes <= 25)//driver time is good
                                    {
                                        trprcd.SuggestedDOTime = trprcd.DOTime;
                                    }
                                    else
                                    {
                                        trprcd.Alerts = trprcd.Alerts + " EDO";
                                        rInt = r.Next(0, 25);
                                        scheddotime = scheddotime - new TimeSpan(0, rInt, 0);
                                        trprcd.SuggestedDOTime = scheddotime.ToString("HH:mm");
                                        trprcd.Status = "Fixable";
                                    }
                                    break;//up to 25 minutes early
                                case 1://driver is late
                                    trprcd.Alerts = trprcd.Alerts + " LDO";
                                    rInt = r.Next(0, 25);
                                    scheddotime = scheddotime - new TimeSpan(0, rInt, 0);
                                    trprcd.SuggestedDOTime = scheddotime.ToString("HH:mm");
                                    trprcd.Status = "Fixable";
                                    break;//cant be late
                            }
                            continue;
                        }
                        if ((scheddotime - schedputime).TotalMinutes >= 45 & (scheddotime - schedputime).TotalMinutes < 60)
                        {
                            switch (putimediff)
                            {
                                case 0://times are same
                                    trprcd.SuggestedPUTime = trprcd.PUTime;
                                    break;
                                case -1://driver is early
                                    trprcd.Alerts = trprcd.Alerts + " EPU";
                                    rInt = r.Next(0, 20);
                                    schedputime = schedputime + new TimeSpan(0, rInt, 0);
                                    trprcd.SuggestedPUTime = schedputime.ToString("HH:mm");
                                    trprcd.Status = "Fixable";
                                    break; //cant be early
                                case 1://driver is late
                                    if (McTripTimingRules.PuLateMinutesOk(trprcd.Trip, driverputime, schedputime))//driver time is good
                                    {
                                        trprcd.SuggestedPUTime = trprcd.PUTime;
                                    }
                                    else
                                    {
                                        trprcd.Alerts = trprcd.Alerts + " LPU";
                                        rInt = r.Next(0, 20);
                                        schedputime = schedputime + new TimeSpan(0, rInt, 0);
                                        trprcd.SuggestedPUTime = schedputime.ToString("HH:mm");
                                        trprcd.Status = "Fixable";
                                    }
                                    break;//up to 20 minutes late
                            }
                            if (scheddotime.ToString().Contains("00:00"))
                            {
                                trprcd.SuggestedDOTime = trprcd.DOTime;
                                continue;
                            }
                            switch (dotimediff)
                            {
                                case 0://times are same
                                    trprcd.SuggestedDOTime = trprcd.DOTime;
                                    break;
                                case -1://driver is early
                                    if ((scheddotime - driverdotime).TotalMinutes <= 20)//driver time is good
                                    {
                                        trprcd.SuggestedDOTime = trprcd.DOTime;
                                    }
                                    else
                                    {
                                        trprcd.Alerts = trprcd.Alerts + " EDO";
                                        rInt = r.Next(0, 20);
                                        scheddotime = scheddotime - new TimeSpan(0, rInt, 0);
                                        trprcd.SuggestedDOTime = scheddotime.ToString("HH:mm");
                                        trprcd.Status = "Fixable";
                                    }
                                    break;//up to 20 minutes early
                                case 1://driver is late
                                    trprcd.Alerts = trprcd.Alerts + " LDO";
                                    rInt = r.Next(0, 20);
                                    scheddotime = scheddotime - new TimeSpan(0, rInt, 0);
                                    trprcd.SuggestedDOTime = scheddotime.ToString("HH:mm");
                                    trprcd.Status = "Fixable";
                                    break;//cant be late
                            }
                            continue;
                        }
                        if ((scheddotime - schedputime).TotalMinutes >= 30 & (scheddotime - schedputime).TotalMinutes < 45)
                        {
                            switch (putimediff)
                            {
                                case 0://times are same
                                    trprcd.SuggestedPUTime = trprcd.PUTime;
                                    break;
                                case -1://driver is early
                                    trprcd.Alerts = trprcd.Alerts + " EPU";
                                    rInt = r.Next(0, 10);
                                    schedputime = schedputime + new TimeSpan(0, rInt, 0);
                                    trprcd.SuggestedPUTime = schedputime.ToString("HH:mm");
                                    trprcd.Status = "Fixable";
                                    break; //up to 10 minutes late
                                case 1://driver is late
                                    if (McTripTimingRules.PuLateMinutesOk(trprcd.Trip, driverputime, schedputime))//driver time is good
                                    {
                                        trprcd.SuggestedPUTime = trprcd.PUTime;
                                    }
                                    else
                                    {
                                        trprcd.Alerts = trprcd.Alerts + " LPU";
                                        rInt = r.Next(0, 10);
                                        schedputime = schedputime + new TimeSpan(0, rInt, 0);
                                        trprcd.SuggestedPUTime = schedputime.ToString("HH:mm");
                                        trprcd.Status = "Fixable";
                                    }
                                    break;//up to 10 minutes late
                            }
                            if (scheddotime.ToString().Contains("00:00"))
                            {
                                trprcd.SuggestedDOTime = trprcd.DOTime;
                                continue;
                            }
                            switch (dotimediff)
                            {
                                case 0://times are same
                                    trprcd.SuggestedDOTime = trprcd.DOTime;
                                    break;
                                case -1://driver is early
                                    if ((scheddotime - driverdotime).TotalMinutes <= 15)//driver time is good
                                    {
                                        trprcd.SuggestedDOTime = trprcd.DOTime;
                                    }
                                    else
                                    {
                                        trprcd.Alerts = trprcd.Alerts + " EDO";
                                        rInt = r.Next(0, 15);
                                        scheddotime = scheddotime - new TimeSpan(0, rInt, 0);
                                        trprcd.SuggestedDOTime = scheddotime.ToString("HH:mm");
                                        trprcd.Status = "Fixable";
                                    }
                                    break;//up to 15 minutes early
                                case 1://driver is late
                                    trprcd.Alerts = trprcd.Alerts + " LDO";
                                    rInt = r.Next(0, 15);
                                    scheddotime = scheddotime - new TimeSpan(0, rInt, 0);
                                    trprcd.SuggestedDOTime = scheddotime.ToString("HH:mm");
                                    trprcd.Status = "Fixable";
                                    break;//cant be late
                            }
                            continue;
                        }
                        if ((scheddotime - schedputime).TotalMinutes >= 15 & (scheddotime - schedputime).TotalMinutes < 30)
                        {
                            switch (putimediff)
                            {
                                case 0://times are same
                                    trprcd.SuggestedPUTime = trprcd.PUTime;
                                    break;
                                case -1://driver is early
                                    trprcd.Alerts = trprcd.Alerts + " EPU";
                                    rInt = r.Next(0, 5);
                                    schedputime = schedputime + new TimeSpan(0, rInt, 0);
                                    trprcd.SuggestedPUTime = schedputime.ToString("HH:mm");
                                    trprcd.Status = "Fixable";
                                    break; //up to 30 minutes early
                                case 1://driver is late
                                    if (McTripTimingRules.PuLateMinutesOk(trprcd.Trip, driverputime, schedputime))//driver time is good
                                    {
                                        trprcd.SuggestedPUTime = trprcd.PUTime;
                                    }
                                    else
                                    {
                                        trprcd.Alerts = trprcd.Alerts + " LPU";
                                        rInt = r.Next(0, 5);
                                        schedputime = schedputime + new TimeSpan(0, rInt, 0);
                                        trprcd.SuggestedPUTime = schedputime.ToString("HH:mm");
                                        trprcd.Status = "Fixable";
                                    }
                                    break;//up to 5 minutes late
                            }
                            if (scheddotime.ToString().Contains("00:00"))
                            {
                                trprcd.SuggestedDOTime = trprcd.DOTime;
                                continue;
                            }
                            switch (dotimediff)
                            {
                                case 0://times are same
                                    trprcd.SuggestedDOTime = trprcd.DOTime;
                                    break;
                                case -1://driver is early
                                    if ((scheddotime - driverdotime).TotalMinutes <= 5)//driver time is good
                                    {
                                        trprcd.SuggestedDOTime = trprcd.DOTime;
                                    }
                                    else
                                    {
                                        trprcd.Alerts = trprcd.Alerts + " EDO";
                                        rInt = r.Next(0, 5);
                                        scheddotime = scheddotime - new TimeSpan(0, rInt, 0);
                                        trprcd.SuggestedDOTime = scheddotime.ToString("HH:mm");
                                        trprcd.Status = "Fixable";
                                    }
                                    break;//up to 5 minutes early
                                case 1://driver is late
                                    trprcd.Alerts = trprcd.Alerts + " LDO";
                                    rInt = r.Next(0, 5);
                                    scheddotime = scheddotime - new TimeSpan(0, rInt, 0);
                                    trprcd.SuggestedDOTime = scheddotime.ToString("HH:mm");
                                    trprcd.Status = "Fixable";

                                    break;//cant be late
                            }
                            continue;
                        }
                        if ((scheddotime - schedputime).TotalMinutes < 15)
                        {
                            switch (putimediff)
                            {
                                case 0://times are same
                                    trprcd.SuggestedPUTime = trprcd.PUTime;
                                    break;
                                case -1://driver is early
                                    trprcd.Alerts = trprcd.Alerts + " EPU";
                                    trprcd.SuggestedPUTime = schedputime.ToString("HH:mm");
                                    trprcd.Status = "Fixable";
                                    break;
                                case 1://driver is late
                                    trprcd.Alerts = trprcd.Alerts + " LPU";
                                    trprcd.SuggestedPUTime = schedputime.ToString("HH:mm");
                                    trprcd.Status = "Fixable";
                                    break;//cant be late
                            }
                            if (scheddotime.ToString().Contains("00:00"))
                            {
                                trprcd.SuggestedDOTime = trprcd.DOTime;
                                continue;
                            }
                            switch (dotimediff)
                            {
                                case 0://times are same
                                    trprcd.SuggestedDOTime = trprcd.DOTime;
                                    break;
                                case -1://driver is early
                                    trprcd.Alerts = trprcd.Alerts + " EDO";
                                    trprcd.SuggestedDOTime = scheddotime.ToString("HH:mm");
                                    trprcd.Status = "Fixable";
                                    break;
                                case 1://driver is late
                                    trprcd.Alerts = trprcd.Alerts + " LDO";
                                    trprcd.SuggestedDOTime = scheddotime.ToString("HH:mm");
                                    trprcd.Status = "Fixable";
                                    break;//cant be late
                            }
                            continue;
                        }
                    }
                    if (trprcd.Trip.Contains("D"))
                    {
                        //Console.WriteLine(trprcd.Trip + " : trip is an D leg");
                        // returns <0 since d1 is earlier than d2
                        if ((scheddotime - schedputime).TotalMinutes >= 60)
                        {
                            switch (putimediff)
                            {
                                case 0://times are same
                                    trprcd.SuggestedPUTime = trprcd.PUTime;
                                    break;
                                case -1://driver is early
                                    trprcd.Alerts = trprcd.Alerts + " EPU";
                                    rInt = r.Next(0, 30);
                                    schedputime = schedputime + new TimeSpan(0, rInt, 0);
                                    trprcd.SuggestedPUTime = schedputime.ToString("HH:mm");
                                    trprcd.Status = "Fixable";
                                    break; //up to 30 minutes late
                                case 1://driver is late
                                    if (McTripTimingRules.PuLateMinutesOk(trprcd.Trip, driverputime, schedputime))//driver time is good
                                    {
                                        trprcd.SuggestedPUTime = trprcd.PUTime;
                                    }
                                    else
                                    {
                                        trprcd.Alerts = trprcd.Alerts + " LPU";
                                        rInt = r.Next(0, 25);
                                        schedputime = schedputime + new TimeSpan(0, rInt, 0);
                                        trprcd.SuggestedPUTime = schedputime.ToString("HH:mm");
                                        trprcd.Status = "Fixable";
                                    }
                                    break;//up to 25 minutes late
                            }
                            if (scheddotime.ToString().Contains("00:00"))
                            {
                                trprcd.SuggestedDOTime = trprcd.DOTime;
                                continue;
                            }
                            switch (dotimediff)
                            {
                                case 0://times are same
                                    trprcd.SuggestedDOTime = trprcd.DOTime;
                                    break;
                                case -1://driver is early
                                    if ((scheddotime - driverdotime).TotalMinutes <= 25)//driver time is good
                                    {
                                        trprcd.SuggestedDOTime = trprcd.DOTime;
                                    }
                                    else
                                    {
                                        trprcd.Alerts = trprcd.Alerts + " EDO";
                                        rInt = r.Next(0, 25);
                                        scheddotime = scheddotime - new TimeSpan(0, rInt, 0);
                                        trprcd.SuggestedDOTime = scheddotime.ToString("HH:mm");
                                        trprcd.Status = "Fixable";
                                    }
                                    break;//up to 25 minutes early
                                case 1://driver is late
                                    trprcd.Alerts = trprcd.Alerts + " LDO";
                                    rInt = r.Next(0, 25);
                                    scheddotime = scheddotime - new TimeSpan(0, rInt, 0);
                                    trprcd.SuggestedDOTime = scheddotime.ToString("HH:mm");
                                    trprcd.Status = "Fixable";
                                    break;//cant be late
                            }
                            continue;
                        }
                        if ((scheddotime - schedputime).TotalMinutes >= 45 & (scheddotime - schedputime).TotalMinutes < 60)
                        {
                            switch (putimediff)
                            {
                                case 0://times are same
                                    trprcd.SuggestedPUTime = trprcd.PUTime;
                                    break;
                                case -1://driver is early
                                    trprcd.Alerts = trprcd.Alerts + " EPU";
                                    rInt = r.Next(0, 20);
                                    schedputime = schedputime + new TimeSpan(0, rInt, 0);
                                    trprcd.SuggestedPUTime = schedputime.ToString("HH:mm");
                                    trprcd.Status = "Fixable";
                                    break; //cant be early
                                case 1://driver is late
                                    if (McTripTimingRules.PuLateMinutesOk(trprcd.Trip, driverputime, schedputime))//driver time is good
                                    {
                                        trprcd.SuggestedPUTime = trprcd.PUTime;
                                    }
                                    else
                                    {
                                        trprcd.Alerts = trprcd.Alerts + " LPU";
                                        rInt = r.Next(0, 20);
                                        schedputime = schedputime + new TimeSpan(0, rInt, 0);
                                        trprcd.SuggestedPUTime = schedputime.ToString("HH:mm");
                                        trprcd.Status = "Fixable";
                                    }
                                    break;//up to 20 minutes late
                            }
                            if (scheddotime.ToString().Contains("00:00"))
                            {
                                trprcd.SuggestedDOTime = trprcd.DOTime;
                                continue;
                            }
                            switch (dotimediff)
                            {
                                case 0://times are same
                                    trprcd.SuggestedDOTime = trprcd.DOTime;
                                    break;
                                case -1://driver is early
                                    if ((scheddotime - driverdotime).TotalMinutes <= 20)//driver time is good
                                    {
                                        trprcd.SuggestedDOTime = trprcd.DOTime;
                                    }
                                    else
                                    {
                                        trprcd.Alerts = trprcd.Alerts + " EDO";
                                        rInt = r.Next(0, 20);
                                        scheddotime = scheddotime - new TimeSpan(0, rInt, 0);
                                        trprcd.SuggestedDOTime = scheddotime.ToString("HH:mm");
                                        trprcd.Status = "Fixable";
                                    }
                                    break;//up to 20 minutes early
                                case 1://driver is late
                                    trprcd.Alerts = trprcd.Alerts + " LDO";
                                    rInt = r.Next(0, 20);
                                    scheddotime = scheddotime - new TimeSpan(0, rInt, 0);
                                    trprcd.SuggestedDOTime = scheddotime.ToString("HH:mm");
                                    trprcd.Status = "Fixable";
                                    break;//cant be late
                            }
                            continue;
                        }
                        if ((scheddotime - schedputime).TotalMinutes >= 30 & (scheddotime - schedputime).TotalMinutes < 45)
                        {
                            switch (putimediff)
                            {
                                case 0://times are same
                                    trprcd.SuggestedPUTime = trprcd.PUTime;
                                    break;
                                case -1://driver is early
                                    trprcd.Alerts = trprcd.Alerts + " EPU";
                                    rInt = r.Next(0, 10);
                                    schedputime = schedputime + new TimeSpan(0, rInt, 0);
                                    trprcd.SuggestedPUTime = schedputime.ToString("HH:mm");
                                    trprcd.Status = "Fixable";
                                    break; //up to 10 minutes late
                                case 1://driver is late
                                    if (McTripTimingRules.PuLateMinutesOk(trprcd.Trip, driverputime, schedputime))//driver time is good
                                    {
                                        trprcd.SuggestedPUTime = trprcd.PUTime;
                                    }
                                    else
                                    {
                                        trprcd.Alerts = trprcd.Alerts + " LPU";
                                        rInt = r.Next(0, 10);
                                        schedputime = schedputime + new TimeSpan(0, rInt, 0);
                                        trprcd.SuggestedPUTime = schedputime.ToString("HH:mm");
                                        trprcd.Status = "Fixable";
                                    }
                                    break;//up to 10 minutes late
                            }
                            if (scheddotime.ToString().Contains("00:00"))
                            {
                                trprcd.SuggestedDOTime = trprcd.DOTime;
                                continue;
                            }
                            switch (dotimediff)
                            {
                                case 0://times are same
                                    trprcd.SuggestedDOTime = trprcd.DOTime;
                                    break;
                                case -1://driver is early
                                    if ((scheddotime - driverdotime).TotalMinutes <= 15)//driver time is good
                                    {
                                        trprcd.SuggestedDOTime = trprcd.DOTime;
                                    }
                                    else
                                    {
                                        trprcd.Alerts = trprcd.Alerts + " EDO";
                                        rInt = r.Next(0, 15);
                                        scheddotime = scheddotime - new TimeSpan(0, rInt, 0);
                                        trprcd.SuggestedDOTime = scheddotime.ToString("HH:mm");
                                        trprcd.Status = "Fixable";
                                    }
                                    break;//up to 15 minutes early
                                case 1://driver is late
                                    trprcd.Alerts = trprcd.Alerts + " LDO";
                                    rInt = r.Next(0, 15);
                                    scheddotime = scheddotime - new TimeSpan(0, rInt, 0);
                                    trprcd.SuggestedDOTime = scheddotime.ToString("HH:mm");
                                    trprcd.Status = "Fixable";
                                    break;//cant be late
                            }
                            continue;
                        }
                        if ((scheddotime - schedputime).TotalMinutes >= 15 & (scheddotime - schedputime).TotalMinutes < 30)
                        {
                            switch (putimediff)
                            {
                                case 0://times are same
                                    trprcd.SuggestedPUTime = trprcd.PUTime;
                                    break;
                                case -1://driver is early
                                    trprcd.Alerts = trprcd.Alerts + " EPU";
                                    rInt = r.Next(0, 5);
                                    schedputime = schedputime + new TimeSpan(0, rInt, 0);
                                    trprcd.SuggestedPUTime = schedputime.ToString("HH:mm");
                                    trprcd.Status = "Fixable";
                                    break; //up to 30 minutes early
                                case 1://driver is late
                                    if (McTripTimingRules.PuLateMinutesOk(trprcd.Trip, driverputime, schedputime))//driver time is good
                                    {
                                        trprcd.SuggestedPUTime = trprcd.PUTime;
                                    }
                                    else
                                    {
                                        trprcd.Alerts = trprcd.Alerts + " LPU";
                                        rInt = r.Next(0, 5);
                                        schedputime = schedputime + new TimeSpan(0, rInt, 0);
                                        trprcd.SuggestedPUTime = schedputime.ToString("HH:mm");
                                        trprcd.Status = "Fixable";
                                    }
                                    break;//up to 5 minutes late
                            }
                            if (scheddotime.ToString().Contains("00:00"))
                            {
                                trprcd.SuggestedDOTime = trprcd.DOTime;
                                continue;
                            }
                            switch (dotimediff)
                            {
                                case 0://times are same
                                    trprcd.SuggestedDOTime = trprcd.DOTime;
                                    break;
                                case -1://driver is early
                                    if ((scheddotime - driverdotime).TotalMinutes <= 5)//driver time is good
                                    {
                                        trprcd.SuggestedDOTime = trprcd.DOTime;
                                    }
                                    else
                                    {
                                        trprcd.Alerts = trprcd.Alerts + " EDO";
                                        rInt = r.Next(0, 5);
                                        scheddotime = scheddotime - new TimeSpan(0, rInt, 0);
                                        trprcd.SuggestedDOTime = scheddotime.ToString("HH:mm");
                                        trprcd.Status = "Fixable";
                                    }
                                    break;//up to 5 minutes early
                                case 1://driver is late
                                    trprcd.Alerts = trprcd.Alerts + " LDO";
                                    rInt = r.Next(0, 5);
                                    scheddotime = scheddotime - new TimeSpan(0, rInt, 0);
                                    trprcd.SuggestedDOTime = scheddotime.ToString("HH:mm");
                                    trprcd.Status = "Fixable";

                                    break;//cant be late
                            }
                            continue;
                        }
                        if ((scheddotime - schedputime).TotalMinutes < 15)
                        {
                            switch (putimediff)
                            {
                                case 0://times are same
                                    trprcd.SuggestedPUTime = trprcd.PUTime;
                                    break;
                                case -1://driver is early
                                    trprcd.Alerts = trprcd.Alerts + " EPU";
                                    trprcd.SuggestedPUTime = schedputime.ToString("HH:mm");
                                    trprcd.Status = "Fixable";
                                    break;
                                case 1://driver is late
                                    trprcd.Alerts = trprcd.Alerts + " LPU";
                                    trprcd.SuggestedPUTime = schedputime.ToString("HH:mm");
                                    trprcd.Status = "Fixable";
                                    break;//cant be late
                            }
                            if (scheddotime.ToString().Contains("00:00"))
                            {
                                trprcd.SuggestedDOTime = trprcd.DOTime;
                                continue;
                            }
                            switch (dotimediff)
                            {
                                case 0://times are same
                                    trprcd.SuggestedDOTime = trprcd.DOTime;
                                    break;
                                case -1://driver is early
                                    trprcd.Alerts = trprcd.Alerts + " EDO";
                                    trprcd.SuggestedDOTime = scheddotime.ToString("HH:mm");
                                    trprcd.Status = "Fixable";
                                    break;
                                case 1://driver is late
                                    trprcd.Alerts = trprcd.Alerts + " LDO";
                                    trprcd.SuggestedDOTime = scheddotime.ToString("HH:mm");
                                    trprcd.Status = "Fixable";
                                    break;//cant be late
                            }
                            continue;
                        }
                    }

                }
            }








        }
        internal static bool ParseModivcarePortalErrorFlag(string tripHtmlFragment)
        {
            if (string.IsNullOrEmpty(tripHtmlFragment))
                return false;
            if (tripHtmlFragment.IndexOf("color:Red", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (tripHtmlFragment.IndexOf("ForeColor=Red", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (tripHtmlFragment.IndexOf("color=\"Red\"", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            return tripHtmlFragment.IndexOf("Red", StringComparison.Ordinal) >= 0;
        }

        private bool ShouldApplyCorrectionsToTrip(MCBatchTripRecord trip)
        {
            if (LoadMode != TimeCorrectionLoadMode.ModivcareRedOnly)
                return true;
            return trip != null && trip.TripErrors;
        }

        /// <summary>Lenient and data-only modes handle blue rows lightly; portal-red rows get standard scoreboard in a second pass.</summary>
        private bool UsesPortalRedStandardOverlay() =>
            LoadMode == TimeCorrectionLoadMode.Lenient || LoadMode == TimeCorrectionLoadMode.DataOnly;

        private bool ShouldDeferPortalRedToStandardPass(MCBatchTripRecord trip) =>
            UsesPortalRedStandardOverlay() && trip != null && trip.TripErrors;

        private void ApplyPortalRedStandardPass(List<MCBatchTripRecord> batchtrips, MCBatchAdditionalInfo addinfo) =>
            RevampedCorrectModivcareTimes(batchtrips, addinfo, onlyPortalRedTrips: true);

        /// <summary>Any row Modivcare still shows in red must stay in the fixable list for Execute.</summary>
        private static void EnsurePortalRedTripsAreFixable(List<MCBatchTripRecord> batchtrips)
        {
            foreach (MCBatchTripRecord tr in batchtrips)
            {
                if (tr == null || !tr.TripErrors)
                    continue;
                AppendTripAlert(tr, "RED");
                tr.Status = "Fixable";
            }
        }

        private static void LeaveTripUnchangedByTimeCorrection(MCBatchTripRecord trprcd)
        {
            trprcd.Alerts = null;
            trprcd.Status = null;
            if (TryParseBatchTime(trprcd.PUTime, out _) && TryParseBatchTime(trprcd.DOTime, out _))
            {
                trprcd.SuggestedPUTime = trprcd.PUTime;
                trprcd.SuggestedDOTime = trprcd.DOTime;
                trprcd.Status = "Passed";
            }
        }

        private void RevampedCorrectModivcareTimes(List<MCBatchTripRecord> batchtrips, MCBatchAdditionalInfo dledtripsaddinfo,
            bool onlyPortalRedTrips = false)
        {
            Random r = new Random();
            int rInt;

            foreach (MCBatchTripRecord trprcd in batchtrips)
            {
                if (trprcd.Date == dledtripsaddinfo.MCBatchDate)
                {
                    if (onlyPortalRedTrips)
                    {
                        if (!trprcd.TripErrors)
                            continue;
                    }
                    else if (!ShouldApplyCorrectionsToTrip(trprcd))
                    {
                        LeaveTripUnchangedByTimeCorrection(trprcd);
                        continue;
                    }
                    else if (ShouldDeferPortalRedToStandardPass(trprcd))
                    {
                        continue;
                    }

                    if (!TryParseBatchTime(trprcd.ScheduledPUTime, out DateTime schedputime) ||
                        !TryParseBatchTime(trprcd.ScheduledDOTime, out DateTime scheddotime) ||
                        !TryParseBatchTime(trprcd.PUTime, out DateTime driverputime) ||
                        !TryParseBatchTime(trprcd.DOTime, out DateTime driverdotime))
                    {
                        trprcd.Alerts = (trprcd.Alerts ?? "") + " SCH";
                        continue;
                    }

                    decimal miles = 0;
                    if (dledtripsaddinfo.mcDownloadedTrips != null && dledtripsaddinfo.mcDownloadedTrips.Any())
                    {
                        foreach (MCDownloadedTrip mcdledtrip in dledtripsaddinfo.mcDownloadedTrips)
                        {
                            if (mcdledtrip.TripNumber.Replace(" ", "") == trprcd.Trip.Replace(" ", ""))
                            {
                                miles = Convert.ToDecimal(mcdledtrip.Miles);
                            }
                        }
                    }

                    double milemins = (int)(5 + miles);

                    if (!trprcd.RiderCallTime.Contains("nbsp") &&
                        TryParseBatchTime(trprcd.RiderCallTime, out DateTime riderCallPu))
                    {
                        ApplyRiderCallTimeCorrections(trprcd, riderCallPu, driverputime, driverdotime, miles);
                        continue;
                    }
                        
                    int putimediff = DateTime.Compare(driverputime, schedputime);
                    int dotimediff = DateTime.Compare(driverdotime, scheddotime);

                    if (trprcd.Trip.Contains("A"))
                    {
                        if ((scheddotime - schedputime).TotalMinutes >= 60)
                        {
                            switch (putimediff)
                            {
                                case 0://times are same
                                    trprcd.SuggestedPUTime = trprcd.PUTime;
                                    break;
                                case -1://driver is early
                                    if (McTripTimingRules.PuEarlyMinutesOk(trprcd.Trip, driverputime, schedputime))//driver time is good
                                    {
                                        trprcd.SuggestedPUTime = trprcd.PUTime;
                                    }
                                    else
                                    {
                                        trprcd.Alerts = trprcd.Alerts + " EPU";
                                        rInt = r.Next(0, 30);
                                        schedputime = schedputime - new TimeSpan(0, rInt, 0);
                                        trprcd.SuggestedPUTime = schedputime.ToString("HH:mm");
                                        trprcd.Status = "Fixable";
                                    }
                                    break; //up to 30 minutes early
                                case 1://driver is late
                                    if (McTripTimingRules.PuLateMinutesOk(trprcd.Trip, driverputime, schedputime))//driver time is good
                                    {
                                        trprcd.SuggestedPUTime = trprcd.PUTime;
                                    }
                                    else
                                    {
                                        trprcd.Alerts = trprcd.Alerts + " LPU";
                                        rInt = r.Next(0, 15);
                                        schedputime = schedputime + new TimeSpan(0, rInt, 0);
                                        trprcd.SuggestedPUTime = schedputime.ToString("HH:mm");
                                        trprcd.Status = "Fixable";
                                    }
                                    break;//up to 15 minutes late
                            }
                            switch (dotimediff)
                            {
                                case 0://times are same
                                    trprcd.SuggestedDOTime = trprcd.DOTime;
                                    break;
                                case -1://driver is early
                                    if ((scheddotime - driverdotime).TotalMinutes <= 30)//driver time is good
                                    {
                                        trprcd.SuggestedDOTime = trprcd.DOTime;
                                    }
                                    else
                                    {
                                        trprcd.Alerts = trprcd.Alerts + " EDO";
                                        rInt = r.Next(0, 30);
                                        scheddotime = scheddotime - new TimeSpan(0, rInt, 0);
                                        trprcd.SuggestedDOTime = scheddotime.ToString("HH:mm");
                                        trprcd.Status = "Fixable";
                                    }
                                    break;//up to 30 minutes early
                                case 1://driver is late
                                    trprcd.Alerts = trprcd.Alerts + " LDO";
                                    rInt = r.Next(0, 30);
                                    scheddotime = scheddotime - new TimeSpan(0, rInt, 0);
                                    trprcd.SuggestedDOTime = scheddotime.ToString("HH:mm");
                                    trprcd.Status = "Fixable";
                                    break;//cant be late
                            }
                            continue;
                        }
                        if ((scheddotime - schedputime).TotalMinutes >= 45 & (scheddotime - schedputime).TotalMinutes < 60)
                        {
                            switch (putimediff)
                            {
                                case 0://times are same
                                    trprcd.SuggestedPUTime = trprcd.PUTime;
                                    break;
                                case -1://driver is early
                                    if (McTripTimingRules.PuEarlyMinutesOk(trprcd.Trip, driverputime, schedputime))//driver time is good
                                    {
                                        trprcd.SuggestedPUTime = trprcd.PUTime;
                                    }
                                    else
                                    {
                                        trprcd.Alerts = trprcd.Alerts + " EPU";
                                        rInt = r.Next(0, 30);
                                        schedputime = schedputime - new TimeSpan(0, rInt, 0);
                                        trprcd.SuggestedPUTime = schedputime.ToString("HH:mm");
                                        trprcd.Status = "Fixable";
                                    }
                                    break; //up to 30 minutes early
                                case 1://driver is late
                                    if (McTripTimingRules.PuLateMinutesOk(trprcd.Trip, driverputime, schedputime))//driver time is good
                                    {
                                        trprcd.SuggestedPUTime = trprcd.PUTime;
                                    }
                                    else
                                    {
                                        trprcd.Alerts = trprcd.Alerts + " LPU";
                                        rInt = r.Next(0, 15);
                                        schedputime = schedputime + new TimeSpan(0, rInt, 0);
                                        trprcd.SuggestedPUTime = schedputime.ToString("HH:mm");
                                        trprcd.Status = "Fixable";
                                    }
                                    break;//up to 10 minutes late
                            }
                            switch (dotimediff)
                            {
                                case 0://times are same
                                    trprcd.SuggestedDOTime = trprcd.DOTime;
                                    break;
                                case -1://driver is early
                                    if ((scheddotime - driverdotime).TotalMinutes <= 20)//driver time is good
                                    {
                                        trprcd.SuggestedDOTime = trprcd.DOTime;
                                    }
                                    else
                                    {
                                        trprcd.Alerts = trprcd.Alerts + " EDO";
                                        rInt = r.Next(0, 20);
                                        scheddotime = scheddotime - new TimeSpan(0, rInt, 0);
                                        trprcd.SuggestedDOTime = scheddotime.ToString("HH:mm");
                                        trprcd.Status = "Fixable";
                                    }
                                    break;//up to 20 minutes early
                                case 1://driver is late
                                    trprcd.Alerts = trprcd.Alerts + " LDO";
                                    rInt = r.Next(0, 20);
                                    scheddotime = scheddotime - new TimeSpan(0, rInt, 0);
                                    trprcd.SuggestedDOTime = scheddotime.ToString("HH:mm");
                                    trprcd.Status = "Fixable";
                                    break;//cant be late
                            }
                            continue;
                        }
                        if ((scheddotime - schedputime).TotalMinutes >= 30 & (scheddotime - schedputime).TotalMinutes < 45)
                        {
                            switch (putimediff)
                            {
                                case 0://times are same
                                    trprcd.SuggestedPUTime = trprcd.PUTime;
                                    break;
                                case -1://driver is early
                                    if (McTripTimingRules.PuEarlyMinutesOk(trprcd.Trip, driverputime, schedputime))//driver time is good
                                    {
                                        trprcd.SuggestedPUTime = trprcd.PUTime;
                                    }
                                    else
                                    {
                                        trprcd.Alerts = trprcd.Alerts + " EPU";
                                        rInt = r.Next(0, 30);
                                        schedputime = schedputime - new TimeSpan(0, rInt, 0);
                                        trprcd.SuggestedPUTime = schedputime.ToString("HH:mm");
                                        trprcd.Status = "Fixable";
                                    }
                                    break; //up to 30 minutes early
                                case 1://driver is late
                                    if (McTripTimingRules.PuLateMinutesOk(trprcd.Trip, driverputime, schedputime))//driver time is good
                                    {
                                        trprcd.SuggestedPUTime = trprcd.PUTime;
                                    }
                                    else
                                    {
                                        trprcd.Alerts = trprcd.Alerts + " LPU";
                                        rInt = r.Next(0, 10);
                                        schedputime = schedputime + new TimeSpan(0, rInt, 0);
                                        trprcd.SuggestedPUTime = schedputime.ToString("HH:mm");
                                        trprcd.Status = "Fixable";
                                    }
                                    break;//up to 10 minutes late
                            }
                            switch (dotimediff)
                            {
                                case 0://times are same
                                    trprcd.SuggestedDOTime = trprcd.DOTime;
                                    break;
                                case -1://driver is early
                                    if ((scheddotime - driverdotime).TotalMinutes <= 15)//driver time is good
                                    {
                                        trprcd.SuggestedDOTime = trprcd.DOTime;
                                    }
                                    else
                                    {
                                        trprcd.Alerts = trprcd.Alerts + " EDO";
                                        rInt = r.Next(0, 15);
                                        scheddotime = scheddotime - new TimeSpan(0, rInt, 0);
                                        trprcd.SuggestedDOTime = scheddotime.ToString("HH:mm");
                                        trprcd.Status = "Fixable";
                                    }
                                    break;//up to 15 minutes early
                                case 1://driver is late
                                    trprcd.Alerts = trprcd.Alerts + " LDO";
                                    rInt = r.Next(0, 15);
                                    scheddotime = scheddotime - new TimeSpan(0, rInt, 0);
                                    trprcd.SuggestedDOTime = scheddotime.ToString("HH:mm");
                                    trprcd.Status = "Fixable";
                                    break;//cant be late
                            }
                            continue;
                        }
                        if ((scheddotime - schedputime).TotalMinutes >= 15 & (scheddotime - schedputime).TotalMinutes < 30)
                        {
                            switch (putimediff)
                            {
                                case 0://times are same
                                    trprcd.SuggestedPUTime = trprcd.PUTime;
                                    break;
                                case -1://driver is early
                                    if (McTripTimingRules.PuEarlyMinutesOk(trprcd.Trip, driverputime, schedputime))//driver time is good
                                    {
                                        trprcd.SuggestedPUTime = trprcd.PUTime;
                                    }
                                    else
                                    {
                                        trprcd.Alerts = trprcd.Alerts + " EPU";
                                        rInt = r.Next(0, 30);
                                        schedputime = schedputime - new TimeSpan(0, rInt, 0);
                                        trprcd.SuggestedPUTime = schedputime.ToString("HH:mm");
                                        trprcd.Status = "Fixable";
                                    }
                                    break; //up to 30 minutes early
                                case 1://driver is late
                                    if (McTripTimingRules.PuLateMinutesOk(trprcd.Trip, driverputime, schedputime))//driver time is good
                                    {
                                        trprcd.SuggestedPUTime = trprcd.PUTime;
                                    }
                                    else
                                    {
                                        trprcd.Alerts = trprcd.Alerts + " LPU";
                                        rInt = r.Next(0, 5);
                                        schedputime = schedputime + new TimeSpan(0, rInt, 0);
                                        trprcd.SuggestedPUTime = schedputime.ToString("HH:mm");
                                        trprcd.Status = "Fixable";
                                    }
                                    break;//up to 5 minutes late
                            }
                            switch (dotimediff)
                            {
                                case 0://times are same
                                    trprcd.SuggestedDOTime = trprcd.DOTime;
                                    break;
                                case -1://driver is early
                                    if ((scheddotime - driverdotime).TotalMinutes <= 5)//driver time is good
                                    {
                                        trprcd.SuggestedDOTime = trprcd.DOTime;
                                    }
                                    else
                                    {
                                        trprcd.Alerts = trprcd.Alerts + " EDO";
                                        rInt = r.Next(0, 5);
                                        scheddotime = scheddotime - new TimeSpan(0, rInt, 0);
                                        trprcd.SuggestedDOTime = scheddotime.ToString("HH:mm");
                                        trprcd.Status = "Fixable";
                                    }
                                    break;//up to 5 minutes early
                                case 1://driver is late
                                    trprcd.Alerts = trprcd.Alerts + " LDO";
                                    rInt = r.Next(0, 5);
                                    scheddotime = scheddotime - new TimeSpan(0, rInt, 0);
                                    trprcd.SuggestedDOTime = scheddotime.ToString("HH:mm");
                                    trprcd.Status = "Fixable";

                                    break;//cant be late
                            }
                            continue;
                        }
                        if ((scheddotime - schedputime).TotalMinutes < 15)
                        {
                            switch (putimediff)
                            {
                                case 0://times are same
                                    trprcd.SuggestedPUTime = trprcd.PUTime;
                                    break;
                                case -1://driver is early
                                    if (McTripTimingRules.PuEarlyMinutesOk(trprcd.Trip, driverputime, schedputime))//driver time is good
                                    {
                                        trprcd.SuggestedPUTime = trprcd.PUTime;
                                    }
                                    else
                                    {
                                        trprcd.Alerts = trprcd.Alerts + " EPU";
                                        rInt = r.Next(0, 30);
                                        schedputime = schedputime - new TimeSpan(0, rInt, 0);
                                        trprcd.SuggestedPUTime = schedputime.ToString("HH:mm");
                                        trprcd.Status = "Fixable";
                                    }
                                    break; //up to 30 minutes early
                                case 1://driver is late
                                    if (McTripTimingRules.PuLateMinutesOk(trprcd.Trip, driverputime, schedputime))
                                        trprcd.SuggestedPUTime = trprcd.PUTime;
                                    else
                                    {
                                        trprcd.Alerts = trprcd.Alerts + " LPU";
                                        rInt = r.Next(0, McTripTimingRules.RandomLatePuCap(trprcd.Trip) + 1);
                                        schedputime = schedputime + new TimeSpan(0, rInt, 0);
                                        trprcd.SuggestedPUTime = schedputime.ToString("HH:mm");
                                        trprcd.Status = "Fixable";
                                    }
                                    break;
                            }
                            switch (dotimediff)
                            {
                                case 0://times are same
                                    trprcd.SuggestedDOTime = trprcd.DOTime;
                                    break;
                                case -1://driver is early
                                    trprcd.Alerts = trprcd.Alerts + " EDO";
                                    trprcd.SuggestedDOTime = scheddotime.ToString("HH:mm");
                                    trprcd.Status = "Fixable";
                                    break;//up to 5 minutes early
                                case 1://driver is late
                                    trprcd.Alerts = trprcd.Alerts + " LDO";
                                    trprcd.SuggestedDOTime = scheddotime.ToString("HH:mm");
                                    trprcd.Status = "Fixable";
                                    break;//cant be late
                            }
                            continue;
                        }
                    }
                    else
                    {











                        bool nullputime = false;

                        if (schedputime.TimeOfDay.Ticks == 0)
                        {
                            if (dledtripsaddinfo.mcDownloadedTrips != null)
                            {
                                foreach (MCDownloadedTrip mc in dledtripsaddinfo.mcDownloadedTrips)
                                {
                                    if (!TripNumbersMatch(trprcd.Trip, mc.TripNumber))
                                        continue;
                                    if (MCTimeCorrection.TryParseBatchTime(mc.PUTime, out DateTime mcPu))
                                        schedputime = mcPu;
                                    if (MCTimeCorrection.TryParseBatchTime(mc.DOTime, out DateTime mcDo))
                                        scheddotime = mcDo;
                                    trprcd.Alerts = (trprcd.Alerts ?? "") + " LPU";
                                    trprcd.SuggestedPUTime = schedputime.ToString("HH:mm");
                                    trprcd.SuggestedDOTime = scheddotime.ToString("HH:mm");
                                    nullputime = true;
                                    trprcd.Status = "Fixable";
                                    break;
                                }
                            }
                        }
                        
                        if (nullputime)
                        {
                            continue;
                        }
                        










                            if (scheddotime.TimeOfDay.Ticks == 0)
                            {
                                switch (putimediff)
                                {
                                    case 0://times are same
                                        trprcd.SuggestedPUTime = trprcd.PUTime;
                                        break;
                                    case -1://driver is early. cant be early.
                                        trprcd.Alerts = trprcd.Alerts + " EPU";
                                        rInt = r.Next(0, 5);
                                        schedputime = schedputime + new TimeSpan(0, rInt, 0);
                                        trprcd.SuggestedPUTime = schedputime.ToString("HH:mm");
                                        trprcd.Status = "Fixable";
                                        break;
                                    case 1://driver is late
                                        if (McTripTimingRules.PuLateMinutesOk(trprcd.Trip, driverputime, schedputime))//driver time is good
                                        {
                                            trprcd.SuggestedPUTime = trprcd.PUTime;
                                            schedputime = driverputime;

                                        }
                                        else
                                        {
                                        trprcd.Alerts = trprcd.Alerts + " LPU";
                                        rInt = r.Next(0, 30);
                                        schedputime = schedputime + new TimeSpan(0, rInt, 0);
                                        trprcd.SuggestedPUTime = schedputime.ToString("HH:mm");
                                        trprcd.Status = "Fixable";
                                    }
                                        break;//up to 30 minutes late
                                }

                            double triptotalmins = (driverdotime - schedputime).TotalMinutes;
                            if (triptotalmins == milemins)
                            {
                                trprcd.SuggestedDOTime = trprcd.DOTime;
                            }

                            if (triptotalmins > milemins & triptotalmins <= milemins + 30)
                            {
                                trprcd.SuggestedDOTime = trprcd.DOTime;
                            }

                            if (triptotalmins < milemins)
                            {
                                trprcd.Alerts = trprcd.Alerts + " EDO";
                                scheddotime = schedputime + new TimeSpan(0, (int)milemins, 0);
                                trprcd.SuggestedDOTime = scheddotime.ToString("HH:mm");
                                trprcd.Status = "Fixable";
                              
                            }

                            if (triptotalmins > milemins + 30)
                            {
                                trprcd.Alerts = trprcd.Alerts + " LDO";
                                scheddotime = schedputime + new TimeSpan(0, (int)milemins, 0);
                                trprcd.SuggestedDOTime = scheddotime.ToString("HH:mm");
                                trprcd.Status = "Fixable";
                               
                            }

                            continue;

                        }

                            if ((scheddotime - schedputime).TotalMinutes >= 60)
                            {
                                switch (putimediff)
                                {
                                    case 0://times are same
                                        trprcd.SuggestedPUTime = trprcd.PUTime;
                                        break;
                                    case -1://driver is early
                                        trprcd.Alerts = trprcd.Alerts + " EPU";
                                        rInt = r.Next(0, 30);
                                        schedputime = schedputime + new TimeSpan(0, rInt, 0);
                                        trprcd.SuggestedPUTime = schedputime.ToString("HH:mm");
                                        trprcd.Status = "Fixable";
                                        break; //up to 30 minutes late
                                    case 1://driver is late
                                        if (McTripTimingRules.PuLateMinutesOk(trprcd.Trip, driverputime, schedputime))//driver time is good
                                        {
                                            trprcd.SuggestedPUTime = trprcd.PUTime;
                                        }
                                        else
                                        {
                                            trprcd.Alerts = trprcd.Alerts + " LPU";
                                            rInt = r.Next(0, 25);
                                            schedputime = schedputime + new TimeSpan(0, rInt, 0);
                                            trprcd.SuggestedPUTime = schedputime.ToString("HH:mm");
                                            trprcd.Status = "Fixable";
                                        }
                                        break;//up to 25 minutes late
                                }
                                switch (dotimediff)
                                {
                                    case 0://times are same
                                        trprcd.SuggestedDOTime = trprcd.DOTime;
                                        break;
                                    case -1://driver is early
                                        if ((scheddotime - driverdotime).TotalMinutes <= 25)//driver time is good
                                        {
                                            trprcd.SuggestedDOTime = trprcd.DOTime;
                                        }
                                        else
                                        {
                                            trprcd.Alerts = trprcd.Alerts + " EDO";
                                            rInt = r.Next(0, 25);
                                            scheddotime = scheddotime - new TimeSpan(0, rInt, 0);
                                            trprcd.SuggestedDOTime = scheddotime.ToString("HH:mm");
                                            trprcd.Status = "Fixable";
                                        }
                                        break;//up to 25 minutes early
                                    case 1://driver is late
                                        trprcd.Alerts = trprcd.Alerts + " LDO";
                                        rInt = r.Next(0, 25);
                                        scheddotime = scheddotime - new TimeSpan(0, rInt, 0);
                                        trprcd.SuggestedDOTime = scheddotime.ToString("HH:mm");
                                        trprcd.Status = "Fixable";
                                        break;//cant be late
                                }
                                continue;
                            }
                            if ((scheddotime - schedputime).TotalMinutes >= 45 & (scheddotime - schedputime).TotalMinutes < 60)
                            {
                                switch (putimediff)
                                {
                                    case 0://times are same
                                        trprcd.SuggestedPUTime = trprcd.PUTime;
                                        break;
                                    case -1://driver is early
                                        trprcd.Alerts = trprcd.Alerts + " EPU";
                                        rInt = r.Next(0, 20);
                                        schedputime = schedputime + new TimeSpan(0, rInt, 0);
                                        trprcd.SuggestedPUTime = schedputime.ToString("HH:mm");
                                        trprcd.Status = "Fixable";
                                        break; //cant be early
                                    case 1://driver is late
                                        if (McTripTimingRules.PuLateMinutesOk(trprcd.Trip, driverputime, schedputime))//driver time is good
                                        {
                                            trprcd.SuggestedPUTime = trprcd.PUTime;
                                        }
                                        else
                                        {
                                            trprcd.Alerts = trprcd.Alerts + " LPU";
                                            rInt = r.Next(0, 20);
                                            schedputime = schedputime + new TimeSpan(0, rInt, 0);
                                            trprcd.SuggestedPUTime = schedputime.ToString("HH:mm");
                                            trprcd.Status = "Fixable";
                                        }
                                        break;//up to 20 minutes late
                                }
                                switch (dotimediff)
                                {
                                    case 0://times are same
                                        trprcd.SuggestedDOTime = trprcd.DOTime;
                                        break;
                                    case -1://driver is early
                                        if ((scheddotime - driverdotime).TotalMinutes <= 20)//driver time is good
                                        {
                                            trprcd.SuggestedDOTime = trprcd.DOTime;
                                        }
                                        else
                                        {
                                            trprcd.Alerts = trprcd.Alerts + " EDO";
                                            rInt = r.Next(0, 20);
                                            scheddotime = scheddotime - new TimeSpan(0, rInt, 0);
                                            trprcd.SuggestedDOTime = scheddotime.ToString("HH:mm");
                                            trprcd.Status = "Fixable";
                                        }
                                        break;//up to 20 minutes early
                                    case 1://driver is late
                                        trprcd.Alerts = trprcd.Alerts + " LDO";
                                        rInt = r.Next(0, 20);
                                        scheddotime = scheddotime - new TimeSpan(0, rInt, 0);
                                        trprcd.SuggestedDOTime = scheddotime.ToString("HH:mm");
                                        trprcd.Status = "Fixable";
                                        break;//cant be late
                                }
                                continue;
                            }
                            if ((scheddotime - schedputime).TotalMinutes >= 30 & (scheddotime - schedputime).TotalMinutes < 45)
                            {
                                switch (putimediff)
                                {
                                    case 0://times are same
                                        trprcd.SuggestedPUTime = trprcd.PUTime;
                                        break;
                                    case -1://driver is early
                                        trprcd.Alerts = trprcd.Alerts + " EPU";
                                        rInt = r.Next(0, 10);
                                        schedputime = schedputime + new TimeSpan(0, rInt, 0);
                                        trprcd.SuggestedPUTime = schedputime.ToString("HH:mm");
                                        trprcd.Status = "Fixable";
                                        break; //up to 10 minutes late
                                    case 1://driver is late
                                        if (McTripTimingRules.PuLateMinutesOk(trprcd.Trip, driverputime, schedputime))//driver time is good
                                        {
                                            trprcd.SuggestedPUTime = trprcd.PUTime;
                                        }
                                        else
                                        {
                                            trprcd.Alerts = trprcd.Alerts + " LPU";
                                            rInt = r.Next(0, 10);
                                            schedputime = schedputime + new TimeSpan(0, rInt, 0);
                                            trprcd.SuggestedPUTime = schedputime.ToString("HH:mm");
                                            trprcd.Status = "Fixable";
                                        }
                                        break;//up to 10 minutes late
                                }
                                switch (dotimediff)
                                {
                                    case 0://times are same
                                        trprcd.SuggestedDOTime = trprcd.DOTime;
                                        break;
                                    case -1://driver is early
                                        if ((scheddotime - driverdotime).TotalMinutes <= 15)//driver time is good
                                        {
                                            trprcd.SuggestedDOTime = trprcd.DOTime;
                                        }
                                        else
                                        {
                                            trprcd.Alerts = trprcd.Alerts + " EDO";
                                            rInt = r.Next(0, 15);
                                            scheddotime = scheddotime - new TimeSpan(0, rInt, 0);
                                            trprcd.SuggestedDOTime = scheddotime.ToString("HH:mm");
                                            trprcd.Status = "Fixable";
                                        }
                                        break;//up to 15 minutes early
                                    case 1://driver is late
                                        trprcd.Alerts = trprcd.Alerts + " LDO";
                                        rInt = r.Next(0, 15);
                                        scheddotime = scheddotime - new TimeSpan(0, rInt, 0);
                                        trprcd.SuggestedDOTime = scheddotime.ToString("HH:mm");
                                        trprcd.Status = "Fixable";
                                        break;//cant be late
                                }
                                continue;
                            }
                            if ((scheddotime - schedputime).TotalMinutes >= 15 & (scheddotime - schedputime).TotalMinutes < 30)
                            {
                                switch (putimediff)
                                {
                                    case 0://times are same
                                        trprcd.SuggestedPUTime = trprcd.PUTime;
                                        break;
                                    case -1://driver is early
                                        trprcd.Alerts = trprcd.Alerts + " EPU";
                                        rInt = r.Next(0, 5);
                                        schedputime = schedputime + new TimeSpan(0, rInt, 0);
                                        trprcd.SuggestedPUTime = schedputime.ToString("HH:mm");
                                        trprcd.Status = "Fixable";
                                        break; //up to 30 minutes early
                                    case 1://driver is late
                                        if (McTripTimingRules.PuLateMinutesOk(trprcd.Trip, driverputime, schedputime))//driver time is good
                                        {
                                            trprcd.SuggestedPUTime = trprcd.PUTime;
                                        }
                                        else
                                        {
                                            trprcd.Alerts = trprcd.Alerts + " LPU";
                                            rInt = r.Next(0, 5);
                                            schedputime = schedputime + new TimeSpan(0, rInt, 0);
                                            trprcd.SuggestedPUTime = schedputime.ToString("HH:mm");
                                            trprcd.Status = "Fixable";
                                        }
                                        break;//up to 5 minutes late
                                }
                                switch (dotimediff)
                                {
                                    case 0://times are same
                                        trprcd.SuggestedDOTime = trprcd.DOTime;
                                        break;
                                    case -1://driver is early
                                        if ((scheddotime - driverdotime).TotalMinutes <= 5)//driver time is good
                                        {
                                            trprcd.SuggestedDOTime = trprcd.DOTime;
                                        }
                                        else
                                        {
                                            trprcd.Alerts = trprcd.Alerts + " EDO";
                                            rInt = r.Next(0, 5);
                                            scheddotime = scheddotime - new TimeSpan(0, rInt, 0);
                                            trprcd.SuggestedDOTime = scheddotime.ToString("HH:mm");
                                            trprcd.Status = "Fixable";
                                        }
                                        break;//up to 5 minutes early
                                    case 1://driver is late
                                        trprcd.Alerts = trprcd.Alerts + " LDO";
                                        rInt = r.Next(0, 5);
                                        scheddotime = scheddotime - new TimeSpan(0, rInt, 0);
                                        trprcd.SuggestedDOTime = scheddotime.ToString("HH:mm");
                                        trprcd.Status = "Fixable";

                                        break;//cant be late
                                }
                                continue;
                            }
                            if ((scheddotime - schedputime).TotalMinutes < 15)
                            {
                                switch (putimediff)
                                {
                                    case 0://times are same
                                        trprcd.SuggestedPUTime = trprcd.PUTime;
                                        break;
                                    case -1://driver is early
                                        trprcd.Alerts = trprcd.Alerts + " EPU";
                                        trprcd.SuggestedPUTime = schedputime.ToString("HH:mm");
                                        trprcd.Status = "Fixable";
                                        break;
                                    case 1://driver is late
                                        trprcd.Alerts = trprcd.Alerts + " LPU";
                                        trprcd.SuggestedPUTime = schedputime.ToString("HH:mm");
                                        trprcd.Status = "Fixable";
                                        break;//cant be late
                                }
                                switch (dotimediff)
                                {
                                    case 0://times are same
                                        trprcd.SuggestedDOTime = trprcd.DOTime;
                                        break;
                                    case -1://driver is early
                                        trprcd.Alerts = trprcd.Alerts + " EDO";
                                        trprcd.SuggestedDOTime = scheddotime.ToString("HH:mm");
                                        trprcd.Status = "Fixable";
                                        break;
                                    case 1://driver is late
                                        trprcd.Alerts = trprcd.Alerts + " LDO";
                                        trprcd.SuggestedDOTime = scheddotime.ToString("HH:mm");
                                        trprcd.Status = "Fixable";
                                        break;//cant be late
                                }
                                continue;
                            }
                        }
                    



                    /*
                    if (trprcd.Trip.Contains("C"))
                    {
                        double triptotalmins = (driverdotime - driverputime).TotalMinutes;
                        if (scheddotime.TimeOfDay.Ticks == 0)
                        {
                            switch (putimediff)
                            {
                                case 0://times are same
                                    trprcd.SuggestedPUTime = trprcd.PUTime;
                                    break;
                                case -1://driver is early. cant be early.
                                    trprcd.Alerts = trprcd.Alerts + " EPU";
                                    rInt = r.Next(0, 5);
                                    schedputime = schedputime + new TimeSpan(0, rInt, 0);
                                    trprcd.SuggestedPUTime = schedputime.ToString("HH:mm");
                                    trprcd.Status = "Fixable";
                                    break;
                                case 1://driver is late
                                    if (McTripTimingRules.PuLateMinutesOk(trprcd.Trip, driverputime, schedputime))//driver time is good
                                    {
                                        trprcd.SuggestedPUTime = trprcd.PUTime;
                                        schedputime = driverputime;
                                    }
                                    else
                                    {
                                        trprcd.Alerts = trprcd.Alerts + " LPU";
                                        rInt = r.Next(0, 30);
                                        schedputime = schedputime + new TimeSpan(0, rInt, 0);
                                        trprcd.SuggestedPUTime = schedputime.ToString("HH:mm");
                                        trprcd.Status = "Fixable";
                                    }
                                    break;//up to 30 minutes late
                            }


                            if ((driverdotime - schedputime).TotalMinutes >= milemins)
                            {
                                trprcd.SuggestedDOTime = trprcd.DOTime;
                            }
                            else
                            {
                                trprcd.Alerts = trprcd.Alerts + " EDO";
                                scheddotime = schedputime + new TimeSpan(0, (int)milemins, 0);
                                trprcd.SuggestedDOTime = scheddotime.ToString("HH:mm");
                                trprcd.Status = "Fixable";
                            }
                            continue;
                        }

                        if ((scheddotime - schedputime).TotalMinutes >= 60)
                        {
                            switch (putimediff)
                            {
                                case 0://times are same
                                    trprcd.SuggestedPUTime = trprcd.PUTime;
                                    break;
                                case -1://driver is early
                                    trprcd.Alerts = trprcd.Alerts + " EPU";
                                    rInt = r.Next(0, 30);
                                    schedputime = schedputime + new TimeSpan(0, rInt, 0);
                                    trprcd.SuggestedPUTime = schedputime.ToString("HH:mm");
                                    trprcd.Status = "Fixable";
                                    break; //up to 30 minutes late
                                case 1://driver is late
                                    if (McTripTimingRules.PuLateMinutesOk(trprcd.Trip, driverputime, schedputime))//driver time is good
                                    {
                                        trprcd.SuggestedPUTime = trprcd.PUTime;
                                    }
                                    else
                                    {
                                        trprcd.Alerts = trprcd.Alerts + " LPU";
                                        rInt = r.Next(0, 25);
                                        schedputime = schedputime + new TimeSpan(0, rInt, 0);
                                        trprcd.SuggestedPUTime = schedputime.ToString("HH:mm");
                                        trprcd.Status = "Fixable";
                                    }
                                    break;//up to 25 minutes late
                            }
                            switch (dotimediff)
                            {
                                case 0://times are same
                                    trprcd.SuggestedDOTime = trprcd.DOTime;
                                    break;
                                case -1://driver is early
                                    if ((scheddotime - driverdotime).TotalMinutes <= 25)//driver time is good
                                    {
                                        trprcd.SuggestedDOTime = trprcd.DOTime;
                                    }
                                    else
                                    {
                                        trprcd.Alerts = trprcd.Alerts + " EDO";
                                        rInt = r.Next(0, 25);
                                        scheddotime = scheddotime - new TimeSpan(0, rInt, 0);
                                        trprcd.SuggestedDOTime = scheddotime.ToString("HH:mm");
                                        trprcd.Status = "Fixable";
                                    }
                                    break;//up to 25 minutes early
                                case 1://driver is late
                                    trprcd.Alerts = trprcd.Alerts + " LDO";
                                    rInt = r.Next(0, 25);
                                    scheddotime = scheddotime - new TimeSpan(0, rInt, 0);
                                    trprcd.SuggestedDOTime = scheddotime.ToString("HH:mm");
                                    trprcd.Status = "Fixable";
                                    break;//cant be late
                            }
                            continue;
                        }
                        if ((scheddotime - schedputime).TotalMinutes >= 45 & (scheddotime - schedputime).TotalMinutes < 60)
                        {
                            switch (putimediff)
                            {
                                case 0://times are same
                                    trprcd.SuggestedPUTime = trprcd.PUTime;
                                    break;
                                case -1://driver is early
                                    trprcd.Alerts = trprcd.Alerts + " EPU";
                                    rInt = r.Next(0, 20);
                                    schedputime = schedputime + new TimeSpan(0, rInt, 0);
                                    trprcd.SuggestedPUTime = schedputime.ToString("HH:mm");
                                    trprcd.Status = "Fixable";
                                    break; //cant be early
                                case 1://driver is late
                                    if (McTripTimingRules.PuLateMinutesOk(trprcd.Trip, driverputime, schedputime))//driver time is good
                                    {
                                        trprcd.SuggestedPUTime = trprcd.PUTime;
                                    }
                                    else
                                    {
                                        trprcd.Alerts = trprcd.Alerts + " LPU";
                                        rInt = r.Next(0, 20);
                                        schedputime = schedputime + new TimeSpan(0, rInt, 0);
                                        trprcd.SuggestedPUTime = schedputime.ToString("HH:mm");
                                        trprcd.Status = "Fixable";
                                    }
                                    break;//up to 20 minutes late
                            }
                            switch (dotimediff)
                            {
                                case 0://times are same
                                    trprcd.SuggestedDOTime = trprcd.DOTime;
                                    break;
                                case -1://driver is early
                                    if ((scheddotime - driverdotime).TotalMinutes <= 20)//driver time is good
                                    {
                                        trprcd.SuggestedDOTime = trprcd.DOTime;
                                    }
                                    else
                                    {
                                        trprcd.Alerts = trprcd.Alerts + " EDO";
                                        rInt = r.Next(0, 20);
                                        scheddotime = scheddotime - new TimeSpan(0, rInt, 0);
                                        trprcd.SuggestedDOTime = scheddotime.ToString("HH:mm");
                                        trprcd.Status = "Fixable";
                                    }
                                    break;//up to 20 minutes early
                                case 1://driver is late
                                    trprcd.Alerts = trprcd.Alerts + " LDO";
                                    rInt = r.Next(0, 20);
                                    scheddotime = scheddotime - new TimeSpan(0, rInt, 0);
                                    trprcd.SuggestedDOTime = scheddotime.ToString("HH:mm");
                                    trprcd.Status = "Fixable";
                                    break;//cant be late
                            }
                            continue;
                        }
                        if ((scheddotime - schedputime).TotalMinutes >= 30 & (scheddotime - schedputime).TotalMinutes < 45)
                        {
                            switch (putimediff)
                            {
                                case 0://times are same
                                    trprcd.SuggestedPUTime = trprcd.PUTime;
                                    break;
                                case -1://driver is early
                                    trprcd.Alerts = trprcd.Alerts + " EPU";
                                    rInt = r.Next(0, 10);
                                    schedputime = schedputime + new TimeSpan(0, rInt, 0);
                                    trprcd.SuggestedPUTime = schedputime.ToString("HH:mm");
                                    trprcd.Status = "Fixable";
                                    break; //up to 10 minutes late
                                case 1://driver is late
                                    if (McTripTimingRules.PuLateMinutesOk(trprcd.Trip, driverputime, schedputime))//driver time is good
                                    {
                                        trprcd.SuggestedPUTime = trprcd.PUTime;
                                    }
                                    else
                                    {
                                        trprcd.Alerts = trprcd.Alerts + " LPU";
                                        rInt = r.Next(0, 10);
                                        schedputime = schedputime + new TimeSpan(0, rInt, 0);
                                        trprcd.SuggestedPUTime = schedputime.ToString("HH:mm");
                                        trprcd.Status = "Fixable";
                                    }
                                    break;//up to 10 minutes late
                            }
                            switch (dotimediff)
                            {
                                case 0://times are same
                                    trprcd.SuggestedDOTime = trprcd.DOTime;
                                    break;
                                case -1://driver is early
                                    if ((scheddotime - driverdotime).TotalMinutes <= 15)//driver time is good
                                    {
                                        trprcd.SuggestedDOTime = trprcd.DOTime;
                                    }
                                    else
                                    {
                                        trprcd.Alerts = trprcd.Alerts + " EDO";
                                        rInt = r.Next(0, 15);
                                        scheddotime = scheddotime - new TimeSpan(0, rInt, 0);
                                        trprcd.SuggestedDOTime = scheddotime.ToString("HH:mm");
                                        trprcd.Status = "Fixable";
                                    }
                                    break;//up to 15 minutes early
                                case 1://driver is late
                                    trprcd.Alerts = trprcd.Alerts + " LDO";
                                    rInt = r.Next(0, 15);
                                    scheddotime = scheddotime - new TimeSpan(0, rInt, 0);
                                    trprcd.SuggestedDOTime = scheddotime.ToString("HH:mm");
                                    trprcd.Status = "Fixable";
                                    break;//cant be late
                            }
                            continue;
                        }
                        if ((scheddotime - schedputime).TotalMinutes >= 15 & (scheddotime - schedputime).TotalMinutes < 30)
                        {
                            switch (putimediff)
                            {
                                case 0://times are same
                                    trprcd.SuggestedPUTime = trprcd.PUTime;
                                    break;
                                case -1://driver is early
                                    trprcd.Alerts = trprcd.Alerts + " EPU";
                                    rInt = r.Next(0, 5);
                                    schedputime = schedputime + new TimeSpan(0, rInt, 0);
                                    trprcd.SuggestedPUTime = schedputime.ToString("HH:mm");
                                    trprcd.Status = "Fixable";
                                    break; //up to 30 minutes early
                                case 1://driver is late
                                    if (McTripTimingRules.PuLateMinutesOk(trprcd.Trip, driverputime, schedputime))//driver time is good
                                    {
                                        trprcd.SuggestedPUTime = trprcd.PUTime;
                                    }
                                    else
                                    {
                                        trprcd.Alerts = trprcd.Alerts + " LPU";
                                        rInt = r.Next(0, 5);
                                        schedputime = schedputime + new TimeSpan(0, rInt, 0);
                                        trprcd.SuggestedPUTime = schedputime.ToString("HH:mm");
                                        trprcd.Status = "Fixable";
                                    }
                                    break;//up to 5 minutes late
                            }
                            switch (dotimediff)
                            {
                                case 0://times are same
                                    trprcd.SuggestedDOTime = trprcd.DOTime;
                                    break;
                                case -1://driver is early
                                    if ((scheddotime - driverdotime).TotalMinutes <= 5)//driver time is good
                                    {
                                        trprcd.SuggestedDOTime = trprcd.DOTime;
                                    }
                                    else
                                    {
                                        trprcd.Alerts = trprcd.Alerts + " EDO";
                                        rInt = r.Next(0, 5);
                                        scheddotime = scheddotime - new TimeSpan(0, rInt, 0);
                                        trprcd.SuggestedDOTime = scheddotime.ToString("HH:mm");
                                        trprcd.Status = "Fixable";
                                    }
                                    break;//up to 5 minutes early
                                case 1://driver is late
                                    trprcd.Alerts = trprcd.Alerts + " LDO";
                                    rInt = r.Next(0, 5);
                                    scheddotime = scheddotime - new TimeSpan(0, rInt, 0);
                                    trprcd.SuggestedDOTime = scheddotime.ToString("HH:mm");
                                    trprcd.Status = "Fixable";

                                    break;//cant be late
                            }
                            continue;
                        }
                        if ((scheddotime - schedputime).TotalMinutes < 15)
                        {
                            switch (putimediff)
                            {
                                case 0://times are same
                                    trprcd.SuggestedPUTime = trprcd.PUTime;
                                    break;
                                case -1://driver is early
                                    trprcd.Alerts = trprcd.Alerts + " EPU";
                                    trprcd.SuggestedPUTime = schedputime.ToString("HH:mm");
                                    trprcd.Status = "Fixable";
                                    break;
                                case 1://driver is late
                                    trprcd.Alerts = trprcd.Alerts + " LPU";
                                    trprcd.SuggestedPUTime = schedputime.ToString("HH:mm");
                                    trprcd.Status = "Fixable";
                                    break;//cant be late
                            }
                            switch (dotimediff)
                            {
                                case 0://times are same
                                    trprcd.SuggestedDOTime = trprcd.DOTime;
                                    break;
                                case -1://driver is early
                                    trprcd.Alerts = trprcd.Alerts + " EDO";
                                    trprcd.SuggestedDOTime = scheddotime.ToString("HH:mm");
                                    trprcd.Status = "Fixable";
                                    break;
                                case 1://driver is late
                                    trprcd.Alerts = trprcd.Alerts + " LDO";
                                    trprcd.SuggestedDOTime = scheddotime.ToString("HH:mm");
                                    trprcd.Status = "Fixable";
                                    break;//cant be late
                            }
                            continue;
                        }
                    }
                    if (trprcd.Trip.Contains("D"))
                    {
                        double triptotalmins = (driverdotime - driverputime).TotalMinutes;
                        if (scheddotime.TimeOfDay.Ticks == 0)
                        {
                            switch (putimediff)
                            {
                                case 0://times are same
                                    trprcd.SuggestedPUTime = trprcd.PUTime;
                                    break;
                                case -1://driver is early. cant be early.
                                    trprcd.Alerts = trprcd.Alerts + " EPU";
                                    rInt = r.Next(0, 5);
                                    schedputime = schedputime + new TimeSpan(0, rInt, 0);
                                    trprcd.SuggestedPUTime = schedputime.ToString("HH:mm");
                                    trprcd.Status = "Fixable";
                                    break;
                                case 1://driver is late
                                    if (McTripTimingRules.PuLateMinutesOk(trprcd.Trip, driverputime, schedputime))//driver time is good
                                    {
                                        trprcd.SuggestedPUTime = trprcd.PUTime;
                                        schedputime = driverputime;
                                    }
                                    else
                                    {
                                        trprcd.Alerts = trprcd.Alerts + " LPU";
                                        rInt = r.Next(0, 30);
                                        schedputime = schedputime + new TimeSpan(0, rInt, 0);
                                        trprcd.SuggestedPUTime = schedputime.ToString("HH:mm");
                                        trprcd.Status = "Fixable";
                                    }
                                    break;//up to 30 minutes late
                            }


                            if ((driverdotime - schedputime).TotalMinutes >= milemins)
                            {
                                trprcd.SuggestedDOTime = trprcd.DOTime;
                            }
                            else
                            {
                                trprcd.Alerts = trprcd.Alerts + " EDO";
                                scheddotime = schedputime + new TimeSpan(0, (int)milemins, 0);
                                trprcd.SuggestedDOTime = scheddotime.ToString("HH:mm");
                                trprcd.Status = "Fixable";
                            }
                            continue;
                        }

                        if ((scheddotime - schedputime).TotalMinutes >= 60)
                        {
                            switch (putimediff)
                            {
                                case 0://times are same
                                    trprcd.SuggestedPUTime = trprcd.PUTime;
                                    break;
                                case -1://driver is early
                                    trprcd.Alerts = trprcd.Alerts + " EPU";
                                    rInt = r.Next(0, 30);
                                    schedputime = schedputime + new TimeSpan(0, rInt, 0);
                                    trprcd.SuggestedPUTime = schedputime.ToString("HH:mm");
                                    trprcd.Status = "Fixable";
                                    break; //up to 30 minutes late
                                case 1://driver is late
                                    if (McTripTimingRules.PuLateMinutesOk(trprcd.Trip, driverputime, schedputime))//driver time is good
                                    {
                                        trprcd.SuggestedPUTime = trprcd.PUTime;
                                    }
                                    else
                                    {
                                        trprcd.Alerts = trprcd.Alerts + " LPU";
                                        rInt = r.Next(0, 25);
                                        schedputime = schedputime + new TimeSpan(0, rInt, 0);
                                        trprcd.SuggestedPUTime = schedputime.ToString("HH:mm");
                                        trprcd.Status = "Fixable";
                                    }
                                    break;//up to 25 minutes late
                            }
                            switch (dotimediff)
                            {
                                case 0://times are same
                                    trprcd.SuggestedDOTime = trprcd.DOTime;
                                    break;
                                case -1://driver is early
                                    if ((scheddotime - driverdotime).TotalMinutes <= 25)//driver time is good
                                    {
                                        trprcd.SuggestedDOTime = trprcd.DOTime;
                                    }
                                    else
                                    {
                                        trprcd.Alerts = trprcd.Alerts + " EDO";
                                        rInt = r.Next(0, 25);
                                        scheddotime = scheddotime - new TimeSpan(0, rInt, 0);
                                        trprcd.SuggestedDOTime = scheddotime.ToString("HH:mm");
                                        trprcd.Status = "Fixable";
                                    }
                                    break;//up to 25 minutes early
                                case 1://driver is late
                                    trprcd.Alerts = trprcd.Alerts + " LDO";
                                    rInt = r.Next(0, 25);
                                    scheddotime = scheddotime - new TimeSpan(0, rInt, 0);
                                    trprcd.SuggestedDOTime = scheddotime.ToString("HH:mm");
                                    trprcd.Status = "Fixable";
                                    break;//cant be late
                            }
                            continue;
                        }
                        if ((scheddotime - schedputime).TotalMinutes >= 45 & (scheddotime - schedputime).TotalMinutes < 60)
                        {
                            switch (putimediff)
                            {
                                case 0://times are same
                                    trprcd.SuggestedPUTime = trprcd.PUTime;
                                    break;
                                case -1://driver is early
                                    trprcd.Alerts = trprcd.Alerts + " EPU";
                                    rInt = r.Next(0, 20);
                                    schedputime = schedputime + new TimeSpan(0, rInt, 0);
                                    trprcd.SuggestedPUTime = schedputime.ToString("HH:mm");
                                    trprcd.Status = "Fixable";
                                    break; //cant be early
                                case 1://driver is late
                                    if (McTripTimingRules.PuLateMinutesOk(trprcd.Trip, driverputime, schedputime))//driver time is good
                                    {
                                        trprcd.SuggestedPUTime = trprcd.PUTime;
                                    }
                                    else
                                    {
                                        trprcd.Alerts = trprcd.Alerts + " LPU";
                                        rInt = r.Next(0, 20);
                                        schedputime = schedputime + new TimeSpan(0, rInt, 0);
                                        trprcd.SuggestedPUTime = schedputime.ToString("HH:mm");
                                        trprcd.Status = "Fixable";
                                    }
                                    break;//up to 20 minutes late
                            }
                            switch (dotimediff)
                            {
                                case 0://times are same
                                    trprcd.SuggestedDOTime = trprcd.DOTime;
                                    break;
                                case -1://driver is early
                                    if ((scheddotime - driverdotime).TotalMinutes <= 20)//driver time is good
                                    {
                                        trprcd.SuggestedDOTime = trprcd.DOTime;
                                    }
                                    else
                                    {
                                        trprcd.Alerts = trprcd.Alerts + " EDO";
                                        rInt = r.Next(0, 20);
                                        scheddotime = scheddotime - new TimeSpan(0, rInt, 0);
                                        trprcd.SuggestedDOTime = scheddotime.ToString("HH:mm");
                                        trprcd.Status = "Fixable";
                                    }
                                    break;//up to 20 minutes early
                                case 1://driver is late
                                    trprcd.Alerts = trprcd.Alerts + " LDO";
                                    rInt = r.Next(0, 20);
                                    scheddotime = scheddotime - new TimeSpan(0, rInt, 0);
                                    trprcd.SuggestedDOTime = scheddotime.ToString("HH:mm");
                                    trprcd.Status = "Fixable";
                                    break;//cant be late
                            }
                            continue;
                        }
                        if ((scheddotime - schedputime).TotalMinutes >= 30 & (scheddotime - schedputime).TotalMinutes < 45)
                        {
                            switch (putimediff)
                            {
                                case 0://times are same
                                    trprcd.SuggestedPUTime = trprcd.PUTime;
                                    break;
                                case -1://driver is early
                                    trprcd.Alerts = trprcd.Alerts + " EPU";
                                    rInt = r.Next(0, 10);
                                    schedputime = schedputime + new TimeSpan(0, rInt, 0);
                                    trprcd.SuggestedPUTime = schedputime.ToString("HH:mm");
                                    trprcd.Status = "Fixable";
                                    break; //up to 10 minutes late
                                case 1://driver is late
                                    if (McTripTimingRules.PuLateMinutesOk(trprcd.Trip, driverputime, schedputime))//driver time is good
                                    {
                                        trprcd.SuggestedPUTime = trprcd.PUTime;
                                    }
                                    else
                                    {
                                        trprcd.Alerts = trprcd.Alerts + " LPU";
                                        rInt = r.Next(0, 10);
                                        schedputime = schedputime + new TimeSpan(0, rInt, 0);
                                        trprcd.SuggestedPUTime = schedputime.ToString("HH:mm");
                                        trprcd.Status = "Fixable";
                                    }
                                    break;//up to 10 minutes late
                            }
                            switch (dotimediff)
                            {
                                case 0://times are same
                                    trprcd.SuggestedDOTime = trprcd.DOTime;
                                    break;
                                case -1://driver is early
                                    if ((scheddotime - driverdotime).TotalMinutes <= 15)//driver time is good
                                    {
                                        trprcd.SuggestedDOTime = trprcd.DOTime;
                                    }
                                    else
                                    {
                                        trprcd.Alerts = trprcd.Alerts + " EDO";
                                        rInt = r.Next(0, 15);
                                        scheddotime = scheddotime - new TimeSpan(0, rInt, 0);
                                        trprcd.SuggestedDOTime = scheddotime.ToString("HH:mm");
                                        trprcd.Status = "Fixable";
                                    }
                                    break;//up to 15 minutes early
                                case 1://driver is late
                                    trprcd.Alerts = trprcd.Alerts + " LDO";
                                    rInt = r.Next(0, 15);
                                    scheddotime = scheddotime - new TimeSpan(0, rInt, 0);
                                    trprcd.SuggestedDOTime = scheddotime.ToString("HH:mm");
                                    trprcd.Status = "Fixable";
                                    break;//cant be late
                            }
                            continue;
                        }
                        if ((scheddotime - schedputime).TotalMinutes >= 15 & (scheddotime - schedputime).TotalMinutes < 30)
                        {
                            switch (putimediff)
                            {
                                case 0://times are same
                                    trprcd.SuggestedPUTime = trprcd.PUTime;
                                    break;
                                case -1://driver is early
                                    trprcd.Alerts = trprcd.Alerts + " EPU";
                                    rInt = r.Next(0, 5);
                                    schedputime = schedputime + new TimeSpan(0, rInt, 0);
                                    trprcd.SuggestedPUTime = schedputime.ToString("HH:mm");
                                    trprcd.Status = "Fixable";
                                    break; //up to 30 minutes early
                                case 1://driver is late
                                    if (McTripTimingRules.PuLateMinutesOk(trprcd.Trip, driverputime, schedputime))//driver time is good
                                    {
                                        trprcd.SuggestedPUTime = trprcd.PUTime;
                                    }
                                    else
                                    {
                                        trprcd.Alerts = trprcd.Alerts + " LPU";
                                        rInt = r.Next(0, 5);
                                        schedputime = schedputime + new TimeSpan(0, rInt, 0);
                                        trprcd.SuggestedPUTime = schedputime.ToString("HH:mm");
                                        trprcd.Status = "Fixable";
                                    }
                                    break;//up to 5 minutes late
                            }
                            switch (dotimediff)
                            {
                                case 0://times are same
                                    trprcd.SuggestedDOTime = trprcd.DOTime;
                                    break;
                                case -1://driver is early
                                    if ((scheddotime - driverdotime).TotalMinutes <= 5)//driver time is good
                                    {
                                        trprcd.SuggestedDOTime = trprcd.DOTime;
                                    }
                                    else
                                    {
                                        trprcd.Alerts = trprcd.Alerts + " EDO";
                                        rInt = r.Next(0, 5);
                                        scheddotime = scheddotime - new TimeSpan(0, rInt, 0);
                                        trprcd.SuggestedDOTime = scheddotime.ToString("HH:mm");
                                        trprcd.Status = "Fixable";
                                    }
                                    break;//up to 5 minutes early
                                case 1://driver is late
                                    trprcd.Alerts = trprcd.Alerts + " LDO";
                                    rInt = r.Next(0, 5);
                                    scheddotime = scheddotime - new TimeSpan(0, rInt, 0);
                                    trprcd.SuggestedDOTime = scheddotime.ToString("HH:mm");
                                    trprcd.Status = "Fixable";

                                    break;//cant be late
                            }
                            continue;
                        }
                        if ((scheddotime - schedputime).TotalMinutes < 15)
                        {
                            switch (putimediff)
                            {
                                case 0://times are same
                                    trprcd.SuggestedPUTime = trprcd.PUTime;
                                    break;
                                case -1://driver is early
                                    trprcd.Alerts = trprcd.Alerts + " EPU";
                                    trprcd.SuggestedPUTime = schedputime.ToString("HH:mm");
                                    trprcd.Status = "Fixable";
                                    break;
                                case 1://driver is late
                                    trprcd.Alerts = trprcd.Alerts + " LPU";
                                    trprcd.SuggestedPUTime = schedputime.ToString("HH:mm");
                                    trprcd.Status = "Fixable";
                                    break;//cant be late
                            }
                            switch (dotimediff)
                            {
                                case 0://times are same
                                    trprcd.SuggestedDOTime = trprcd.DOTime;
                                    break;
                                case -1://driver is early
                                    trprcd.Alerts = trprcd.Alerts + " EDO";
                                    trprcd.SuggestedDOTime = scheddotime.ToString("HH:mm");
                                    trprcd.Status = "Fixable";
                                    break;
                                case 1://driver is late
                                    trprcd.Alerts = trprcd.Alerts + " LDO";
                                    trprcd.SuggestedDOTime = scheddotime.ToString("HH:mm");
                                    trprcd.Status = "Fixable";
                                    break;//cant be late
                            }
                            continue;
                        }
                    }
                    */
                }
            }

            if (onlyPortalRedTrips)
                FinalizePassedTripStatus(batchtrips, t => t.TripErrors);
            else
                FinalizePassedTripStatus(batchtrips);
        }

        /// <summary>Data-only load: no PU/DO timing rules; suggested times mirror driver actuals.</summary>
        private void ApplyDataOnlyTripDefaults(List<MCBatchTripRecord> batchtrips, MCBatchAdditionalInfo dledtripsaddinfo)
        {
            foreach (MCBatchTripRecord trprcd in batchtrips)
            {
                if (trprcd.Date != dledtripsaddinfo.MCBatchDate)
                    continue;

                if (ShouldDeferPortalRedToStandardPass(trprcd))
                    continue;

                trprcd.Alerts = null;
                trprcd.Status = null;

                if (!TryParseBatchTime(trprcd.PUTime, out _) ||
                    !TryParseBatchTime(trprcd.DOTime, out _))
                {
                    AppendTripAlert(trprcd, "SCH");
                    trprcd.Status = "Fixable";
                    continue;
                }

                trprcd.SuggestedPUTime = trprcd.PUTime;
                trprcd.SuggestedDOTime = trprcd.DOTime;
            }

            FinalizePassedTripStatus(batchtrips);
        }

        /// <summary>
        /// Lenient load: keep driver times when they are a few minutes off scheduled (natural variance).
        /// Flag only scoreboard failures, severe early/late, or rider-call issues.
        /// Missing driver/vehicle is handled after this via <see cref="FinalizeDataQualityFixable"/>.
        /// </summary>
        private void LenientCorrectModivcareTimes(List<MCBatchTripRecord> batchtrips, MCBatchAdditionalInfo dledtripsaddinfo)
        {
            var r = new Random();

            foreach (MCBatchTripRecord trprcd in batchtrips)
            {
                if (trprcd.Date != dledtripsaddinfo.MCBatchDate)
                    continue;

                if (ShouldDeferPortalRedToStandardPass(trprcd))
                    continue;

                trprcd.Alerts = null;
                trprcd.Status = null;

                if (!TryParseBatchTime(trprcd.ScheduledPUTime, out DateTime schedputime) ||
                    !TryParseBatchTime(trprcd.ScheduledDOTime, out DateTime scheddotime) ||
                    !TryParseBatchTime(trprcd.PUTime, out DateTime driverputime) ||
                    !TryParseBatchTime(trprcd.DOTime, out DateTime driverdotime))
                {
                    AppendTripAlert(trprcd, "SCH");
                    trprcd.Status = "Fixable";
                    continue;
                }

                decimal miles = 0;
                if (dledtripsaddinfo.mcDownloadedTrips != null && dledtripsaddinfo.mcDownloadedTrips.Any())
                {
                    foreach (MCDownloadedTrip mcdledtrip in dledtripsaddinfo.mcDownloadedTrips)
                    {
                        if (mcdledtrip.TripNumber.Replace(" ", "") == trprcd.Trip.Replace(" ", ""))
                        {
                            miles = Convert.ToDecimal(mcdledtrip.Miles);
                            break;
                        }
                    }
                }

                if (!trprcd.RiderCallTime.Contains("nbsp") &&
                    TryParseBatchTime(trprcd.RiderCallTime, out DateTime riderCallPu))
                {
                    ApplyRiderCallTimeCorrections(trprcd, riderCallPu, driverputime, driverdotime, miles);
                    continue;
                }

                trprcd.SuggestedPUTime = trprcd.PUTime;
                trprcd.SuggestedDOTime = trprcd.DOTime;

                DateTime workSchedPu = schedputime;
                DateTime workSchedDo = scheddotime;
                bool fix = false;

                if (McTripTimingRules.IsLenientPuLateViolation(trprcd.Trip, driverputime, schedputime))
                {
                    AppendTripAlert(trprcd, "LPU");
                    int cap = McTripTimingRules.RandomLatePuCap(trprcd.Trip);
                    int rInt = McTripTimingRules.LenientNudgeMinutes(cap, r);
                    workSchedPu = schedputime + new TimeSpan(0, rInt, 0);
                    trprcd.SuggestedPUTime = workSchedPu.ToString("HH:mm");
                    fix = true;
                }

                if (McTripTimingRules.IsLenientPuEarlyViolation(trprcd.Trip, driverputime, schedputime))
                {
                    AppendTripAlert(trprcd, "EPU");
                    int rInt = McTripTimingRules.LenientNudgeMinutes(29, r);
                    if (McTripTimingRules.IsALeg(trprcd.Trip))
                        workSchedPu = schedputime - new TimeSpan(0, rInt, 0);
                    else
                        workSchedPu = schedputime + new TimeSpan(0, rInt, 0);
                    trprcd.SuggestedPUTime = workSchedPu.ToString("HH:mm");
                    fix = true;
                }

                if (scheddotime.TimeOfDay.Ticks != 0)
                {
                    if (McTripTimingRules.IsLenientDoLateViolation(driverdotime, scheddotime))
                    {
                        AppendTripAlert(trprcd, "LDO");
                        int rInt = McTripTimingRules.LenientNudgeMinutes(29, r);
                        workSchedDo = scheddotime - new TimeSpan(0, rInt, 0);
                        trprcd.SuggestedDOTime = workSchedDo.ToString("HH:mm");
                        fix = true;
                    }
                    else if (McTripTimingRules.IsLenientDoEarlyViolation(driverdotime, scheddotime))
                    {
                        AppendTripAlert(trprcd, "EDO");
                        int rInt = McTripTimingRules.LenientNudgeMinutes(29, r);
                        workSchedDo = scheddotime - new TimeSpan(0, rInt, 0);
                        trprcd.SuggestedDOTime = workSchedDo.ToString("HH:mm");
                        fix = true;
                    }
                }

                EnforceSuggestedTimesOrder(trprcd, miles, scheddotime);
                FinalizeLenientTimingFlags(trprcd, ref fix);

                if (fix)
                    trprcd.Status = "Fixable";
            }

            FinalizePassedTripStatus(batchtrips);
        }

        /// <summary>
        /// If timing rules flagged LDO/LPU/etc. but suggested times stayed at driver actuals, do not keep the trip fixable.
        /// </summary>
        private static void FinalizeLenientTimingFlags(MCBatchTripRecord trprcd, ref bool fix)
        {
            if (!fix)
                return;

            if (!TryParseBatchTime(trprcd.SuggestedPUTime, out DateTime suggPu) ||
                !TryParseBatchTime(trprcd.PUTime, out DateTime driverPu) ||
                !TryParseBatchTime(trprcd.SuggestedDOTime, out DateTime suggDo) ||
                !TryParseBatchTime(trprcd.DOTime, out DateTime driverDo))
                return;

            if (suggPu != driverPu || suggDo != driverDo)
                return;

            string alerts = trprcd.Alerts ?? "";
            if (alerts.IndexOf("RCT", StringComparison.OrdinalIgnoreCase) >= 0 ||
                alerts.IndexOf("SCH", StringComparison.OrdinalIgnoreCase) >= 0 ||
                alerts.IndexOf("MIS-", StringComparison.OrdinalIgnoreCase) >= 0 ||
                alerts.IndexOf("INV-", StringComparison.OrdinalIgnoreCase) >= 0 ||
                alerts.IndexOf("ASG-", StringComparison.OrdinalIgnoreCase) >= 0)
                return;

            trprcd.Alerts = null;
            fix = false;
        }

        /// <summary>
        /// Modivcare TripActuals rejects posts when drop-off is not after pick-up. Timing fixes can move PU and DO independently.
        /// </summary>
        private static void EnforceSuggestedTimesOrder(MCBatchTripRecord trprcd, decimal miles, DateTime schedDo)
        {
            if (!TryParseBatchTime(trprcd.SuggestedPUTime, out DateTime suggPu) ||
                !TryParseBatchTime(trprcd.SuggestedDOTime, out DateTime suggDo))
                return;

            double minTripMins = Math.Max(5, (int)(5 + miles));
            DateTime minDo = suggPu + TimeSpan.FromMinutes(minTripMins);

            if (suggDo > minDo)
                return;

            DateTime resolved = minDo;
            if (schedDo.TimeOfDay.Ticks != 0 && schedDo >= minDo)
                resolved = schedDo;
            else if (TryParseBatchTime(trprcd.DOTime, out DateTime driverDo) &&
                     TryParseBatchTime(trprcd.PUTime, out DateTime driverPu) &&
                     driverDo >= driverPu + TimeSpan.FromMinutes(minTripMins) &&
                     driverDo >= minDo)
                resolved = driverDo;

            trprcd.SuggestedDOTime = resolved.ToString("HH:mm");
        }

        private static void ApplyRiderCallTimeCorrections(MCBatchTripRecord trprcd, DateTime riderCallPu,
            DateTime driverputime, DateTime driverdotime, decimal miles)
        {
            double milemins = (int)(5 + miles);
            DateTime minDo = riderCallPu + TimeSpan.FromMinutes(milemins);

            // TripActuals: pickup = driver actual; rider call is a separate field (they often differ).
            DateTime pickupForSubmit = driverputime >= riderCallPu ? driverputime : riderCallPu;
            trprcd.SuggestedPUTime = pickupForSubmit.ToString("HH:mm", CultureInfo.InvariantCulture);

            if (driverdotime >= minDo)
                trprcd.SuggestedDOTime = driverdotime.ToString("HH:mm", CultureInfo.InvariantCulture);
            else
                trprcd.SuggestedDOTime = minDo.ToString("HH:mm", CultureInfo.InvariantCulture);

            EnforceRiderCallSuggestedTimesOrder(trprcd, minDo);

            if (!NeedsRiderCallTripCorrection(trprcd, riderCallPu, driverputime, driverdotime, minDo))
                return;

            AppendTripAlert(trprcd, "RCT");
            trprcd.Status = "Fixable";
        }

        /// <summary>
        /// Rider-call rows often show different call vs pickup times on the batch grid; that alone is not a fix.
        /// Flag only when times are invalid for TripActuals submit or suggested values still need adjustment.
        /// </summary>
        private static bool NeedsRiderCallTripCorrection(MCBatchTripRecord trprcd, DateTime riderCallPu,
            DateTime driverputime, DateTime driverdotime, DateTime minDo)
        {
            if (driverputime < riderCallPu)
                return true;
            if (driverdotime < minDo)
                return true;

            if (!TryParseBatchTime(trprcd.SuggestedPUTime, out DateTime suggPu) ||
                !TryParseBatchTime(trprcd.SuggestedDOTime, out DateTime suggDo))
                return true;

            if (suggPu < riderCallPu || suggDo <= suggPu || suggDo < minDo)
                return true;

            return false;
        }

        /// <summary>
        /// RCT trips: never snap DO to scheduled (often wrong); only ensure DO is after rider-call PU + mile time.
        /// </summary>
        private static void EnforceRiderCallSuggestedTimesOrder(MCBatchTripRecord trprcd, DateTime minDo)
        {
            if (!TryParseBatchTime(trprcd.SuggestedPUTime, out DateTime suggPu) ||
                !TryParseBatchTime(trprcd.SuggestedDOTime, out DateTime suggDo))
                return;

            if (suggDo >= minDo && suggDo > suggPu)
                return;

            if (TryParseBatchTime(trprcd.DOTime, out DateTime driverDo) && driverDo >= minDo && driverDo > suggPu)
                trprcd.SuggestedDOTime = driverDo.ToString("HH:mm", CultureInfo.InvariantCulture);
            else
                trprcd.SuggestedDOTime = minDo.ToString("HH:mm", CultureInfo.InvariantCulture);
        }

        private static void FinalizePassedTripStatus(List<MCBatchTripRecord> batchtrips,
            Func<MCBatchTripRecord, bool> includeTrip = null)
        {
            foreach (MCBatchTripRecord trprcd in batchtrips)
            {
                if (includeTrip != null && !includeTrip(trprcd))
                    continue;
                if (string.IsNullOrWhiteSpace(trprcd.Alerts))
                    trprcd.Status = "Passed";
            }
        }

        private static void AppendTripAlert(MCBatchTripRecord trip, string alertCode)
        {
            string alerts = trip.Alerts ?? "";
            if (alerts.IndexOf(alertCode, StringComparison.OrdinalIgnoreCase) < 0)
                trip.Alerts = alerts + " " + alertCode;
        }

        public async Task SubmitTrip(MCLoginHandler mcloginhandler, MCBatchTripRecord trip)
        {

           // Console.WriteLine("Submitting trip " + trip.TripFull + " with driver ID " + trip.AssignedVehicle.DriverTag + " and vehicle ID " + trip.AssignedVehicle.VehicleTag);

            string maincontentstring = HttpUtility.UrlDecode("ctl00%24cphMainContent%24");
            string signaturereceived = "";
            string signatureneeded = "";
            if (trip.SignatureReceived == "Rider Signature Received")
            {
                signaturereceived = "1";
                signatureneeded = "true";
            }
            if (trip.SignatureReceived == "Rider Unable to Sign")
            {
                signaturereceived = "2";
                signatureneeded = "false";
            }
            if (trip.SignatureReceived == "Signature Missing")
            {
                signaturereceived = "2";
                signatureneeded = "false";
            }

            bool hasRiderCall = !trip.RiderCallTime.Contains("nbsp");
            string submitPu = FormatTimeForModivcareSubmit(
                !string.IsNullOrWhiteSpace(trip.SuggestedPUTime) ? trip.SuggestedPUTime : trip.PUTime);
            string submitDo = FormatTimeForModivcareSubmit(
                !string.IsNullOrWhiteSpace(trip.SuggestedDOTime) ? trip.SuggestedDOTime : trip.DOTime);
            string submitRiderCall = hasRiderCall
                ? FormatTimeForModivcareSubmit(trip.RiderCallTime)
                : "";

            TripActualsLateReasonFields lateFields = ParseLateReasonFieldsFromTripActualsHtml(_tripActualsFormHtml);

            if (!hasRiderCall)
            {
                testformContent = new MyFormUrlEncodedContent(new[]{
                new KeyValuePair<string, string>("__LASTFOCUS", ""),
                new KeyValuePair<string, string>("__EVENTTARGET", ""),
                new KeyValuePair<string, string>("__EVENTARGUMENT", ""),
                new KeyValuePair<string, string>("__VIEWSTATE", mcloginhandler.ViewStateToken),
                new KeyValuePair<string, string>("__VIEWSTATEGENERATOR", mcloginhandler.ViewStateGeneratorToken),
                new KeyValuePair<string, string>("__SCROLLPOSITIONX", "0"),
                new KeyValuePair<string, string>("__SCROLLPOSITIONY", "0"),
                new KeyValuePair<string, string>("__EVENTVALIDATION", mcloginhandler.EventValidationToken),
                new KeyValuePair<string, string>(maincontentstring + "hdnPULateReasonSelectedText", lateFields.PuLateText ?? ""),
                new KeyValuePair<string, string>(maincontentstring + "hdnPULateReasonSelectedCode", lateFields.PuLateCode ?? ""),
                new KeyValuePair<string, string>(maincontentstring + "hdnDOLateReasonSelectedText", lateFields.DoLateText ?? ""),
                new KeyValuePair<string, string>(maincontentstring + "hdnDOLateReasonSelectedCode", lateFields.DoLateCode ?? ""),
                new KeyValuePair<string, string>(maincontentstring + "hdnLateDialogToShow", lateFields.LateDialogToShow ?? ""),
                new KeyValuePair<string, string>(maincontentstring + "ddlVehicle", trip.AssignedVehicle.VehicleTag),
                new KeyValuePair<string, string>(maincontentstring + "ddlDriver", trip.AssignedVehicle.DriverTag),
                new KeyValuePair<string, string>(maincontentstring + "ddlSignatureReceived", signaturereceived),
                new KeyValuePair<string, string>(maincontentstring + "hdnSignatureNeeded", signatureneeded),
                new KeyValuePair<string, string>(maincontentstring + "hdnInvalidOrMissingSignature", "false"),
                new KeyValuePair<string, string>(maincontentstring + "txtPickupTime", submitPu),
                new KeyValuePair<string, string>(maincontentstring + "hdnShowPULateReasonFields", lateFields.ShowPuLate ?? ""),
                new KeyValuePair<string, string>(maincontentstring + "txtDropOffTime", submitDo),
                new KeyValuePair<string, string>(maincontentstring + "hdnShowDOLateReasonFields", lateFields.ShowDoLate ?? ""),
                new KeyValuePair<string, string>(maincontentstring + "txtBilledAmt", trip.BilledAmount.Replace("$", "")),
                new KeyValuePair<string, string>(maincontentstring + "txtBillingNotes", ""),
                new KeyValuePair<string, string>(maincontentstring + "btnSubmit", "Submit"),
             });
            }
            else
            {
                testformContent = new MyFormUrlEncodedContent(new[]{
                new KeyValuePair<string, string>("__LASTFOCUS", ""),
                new KeyValuePair<string, string>("__EVENTTARGET", ""),
                new KeyValuePair<string, string>("__EVENTARGUMENT", ""),
                new KeyValuePair<string, string>("__VIEWSTATE", mcloginhandler.ViewStateToken),
                new KeyValuePair<string, string>("__VIEWSTATEGENERATOR", mcloginhandler.ViewStateGeneratorToken),
                new KeyValuePair<string, string>("__SCROLLPOSITIONX", "0"),
                new KeyValuePair<string, string>("__SCROLLPOSITIONY", "0"),
                new KeyValuePair<string, string>("__EVENTVALIDATION", mcloginhandler.EventValidationToken),
                new KeyValuePair<string, string>(maincontentstring + "hdnPULateReasonSelectedText", lateFields.PuLateText ?? ""),
                new KeyValuePair<string, string>(maincontentstring + "hdnPULateReasonSelectedCode", lateFields.PuLateCode ?? ""),
                new KeyValuePair<string, string>(maincontentstring + "hdnDOLateReasonSelectedText", lateFields.DoLateText ?? ""),
                new KeyValuePair<string, string>(maincontentstring + "hdnDOLateReasonSelectedCode", lateFields.DoLateCode ?? ""),
                new KeyValuePair<string, string>(maincontentstring + "hdnLateDialogToShow", lateFields.LateDialogToShow ?? ""),
                new KeyValuePair<string, string>(maincontentstring + "ddlVehicle", trip.AssignedVehicle.VehicleTag),
                new KeyValuePair<string, string>(maincontentstring + "ddlDriver", trip.AssignedVehicle.DriverTag),
                new KeyValuePair<string, string>(maincontentstring + "ddlSignatureReceived", signaturereceived),
                new KeyValuePair<string, string>(maincontentstring + "hdnSignatureNeeded", signatureneeded),
                new KeyValuePair<string, string>(maincontentstring + "hdnInvalidOrMissingSignature", "false"),
                new KeyValuePair<string, string>(maincontentstring + "txtPickupTime", submitPu),
                new KeyValuePair<string, string>(maincontentstring + "hdnShowPULateReasonFields", lateFields.ShowPuLate ?? ""),
                new KeyValuePair<string, string>(maincontentstring + "txtDropOffTime", submitDo),
                new KeyValuePair<string, string>(maincontentstring + "hdnShowDOLateReasonFields", lateFields.ShowDoLate ?? ""),
                new KeyValuePair<string, string>(maincontentstring + "txtRiderCallTime", submitRiderCall),
                new KeyValuePair<string, string>(maincontentstring + "txtBilledAmt", trip.BilledAmount.Replace("$", "")),
                new KeyValuePair<string, string>(maincontentstring + "txtBillingNotes", ""),
                new KeyValuePair<string, string>(maincontentstring + "btnSubmit", "Submit"),
                });
            }
            HttpResponseMessage res = await mcloginhandler.Client.PostAsync("https://transportationco.logisticare.com/TripActuals.aspx", testformContent);
            var response = await res.Content.ReadAsStringAsync();

            // Mid-batch session expiry: mark this trip failed and bail. Outer loop will see the exception and
            // can reconnect + tell the user to retry the batch (rather than marching through and failing every trip).
            if (MCLoginHandler.IsAuthRedirect(res))
            {
                trip.Status = "Failed";
                throw new ModivcareSessionExpiredException();
            }

            mcloginhandler.GrabTokens(response);
            try
            {
                string responseUri = res.RequestMessage.RequestUri.ToString();
                if (responseUri.Contains("ProcessATMBatches.aspx"))
                {
                    trip.Status = "Passed";
                    //await GetTripActualsReponse(mcloginhandler);
                    //Console.WriteLine("Get Batch Page1");
                    //Console.WriteLine(mcBatchRecords.ActiveBatchLink);
                    //await GetBatchPage(mcloginhandler, mcBatchRecords.ActiveBatchLink, false);
                }
                if (responseUri.Contains("/error/error.html?aspxerrorpath=/TripActuals.aspx"))
                {
                    trip.Status = "Passed";
                    Console.WriteLine("Get Batch Page2");
                    //await GetTripActualsReponse(mcloginhandler);

                    //await GetBatchPage(mcloginhandler, mcBatchRecords.ActiveBatchLink, false);
                }
                if (responseUri.Contains("TripActuals.aspx"))
                {
                    trip.Status = "Failed";
                    Console.WriteLine("Get Batch Page3");
                    await GetBatchPage(mcloginhandler, mcBatchRecords.ActiveBatchLink, false);
                }
            }
            catch (ModivcareSessionExpiredException)
            {
                trip.Status = "Failed";
                throw;
            }
            catch
            {
                Console.WriteLine("There was a problem retrieving location.");
            }

        }

        




        public async Task LoadAvailableVehicles(MCBatchTripRecord mctriprecord, MCLoginHandler mcloginhandler, string date)
        {
            await LoadPortalEligibleListsFromSampleTrip(mcloginhandler, mctriprecord, date);
        }

        /// <summary>
        /// Posts a sample trip on the batch page, then reads TripActuals dropdowns for eligible drivers/vehicles.
        /// </summary>
        public async Task LoadPortalEligibleListsFromSampleTrip(MCLoginHandler mcloginhandler,
            MCBatchTripRecord sampleTrip, string date)
        {
            string html = await FetchTripActualsPageHtmlAsync(mcloginhandler, sampleTrip);
            if (string.IsNullOrEmpty(html))
            {
                SetPortalListLoadMessage(date, "TripActuals page was empty — assignment check skipped.");
                Console.WriteLine("MCTimeCorrection: TripActuals page empty for " + date + ".");
                return;
            }

            ParseVehicles(html, date, clearListsBeforeParse: true);
            UpdatePortalListLoadMessageFromParse(date, html);
        }

        private void SetPortalListLoadMessage(string batchDate, string message)
        {
            foreach (MCBatchAdditionalInfo batchInfo in mcBatchRecords.MCBatchAdditionalInfo)
            {
                if (batchInfo.MCBatchDate == batchDate)
                {
                    batchInfo.PortalListLoadMessage = message;
                    batchInfo.PortalEligibleDriverCount = batchInfo.MCDrivers?.Count ?? 0;
                    batchInfo.PortalEligibleVehicleCount = batchInfo.MCAvailableVehicles?.Count ?? 0;
                }
            }
        }

        private void UpdatePortalListLoadMessageFromParse(string batchDate, string html)
        {
            foreach (MCBatchAdditionalInfo batchInfo in mcBatchRecords.MCBatchAdditionalInfo)
            {
                if (batchInfo.MCBatchDate != batchDate)
                    continue;

                batchInfo.PortalEligibleDriverCount = batchInfo.MCDrivers?.Count ?? 0;
                batchInfo.PortalEligibleVehicleCount = batchInfo.MCAvailableVehicles?.Count ?? 0;

                bool onTripActuals = (html ?? "").IndexOf("TripActuals", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (html ?? "").IndexOf("ddlDriver", StringComparison.OrdinalIgnoreCase) >= 0;
                bool hasDriverDropdown = (html ?? "").IndexOf("ddlDriver", StringComparison.OrdinalIgnoreCase) >= 0;

                if (batchInfo.PortalEligibleDriverCount == 0 && batchInfo.PortalEligibleVehicleCount == 0)
                {
                    if (!onTripActuals)
                        batchInfo.PortalListLoadMessage =
                            "HTML does not look like TripActuals (still on batch/login?) — assignment check skipped.";
                    else if (!hasDriverDropdown)
                        batchInfo.PortalListLoadMessage =
                            "TripActuals page had no driver dropdown — assignment check skipped.";
                    else
                        batchInfo.PortalListLoadMessage =
                            "Driver/vehicle dropdowns found but no options parsed — assignment check skipped.";
                }
                else
                {
                    batchInfo.PortalListLoadMessage = batchInfo.PortalEligibleDriverCount + " eligible drivers, " +
                        batchInfo.PortalEligibleVehicleCount + " vehicles from TripActuals.";
                }
            }
        }

        /// <summary>Human-readable post-load check: portal list sizes and how many batch rows match.</summary>
        public string BuildPortalAssignmentAuditSummary()
        {
            if (mcBatchRecords?.MCBatchAdditionalInfo == null || mcBatchRecords.MCBatchTrips == null)
                return "No batch loaded.";

            var sb = new StringBuilder();
            foreach (MCBatchAdditionalInfo info in mcBatchRecords.MCBatchAdditionalInfo)
            {
                if (sb.Length > 0)
                    sb.Append(" | ");

                int tripCount = 0;
                int invD = 0;
                int invV = 0;
                int missingD = 0;
                int missingV = 0;

                foreach (MCBatchTripRecord trip in mcBatchRecords.MCBatchTrips)
                {
                    if (trip.Date != info.MCBatchDate)
                        continue;
                    tripCount++;

                    if (IsMissingAssignment(trip.Driver))
                        missingD++;
                    else if (info.PortalEligibleDriverCount > 0 && !IsDriverInPortalList(info, trip.Driver))
                        invD++;

                    if (IsMissingAssignment(trip.Vehicle))
                        missingV++;
                    else if (info.PortalEligibleVehicleCount > 0 && !IsVehicleInPortalList(info, trip.Vehicle))
                        invV++;
                }

                sb.Append(info.MCBatchDate);
                sb.Append(": ");
                if (!string.IsNullOrWhiteSpace(info.PortalListLoadMessage))
                    sb.Append(info.PortalListLoadMessage);
                else
                    sb.Append("portal lists not reported");

                sb.Append(" Trips ").Append(tripCount);
                sb.Append(" — missing driver ").Append(missingD);
                sb.Append(", missing vehicle ").Append(missingV);
                sb.Append(", INV-D ").Append(invD);
                sb.Append(", INV-V ").Append(invV);

                if (info.PortalEligibleDriverCount == 0 && info.PortalEligibleVehicleCount == 0)
                    sb.Append(" (assignment validation was skipped)");
            }

            return sb.ToString();
        }

        /// <summary>One-line assignment summary for the status bar (no per-date portal messages).</summary>
        public string BuildPortalAssignmentAuditSummaryCompact()
        {
            if (mcBatchRecords?.MCBatchAdditionalInfo == null || mcBatchRecords.MCBatchTrips == null)
                return string.Empty;

            int tripCount = 0;
            int missingD = 0;
            int missingV = 0;
            int invD = 0;
            int invV = 0;
            bool validationSkipped = false;

            foreach (MCBatchAdditionalInfo info in mcBatchRecords.MCBatchAdditionalInfo)
            {
                if (info.PortalEligibleDriverCount == 0 && info.PortalEligibleVehicleCount == 0)
                    validationSkipped = true;

                foreach (MCBatchTripRecord trip in mcBatchRecords.MCBatchTrips)
                {
                    if (trip.Date != info.MCBatchDate)
                        continue;
                    tripCount++;

                    if (IsMissingAssignment(trip.Driver))
                        missingD++;
                    else if (info.PortalEligibleDriverCount > 0 && !IsDriverInPortalList(info, trip.Driver))
                        invD++;

                    if (IsMissingAssignment(trip.Vehicle))
                        missingV++;
                    else if (info.PortalEligibleVehicleCount > 0 && !IsVehicleInPortalList(info, trip.Vehicle))
                        invV++;
                }
            }

            if (tripCount == 0)
                return string.Empty;

            if (validationSkipped && missingD == 0 && missingV == 0 && invD == 0 && invV == 0)
                return "Assignments not validated";

            if (missingD == 0 && missingV == 0 && invD == 0 && invV == 0)
                return "Assignments OK";

            var parts = new List<string>();
            if (missingD > 0)
                parts.Add(missingD + " no drv");
            if (missingV > 0)
                parts.Add(missingV + " no veh");
            if (invD > 0)
                parts.Add(invD + " INV-D");
            if (invV > 0)
                parts.Add(invV + " INV-V");
            return string.Join(", ", parts);
        }

        /// <summary>
        /// Mirrors browser flow: postback opens trip on ProcessATMBatches, then GET TripActuals.aspx
        /// with Referer = batch page (see DevTools curl).
        /// </summary>
        private async Task<string> FetchTripActualsPageHtmlAsync(MCLoginHandler handler, MCBatchTripRecord sampleTrip)
        {
            if (handler == null)
                return "";

            handler.UpdateTripActualsHeaders(MCLoginHandler.ProcessAtmBatchesUrl);

            if (sampleTrip != null && !string.IsNullOrWhiteSpace(sampleTrip.TripToken))
            {
                try
                {
                    HttpResponseMessage postRes = await handler.PostWithAuthRetryAsync(
                        MCLoginHandler.ProcessAtmBatchesUrl,
                        () => BuildOpenTripOnBatchFormContent(handler, sampleTrip.TripToken));

                    string postBody = await postRes.Content.ReadAsStringAsync();
                    if (MCLoginHandler.IsAuthRedirect(postRes))
                        throw new ModivcareSessionExpiredException();

                    string finalPath = postRes.RequestMessage?.RequestUri?.AbsolutePath ?? "";
                    if (finalPath.IndexOf("TripActuals.aspx", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        handler.GrabTokens(postBody);
                        _tripActualsFormHtml = postBody;
                        return postBody;
                    }

                    if (!string.IsNullOrEmpty(postBody))
                    {
                        handler.GrabTokens(postBody);
                        _tripActualsFormHtml = postBody;
                    }
                }
                catch (ModivcareSessionExpiredException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Console.WriteLine("MCTimeCorrection: sample trip open failed: " + ex.Message);
                }
            }
            else
            {
                Console.WriteLine("MCTimeCorrection: no sample trip token; GET TripActuals without batch postback.");
            }

            handler.UpdateTripActualsHeaders(MCLoginHandler.ProcessAtmBatchesUrl);
            try
            {
                HttpResponseMessage getRes = await handler.GetWithAuthRetryAsync(MCLoginHandler.TripActualsUrl);
                string html = await getRes.Content.ReadAsStringAsync();
                getRes.EnsureSuccessStatusCode();
                if (MCLoginHandler.IsAuthRedirect(getRes))
                    throw new ModivcareSessionExpiredException();

                if (!string.IsNullOrEmpty(html))
                {
                    handler.GrabTokens(html);
                    if (sampleTrip != null)
                        _tripActualsFormHtml = html;
                }
                return html;
            }
            catch (ModivcareSessionExpiredException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Console.WriteLine("MCTimeCorrection: TripActuals GET failed: " + ex.Message);
                return "";
            }
        }

        private static MyFormUrlEncodedContent BuildOpenTripOnBatchFormContent(MCLoginHandler handler, string tripToken)
        {
            return new MyFormUrlEncodedContent(new[]{
                new KeyValuePair<string, string>("__EVENTTARGET", tripToken),
                new KeyValuePair<string, string>("__EVENTARGUMENT", ""),
                new KeyValuePair<string, string>("__LASTFOCUS", ""),
                new KeyValuePair<string, string>("__VIEWSTATE", handler.ViewStateToken),
                new KeyValuePair<string, string>("__VIEWSTATEGENERATOR", handler.ViewStateGeneratorToken),
                new KeyValuePair<string, string>("__VIEWSTATEENCRYPTED", ""),
                new KeyValuePair<string, string>("__EVENTVALIDATION", handler.EventValidationToken),
                new KeyValuePair<string, string>("ctl00$cphMainContent$ddlTripSortBy", "VerifiedDenied"),
            });
        }
        /// <summary>
        /// When batch rows have no driver and/or vehicle, assign from Modivcare only (not WellRyde):
        /// trip download records, same-day batch siblings, and Trip Actuals dropdown lists.
        /// </summary>
        private void FillMissingDriversAndVehicles(MCBatchAdditionalInfo batchInfo)
        {
            if (batchInfo == null || mcBatchRecords?.MCBatchTrips == null)
                return;

            var vehiclesInUse = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var driverUsage = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (MCBatchTripRecord t in mcBatchRecords.MCBatchTrips)
            {
                if (t.Date != batchInfo.MCBatchDate)
                    continue;
                if (!IsMissingAssignment(t.Vehicle))
                    vehiclesInUse.Add(NormalizeName(t.Vehicle));
                if (!IsMissingAssignment(t.Driver))
                {
                    string dk = NormalizeName(t.Driver);
                    driverUsage[dk] = driverUsage.TryGetValue(dk, out int n) ? n + 1 : 1;
                }
            }

            foreach (MCBatchTripRecord trip in mcBatchRecords.MCBatchTrips)
            {
                if (trip.Date != batchInfo.MCBatchDate)
                    continue;
                if (!ShouldApplyCorrectionsToTrip(trip))
                    continue;

                bool driverWasMissing = IsMissingAssignment(trip.Driver);
                bool vehicleWasMissing = IsMissingAssignment(trip.Vehicle);

                if (driverWasMissing)
                {
                    string resolved = TryResolveDriverFromModivcareDownload(trip, batchInfo);
                    if (!IsMissingAssignment(resolved))
                    {
                        trip.Driver = resolved.Trim();
                        MarkTripAssignmentFix(trip, "ASG-D");
                    }
                }

                if (IsMissingAssignment(trip.Driver))
                {
                    string picked = PickDriverFromPortalList(batchInfo, driverUsage);
                    if (!IsMissingAssignment(picked))
                    {
                        trip.Driver = picked;
                        MarkTripAssignmentFix(trip, "ASG-D");
                        string dk = NormalizeName(picked);
                        driverUsage[dk] = driverUsage.TryGetValue(dk, out int n) ? n + 1 : 1;
                    }
                }

                if (vehicleWasMissing || IsMissingAssignment(trip.Vehicle))
                {
                    string vehicle = TryResolveVehicleForTrip(trip, batchInfo, vehiclesInUse);
                    if (!IsMissingAssignment(vehicle))
                    {
                        trip.Vehicle = vehicle;
                        MarkTripAssignmentFix(trip, "ASG-V");
                        vehiclesInUse.Add(NormalizeName(vehicle));
                    }
                }
            }
        }

        /// <summary>
        /// Batch grid may show a driver/vehicle who performed the trip, but Modivcare's trip form only
        /// lists insured/eligible options. Compare each row to lists loaded from a sample trip open.
        /// </summary>
        private void ValidateAssignmentsAgainstPortalLists(MCBatchAdditionalInfo addinfo)
        {
            if (addinfo == null || mcBatchRecords?.MCBatchTrips == null)
                return;

            bool haveDriverList = addinfo.MCDrivers != null && addinfo.MCDrivers.Count > 0;
            bool haveVehicleList = addinfo.MCAvailableVehicles != null && addinfo.MCAvailableVehicles.Count > 0;
            if (!haveDriverList && !haveVehicleList)
                return;

            foreach (MCBatchTripRecord trprcd in mcBatchRecords.MCBatchTrips)
            {
                if (trprcd.Date != addinfo.MCBatchDate)
                    continue;
                if (!ShouldApplyCorrectionsToTrip(trprcd))
                    continue;

                if (haveDriverList && !IsMissingAssignment(trprcd.Driver) &&
                    !IsDriverInPortalList(addinfo, trprcd.Driver))
                {
                    AppendTripAlert(trprcd, "INV-D");
                    trprcd.Status = "Fixable";
                }

                if (haveVehicleList && !IsMissingAssignment(trprcd.Vehicle) &&
                    !IsVehicleInPortalList(addinfo, trprcd.Vehicle))
                {
                    AppendTripAlert(trprcd, "INV-V");
                    trprcd.Status = "Fixable";
                }
            }
        }

        private static bool IsDriverInPortalList(MCBatchAdditionalInfo addinfo, string driverName)
        {
            foreach (MCDriver driver in addinfo.MCDrivers)
            {
                if (NamesEqual(driver.Driver, driverName))
                    return true;
            }
            return false;
        }

        private static bool IsVehicleInPortalList(MCBatchAdditionalInfo addinfo, string batchVehicle)
        {
            if (string.IsNullOrWhiteSpace(batchVehicle))
                return false;

            string key = NormalizeName(batchVehicle);
            foreach (MCAvailableVehicle vehicle in addinfo.MCAvailableVehicles)
            {
                if (NamesEqual(vehicle.Vehicle, batchVehicle))
                    return true;
                if (NamesEqual(vehicle.VehicleTag, batchVehicle))
                    return true;
                string label = vehicle.Vehicle ?? "";
                if (label.IndexOf(batchVehicle, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
                if (NormalizeName(label).IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        private void FinalizeDataQualityFixable(MCBatchAdditionalInfo batchInfo)
        {
            if (batchInfo == null || mcBatchRecords?.MCBatchTrips == null)
                return;

            foreach (MCBatchTripRecord trip in mcBatchRecords.MCBatchTrips)
            {
                if (trip.Date != batchInfo.MCBatchDate)
                    continue;
                if (!ShouldApplyCorrectionsToTrip(trip))
                    continue;

                bool needsFix = false;

                if (IsMissingAssignment(trip.Driver))
                {
                    AppendTripAlert(trip, "MIS-D");
                    needsFix = true;
                }

                if (IsMissingAssignment(trip.Vehicle))
                {
                    AppendTripAlert(trip, "MIS-V");
                    needsFix = true;
                }

                string alerts = trip.Alerts ?? "";
                if (alerts.IndexOf("ASG-", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    alerts.IndexOf("DRV", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    alerts.IndexOf("INV-", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    alerts.IndexOf("MIS-", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    alerts.IndexOf("RCT", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    alerts.IndexOf("SCH", StringComparison.OrdinalIgnoreCase) >= 0)
                    needsFix = true;

                if (needsFix)
                    trip.Status = "Fixable";
            }
        }

        private static bool IsMissingAssignment(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return true;
            string v = value.Trim();
            if (v.IndexOf("nbsp", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (v.IndexOf("select driver", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (v.IndexOf("select vehicle", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            return false;
        }

        private static bool NamesEqual(string a, string b) =>
            string.Equals(NormalizeName(a), NormalizeName(b), StringComparison.OrdinalIgnoreCase);

        private static string NormalizeName(string value) =>
            (value ?? "").Replace(" ", "");

        private static void MarkTripAssignmentFix(MCBatchTripRecord trip, string alertCode)
        {
            AppendTripAlert(trip, alertCode);
            trip.Status = "Fixable";
        }

        private static bool TripNumbersMatch(string batchTrip, string downloaded)
        {
            string a = NormalizeName(batchTrip);
            string b = NormalizeName(downloaded);
            if (a.Length == 0 || b.Length == 0)
                return false;
            if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
                return true;
            if (a.StartsWith("1", StringComparison.Ordinal) && a.Length > 1 &&
                string.Equals(a.Substring(1), b, StringComparison.OrdinalIgnoreCase))
                return true;
            if (b.StartsWith("1", StringComparison.Ordinal) && b.Length > 1 &&
                string.Equals(b.Substring(1), a, StringComparison.OrdinalIgnoreCase))
                return true;
            return false;
        }

        private static string TryResolveDriverFromModivcareDownload(MCBatchTripRecord trip,
            MCBatchAdditionalInfo batchInfo)
        {
            if (batchInfo.mcDownloadedTrips == null)
                return null;

            foreach (MCDownloadedTrip mc in batchInfo.mcDownloadedTrips)
            {
                if (!TripNumbersMatch(trip.Trip, mc.TripNumber))
                    continue;
                if (!IsMissingAssignment(mc.DriverNameParsed) &&
                    !mc.DriverNameParsed.Equals("Reserves", StringComparison.OrdinalIgnoreCase))
                    return mc.DriverNameParsed.Trim();
            }

            return null;
        }

        private string PickDriverFromPortalList(MCBatchAdditionalInfo batchInfo,
            Dictionary<string, int> driverUsage)
        {
            MCDriver best = null;
            int bestCount = -1;

            foreach (MCDriver driver in batchInfo.MCDrivers)
            {
                if (driver == null || IsMissingAssignment(driver.Driver) || IsMissingAssignment(driver.DriverTag))
                    continue;
                if (driver.Driver.IndexOf("Reserves", StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;

                string key = NormalizeName(driver.Driver);
                int count = driverUsage.TryGetValue(key, out int n) ? n : 0;
                if (count > bestCount)
                {
                    bestCount = count;
                    best = driver;
                }
            }

            if (best != null)
                return best.Driver.Trim();

            return null;
        }

        private string TryResolveVehicleForTrip(MCBatchTripRecord trip, MCBatchAdditionalInfo batchInfo,
            HashSet<string> vehiclesInUse)
        {
            if (!IsMissingAssignment(trip.Driver))
            {
                string fromSibling = FindVehicleForDriverOnDate(trip.Driver, trip.Date);
                if (!IsMissingAssignment(fromSibling))
                    return fromSibling.Trim();
            }

            if (batchInfo.MCAvailableVehicles != null)
            {
                foreach (MCAvailableVehicle v in batchInfo.MCAvailableVehicles)
                {
                    if (IsMissingAssignment(v.Vehicle))
                        continue;
                    string key = NormalizeName(v.Vehicle);
                    if (!vehiclesInUse.Contains(key))
                        return v.Vehicle.Trim();
                }
            }

            if (batchInfo.MCAvailableVehicles != null && batchInfo.MCAvailableVehicles.Count > 0 &&
                !IsMissingAssignment(batchInfo.MCAvailableVehicles[0].Vehicle))
                return batchInfo.MCAvailableVehicles[0].Vehicle.Trim();

            return null;
        }

        private string FindVehicleForDriverOnDate(string driver, string batchDate)
        {
            foreach (MCBatchTripRecord t in mcBatchRecords.MCBatchTrips)
            {
                if (t.Date != batchDate)
                    continue;
                if (!NamesEqual(t.Driver, driver))
                    continue;
                if (!IsMissingAssignment(t.Vehicle))
                    return t.Vehicle;
            }
            return null;
        }

        private void LoadAssignedVehicles(MCBatchAdditionalInfo batchInfos)
        {
            foreach (MCBatchTripRecord trprcd in mcBatchRecords.MCBatchTrips)
            {
                bool driverfound = false;
                if (trprcd.Date == batchInfos.MCBatchDate)
                {
                    if (!batchInfos.MCAssignedVehicles.Any())
                    {
                        MCAssignedVehicle mCAssignedVehicle = new MCAssignedVehicle();
                        mCAssignedVehicle.Driver = trprcd.Driver;
                        mCAssignedVehicle.Vehicle = trprcd.Vehicle;
                        batchInfos.MCAssignedVehicles.Add(mCAssignedVehicle);
                        //Console.WriteLine(mCAssignedVehicle.Driver + " : " + mCAssignedVehicle.Vehicle);
                        continue;
                    }
                }

                foreach (MCAssignedVehicle mcassvehicle in batchInfos.MCAssignedVehicles)
                {
                    if (mcassvehicle.Driver.Replace(" ", "") == trprcd.Driver.Replace(" ", ""))
                    {
                        driverfound = true; break;
                    }
                }

                if (driverfound)
                {
                    continue;
                }
                else
                {
                    MCAssignedVehicle mCAssignedVehicle = new MCAssignedVehicle();
                    mCAssignedVehicle.Driver = trprcd.Driver;
                    mCAssignedVehicle.Vehicle = trprcd.Vehicle;
                    batchInfos.MCAssignedVehicles.Add(mCAssignedVehicle);
                    //Console.WriteLine(mCAssignedVehicle.Driver + " : " + mCAssignedVehicle.Vehicle);
                }
            }
        }
        private void AdjustVehicles(MCBatchAdditionalInfo batchInfos)
        {
            Console.WriteLine(batchInfos.MCBatchDate);


                foreach (MCAssignedVehicle mcav in batchInfos.MCAssignedVehicles)
            {
                bool vehicleverified = false;
                //Console.WriteLine(mcav.Driver + " : " + mcav.Vehicle + " Found in assigned vehicles.");
                //Console.WriteLine("Checking if vehicle is verified...");
                foreach (MCAvailableVehicle mcavehicle in batchInfos.MCAvailableVehicles)
                {
                    if (mcavehicle.Vehicle.Replace(" ", "") == mcav.Vehicle.Replace(" ", ""))
                    {
                        //Console.WriteLine("Vehicle is verified.");
                        vehicleverified = true; break;
                    }
                }
                //if vehicle is not verified then assign a verified one
                if (!vehicleverified)
                {
                    //Console.WriteLine("Not verified!");

                    //search available vehicles for a vehicle
                    foreach (MCAvailableVehicle mcavehicle in batchInfos.MCAvailableVehicles)
                    {
                        bool vehicleinuse = false;
                        //check if vehicle is being used
                        foreach (MCAssignedVehicle mcav2 in batchInfos.MCAssignedVehicles)
                        {
                            if (mcav2.Vehicle.Replace(" ", "") == mcavehicle.Vehicle.Replace(" ", ""))
                            {
                                //vehicle is in use
                                vehicleinuse = true; break;
                            }
                        }
                        if (!vehicleinuse)
                        {
                            mcav.Vehicle = mcavehicle.Vehicle;
                            mcav.VehicleTag = mcavehicle.VehicleTag;
                            mcav.updated = true;
                            vehicleverified = true;
                        }
                    }

                    //if vehicle is still not verified 
                    if (!vehicleverified)
                    {
                        //Console.WriteLine("Still couldnt find a vehicle in MCTimeCorrections AdjustVehicles()");
                        //pick random vehicle from assigned vehicles and assign it
                        Random rnd = new Random();
                        int r = rnd.Next(batchInfos.MCAssignedVehicles.Count);
                        batchInfos.MCAssignedVehicles[r].Vehicle = mcav.Vehicle;
                        batchInfos.MCAssignedVehicles[r].VehicleTag = mcav.VehicleTag;
                        batchInfos.MCAssignedVehicles[r].updated = true;
                    }
                }
            }

            //update batch trip vehicles
            foreach (MCAssignedVehicle mcav in batchInfos.MCAssignedVehicles)
            {
                if (mcav.updated)
                {
                    foreach (MCBatchTripRecord batchTripRecord in mcBatchRecords.MCBatchTrips)
                    {
                        if (!ShouldApplyCorrectionsToTrip(batchTripRecord))
                            continue;

                        bool vehcileisgoodenough = false;
                        //if trip vehicle is not in availible vehicles then change it and set fixable
                        foreach (MCAvailableVehicle mcavailveh in batchInfos.MCAvailableVehicles)
                        {
                            if (batchTripRecord.Vehicle.Replace(" ", "") == mcavailveh.Vehicle.Replace(" ", ""))
                            {
                                vehcileisgoodenough = true;
                                break;
                            }
                        }
                        if(vehcileisgoodenough)
                        {
                            //Console.WriteLine("Drivers vehicle appears to be set: " + batchTripRecord.Driver + " : trip: " + batchTripRecord.TripFull);
                            continue;
                        }






                        if (batchTripRecord.Driver.Replace(" ", "") == mcav.Driver.Replace(" ", ""))
                        {
                            batchTripRecord.Vehicle = mcav.Vehicle;
                            batchTripRecord.Status = "Fixable";
                            batchTripRecord.Alerts = batchTripRecord.Alerts + " VEH";
                        }
                    }
                }
            }
            Console.WriteLine(batchInfos.MCAssignedVehicles.Count.ToString());
        }
        private void SetVehiclesToTrips()
        {
            foreach (MCBatchAdditionalInfo addinfo in mcBatchRecords.MCBatchAdditionalInfo)
            {
                foreach (MCAssignedVehicle assveh in addinfo.MCAssignedVehicles)
                {
                    foreach (MCAvailableVehicle mcavveh in addinfo.MCAvailableVehicles)
                    {
                        if (mcavveh.Vehicle.Replace(" ", "") == assveh.Vehicle.Replace(" ", ""))
                        {
                            assveh.VehicleTag = mcavveh.VehicleTag;
                            //Console.WriteLine("vehicle: " + mcavveh.Vehicle + " vehicle ID: " + mcavveh.VehicleTag);
                            break;
                        }
                    }
                }
            }

            foreach (MCBatchAdditionalInfo addinfo in mcBatchRecords.MCBatchAdditionalInfo)
            {
                foreach (MCAssignedVehicle assveh in addinfo.MCAssignedVehicles)
                {
                    foreach (MCDriver mcavveh in addinfo.MCDrivers)
                    {
                        if (mcavveh.Driver.Replace(" ", "") == assveh.Driver.Replace(" ", ""))
                        {
                            assveh.DriverTag = mcavveh.DriverTag;
                            //Console.WriteLine("vehicle: " + mcavveh.Vehicle + " vehicle ID: " + mcavveh.VehicleTag);
                            break;
                        }
                    }
                }
            }

            foreach (MCBatchTripRecord batchrcd in mcBatchRecords.MCBatchTrips)
            {
                foreach (MCBatchAdditionalInfo addinfo in mcBatchRecords.MCBatchAdditionalInfo)
                {
                    if (addinfo.MCBatchDate == batchrcd.Date)
                    {
                        foreach (MCAssignedVehicle assveh in addinfo.MCAssignedVehicles)
                        {
                            if (assveh.Driver.Replace(" ", "") == batchrcd.Driver.Replace(" ", ""))
                            {
                                batchrcd.AssignedVehicle = assveh;
                            }
                        }
                    }
                }
            }
        }
        public async Task GetTripActuals(MCLoginHandler handler, string date, bool clearListsBeforeParse = false)
        {
            string response = await FetchTripActualsPageHtmlAsync(handler, sampleTrip: null);
            ParseVehicles(response, date, clearListsBeforeParse);
        }
        public void ParseVehicles(string webresponse, string date, bool clearListsBeforeParse = false)
        {
            if (clearListsBeforeParse)
            {
                foreach (MCBatchAdditionalInfo batchInfo in mcBatchRecords.MCBatchAdditionalInfo)
                {
                    if (batchInfo.MCBatchDate == date)
                    {
                        batchInfo.MCDrivers.Clear();
                        batchInfo.MCAvailableVehicles.Clear();
                    }
                }
            }

            string driverslistbulk = GetContentBulkRegex(webresponse, "ctl00$cphMainContent$ddlDriver", "ctl00$cphMainContent$ddlSignatureReceived");
            if (string.IsNullOrWhiteSpace(driverslistbulk))
                driverslistbulk = GetContentBulkRegex(webresponse, "ctl00_cphMainContent_ddlDriver", "ctl00_cphMainContent_ddlSignatureReceived");
            if (string.IsNullOrWhiteSpace(driverslistbulk))
                driverslistbulk = GetContentBulkRegex(webresponse, "id=\"ctl00_cphMainContent_ddlDriver\"", "ctl00_cphMainContent_ddlSignatureReceived");

            string vehicleslistbulk = GetContentBulkRegex(webresponse, "ctl00$cphMainContent$ddlVehicle", "ctl00$cphMainContent$ddlDriver");
            if (string.IsNullOrWhiteSpace(vehicleslistbulk))
                vehicleslistbulk = GetContentBulkRegex(webresponse, "ctl00_cphMainContent_ddlVehicle", "ctl00_cphMainContent_ddlDriver");
            if (string.IsNullOrWhiteSpace(vehicleslistbulk))
                vehicleslistbulk = GetContentBulkRegex(webresponse, "id=\"ctl00_cphMainContent_ddlVehicle\"", "ctl00_cphMainContent_ddlDriver");

            //Vehicles found and inserted into list
            foreach (Match match in GetStrBetweenTags(vehicleslistbulk, @"<option value=", "</option>"))
            {
                string itemtmpstring = match.Value;
                //Console.WriteLine(itemtmpstring);//string contains vehicles and their ids
                itemtmpstring = itemtmpstring.Replace(@"<option value=", "");
                itemtmpstring = itemtmpstring.Replace(@"</option>", "");

                string[] itemtmpstringsubs = itemtmpstring.Split('>');
                string semifinalstring = itemtmpstringsubs[1];
                string finalVehicleID = Regex.Replace(itemtmpstringsubs[0], "^\"|\"$", "");
                //Console.WriteLine(finalVehicleID);
                //add driver name and id to class dictionary

                //Console.WriteLine(semifinalstring);
                if (semifinalstring.Contains("---- Select Vehicle ----"))
                {

                }
                else
                {

                    //Console.WriteLine(semifinalstring);
                    foreach (MCBatchAdditionalInfo batchInfo in mcBatchRecords.MCBatchAdditionalInfo)
                    {
                        if (batchInfo.MCBatchDate == date)
                        {
                            MCAvailableVehicle av = new MCAvailableVehicle();
                            av.VehicleTag = finalVehicleID;
                            av.Vehicle = semifinalstring;
                            batchInfo.MCAvailableVehicles.Add(av);
                            //Console.WriteLine("vehicle tag: " + finalVehicleID);
                        }
                    }




                    foreach (MCBatchAdditionalInfo batchInfo in mcBatchRecords.MCBatchAdditionalInfo)
                    {
                        if (batchInfo.MCBatchDate == date)
                        {
                            foreach (MCAssignedVehicle mcassv in batchInfo.MCAssignedVehicles)
                            {
                                if (mcassv.Vehicle.Replace(" ", "") == semifinalstring.Replace(" ", ""))
                                {
                                    mcassv.VehicleTag = finalVehicleID;
                                    //Console.WriteLine("vehicle tag: " + finalVehicleID);
                                }
                            }
                        }
                    }













                }
            }

            //grab any items that are selected
            foreach (Match match in GetStrBetweenTags(vehicleslistbulk, @"<option selected=""selected"" value=", "</option>"))
            {
                string itemtmpstring = match.Value;
                //Console.WriteLine(itemtmpstring);//string contains vehicles and their ids
                itemtmpstring = itemtmpstring.Replace(@"<option selected=""selected"" value=", "");
                itemtmpstring = itemtmpstring.Replace(@"</option>", "");

                string[] itemtmpstringsubs = itemtmpstring.Split('>');
                string semifinalstring = itemtmpstringsubs[1];
                string finalVehicleID = Regex.Replace(itemtmpstringsubs[0], "^\"|\"$", "");
                //Console.WriteLine(finalVehicleID);
                if (semifinalstring.Contains("---- Select Vehicle ----"))
                {

                }
                else
                {

                    foreach (MCBatchAdditionalInfo batchInfo in mcBatchRecords.MCBatchAdditionalInfo)
                    {
                        if (batchInfo.MCBatchDate == date)
                        {
                            MCAvailableVehicle av = new MCAvailableVehicle();
                            av.VehicleTag = finalVehicleID;
                            av.Vehicle = semifinalstring;
                            batchInfo.MCAvailableVehicles.Add(av);
                            //Console.WriteLine(av.Vehicle + " added to available vehicles.");
                        }
                    }



                    foreach (MCBatchAdditionalInfo batchInfo in mcBatchRecords.MCBatchAdditionalInfo)
                    {
                        if (batchInfo.MCBatchDate == date)
                        {
                            foreach (MCAssignedVehicle mcassv in batchInfo.MCAssignedVehicles)
                            {
                                if (mcassv.Vehicle.Replace(" ", "") == semifinalstring.Replace(" ", ""))
                                {
                                    mcassv.VehicleTag = finalVehicleID;
                                }
                            }
                        }
                    }





















                }
            }

            //Drivers found and inserted into list
            foreach (Match match in GetStrBetweenTags(driverslistbulk, @"<option value=", "</option>"))
            {
                string itemtmpstring = match.Value;
                //Console.WriteLine(itemtmpstring);//string contains drivers names and their ids
                itemtmpstring = itemtmpstring.Replace(@"<option value=", "");
                itemtmpstring = itemtmpstring.Replace(@"</option>", "");

                string[] itemtmpstringsubs = itemtmpstring.Split('>');
                string semifinalstring = itemtmpstringsubs[1];
                string finalDriverID = Regex.Replace(itemtmpstringsubs[0], "^\"|\"$", "");

                //Console.WriteLine(finalDriverID);

                if (semifinalstring.Contains("---- Select Driver ----"))
                {

                }
                else
                {




                    foreach (MCBatchAdditionalInfo batchInfo in mcBatchRecords.MCBatchAdditionalInfo)
                    {
                        if (batchInfo.MCBatchDate == date)
                        {
                            MCDriver driver = new MCDriver();
                            driver.Driver = semifinalstring;
                            driver.DriverTag = finalDriverID;
                            batchInfo.MCDrivers.Add(driver);
                            //Console.WriteLine("driver: " + driver.Driver + " driver tag: " + driver.DriverTag);
                        }

                    }















                }
            }

            //grab any items that are selected
            foreach (Match match in GetStrBetweenTags(driverslistbulk, @"<option selected=""selected"" value=", "</option>"))
            {
                string itemtmpstring = match.Value;
                //Console.WriteLine("Test: " + itemtmpstring);//string contains drivers names and their ids
                itemtmpstring = itemtmpstring.Replace(@"<option selected=""selected"" value=", "");
                itemtmpstring = itemtmpstring.Replace(@"</option>", "");

                string[] itemtmpstringsubs = itemtmpstring.Split('>');
                string semifinalstring = itemtmpstringsubs[1];
                string finalDriverID = Regex.Replace(itemtmpstringsubs[0], "^\"|\"$", "");

                //Console.WriteLine(finalDriverID);

                if (semifinalstring.Contains("---- Select Driver ----"))
                {

                }
                else
                {









                    foreach (MCBatchAdditionalInfo batchInfo in mcBatchRecords.MCBatchAdditionalInfo)
                    {
                        if (batchInfo.MCBatchDate == date)
                        {
                            MCDriver driver = new MCDriver();
                            driver.Driver = semifinalstring;
                            driver.DriverTag = finalDriverID;
                            batchInfo.MCDrivers.Add(driver);
                            //Console.WriteLine("driver: " + driver.Driver + " driver tag: " + driver.DriverTag);
                        }

                    }













                }
            }

        }
        public async Task GetDriverPageForSubit(MCLoginHandler handler, MCBatchTripRecord trip)
        {
            try
            {
                await FetchTripActualsPageHtmlAsync(handler, trip);
            }
            catch (ModivcareSessionExpiredException)
            {
                throw;
            }
            catch (Exception ex) 
            {
                Console.WriteLine(ex);
                Console.WriteLine("There was a problem in GetDriverPageForSubit.");
            }
        }
        public async Task GetTripActualsReponse(MCLoginHandler mchandler)
        {
            await FetchTripActualsPageHtmlAsync(mchandler, sampleTrip: null);
        }






        /// <summary>Modivcare Trip Download calendar only exposes about 8 days back and 30 days forward.</summary>
        private static bool IsModivcareTripDownloadDateSupported(DateTime tripDate)
        {
            DateTime today = DateTime.Today;
            DateTime d = tripDate.Date;
            return d >= today.AddDays(-8) && d <= today.AddDays(30);
        }

        /// <summary>Fills scheduled PU/DO from Modivcare or WellRyde downloads when the MC calendar step failed.</summary>
        private void EnsureScheduledTimesForBatchDate(MCBatchAdditionalInfo info)
        {
            if (info.mcDownloadedTrips == null)
                info.mcDownloadedTrips = new List<MCDownloadedTrip>();
            if (info.wrDownloadedTrips == null)
                info.wrDownloadedTrips = new List<WRDownloadedTrip>();

            foreach (MCBatchTripRecord trp in mcBatchRecords.MCBatchTrips)
            {
                if (trp.Date != info.MCBatchDate)
                    continue;
                if (TryParseBatchTime(trp.ScheduledPUTime, out _) && TryParseBatchTime(trp.ScheduledDOTime, out _))
                    continue;

                string tripKey = (trp.Trip ?? "").Replace(" ", "");
                MCDownloadedTrip mc = null;
                foreach (MCDownloadedTrip t in info.mcDownloadedTrips)
                {
                    if ((t.TripNumber ?? "").Replace(" ", "") == tripKey)
                    {
                        mc = t;
                        break;
                    }
                }
                if (mc != null)
                {
                    trp.ScheduledPUTime = NormalizeBatchTime(mc.PUTime);
                    trp.ScheduledDOTime = NormalizeBatchTime(mc.DOTime);
                    continue;
                }

                WRDownloadedTrip wr = null;
                foreach (WRDownloadedTrip t in info.wrDownloadedTrips)
                {
                    if ((t.TripNumber ?? "").Replace(" ", "") == tripKey)
                    {
                        wr = t;
                        break;
                    }
                }
                if (wr != null)
                {
                    trp.ScheduledPUTime = NormalizeBatchTime(wr.PUTime);
                    trp.ScheduledDOTime = NormalizeBatchTime(wr.DOTime);
                }
            }
        }

        internal static bool TryParseBatchTime(string value, out DateTime time)
        {
            time = default;
            if (string.IsNullOrWhiteSpace(value))
                return false;
            string t = value.Trim();
            if (t.IndexOf("nbsp", StringComparison.OrdinalIgnoreCase) >= 0)
                return false;

            string[] formats =
            {
                "HH:mm", "H:mm", "HH:mm:ss", "H:mm:ss",
                "hh:mm tt", "h:mm tt", "hh:mm:ss tt", "h:mm:ss tt",
            };
            if (DateTime.TryParseExact(t, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out time))
                return true;
            if (DateTime.TryParse(t, CultureInfo.InvariantCulture, DateTimeStyles.None, out time))
                return true;
            if (DateTime.TryParse(t, CultureInfo.CurrentCulture, DateTimeStyles.None, out time))
                return true;
            return false;
        }

        internal static string NormalizeBatchTime(string value)
        {
            if (!TryParseBatchTime(value, out DateTime time))
                return value ?? "";
            return time.ToString("HH:mm", CultureInfo.InvariantCulture);
        }

        private static string FormatTimeForModivcareSubmit(string time)
        {
            string normalized = NormalizeBatchTime(time);
            if (string.IsNullOrWhiteSpace(normalized))
                return "";
            return HttpUtility.UrlDecode(normalized.Replace(":", "%3A"));
        }

        private sealed class TripActualsLateReasonFields
        {
            public string PuLateText;
            public string PuLateCode;
            public string DoLateText;
            public string DoLateCode;
            public string ShowPuLate;
            public string ShowDoLate;
            public string LateDialogToShow;
        }

        private static TripActualsLateReasonFields ParseLateReasonFieldsFromTripActualsHtml(string html)
        {
            var fields = new TripActualsLateReasonFields
            {
                ShowPuLate = ParseTripActualsHiddenValue(html, "hdnShowPULateReasonFields") ?? "",
                ShowDoLate = ParseTripActualsHiddenValue(html, "hdnShowDOLateReasonFields") ?? "",
                LateDialogToShow = ParseTripActualsHiddenValue(html, "hdnLateDialogToShow") ?? "",
                PuLateText = ParseTripActualsHiddenValue(html, "hdnPULateReasonSelectedText") ?? "",
                PuLateCode = ParseTripActualsHiddenValue(html, "hdnPULateReasonSelectedCode") ?? "",
                DoLateText = ParseTripActualsHiddenValue(html, "hdnDOLateReasonSelectedText") ?? "",
                DoLateCode = ParseTripActualsHiddenValue(html, "hdnDOLateReasonSelectedCode") ?? "",
            };

            if (IsTruthyHiddenFlag(fields.ShowPuLate) && string.IsNullOrWhiteSpace(fields.PuLateCode) &&
                TryParseFirstTripActualsDropdownOption(html, "ddlPULateReason", out string puCode, out string puText))
            {
                fields.PuLateCode = puCode;
                fields.PuLateText = puText;
            }

            if (IsTruthyHiddenFlag(fields.ShowDoLate) && string.IsNullOrWhiteSpace(fields.DoLateCode) &&
                TryParseFirstTripActualsDropdownOption(html, "ddlDOLateReason", out string doCode, out string doText))
            {
                fields.DoLateCode = doCode;
                fields.DoLateText = doText;
            }

            return fields;
        }

        private static bool IsTruthyHiddenFlag(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            value = value.Trim();
            return value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                   value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
                   value.Equals("yes", StringComparison.OrdinalIgnoreCase);
        }

        private static string ParseTripActualsHiddenValue(string html, string fieldSuffix)
        {
            if (string.IsNullOrEmpty(html) || string.IsNullOrEmpty(fieldSuffix))
                return null;

            Match m = Regex.Match(html,
                Regex.Escape(fieldSuffix) + @"[^>]*\bvalue\s*=\s*""([^""]*)""",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (m.Success)
                return HttpUtility.HtmlDecode(m.Groups[1].Value);

            m = Regex.Match(html,
                @"\bvalue\s*=\s*""([^""]*)""[^>]*" + Regex.Escape(fieldSuffix),
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (m.Success)
                return HttpUtility.HtmlDecode(m.Groups[1].Value);

            return null;
        }

        private static bool TryParseFirstTripActualsDropdownOption(string html, string ddlNameFragment,
            out string code, out string text)
        {
            code = "";
            text = "";
            if (string.IsNullOrEmpty(html))
                return false;

            int idx = html.IndexOf(ddlNameFragment, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return false;
            int end = Math.Min(html.Length, idx + 6000);
            string bulk = html.Substring(idx, end - idx);

            foreach (Match match in Regex.Matches(bulk, @"<option\s+value=""([^""]*)""[^>]*>([^<]*)</option>",
                         RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                string val = HttpUtility.HtmlDecode(match.Groups[1].Value ?? "").Trim();
                string label = HttpUtility.HtmlDecode(match.Groups[2].Value ?? "").Trim();
                if (string.IsNullOrEmpty(val) || val == "0")
                    continue;
                if (label.IndexOf("select", StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;

                code = val;
                text = label;
                return true;
            }

            return false;
        }

        internal static bool TryParseBatchServiceDate(string value, out DateTime date)
        {
            date = default;
            if (string.IsNullOrWhiteSpace(value))
                return false;

            string s = value.Trim();
            if (DateTime.TryParse(s, CultureInfo.CurrentCulture, DateTimeStyles.None, out date))
                return true;
            if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
                return true;

            string[] parts = s.Split(new[] { '/', '-' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3)
                return false;

            if (!int.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int m) ||
                !int.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int d))
                return false;

            string yearToken = parts[2].Trim().Split(' ')[0];
            if (!int.TryParse(yearToken, NumberStyles.Integer, CultureInfo.InvariantCulture, out int y))
                return false;
            if (y < 100)
                y += 2000;

            try
            {
                date = new DateTime(y, m, d);
                return true;
            }
            catch (ArgumentOutOfRangeException)
            {
                return false;
            }
        }

        private DateTime GetParsedDate(MCBatchAdditionalInfo additionalinfo)
        {
            System.Threading.Thread.Sleep(1000);
            if (TryParseBatchServiceDate(additionalinfo.MCBatchDate, out DateTime parsedDate))
                return parsedDate;

            throw new FormatException(
                "Batch service date is not valid: \"" + (additionalinfo.MCBatchDate ?? "") + "\".");
        }
        public string GetContentBulkRegex(string batchcontent, string beginstring, string endstring)
        {
            int beginFrom = batchcontent.IndexOf(beginstring) + beginstring.Length;
            int goTo = batchcontent.LastIndexOf(endstring);
            String bulk_string = batchcontent.Substring(beginFrom, goTo - beginFrom);

            return bulk_string;
        }
        public MatchCollection GetStrBetweenTags(string value, string startTag, string endTag)
        {
            var regex = new Regex(startTag + "(.*)" + endTag);
            return regex.Matches(value);
        }
        public void GrabBatchIDs(string resp)
        {
            //mcBatchRecords = new MCBatchRecords(); //  <---------NEW
            mcBatchRecords.MCBatchLinks.Clear(); //  <---------NEW

            try
            {
                var r = new Regex(@"doPostBack\(\&#39;(.*)");
                foreach (Match m in r.Matches(resp))
                {
                    //Console.WriteLine(HttpUtility.UrlDecode(m.Value));
                    SeperateBatchValues(m.Value);
                }
            }
            catch
            {
                Console.WriteLine("No batches found!");
            }
        }
        public void SeperateBatchValues(string Batchstring)
        {
            MCBatchLink mcbatchlink = new MCBatchLink();

            //grab batch token ie ctl00$cphMainContent$gvOpenBatchList$ctl02$ctl00
            string _pre_batch_token = Batchstring.Replace("doPostBack(&#39;", "");
            _pre_batch_token = _pre_batch_token.Replace("&#39;,&#39;&#39;)\"", "");

            string decoded_batch_begin = "";
            string decoded_batch_end = ">";
            int pvsFrom = _pre_batch_token.IndexOf(decoded_batch_begin) + decoded_batch_begin.Length;
            int pvsTo = _pre_batch_token.IndexOf(decoded_batch_end);
            String batch_token = _pre_batch_token.Substring(pvsFrom, pvsTo - pvsFrom);

            if (batch_token.Contains("style"))
            {
                int index = batch_token.IndexOf("style");
                string result = batch_token.Substring(0, index);
                mcbatchlink.BatchLinkToken = result.Trim();
            }
            else
            {
                if (batch_token.Contains("Market"))
                {
                    return;
                }
                else
                {
                    mcbatchlink.BatchLinkToken = batch_token;
                    //Console.WriteLine(batch_token);
                }

            }



            //grab batch id
            string batch_id_begin = HttpUtility.UrlDecode(">");
            string batchid_end = HttpUtility.UrlDecode("<");
            int bidFrom = Batchstring.IndexOf(batch_id_begin) + batch_id_begin.Length;
            int bidTo = Batchstring.IndexOf(batchid_end);
            String _batch_id_token = Batchstring.Substring(bidFrom, bidTo - bidFrom);
            mcbatchlink.BatchID = _batch_id_token;


            //Console.WriteLine(_batch_id_token);

            //get rest of table data
            string[] subs = Batchstring.Split(new[] { @"</td><td>", "</td>" },
                   StringSplitOptions.RemoveEmptyEntries);

            mcbatchlink.CreateDate = subs[1];
            mcbatchlink.CreatedBy = subs[2];
            mcbatchlink.TripCount = subs[3];
            mcbatchlink.FailedTripCount = subs[4];
            mcbatchlink.RequiresAttention = subs[5];
            mcbatchlink.TotalBilledAmount = subs[6];

            mcBatchRecords.MCBatchLinks.Add(mcbatchlink);
        }
        public int ReturnAlertCount()
        {
            int alertcount = 0;
            foreach (MCBatchTripRecord mctrprc in mcBatchRecords.MCBatchTrips)
            {
               alertcount += GetNumOfWords(mctrprc.Alerts);
            }
            return alertcount;
        }
        private int GetNumOfWords(string myword)
        {
           
            if (myword == null)
            {
                return 0;
            }
            myword = myword.Trim();
            int l, wrd; // Declare variables for string traversal and word count

            l = 0; // Initialize a variable for string traversal
            wrd = 1; // Initialize word count assuming at least one word exists

            /* Loop till the end of the string */
            while (l <= myword.Length - 1)
            {
                /* Check whether the current character is whitespace, newline, or tab character */
                if (myword[l] == ' ' || myword[l] == '\n' || myword[l] == '\t')
                {
                    wrd++; // Increment word count if whitespace, newline, or tab character is found
                }

                l++; // Move to the next character in the string
            }
            //Console.WriteLine(wrd.ToString());
            return wrd;

        }




    }
}
public class MyFormUrlEncodedContent : ByteArrayContent
{
    public MyFormUrlEncodedContent(IEnumerable<KeyValuePair<string, string>> nameValueCollection)
        : base(MyFormUrlEncodedContent.GetContentByteArray(nameValueCollection))
    {
        base.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded");
    }
    private static byte[] GetContentByteArray(IEnumerable<KeyValuePair<string, string>> nameValueCollection)
    {
        if (nameValueCollection == null)
        {
            throw new ArgumentNullException("nameValueCollection");
        }
        StringBuilder stringBuilder = new StringBuilder();
        foreach (KeyValuePair<string, string> current in nameValueCollection)
        {
            if (stringBuilder.Length > 0)
            {
                stringBuilder.Append('&');
            }
            stringBuilder.Append(MyFormUrlEncodedContent.Encode(current.Key));
            stringBuilder.Append('=');
            stringBuilder.Append(MyFormUrlEncodedContent.Encode(current.Value));
        }
        return Encoding.Default.GetBytes(stringBuilder.ToString());
    }
    private static string Encode(string data)
    {
        if (string.IsNullOrEmpty(data))
        {
            return string.Empty;
        }
        return System.Net.WebUtility.UrlEncode(data).Replace("%20", "+");
    }
}