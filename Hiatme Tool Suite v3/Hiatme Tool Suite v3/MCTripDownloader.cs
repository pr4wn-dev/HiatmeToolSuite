
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    internal class MCTripDownloader
    {
        public MCLoginHandler mcloginhandler { get; set; }

        public List<MCDownloadedTrip> MCTripList;
        public string NameOfDay { get; set; }
        public string Day { get; set; }
        public string NameOfMonth { get; set; }
        public string Month { get; set; }
        public string Year { get; set; }
        public bool InvalidDate { get; set; }

        int calsearchfails = 0;
        bool previousdayschecked = false;
        public async Task <List<MCDownloadedTrip>> DownloadTripRecords(DateTime dtstring, MCLoginHandler mcloginhandle)
        {
            MCTripList = new List<MCDownloadedTrip>();
            NameOfDay = dtstring.DayOfWeek.ToString();
            Day = dtstring.Day.ToString();
            NameOfMonth = dtstring.ToString("MMMM");
            Month = dtstring.ToString();
            Year = dtstring.Year.ToString();
            mcloginhandler = mcloginhandle;

            previousdayschecked = false;
            InvalidDate = false;
            calsearchfails = 0;
            HttpResponseMessage res = await mcloginhandler.Client.GetAsync("https://transportationco.logisticare.com/TripDownload.aspx");
            var response = await res.Content.ReadAsStringAsync();

            try
            {
                mcloginhandler.GrabViewStateGeneratorToken(response);
                mcloginhandler.GrabViewStateToken(response);
                string responseUri = res.RequestMessage.RequestUri.ToString();
                if (responseUri.Contains("Login.aspx"))
                {
                    await mcloginhandler.ResetConnection();
                    await DownloadTripRecords(dtstring, mcloginhandle);
                }
                    await CheckIfDateIsInCurrentPage(response);
            }
            catch
            {
                //Console.WriteLine("There was a problem retrieving location.");
                //mcloginhandler.Connected = false;
                //return null;
            }
                return MCTripList;
        }
        public async Task CheckIfDateIsInCurrentPage(string webpagedata)
        {
            //Console.WriteLine(webpagedata);

            string rawdataset1 = GetContentBulkRegex(webpagedata, "<table id=\"ctl00_cphMainContent_calTripDate\"", "</table></td></tr><tr><th align=\"");//trimmed version of respone (top of calender)
            //check if our month name exists in top calender data
            //Console.WriteLine(rawdataset1);
            //Console.WriteLine(NameOfMonth);

            if (rawdataset1.Contains(NameOfMonth))
            {
                //Console.WriteLine("Selected month found");
                string response = webpagedata;
                await CheckForDateToken(response);
                await NavigateToSelectedDate();
            }
            else//current month not found
            {
                //Console.WriteLine("Selected month not found");
                //InvalidDate = true;
                calsearchfails++;
                if (calsearchfails == 4)
                {
                    InvalidDate = true;
                    MessageBox.Show("Only current and next month is acceptable.");
                    return;
                }
                





                //check next calender page for month
                if (previousdayschecked)
                {
                    //check rawdataset1 for next month token
                    string rawdataset2 = GetContentBulkRegex(rawdataset1, "Go to the previous month", "Go to the next month");//trimmed version of rawdataset1
                    string rawdataset3 = GetContentBulkRegex(rawdataset2, "javascript:__doPostBack('ctl00$cphMainContent$calTripDate','", "')\"");//trimmed version of rawdataset1
                    //Console.WriteLine(rawdataset3);
                    mcloginhandler.EventArguement = rawdataset3;
                    await CheckIfDateIsInNextPage();
                }
                else
                {
                    //check rawdataset1 for previous month token
                    string rawdataset2 = GetContentBulkRegex(rawdataset1, "javascript:__doPostBack('ctl00$cphMainContent$calTripDate','", "')\" style=\"color:#568A9E\" title=\"Go to the previous month\"");//trimmed version of rawdataset1
                    //Console.WriteLine(rawdataset2);
                    //string rawdataset3 = GetContentBulkRegex(rawdataset2, "javascript:__doPostBack('ctl00$cphMainContent$calTripDate','", "')\"");//trimmed version of rawdataset1
                    mcloginhandler.EventArguement = rawdataset2;
                    await CheckIfDateIsInPreviousPage();
                }
                







            }
        }
        private async Task CheckIfDateIsInPreviousPage()
        {
            previousdayschecked = true;
            mcloginhandler.UpdateTripActualsHeaders("https://transportationco.logisticare.com/TripDownload.aspx");
            var formContent = new MyFormUrlEncodedContent(new[]{
                new KeyValuePair<string, string>("__EVENTTARGET", "ctl00$cphMainContent$calTripDate"),
                new KeyValuePair<string, string>("__EVENTARGUMENT", mcloginhandler.EventArguement),
                new KeyValuePair<string, string>("__LASTFOCUS", ""),
                new KeyValuePair<string, string>("__VIEWSTATE", mcloginhandler.ViewStateToken),
                //new KeyValuePair<string, string>("__VIEWSTATEGENERATOR", mcloginhandler.ViewStateGeneratorToken),
                new KeyValuePair<string, string>("__VIEWSTATEGENERATOR", mcloginhandler.ViewStateGeneratorToken),
                new KeyValuePair<string, string>("__SCROLLPOSITIONX", "0"),
                new KeyValuePair<string, string>("__SCROLLPOSITIONY", "0"),
                new KeyValuePair<string, string>("ctl00$cphMainContent$txtTripIDandLegs", ""),
                new KeyValuePair<string, string>("ctl00$cphMainContent$rblMode", "Full"),
                new KeyValuePair<string, string>("ctl00$cphMainContent$rblFormat", "Delimited")
             });

            HttpResponseMessage res = await mcloginhandler.Client.PostAsync("https://transportationco.logisticare.com/TripDownload.aspx", formContent);
            var response = await res.Content.ReadAsStringAsync();

            try
            {
                string responseUri = res.RequestMessage.RequestUri.ToString();
                //Console.WriteLine("Location: " + responseUri);
                if (responseUri.Contains("login.aspx"))
                {
                    mcloginhandler.Connected = false;
                }
            }
            catch
            {
                //Console.WriteLine("There was a problem retrieving location.");
                mcloginhandler.Connected = false;
            }

            await CheckIfDateIsInCurrentPage(response);
        }
        private async Task CheckIfDateIsInNextPage()
        {
            mcloginhandler.UpdateTripActualsHeaders("https://transportationco.logisticare.com/TripDownload.aspx");
            var formContent = new MyFormUrlEncodedContent(new[]{
                new KeyValuePair<string, string>("__EVENTTARGET", "ctl00$cphMainContent$calTripDate"),
                new KeyValuePair<string, string>("__EVENTARGUMENT", mcloginhandler.EventArguement),
                new KeyValuePair<string, string>("__LASTFOCUS", ""),
                new KeyValuePair<string, string>("__VIEWSTATE", mcloginhandler.ViewStateToken),
                //new KeyValuePair<string, string>("__VIEWSTATEGENERATOR", mcloginhandler.ViewStateGeneratorToken),
                new KeyValuePair<string, string>("__VIEWSTATEGENERATOR", mcloginhandler.ViewStateGeneratorToken),
                new KeyValuePair<string, string>("__SCROLLPOSITIONX", "0"),
                new KeyValuePair<string, string>("__SCROLLPOSITIONY", "0"),
                new KeyValuePair<string, string>("ctl00$cphMainContent$txtTripIDandLegs", ""),
                new KeyValuePair<string, string>("ctl00$cphMainContent$rblMode", "Full"),
                new KeyValuePair<string, string>("ctl00$cphMainContent$rblFormat", "Delimited")
             });

            HttpResponseMessage res = await mcloginhandler.Client.PostAsync("https://transportationco.logisticare.com/TripDownload.aspx", formContent);
            var response = await res.Content.ReadAsStringAsync();

            try
            {
                string responseUri = res.RequestMessage.RequestUri.ToString();
                //Console.WriteLine("Location: " + responseUri);
                if (responseUri.Contains("login.aspx"))
                {
                    mcloginhandler.Connected = false;
                }
            }
            catch
            {
                //Console.WriteLine("There was a problem retrieving location.");
                mcloginhandler.Connected = false;
            }

            await CheckIfDateIsInCurrentPage(response);
        }
        public async Task CheckForDateToken(string webpagedata)
        {
            //Console.WriteLine(webpagedata);
            string rawdataset1 = GetContentBulkRegex(webpagedata, "ctl00_cphMainContent_calTripDate", "ctl00_cphMainContent_txtTripIDandLegs");//trimmed version of respone (top of calender)
            //check if our month name exists in top calender data
            //Console.WriteLine(rawdataset1);
            if (rawdataset1.Contains('"'+NameOfMonth + " " + Day+'"'))
            {
                //Console.WriteLine("Selected day found");
                //Console.WriteLine(rawdataset1);
                GrabDateToken(rawdataset1);
            }
            else//current month not found
            {
                Console.WriteLine("Selected day not found");
                MessageBox.Show("Trips can only be downloaded for up to 8 days in the past and up to 30 days in the future. Please try again.");
                InvalidDate = true;
                return;
            }
        }
        public void GrabDateToken(string resp)
        {
            List<string> ids = new List<string>();
            ids = ExtractFromBody(resp, "calTripDate',", ">");

            foreach (string id in ids)
            {
                if (id.Contains('"'+NameOfMonth + " " + Day+'"'))
                {
                    //Console.WriteLine(GetContentBulkRegex(id, "'", "'"));
                    mcloginhandler.EventArguement = GetContentBulkRegex(id, "'", "'");//set event arguement to date
                }
            }
        }
        private async Task NavigateToSelectedDate()
        {
            // mcloginhandler.UpdateTripActualsHeaders("https://transportationco.logisticare.com/TripDownload.aspx");
            var formContent = new MyFormUrlEncodedContent(new[]{
                new KeyValuePair<string, string>("__EVENTTARGET", "ctl00$cphMainContent$calTripDate"),
                new KeyValuePair<string, string>("__EVENTARGUMENT", mcloginhandler.EventArguement),
                new KeyValuePair<string, string>("__LASTFOCUS", ""),
                new KeyValuePair<string, string>("__VIEWSTATE", mcloginhandler.ViewStateToken),
                //new KeyValuePair<string, string>("__VIEWSTATEGENERATOR", mcloginhandler.ViewStateGeneratorToken),
                new KeyValuePair<string, string>("__VIEWSTATEGENERATOR", mcloginhandler.ViewStateGeneratorToken),
                new KeyValuePair<string, string>("__SCROLLPOSITIONX", "0"),
                new KeyValuePair<string, string>("__SCROLLPOSITIONY", "0"),
                new KeyValuePair<string, string>("ctl00$cphMainContent$txtTripIDandLegs", ""),
                new KeyValuePair<string, string>("ctl00$cphMainContent$rblMode", "Update"),
                new KeyValuePair<string, string>("ctl00$cphMainContent$rblFormat", "Delimited")
             });

            HttpResponseMessage res = await mcloginhandler.Client.PostAsync("https://transportationco.logisticare.com/TripDownload.aspx", formContent);
            var response = await res.Content.ReadAsStringAsync();

            try
            {
                string responseUri = res.RequestMessage.RequestUri.ToString();
                //Console.WriteLine("Location: " + responseUri);
                if (responseUri.Contains("login.aspx"))
                {
                    mcloginhandler.Connected = false;
                }
            }
            catch
            {
                //Console.WriteLine("There was a problem retrieving location.");
                mcloginhandler.Connected = false;
            }
            mcloginhandler.GrabViewStateToken(response);
            await GrabTripsIDs(response);
        }
        private async Task GrabTripsIDs(string webpagedata)
        {
            try
            {
                string rawdataset1 = GetContentBulkRegex(webpagedata, "txtTripIDandLegs", "/textarea>");//trimmed version of respone (trip ids)
                string rawdataset2 = GetContentBulkRegex(rawdataset1, ">", "<");//trimmed version of respone (trip ids)

                string[] tripids = rawdataset2.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                //Console.WriteLine(tripids.Length);
                for (int i = 0; i < tripids.Length; i++)
                {
                    //Console.WriteLine(tripids[i]);
                }
                await SubmitTripDownloadRequest(tripids);
            }

            catch
            {

            }
        }
        private async Task SubmitTripDownloadRequest(string[] trips)
        {
            string separator = " ";

            string tripids = String.Join(separator, trips);

            var formContent = new MyFormUrlEncodedContent(new[]{
                new KeyValuePair<string, string>("__EVENTTARGET", ""),
                new KeyValuePair<string, string>("__EVENTARGUMENT", ""),
                new KeyValuePair<string, string>("__LASTFOCUS", ""),
                new KeyValuePair<string, string>("__VIEWSTATE", mcloginhandler.ViewStateToken),
                new KeyValuePair<string, string>("__VIEWSTATEGENERATOR", mcloginhandler.ViewStateGeneratorToken),
                new KeyValuePair<string, string>("ctl00$cphMainContent$txtTripIDandLegs", tripids),
                new KeyValuePair<string, string>("ctl00$cphMainContent$rblMode", "Full"),
                new KeyValuePair<string, string>("ctl00$cphMainContent$rblFormat", "Delimited"),
                new KeyValuePair<string, string>("ctl00$cphMainContent$btnDownloadTrips", "Download " + Month + "/" + Day + "/" + Year + " Trips")
             });

            HttpResponseMessage res = await mcloginhandler.Client.PostAsync("https://transportationco.logisticare.com/TripDownload.aspx", formContent);
            var response = await res.Content.ReadAsStringAsync();

            try
            {
                string responseUri = res.RequestMessage.RequestUri.ToString();
                //Console.WriteLine("Location: " + responseUri);
                if (responseUri.Contains("login.aspx"))
                {
                    mcloginhandler.Connected = false;
                }
            }
            catch
            {
                //Console.WriteLine("There was a problem retrieving location.");
                mcloginhandler.Connected = false;
            }
            //Console.WriteLine(response);
            BuildTripObjects(response);
        }
        public void BuildTripObjects(string data)
        {
            MCTripList = new List<MCDownloadedTrip>();
            using (StringReader reader = new StringReader(data))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {

                    //Initialize a new trip object
                    MCDownloadedTrip trip = new MCDownloadedTrip();
                    //split trip line into different components
                    string[] tripsubitems = line.Split(new string[] { "\",\"" }, StringSplitOptions.None);

                    //if too little subitems then trip is disposed
                    if (tripsubitems.Length < 10)
                    {
                        //Console.WriteLine("Trip is fake");
                    }
                    else
                    {
                        //Console.WriteLine("Line: " + line);
                        //trip is good so lets create object and add it to the list
                        
                        trip.TripNumber = tripsubitems[1].Replace("\"", "").Replace(" ","");
                        trip.Date = tripsubitems[2].Replace("\"", "");
                        trip.ClientFullName = tripsubitems[4].Replace("\"", "");
                        trip.PUStreet = tripsubitems[7].Replace("\"", "");
                        trip.PUCity = tripsubitems[10].Replace("\"", "");
                        trip.PUTelephone = tripsubitems[13].Replace("\"", "");
                        trip.PUTime = tripsubitems[14].Replace("\"", "");
                        trip.DOStreet = tripsubitems[16].Replace("\"", "");
                        trip.DOCITY = tripsubitems[19].Replace("\"", "");
                        trip.DOTelephone = tripsubitems[22].Replace("\"", "");
                        trip.DOTime = tripsubitems[24].Replace("\"", "");
                        trip.Age = tripsubitems[25].Replace("\"", "");
                        trip.Miles = tripsubitems[33].Replace("\"", "");
                        trip.Comments = tripsubitems[34].Replace("\"", "");

                        MCTripList.Add(trip);
                        /*
                        Console.WriteLine(trip.TripNumber);
                        Console.WriteLine(trip.Date);
                        Console.WriteLine(trip.ClientFirstName);
                        Console.WriteLine(trip.ClientLastName);
                        Console.WriteLine(trip.PUStreet);
                        Console.WriteLine(trip.PUCity);
                        Console.WriteLine(trip.PUTelephone);
                        Console.WriteLine(trip.PUTime);
                        Console.WriteLine(trip.DOStreet);
                        Console.WriteLine(trip.DOCITY);
                        Console.WriteLine(trip.DOTelephone);
                        Console.WriteLine(trip.DOTime);
                        Console.WriteLine(trip.Age);
                        Console.WriteLine(trip.Miles);
                        Console.WriteLine(trip.Comments);
                        */
                    }

                }
            }
            Console.WriteLine("Modivcare Downloaded Trips: " + MCTripList.Count.ToString());

        }
        private static List<string> ExtractFromBody(string body, string start, string end)
        {
            List<string> matched = new List<string>();

            int indexStart = 0;
            int indexEnd = 0;

            bool exit = false;
            while (!exit)
            {
                indexStart = body.IndexOf(start);

                if (indexStart != -1)
                {
                    indexEnd = indexStart + body.Substring(indexStart).IndexOf(end);

                    matched.Add(body.Substring(indexStart + start.Length, indexEnd - indexStart - start.Length));

                    body = body.Substring(indexEnd + end.Length);
                }
                else
                {
                    exit = true;
                }
            }

            return matched;
        }
        public string GetContentBulkRegex(string batchcontent, string beginstring, string endstring)
        {
            if (batchcontent != null)
            {
                int beginFrom = batchcontent.IndexOf(beginstring) + beginstring.Length;
                int goTo = batchcontent.LastIndexOf(endstring);
                String bulk_string = batchcontent.Substring(beginFrom, goTo - beginFrom);

                return bulk_string;
            }
            else
            {
                //Console.WriteLine("MCDownloadTripsList is null");
                return "";
            }
           

        }
    }
}
