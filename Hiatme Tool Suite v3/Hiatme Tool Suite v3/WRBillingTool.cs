using Hiatme_Tools;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    internal class WRBillingTool
    {
        public List<WRDownloadedTrip> WRTripList { get; set; }
        public WRCalculations WRCalculations { get; set; }
        public async Task DownloadTrips(string longdatestr, int dayint, int yearint, WRLoginHandler wrlgnhandler)
        {
            WRTripDownloader wrtd = new WRTripDownloader();
            WRTripList = new List<WRDownloadedTrip>();
            WRTripList = await wrtd.DownloadTripRecords(longdatestr, dayint, yearint, wrlgnhandler);
          
            WRCalculations = new WRCalculations(WRTripList);
        }
        public Dictionary<WRDownloadedTrip, WRDownloadedTrip> FindTripPriceMismatches()
        {
            return WRCalculations.GetTripPriceMismatches();
        }
        public async Task<List<BillableTrip>> SendBill(WRLoginHandler wrLoginHandler, System.Windows.Forms.CheckState sendmismatchtrips, System.Windows.Forms.CheckState sendalltrips)
        {
            string jsonString = string.Empty;

            jsonString = JsonConvert.SerializeObject(WRCalculations.BillableTrips(sendmismatchtrips, sendalltrips));

            if (await SendBillRequest(jsonString, wrLoginHandler) == "SUCCESS")
            {
               // wrstatuslbl.Text = "Status: Batch successfully submitted.";
               // MessageBox.Show("Batch successfully submitted!");
            }
            return WRCalculations.BillableTrips(sendmismatchtrips, sendalltrips);
        }
        private async Task<string> SendBillRequest(string formData, WRLoginHandler wrloginhandler)
        {
            try
            {
                string data = formData;

                var formContent = new FormUrlEncodedContent(new[]{
                new KeyValuePair<string, string>("formData", data),
                new KeyValuePair<string, string>("saveSubmit", "true"),
                new KeyValuePair<string, string>("_csrf", wrloginhandler._CsrfToken),
                });

                HttpResponseMessage res = await wrloginhandler.Client.PostAsync("https://portal.app.wellryde.com/portal/trip/saveBillData", formContent);
                var response = await res.Content.ReadAsStringAsync();

                if (res.IsSuccessStatusCode)
                {
                    //MessageBox.Show("Batch successfully submitted!");
                }
                else
                {
                    Console.WriteLine(res.StatusCode.ToString());
                    await wrloginhandler.ResetConnection();
                    await SendBillRequest(formData, wrloginhandler);
                    
                }

                return response;
            }
            // Filter by InnerException.
            catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
            {
                // Handle timeout.
                Console.WriteLine("Timed out: " + ex.Message);
            }
            catch (TaskCanceledException ex)
            {
                // Handle cancellation.
                Console.WriteLine("Canceled: " + ex.Message);
            }
            return "";
        }
    }
    internal class BillableTrip
    {
        public string tripUUID { get; set; }
        public string billedAmount { get; set; }
    }
}
