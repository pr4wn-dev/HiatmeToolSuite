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

        private WRTripDownloader wrtdler;
        public HttpContent testformContent { get; private set; }
        public HttpContent dummyformContent { get; private set; }

        public MCCalculateAccuracies MCCalc { get; private set; }

        public MCTimeCorrection(MaterialComboBox cb)
        {
            mcBatchRecords = new MCBatchRecords();
            mctripdler = new MCTripDownloader();
            wrtdler = new WRTripDownloader();
            MaterialComboBox = new MaterialComboBox();
            MaterialComboBox = cb;
        }

        //Finding Batches
        public async Task GetBatchLinks(MCLoginHandler mCLogin, bool IDs)
        {
            HttpResponseMessage resmsg = mCLogin.Client.GetAsync("https://transportationco.logisticare.com/ProcessATMBatches.aspx").Result;
            var response = await resmsg.Content.ReadAsStringAsync();

            try
            {
                //try to update tokens
                mCLogin.GrabTokens(response);
            if (IDs == true){
                GrabBatchIDs(response);
            }else{

            }
                string responseUri = resmsg.RequestMessage.RequestUri.ToString();
                //Console.WriteLine("Location: " + responseUri);
                if (responseUri.Contains("Login.aspx"))
                {
                    await mCLogin.ResetConnection();
                    await GetBatchLinks(mCLogin, IDs);
                }
            }
            catch
            {
                Console.WriteLine("There was a problem retrieving location.");
                //await mCLogin.ResetConnection();
                //await GetBatchLinks(mCLogin, true);
            }
            
            return;
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

            try
            {
                mcloginhandler.GrabTokens(response);
                string responseUri = res.RequestMessage.RequestUri.ToString();
                if (responseUri.Contains("login.aspx")){
                    mcloginhandler.Connected = false;
                }
            }catch{
                Console.WriteLine("There was a problem retrieving location.");
                mcloginhandler.Connected = false;
            }

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
                    //check trip for errors
                    if (stringwitherrors.Contains("Red"))
                    {
                        triprcd.TripErrors = true;
                    }else{
                        triprcd.TripErrors = false;
                    }
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



        public async Task InitializeCorrections(MCLoginHandler mcLoginHandler, WRLoginHandler wrLoginHandler, MCBatchLink mylink)
        {
            mcBatchRecords = new MCBatchRecords(); //  <---------NEW

            //Console.WriteLine("Starting corrections..");
            await GetBatchPage(mcLoginHandler, mylink.BatchLinkToken, true);
            
            //load drivers and vehicles
            foreach (MCBatchAdditionalInfo addinfo in mcBatchRecords.MCBatchAdditionalInfo)
            {
                foreach (MCBatchTripRecord trprcd in mcBatchRecords.MCBatchTrips)
                {
                    if (trprcd.Date == addinfo.MCBatchDate)
                    {
                        await LoadAvailableVehicles(trprcd, mcLoginHandler, trprcd.Date);


                        /////////////////////////////////////
                        ///
                        ///
                        ///
                        ///
                        //
                        ///
                        ///
                        ///
                        ///
                        ///
                        ///
                        ///
                        /////////////////////////////////////////







                        ///WORKING ON SELECTING A DIFFERENT DRIVER IF TRIP DRIVER IS NOT FOUND


                        bool driverfound = false;
                        foreach (MCDriver driver in addinfo.MCDrivers)
                        {
                            if (driver.Driver == trprcd.Driver)
                            {
                                driverfound = true;
                                Console.WriteLine("Driver Found!");
                            }
                        }

                        if (!driverfound)
                        {
                            trprcd.Alerts = trprcd.Alerts + " DRV";
                        }





                        break;
                    }
                }
            }

            tempbatchdetaillist = new List<MCBatchAdditionalInfo>();
           
            foreach (MCBatchAdditionalInfo addinfo in mcBatchRecords.MCBatchAdditionalInfo)
            {
                TimeSpan numofdays = DateTime.Now - GetParsedDate(addinfo);
                /*
                if (numofdays.TotalDays > 8)//date less than 8 days old
                {
                    await GetModivcareTripsForBatchDates(mcLoginHandler, GetParsedDate(addinfo), addinfo);
                    await GetWellrydesBackupTripsForBatchDates(wrLoginHandler, GetParsedDate(addinfo), addinfo);
                    RevampedCorrectModivcareTimes(mcBatchRecords.MCBatchTrips, addinfo);
                }
                else
                {
                    await GetWellrydesTripsForBatchDates(wrLoginHandler, GetParsedDate(addinfo), addinfo);
                    RevampedCorrectModivcareTimes(mcBatchRecords.MCBatchTrips, addinfo);
                }
                */
                if (MaterialComboBox.SelectedIndex == 1)
                {
                    await GetModivcareTripsForBatchDates(mcLoginHandler, GetParsedDate(addinfo), addinfo);
                    await GetWellrydesBackupTripsForBatchDates(wrLoginHandler, GetParsedDate(addinfo), addinfo);
                    RevampedCorrectModivcareTimes(mcBatchRecords.MCBatchTrips, addinfo);
                }
                else
                {
                    await GetWellrydesTripsForBatchDates(wrLoginHandler, GetParsedDate(addinfo), addinfo);
                    RevampedCorrectModivcareTimes(mcBatchRecords.MCBatchTrips, addinfo);
                }
            }
            mcBatchRecords.MCBatchAdditionalInfo = tempbatchdetaillist;

            foreach (MCBatchAdditionalInfo batchInfo in mcBatchRecords.MCBatchAdditionalInfo)
            {
                LoadAssignedVehicles(batchInfo);
                AdjustVehicles(batchInfo);
            }
            SetVehiclesToTrips();
        }

        public List<MCDriver> CalculateAccuracies()
        {
            MCCalc = new MCCalculateAccuracies();

            foreach (MCBatchAdditionalInfo addinfo in mcBatchRecords.MCBatchAdditionalInfo)
            {
                MCCalc.CalculateAccuracies(mcBatchRecords.MCBatchTrips, addinfo);
            }
            List<MCDriver> driverstemp = new List<MCDriver>();
            driverstemp = MCCalc.ReturnAccuracies(mcBatchRecords.MCBatchAdditionalInfo);

            List<MCDriver> drivers = new List<MCDriver>();
            foreach (MCDriver driver in driverstemp)
            {
                if (driver.Triplegs != 0)
                {
                    driver.AccuracyPercent = Math.Round((double)driver.Accuracies / (double)driver.Triplegs * 100);
                    drivers.Add(driver);
                    //Console.WriteLine("Driver: " + driver.Driver + ". Accuracies: " + driver.Accuracies + ". Inaccuracies: " + driver.Inaccuracies + ". Trip Legs: " + driver.Triplegs + ". Accuracy: " + driver.AccuracyPercent.ToString() + "%");

                }
            }

                return drivers;
        }

        List<MCBatchAdditionalInfo> tempbatchdetaillist;
        private async Task GetModivcareTripsForBatchDates(MCLoginHandler mcrLoginHandler, DateTime mcdate, MCBatchAdditionalInfo additionalInfo)
        {
                MCBatchAdditionalInfo mcbd = additionalInfo;

            //download modivcare trips for date and store them in the batch details
            Console.WriteLine("gathering trips for batch..");
            mcbd.mcDownloadedTrips = new List<MCDownloadedTrip>();
            mcbd.mcDownloadedTrips = await mctripdler.DownloadTripRecords(mcdate, mcrLoginHandler);

            if (mcbd.mcDownloadedTrips != null)
                {
                    foreach (MCDownloadedTrip mcrtr in mcbd.mcDownloadedTrips) 
                    {
                        //Console.WriteLine(mcrtr.Date + ": " + mcrtr.ClientFullName + " " + mcrtr.TripNumber);
                        foreach (MCBatchTripRecord mcbtr in mcBatchRecords.MCBatchTrips)
                        {                    
                            if (mcbtr.Trip.Replace(" ","") == mcrtr.TripNumber.Replace(" ", ""))
                            {
                            mcbtr.ScheduledPUTime = mcrtr.PUTime;
                            mcbtr.ScheduledDOTime = mcrtr.DOTime;
                            }
                        }
                    }
                    tempbatchdetaillist.Add(mcbd);
                }
                else
                {
                    Console.WriteLine("durrrr..");
                }
            Console.WriteLine("finished gathering trips!");
        }
        private async Task GetWellrydesTripsForBatchDates(WRLoginHandler wrrLoginHandler, DateTime mcdate, MCBatchAdditionalInfo additionalInfo)
        {
            MCBatchAdditionalInfo mcbd = additionalInfo;
            //WRTripDownloader wrtder = new WRTripDownloader();

            //List<MCBatchAdditionalInfo> tempbatchdetaillist = new List<MCBatchAdditionalInfo>();

            //download wellryde trips for date and store them in the batch details
            mcbd.wrDownloadedTrips = new List<WRDownloadedTrip>();
            mcbd.wrDownloadedTrips = await wrtdler.DownloadTripRecords(mcdate.DayOfWeek + ", " + CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(mcdate.Month) + " " + mcdate.Day + ", " + mcdate.Year, mcdate.Day, mcdate.Year, wrrLoginHandler);

            if (mcbd.wrDownloadedTrips != null)
                {
                    Console.WriteLine("gathering trips for batch..");
                    foreach (WRDownloadedTrip wrrtr in mcbd.wrDownloadedTrips) // error 'Object reference not set to an instance of an object.'
                    {
                        //Console.WriteLine(mcdate.DayOfWeek + ", " + CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(mcdate.Month) + " " + mcdate.Day + ", " + mcdate.Year + ": " + wrrtr.ClientName + " " + wrrtr.TripNumber);
                        foreach (MCBatchTripRecord mcbtr in mcBatchRecords.MCBatchTrips)
                        {
                       // Console.WriteLine(mcbtr.Trip + " : " + wrrtr.TripNumber);
                            if (mcbtr.Trip.Replace(" ", "") == wrrtr.TripNumber.Replace(" ", ""))
                            {
                                //Console.WriteLine(mcbtr.Trip + " : " + wrrtr.TripNumber);
                                mcbtr.ScheduledPUTime = wrrtr.PUTime;
                                mcbtr.ScheduledDOTime = wrrtr.DOTime;
                             }
                        }
                    }

                    tempbatchdetaillist.Add(mcbd);
                }
                else
                {
                    Console.WriteLine("durrrr..");
                }
            
           // mcBatchRecords.MCBatchAdditionalInfo = tempbatchdetaillist;
            //Console.WriteLine("finished gathering trips!");
        }
        private async Task GetWellrydesBackupTripsForBatchDates(WRLoginHandler wrrLoginHandler, DateTime mcdate, MCBatchAdditionalInfo additionalInfo)
        {
            MCBatchAdditionalInfo mcbd = additionalInfo;
            //WRTripDownloader wrtder = new WRTripDownloader();

            //List<MCBatchAdditionalInfo> tempbatchdetaillist = new List<MCBatchAdditionalInfo>();

            //download wellryde trips for date and store them in the batch details
            mcbd.wrDownloadedTrips = new List<WRDownloadedTrip>();
            mcbd.wrDownloadedTrips = await wrtdler.DownloadTripRecords(mcdate.DayOfWeek + ", " + CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(mcdate.Month) + " " + mcdate.Day + ", " + mcdate.Year, mcdate.Day, mcdate.Year, wrrLoginHandler);
            /*
            if (mcbd.wrDownloadedTrips != null)
            {
                Console.WriteLine("gathering trips for batch..");
                foreach (WRDownloadedTrip wrrtr in mcbd.wrDownloadedTrips) // error 'Object reference not set to an instance of an object.'
                {
                    //Console.WriteLine(mcdate.DayOfWeek + ", " + CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(mcdate.Month) + " " + mcdate.Day + ", " + mcdate.Year + ": " + wrrtr.ClientName + " " + wrrtr.TripNumber);
                    foreach (MCBatchTripRecord mcbtr in mcBatchRecords.MCBatchTrips)
                    {
                        // Console.WriteLine(mcbtr.Trip + " : " + wrrtr.TripNumber);
                        if (mcbtr.Trip.Replace(" ", "") == wrrtr.TripNumber.Replace(" ", ""))
                        {
                            //Console.WriteLine(mcbtr.Trip + " : " + wrrtr.TripNumber);
                            mcbtr.ScheduledPUTime = wrrtr.PUTime;
                            mcbtr.ScheduledDOTime = wrrtr.DOTime;
                        }
                    }
                }

                tempbatchdetaillist.Add(mcbd);
         
            }
            else
            {
                Console.WriteLine("durrrr..");
            }
               */
            // mcBatchRecords.MCBatchAdditionalInfo = tempbatchdetaillist;
            //Console.WriteLine("finished gathering trips!");
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
                                                if ((driverputime - schedputime).TotalMinutes <= 30)//driver time is good
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
                                    if ((schedputime - driverputime).TotalMinutes <= 30)//driver time is good
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
                                    if ((driverputime - schedputime).TotalMinutes <= 15)//driver time is good
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
                                    if ((schedputime - driverputime).TotalMinutes <= 30)//driver time is good
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
                                    if ((driverputime - schedputime).TotalMinutes <= 15)//driver time is good
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
                                    if ((schedputime - driverputime).TotalMinutes <= 30)//driver time is good
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
                                    if ((driverputime - schedputime).TotalMinutes <= 10)//driver time is good
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
                                    if ((schedputime - driverputime).TotalMinutes <= 30)//driver time is good
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
                                    if ((driverputime - schedputime).TotalMinutes <= 5)//driver time is good
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
                                    if ((schedputime - driverputime).TotalMinutes <= 30)//driver time is good
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
                                    if ((driverputime - schedputime).TotalMinutes <= 30)//driver time is good
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
                                    if ((driverputime - schedputime).TotalMinutes <= 25)//driver time is good
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
                                    if ((driverputime - schedputime).TotalMinutes <= 20)//driver time is good
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
                                    if ((driverputime - schedputime).TotalMinutes <= 10)//driver time is good
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
                                    if ((driverputime - schedputime).TotalMinutes <= 5)//driver time is good
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
                                    if ((driverputime - schedputime).TotalMinutes <= 25)//driver time is good
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
                                    if ((driverputime - schedputime).TotalMinutes <= 20)//driver time is good
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
                                    if ((driverputime - schedputime).TotalMinutes <= 10)//driver time is good
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
                                    if ((driverputime - schedputime).TotalMinutes <= 5)//driver time is good
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
                                    if ((driverputime - schedputime).TotalMinutes <= 25)//driver time is good
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
                                    if ((driverputime - schedputime).TotalMinutes <= 20)//driver time is good
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
                                    if ((driverputime - schedputime).TotalMinutes <= 10)//driver time is good
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
                                    if ((driverputime - schedputime).TotalMinutes <= 5)//driver time is good
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
        private void RevampedCorrectModivcareTimes(List<MCBatchTripRecord> batchtrips, MCBatchAdditionalInfo dledtripsaddinfo)
        {
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
                    DateTime ridercalltime = new DateTime();


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

                    double milemins = (int)(5 + miles);
                    if (trprcd.RiderCallTime.Contains("nbsp"))
                    {

                    }
                    else
                    {
                        bool invalidrct = false;
                        double triptotalmins = (driverdotime - schedputime).TotalMinutes;
                        if (driverputime != schedputime)
                        {
                            trprcd.SuggestedPUTime = schedputime.ToString("HH:mm");
                            invalidrct = true;
                        }
                        else
                        {
                            trprcd.SuggestedPUTime = driverputime.ToString("HH:mm");
                        }


                        if (driverdotime < schedputime + new TimeSpan(0, (int)milemins, 0))
                        {
                            DateTime suggtime = schedputime + new TimeSpan(0, (int)milemins, 0);
                            trprcd.SuggestedDOTime = suggtime.ToString("HH:mm");
                            invalidrct = true;
                        }

                        if (driverdotime >= schedputime + new TimeSpan(0, (int)milemins, 0))
                        {
                            if (driverdotime < schedputime + new TimeSpan(0, (int)milemins + 30, 0))
                            {
                                trprcd.SuggestedDOTime = driverdotime.ToString("HH:mm");
                            }
                            else
                            {
                                DateTime suggtime = schedputime + new TimeSpan(0, (int)milemins, 0);
                                trprcd.SuggestedDOTime = suggtime.ToString("HH:mm");
                                invalidrct = true;
                            }
                        }

                        if (invalidrct)
                        {
                            trprcd.Alerts = trprcd.Alerts + " RCT";
                            trprcd.Status = "Fixable";
                        }

                        






                        /*
                        ridercalltime = DateTime.ParseExact(trprcd.RiderCallTime, "HH:mm", CultureInfo.InvariantCulture);
                        bool invalidrct = false;
                        if (driverputime != ridercalltime)
                        {
                            trprcd.SuggestedPUTime = ridercalltime.ToString("HH:mm");
                            invalidrct = true;
                        }
                        else
                        {
                            trprcd.SuggestedPUTime = driverputime.ToString("HH:mm");
                        }
                        
                        if (driverdotime >= ridercalltime + new TimeSpan(0, (int)milemins, 0))
                        {
                            //ridercalltime = ridercalltime + new TimeSpan(0, (int)milemins, 0);
                            trprcd.SuggestedDOTime = driverdotime.ToString("HH:mm");
                        }
                        else
                        {
                            DateTime suggtime = ridercalltime + new TimeSpan(0, (int)milemins, 0);
                            trprcd.SuggestedDOTime = suggtime.ToString("HH:mm");
                            invalidrct = true;

                        }

                        if (invalidrct)
                        {
                            trprcd.Alerts = trprcd.Alerts + " RCT";
                            trprcd.Status = "Fixable";
                        }
                        */
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
                                    if ((schedputime - driverputime).TotalMinutes <= 30)//driver time is good
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
                                    if ((driverputime - schedputime).TotalMinutes <= 15)//driver time is good
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
                                    if ((schedputime - driverputime).TotalMinutes <= 30)//driver time is good
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
                                    if ((driverputime - schedputime).TotalMinutes <= 15)//driver time is good
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
                                    if ((schedputime - driverputime).TotalMinutes <= 30)//driver time is good
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
                                    if ((driverputime - schedputime).TotalMinutes <= 10)//driver time is good
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
                                    if ((schedputime - driverputime).TotalMinutes <= 30)//driver time is good
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
                                    if ((driverputime - schedputime).TotalMinutes <= 5)//driver time is good
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
                                    if ((schedputime - driverputime).TotalMinutes <= 30)//driver time is good
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
                    else
                    {











                        bool nullputime = false;

                        if (schedputime.TimeOfDay.Ticks == 0)
                        {
                            foreach (WRDownloadedTrip wrdt in dledtripsaddinfo.wrDownloadedTrips)
                            {
                                if (wrdt.TripNumber == trprcd.Trip)
                                {
                                    trprcd.Alerts = trprcd.Alerts + " LPU";
                                    trprcd.SuggestedPUTime = wrdt.PUTime;
                                    trprcd.SuggestedDOTime = wrdt.DOTime;
                                    nullputime = true;
                                    trprcd.Status = "Fixable";
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
                                        if ((driverputime - schedputime).TotalMinutes <= 30)//driver time is good
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
                                        if ((driverputime - schedputime).TotalMinutes <= 25)//driver time is good
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
                                        if ((driverputime - schedputime).TotalMinutes <= 20)//driver time is good
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
                                        if ((driverputime - schedputime).TotalMinutes <= 10)//driver time is good
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
                                        if ((driverputime - schedputime).TotalMinutes <= 5)//driver time is good
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
                                    if ((driverputime - schedputime).TotalMinutes <= 30)//driver time is good
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
                                    if ((driverputime - schedputime).TotalMinutes <= 25)//driver time is good
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
                                    if ((driverputime - schedputime).TotalMinutes <= 20)//driver time is good
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
                                    if ((driverputime - schedputime).TotalMinutes <= 10)//driver time is good
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
                                    if ((driverputime - schedputime).TotalMinutes <= 5)//driver time is good
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
                                    if ((driverputime - schedputime).TotalMinutes <= 30)//driver time is good
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
                                    if ((driverputime - schedputime).TotalMinutes <= 25)//driver time is good
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
                                    if ((driverputime - schedputime).TotalMinutes <= 20)//driver time is good
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
                                    if ((driverputime - schedputime).TotalMinutes <= 10)//driver time is good
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
                                    if ((driverputime - schedputime).TotalMinutes <= 5)//driver time is good
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

            foreach (MCBatchTripRecord trprcd in batchtrips)
            {
                if (trprcd.Alerts == null)
                {
                    trprcd.Status = "Passed";
                }
            }
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

            if (trip.RiderCallTime.Contains("nbsp"))//object reference error
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
                new KeyValuePair<string, string>(maincontentstring + "hdnPULateReasonSelectedText", ""),
                new KeyValuePair<string, string>(maincontentstring + "hdnPULateReasonSelectedCode", ""),
                new KeyValuePair<string, string>(maincontentstring + "hdnDOLateReasonSelectedText", ""),
                new KeyValuePair<string, string>(maincontentstring + "hdnDOLateReasonSelectedCode", ""),
                new KeyValuePair<string, string>(maincontentstring + "hdnLateDialogToShow", ""),
                new KeyValuePair<string, string>(maincontentstring + "ddlVehicle", trip.AssignedVehicle.VehicleTag),
                new KeyValuePair<string, string>(maincontentstring + "ddlDriver", trip.AssignedVehicle.DriverTag),
                new KeyValuePair<string, string>(maincontentstring + "ddlSignatureReceived", signaturereceived),
                new KeyValuePair<string, string>(maincontentstring + "hdnSignatureNeeded", signatureneeded),
                new KeyValuePair<string, string>(maincontentstring + "hdnInvalidOrMissingSignature", "false"),
                new KeyValuePair<string, string>(maincontentstring + "txtPickupTime", HttpUtility.UrlDecode(trip.SuggestedPUTime.Replace(":", "%3A"))),
                new KeyValuePair<string, string>(maincontentstring + "hdnShowPULateReasonFields", ""),
                new KeyValuePair<string, string>(maincontentstring + "txtDropOffTime", HttpUtility.UrlDecode(trip.SuggestedDOTime.Replace(":", "%3A"))),
                new KeyValuePair<string, string>(maincontentstring + "hdnShowDOLateReasonFields", ""),
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
                new KeyValuePair<string, string>(maincontentstring + "hdnPULateReasonSelectedText", ""),
                new KeyValuePair<string, string>(maincontentstring + "hdnPULateReasonSelectedCode", ""),
                new KeyValuePair<string, string>(maincontentstring + "hdnDOLateReasonSelectedText", ""),
                new KeyValuePair<string, string>(maincontentstring + "hdnDOLateReasonSelectedCode", ""),
                new KeyValuePair<string, string>(maincontentstring + "hdnLateDialogToShow", ""),
                new KeyValuePair<string, string>(maincontentstring + "ddlVehicle", trip.AssignedVehicle.VehicleTag),
                new KeyValuePair<string, string>(maincontentstring + "ddlDriver", trip.AssignedVehicle.DriverTag),
                new KeyValuePair<string, string>(maincontentstring + "ddlSignatureReceived", signaturereceived),
                new KeyValuePair<string, string>(maincontentstring + "hdnSignatureNeeded", signatureneeded),
                new KeyValuePair<string, string>(maincontentstring + "hdnInvalidOrMissingSignature", "false"),
                new KeyValuePair<string, string>(maincontentstring + "txtPickupTime", HttpUtility.UrlDecode(trip.SuggestedPUTime.Replace(":", "%3A"))),
                new KeyValuePair<string, string>(maincontentstring + "hdnShowPULateReasonFields", ""),
                new KeyValuePair<string, string>(maincontentstring + "txtDropOffTime", HttpUtility.UrlDecode(trip.SuggestedDOTime.Replace(":", "%3A"))),
                new KeyValuePair<string, string>(maincontentstring + "hdnShowDOLateReasonFields", ""),
                new KeyValuePair<string, string>(maincontentstring + "txtRiderCallTime", trip.RiderCallTime),
                new KeyValuePair<string, string>(maincontentstring + "txtBilledAmt", trip.BilledAmount.Replace("$", "")),
                new KeyValuePair<string, string>(maincontentstring + "txtBillingNotes", ""),
                new KeyValuePair<string, string>(maincontentstring + "btnSubmit", "Submit"),
                });
                
            }
            HttpResponseMessage res = await mcloginhandler.Client.PostAsync("https://transportationco.logisticare.com/TripActuals.aspx", testformContent);
            var response = await res.Content.ReadAsStringAsync();
            //await GetTripActualsReponse(mcloginhandler);
            mcloginhandler.GrabTokens(response);
            //mcloginhandler.GrabTokens(response); //JUST ADDED!!!!!
            try
            {
                string responseUri = res.RequestMessage.RequestUri.ToString();
                //Console.WriteLine("Location: " + responseUri);
                if (responseUri.Contains("login.aspx"))
                {
                    mcloginhandler.Connected = false;
                    trip.Status = "Failed";
                }
                if (responseUri.Contains("Login.aspx"))
                {
                    mcloginhandler.Connected = false;
                    trip.Status = "Failed";
                }
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
            catch
            {
                Console.WriteLine("There was a problem retrieving location.");
                mcloginhandler.Connected = false;
            }

        }

        




        public async Task LoadAvailableVehicles(MCBatchTripRecord mctriprecord, MCLoginHandler mcloginhandler, string date)
        {
            try
            {
                var formContent = new MyFormUrlEncodedContent(new[]{
                new KeyValuePair<string, string>("__EVENTTARGET", mctriprecord.TripToken),
                new KeyValuePair<string, string>("__EVENTARGUMENT", ""),
                new KeyValuePair<string, string>("__LASTFOCUS", ""),
                new KeyValuePair<string, string>("__VIEWSTATE", mcloginhandler.ViewStateToken),
                new KeyValuePair<string, string>("__VIEWSTATEGENERATOR", mcloginhandler.ViewStateGeneratorToken),
                new KeyValuePair<string, string>("__VIEWSTATEENCRYPTED", ""),
                new KeyValuePair<string, string>("__EVENTVALIDATION", mcloginhandler.EventValidationToken),
                new KeyValuePair<string, string>("ctl00$cphMainContent$ddlTripSortBy", "VerifiedDenied"),
             });
                HttpResponseMessage res = await mcloginhandler.Client.PostAsync("https://transportationco.logisticare.com/ProcessATMBatches.aspx", formContent);
                var response = await res.Content.ReadAsStringAsync();
            }
            catch
            {
                Console.WriteLine("There was a problem in GetDriversAndVehiclesPage.");
            }
            await GetTripActuals(mcloginhandler, date);
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
        public async Task GetTripActuals(MCLoginHandler handler, string date)
        {
            //handler.UpdateTripActualsHeaders("https://transportationco.logisticare.com/ProcessATMBatches.aspx");
            var response = "";
            try
            {
                HttpResponseMessage res = await handler.Client.GetAsync("https://transportationco.logisticare.com/TripActuals.aspx");
                response = await res.Content.ReadAsStringAsync();
                res.EnsureSuccessStatusCode();
                string responseUri = res.RequestMessage.RequestUri.ToString();
                //Console.WriteLine("Location: " + responseUri);
                if (responseUri.Contains("login.aspx"))
                {
                    Console.WriteLine("You're not logged in.");
                    handler.Connected = false;
                }
            }
            catch
            {
                Console.WriteLine("There was a problem in Test.");
            }
            ParseVehicles(response, date);
        }
        public void ParseVehicles(string webresponse, string date)
        {
            string driverslistbulk = GetContentBulkRegex(webresponse, "ctl00$cphMainContent$ddlDriver", "ctl00$cphMainContent$ddlSignatureReceived");
            string vehicleslistbulk = GetContentBulkRegex(webresponse, "ctl00$cphMainContent$ddlVehicle", "ctl00$cphMainContent$ddlDriver");

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
                //handler.UpdateTripActualsHeaders("https://transportationco.logisticare.com/ProcessATMBatches.aspx");
                //handler.Client.DefaultRequestHeaders.Remove("Referer");
                //handler.Client.DefaultRequestHeaders.Add("Referer", "https://transportationco.logisticare.com/ProcessATMBatches.aspx");
                var formContent = new MyFormUrlEncodedContent(new[]{
                new KeyValuePair<string, string>("__EVENTTARGET", trip.TripToken),
                new KeyValuePair<string, string>("__EVENTARGUMENT", ""),
                new KeyValuePair<string, string>("__LASTFOCUS", ""),
                //new KeyValuePair<string, string>("__VIEWSTATE", ""),
                new KeyValuePair<string, string>("__VIEWSTATE", handler.ViewStateToken),
                //new KeyValuePair<string, string>("__VIEWSTATEGENERATOR", handler.ViewStateGeneratorToken),
                new KeyValuePair<string, string>("__VIEWSTATEGENERATOR", handler.ViewStateGeneratorToken),
                new KeyValuePair<string, string>("__VIEWSTATEENCRYPTED", ""),
                new KeyValuePair<string, string>("__EVENTVALIDATION", handler.EventValidationToken),
                new KeyValuePair<string, string>("ctl00$cphMainContent$ddlTripSortBy", "VerifiedDenied"),
             });
                HttpResponseMessage res = await handler.Client.PostAsync("https://transportationco.logisticare.com/ProcessATMBatches.aspx", formContent);
                var response = await res.Content.ReadAsStringAsync();
                //handler.GrabTokens(response);
                await GetTripActualsReponse(handler);
            }
            catch (Exception ex) 
            {
                Console.WriteLine(ex);
                Console.WriteLine("There was a problem in GetDriverPageForSubit.");
            }
        }
        public async Task GetTripActualsReponse(MCLoginHandler mchandler)
        {

            var response = "";
            try
            {
                
                HttpResponseMessage res = await mchandler.Client.GetAsync("https://transportationco.logisticare.com/TripActuals.aspx");
                response = await res.Content.ReadAsStringAsync();
                mchandler.GrabTokens(response);
                string responseUri = res.RequestMessage.RequestUri.ToString();
                if (responseUri.Contains("login.aspx"))
                {
                    Console.WriteLine("You're not logged in.");
                }

            }
            catch
            {
                Console.WriteLine("There was a problem in Test.");
            }
          
        }






        private DateTime GetParsedDate(MCBatchAdditionalInfo additionalinfo)
        {
            System.Threading.Thread.Sleep(1000);
            //date (1/11/2023) string should be similar to "November+19%2C+2022"
            string predate = additionalinfo.MCBatchDate;

            string[] datesecions = predate.Split('/');

            //convert month number to month name
            string month = CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(int.Parse(datesecions[0]));

            string day = datesecions[1];
            string year = datesecions[2];

            string dateInput = month + day + "," + year;
            var parsedDate = DateTime.Parse(dateInput);
            //Console.WriteLine("::" + parsedDate);
            //string filterdate = month + "+" + day + "%2C+" + year;
            string filterdate = parsedDate.DayOfWeek + ", " + month + " " + day + ", " + year;
            //Console.WriteLine(filterdate);

            return parsedDate;
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