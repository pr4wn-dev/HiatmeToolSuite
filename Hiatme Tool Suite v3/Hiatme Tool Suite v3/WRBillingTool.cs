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
            WRTripList = await wrtd.DownloadTripRecords(longdatestr, dayint, yearint, wrlgnhandler)
                ?? new List<WRDownloadedTrip>();
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
            string lastBody = "";
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    if (string.IsNullOrEmpty(wrloginhandler._CsrfToken))
                        await wrloginhandler.TryRefreshPortalCsrfAsync();

                    var formContent = new FormUrlEncodedContent(new[]{
                    new KeyValuePair<string, string>("formData", formData),
                    new KeyValuePair<string, string>("saveSubmit", "true"),
                    new KeyValuePair<string, string>("_csrf", wrloginhandler._CsrfToken ?? ""),
                    });

                    using (var res = await wrloginhandler.Client.PostAsync(WellRydeConfig.TripSaveBillDataUrl, formContent))
                    {
                        lastBody = await res.Content.ReadAsStringAsync();
                        if (res.IsSuccessStatusCode && WellRydeTripParsing.BillSubmitBodyIndicatesSuccess(lastBody))
                            return "SUCCESS";
                        if (res.IsSuccessStatusCode && !WellRydeTripParsing.BillSubmitBodyIndicatesSuccess(lastBody))
                            Console.WriteLine("saveBillData: unexpected body (not SUCCESS): " + (lastBody?.Length > 200 ? lastBody.Substring(0, 200) : lastBody));
                        else
                            Console.WriteLine("saveBillData: " + res.StatusCode);
                    }
                    await wrloginhandler.TryRefreshPortalCsrfAsync();
                    await Task.Delay(400);
                }
                catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
                {
                    Console.WriteLine("Timed out: " + ex.Message);
                }
                catch (TaskCanceledException ex)
                {
                    Console.WriteLine("Canceled: " + ex.Message);
                }
            }
            return lastBody ?? "";
        }
    }
    internal class BillableTrip
    {
        public string tripUUID { get; set; }
        public string billedAmount { get; set; }
    }
}
