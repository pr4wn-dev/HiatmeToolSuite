using Hiatme_Tools;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using System.Web.UI.WebControls;
using System.Windows.Forms;
using static GMap.NET.Entity.OpenStreetMapGraphHopperRouteEntity;

namespace Hiatme_Tool_Suite_v3
{
    internal class WRTripDownloader
    {
        public IDictionary<string, string> priceChart { get; set; }

        private int retrycounter = 0;
        public WRTripDownloader()
        {
            priceChart = new Dictionary<string, string>();
            BuildPriceChart();
        }
        public async Task<List<WRDownloadedTrip>> DownloadTripRecords(string longdate, int day, int year, WRLoginHandler loginhandler)
        {
            Console.WriteLine("Downloading wellrydes records..");
            retrycounter = 0;
            return BuildWRTripList(DeserializeJSONBatch(await PostTripBatchDetails(GetFilterDate(longdate, day, year), loginhandler)));
        }
        private string GetFilterDate(string ld, int d, int y)
        {
            //return string should be similar to "November+19%2C+2022"
            string finalmonth;
            //Console.WriteLine(datefilter);
            //Get month from between commas
            string monthandday = Regex.Match(ld, @"(?<=^([^,]*,){1})([^,]*)").Value;
            //Console.WriteLine(monthandday);
            //Get month from between spaces
            string actualmonth = Regex.Match(monthandday, @"(?<=^([^ ]* ){1})([^ ]*)").Value;
            finalmonth = actualmonth;
            //Console.WriteLine(actualmonth);
            //Get month complete

            //Console.WriteLine(finalmonth + "+" + dateTimePicker1.Value.Day + "%2C+" + dateTimePicker1.Value.Year);
            return finalmonth + "+" + d + "%2C+" + y;
        }
        private WRBatchData DeserializeJSONBatch(string jsonstring)
        {
            //Console.WriteLine(jsonstring);
            JsonSerializer jsonserializer = new JsonSerializer();
            WRBatchData dynjsonobj = jsonserializer.DeserializeJSONString(jsonstring);
            return dynjsonobj;
        }
        public async Task<string> PostTripBatchDetails(string filterdate, WRLoginHandler wrloginhandler)
        {
            if (retrycounter == 3) { return null; }
            try
            {
                string finaldate = filterdate;

                string decodedfilterlist = HttpUtility.UrlDecode("%5B%7B%22sequence%22%3A%221%22%2C%22value%22%3A%22-1%22%7D%2C%7B%22sequence%22%3A%222%22%2C%22value%22%3A%22-1%22%7D%2C%7B%22sequence%22%3A%223%22%2C%22value%22%3A%22-1%22%7D%2C%7B%22sequence%22%3A%224%22%2C%22value%22%3A%22-1%22%7D%2C%7B%22sequence%22%3A%225%22%2C%22value%22%3A%22-1%22%7D%2C%7B%22sequence%22%3A%226%22%2C%22value%22%3A%22-1%22%7D%2C%7B%22sequence%22%3A%227%22%2C%22value%22%3A%22%7B%5C%22specificDate%5C%22%3A%5C%22" + finaldate + "%5C%22%7D%22%7D%2C%7B%22sequence%22%3A%228%22%2C%22value%22%3A%22-1%22%7D%2C%7B%22sequence%22%3A%229%22%2C%22value%22%3A%22-1%22%7D%2C%7B%22sequence%22%3A%2210%22%2C%22value%22%3A%22-1%22%7D%5D");
                string decodedfilterArgsJson = HttpUtility.UrlDecode("%7B%22fetchColumnInfo%22%3Afalse%7D");
                string decodedfilterValues = HttpUtility.UrlDecode("%5B%5D");

                var formContent = new FormUrlEncodedContent(new[]{
             new KeyValuePair<string, string>("filterList", decodedfilterlist),
             new KeyValuePair<string, string>("listDefId", "271"),
             new KeyValuePair<string, string>("customListDefId", ""),
             new KeyValuePair<string, string>("canDelete", "false"),
             new KeyValuePair<string, string>("canEdit", "false"),
             new KeyValuePair<string, string>("canShow", "false"),
             new KeyValuePair<string, string>("canSelect", "true"),
             new KeyValuePair<string, string>("page", "1"),
             new KeyValuePair<string, string>("currentPageSize", ""),
             new KeyValuePair<string, string>("maxResult", "500"),
             new KeyValuePair<string, string>("defaultSize", "500"),
             new KeyValuePair<string, string>("userDefaultFilter", "true"),
             new KeyValuePair<string, string>("filterArgsJson", decodedfilterArgsJson),
             new KeyValuePair<string, string>("filterValues", decodedfilterValues),
             new KeyValuePair<string, string>("_csrf", wrloginhandler._CsrfToken),
             });

                HttpResponseMessage res = await wrloginhandler.Client.PostAsync("https://portal.app.wellryde.com/portal/filterdata", formContent);
                var response = await res.Content.ReadAsStringAsync();

                string responseUri = res.RequestMessage.RequestUri.ToString();

                if (response.Length == 0)
                {
                    await wrloginhandler.ResetConnection();
                    return await PostTripBatchDetails(filterdate, wrloginhandler);
                }
                else
                {
                    if (responseUri.Contains("login"))
                    {
                        await wrloginhandler.ResetConnection();
                        return await PostTripBatchDetails(filterdate, wrloginhandler);
                    }
                    else
                    {
                        return response;
                    }   
                }
            }

            catch (Exception ex)
            {
                return null;
            }
            
        }
        private List<WRDownloadedTrip> BuildWRTripList(WRBatchData wrBatchObj)
        {
            List<WRDownloadedTrip> triprcdlist = new List<WRDownloadedTrip>();
            //Console.WriteLine("BuildWRTripList");
            if (wrBatchObj == null) { return null; }
            try
            {
                Console.WriteLine("Wellryde Downloaded Trips: " + wrBatchObj.totalRecords);

                JsonSerializer serializer = new JsonSerializer();
                foreach (dynamic trip in wrBatchObj.values)
                {
                    WRDownloadedTrip wrTripRecord = new WRDownloadedTrip();

                    wrTripRecord.TripNumber = FormatTripID(trip[1].ToString().Replace(" ", ""));

                    wrTripRecord.PUTime = FormatTime(trip[8].ToString());
                    wrTripRecord.DOTime = FormatTime(trip[9].ToString());
                    wrTripRecord.ActualPUTime = FormatTime(trip[15].ToString());
                    wrTripRecord.ActualDOTime = FormatTime(trip[16].ToString());
                    wrTripRecord.TripUUID = trip[0];
                    wrTripRecord.Status = trip[5];
                    wrTripRecord.ClientName = trip[6];
                    wrTripRecord.Miles = trip[17];
                    wrTripRecord.Price = CalulatePrice(wrTripRecord.Miles);
                    //Console.WriteLine(wrTripRecord.Price);
                    wrTripRecord.Escorts = trip[14];
                    wrTripRecord.DriverName = serializer.DeserializeDriverJSONString(trip[3].ToString());
                    wrTripRecord.PUStreet = trip[10];
                    wrTripRecord.PUCity = trip[11];
                    wrTripRecord.DOStreet = trip[12];
                    wrTripRecord.DOCITY = trip[13];

                    triprcdlist.Add(wrTripRecord);
                }
            }
            catch (Exception ex) { 
                //Console.WriteLine(ex.ToString()); 
            }
            return triprcdlist;
        }
        public async Task <List<WRDrivers>> GetAllDrivers(WRLoginHandler loginhandler) 
        {
            List<WRDrivers> wrdrivers = new List<WRDrivers>();

            try
            {
                HttpResponseMessage res = await loginhandler.Client.GetAsync("https://portal.app.wellryde.com/portal/trip/getAllDriversForTripAssignment?bpartnerId=0");
                var response = await res.Content.ReadAsStringAsync();
                //Console.WriteLine(response);
                wrdrivers = JsonConvert.DeserializeObject<List<WRDrivers>>(response);

                foreach (WRDrivers driver in wrdrivers)
                {
                    Console.WriteLine("Driver Name: " + driver.text + " Driver ID: " + driver.value);
                }
            }
            catch(NullReferenceException ex)
            {
                return null;
            }
            return wrdrivers;
        }



        private void BuildPriceChart()
        {
            //Build array to contain prices from chart

            double start_price = 30.21;
            for (int i = 11; i < 1000; i++)
            {
                priceChart.Add(i.ToString(), start_price.ToString());
                //Console.WriteLine("Miles: " + i.ToString() + " Price: " + start_price.ToString());
                start_price = start_price + 2;
            }
        }
        public string CalulatePrice(string miles)
        {
            //if miles contains a decimal then round up or down before parsing
            Decimal tempmiles = Decimal.Parse(miles);
            int milesint = (int)decimal.Round(tempmiles);

            //if miles is less then 1 then it equals 1
            if (milesint < 1)
            {
                milesint = 1;
            }

            //Console.WriteLine("Original Miles: " + miles);
            //Console.WriteLine("New Miles: " + milesint.ToString());
            try
            {
                //int milesint = Int32.Parse(miles);
                switch (milesint)
                {
                    case int n when (n >= 0 && n <= 3):
                        //do this
                        return "19.82";

                    case int n when (n >= 4 && n <= 6):
                        //do this
                        return "23.07";

                    case int n when (n >= 7 && n <= 10):
                        //do this
                        return "28.21";

                    case int n when (n >= 11):
                        //convert int n back to string
                        string miles_over_10 = n.ToString();
                        return priceChart[miles_over_10];
                }
                return "unknown price!";
            }
            catch
            {
                MessageBox.Show("A pricing anonmaly has occurred.");
                return "unknown price!";
            }

        }
        private string FormatTripID(string tripid)
        {
            try { 
            string[] tripidsections = tripid.Split(new[] { '-' }, StringSplitOptions.RemoveEmptyEntries);
            string wrtripid = tripidsections[0] + "-" + tripidsections[2] + "-" + tripidsections[3];
                return wrtripid;
            }
            catch (Exception ex)
            {
                return "";
            }
        }
        private string FormatTime(string time)
        {
            try
            {
                if (string.IsNullOrEmpty(time))
                {
                    return "00:00";
                }
                DateTime dt = DateTime.Parse(time);
                //DateTime dt = DateTime.Parse("6/22/2009 07:00:00 AM");

                //dt.ToString("HH:mm"); // 07:00 // 24 hour clock // hour is always 2 digits
                //dt.ToString("hh:mm tt"); // 07:00 AM // 12 hour clock // hour is always 2 digits
                //dt.ToString("H:mm"); // 7:00 // 24 hour clock
                //dt.ToString("h:mm tt"); // 7:00 AM // 12 hour clock

                //Console.WriteLine(tripidsections[0]);
                return dt.ToString("HH:mm");
                //return wrtripid;
            }
            catch (Exception ex)
            {
                return "";
            }
        }
    }
}
