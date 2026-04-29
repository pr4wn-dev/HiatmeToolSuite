using Hiatme_Tools;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq.Expressions;
using System.Net;
using System.Net.Http;
using System.Text;
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
            WellRydeLog.WriteLine("Downloading wellrydes records..");
            retrycounter = 0;
            var batchJson = await PostTripBatchDetails(longdate, day, year, loginhandler);
            if (string.IsNullOrWhiteSpace(batchJson))
                WellRydeLog.WriteLine("WellRyde: trip batch download failed (no JSON). Check login, CSRF, and App.config WellRydeListDefId if trips stay empty.");
            var trips = BuildWRTripList(DeserializeJSONBatch(batchJson));
            return trips ?? new List<WRDownloadedTrip>();
        }

        private WRBatchData DeserializeJSONBatch(string jsonstring)
        {
            //Console.WriteLine(jsonstring);
            JsonSerializer jsonserializer = new JsonSerializer();
            WRBatchData dynjsonobj = jsonserializer.DeserializeJSONString(jsonstring);
            return dynjsonobj;
        }

        public async Task<string> PostTripBatchDetails(string longdate, int day, int year, WRLoginHandler wrloginhandler)
        {
            if (retrycounter == 3) { return null; }
            try
            {
                var tripDate = WellRydeTripParsing.ResolveTripDate(longdate, day, year);
                if (!await wrloginhandler.TryRefreshTripsNuCsrfAsync(tripDate))
                {
                    retrycounter++;
                    WellRydeLog.WriteLine("WellRyde: trips CSRF refresh failed (attempt " + retrycounter + "/3). listDefId=" + WellRydeConfig.TripFilterListDefId);
                    if (retrycounter >= 3)
                        return null;
                    await wrloginhandler.TryRefreshPortalCsrfAsync();
                    await Task.Delay(500);
                    return await PostTripBatchDetails(longdate, day, year, wrloginhandler);
                }

                // VTripBilling sequence 2: Chrome uses <c>{"period":"0d"}</c> for today, else <c>specificDate</c> — see <see cref="WellRydeTripParsing.BuildVtTripBillingDateSlotValueJson"/>. Legacy SEC-J uses sequence 7 + specificDate.
                string dateSlotInner = WellRydeConfig.UsesVtTripBillingFilterListShape()
                    ? WellRydeTripParsing.BuildVtTripBillingDateSlotValueJson(tripDate)
                    : JsonConvert.SerializeObject(new
                    {
                        specificDate = tripDate.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture),
                    });
                string filterListJson;
                if (WellRydeConfig.UsesVtTripBillingFilterListShape())
                {
                    // Chrome: sequence is string "1","2",…; date slot value is a JSON object serialized as a string (not a nested object).
                    var vtList = new object[]
                    {
                        new { sequence = "1", value = "-1" },
                        new { sequence = "2", value = dateSlotInner },
                        new { sequence = "3", value = "-1" },
                        new { sequence = "4", value = "-1" },
                        new { sequence = "5", value = "-1" },
                        new { sequence = "6", value = "-1" },
                    };
                    filterListJson = JsonConvert.SerializeObject(vtList);
                }
                else
                {
                    // Match PHP WellRydeScraper::getTripsViaApi: sequence "1".."10" as strings; date slot value is a JSON string (not a nested object).
                    var filterList = new object[]
                    {
                        new { sequence = "1", value = "-1" },
                        new { sequence = "2", value = "-1" },
                        new { sequence = "3", value = "-1" },
                        new { sequence = "4", value = "-1" },
                        new { sequence = "5", value = "-1" },
                        new { sequence = "6", value = "-1" },
                        new { sequence = "7", value = dateSlotInner },
                        new { sequence = "8", value = "-1" },
                        new { sequence = "9", value = "-1" },
                        new { sequence = "10", value = "-1" },
                    };
                    filterListJson = JsonConvert.SerializeObject(filterList);
                }

                var useVtShape = WellRydeConfig.UsesVtTripBillingFilterListShape();
                WellRydeLog.WriteLine("WellRyde: filterdata request listDefId=" + WellRydeConfig.TripFilterListDefId
                    + " vtTripBillingShape=" + useVtShape + " tripDate=" + tripDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

                var allValues = new JArray();
                int totalRecords = 0;
                JObject lastDecoded = null;

                for (int page = 1; ; page++)
                {
                    var didShellHarvestRepost = false;
                shellHarvestRetry:
                    // Chrome HAR (portal.app 2026): filterArgsJson is "{}" for trip VTripBilling filterdata.
                    var filterArgsJson = "{}";
                    var pageSizeStr = WellRydeConfig.FilterDataPageSize.ToString(CultureInfo.InvariantCulture);
                    var formPairs = new List<KeyValuePair<string, string>>
                    {
                        new KeyValuePair<string, string>("filterList", filterListJson),
                        new KeyValuePair<string, string>("listDefId", WellRydeConfig.TripFilterListDefId),
                        new KeyValuePair<string, string>("customListDefId", ""),
                        new KeyValuePair<string, string>("canDelete", "false"),
                        new KeyValuePair<string, string>("canEdit", "false"),
                        new KeyValuePair<string, string>("canShow", "false"),
                        new KeyValuePair<string, string>("canSelect", "true"),
                        new KeyValuePair<string, string>("page", page.ToString(CultureInfo.InvariantCulture)),
                        new KeyValuePair<string, string>("currentPageSize", ""),
                        new KeyValuePair<string, string>("maxResult", pageSizeStr),
                        new KeyValuePair<string, string>("defaultSize", pageSizeStr),
                        new KeyValuePair<string, string>("userDefaultFilter", "true"),
                        new KeyValuePair<string, string>("filterArgsJson", filterArgsJson),
                        new KeyValuePair<string, string>("filterValues", "[]"),
                        new KeyValuePair<string, string>("_csrf", wrloginhandler._CsrfToken ?? ""),
                    };
                    var encodedFilterForm = EncodePhpStyleFilterForm(formPairs);
                    var formContent = CreatePhpStyleFilterFormContent(encodedFilterForm);

                    // Omit referer → PostWellRydeFilterDataAsync uses TripsRefererForWellRydeAjax (bare /portal/nu per Chrome HAR).
                    HttpResponseMessage res = await wrloginhandler.PostWellRydeFilterDataAsync(formContent, referer: null);
                    string contentType = null;
                    try
                    {
                        contentType = res.Content.Headers.ContentType?.ToString();
                    }
                    catch { /* ignore */ }
                    var response = await res.Content.ReadAsStringAsync();
                    string responseUri = res.RequestMessage?.RequestUri?.ToString() ?? "";
                    int status = (int)res.StatusCode;

                    if (response.Length == 0 || WellRydeTripParsing.LooksLikeNonJsonPayload(response))
                    {
                        if (!didShellHarvestRepost && page == 1 && status == 200
                            && response.IndexOf("meta name=\"_csrf\"", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            didShellHarvestRepost = true;
                            wrloginhandler.TryIngestJsessionFromFilterDataHtmlShell(response);
                            var shellCsrf = wrloginhandler.GrabCSRFToken(response);
                            if (!string.IsNullOrEmpty(shellCsrf))
                                wrloginhandler._CsrfToken = shellCsrf;
                            if (WellRydeConfig.DebugPortalTraffic)
                                WellRydeLog.WriteLine("WellRyde: filterdata HTML shell — scraped servlet session / CSRF, retrying once.");
                            goto shellHarvestRetry;
                        }
                        var hint401 = status == 401
                            ? " — if this persists, set App.config WellRydeManualJsessionId (Chrome → Application → Cookies → JSESSIONID for portal.app.wellryde.com)."
                            : "";
                        WellRydeLog.WriteLine("WellRyde: filterdata bad body HTTP " + status + " content-type=" + (contentType ?? "(unknown)") + " len=" + response.Length + " prefix=" + TrimForLog(response, 350) + hint401);
                        try
                        {
                            WellRydeHttpDiagnostics.DumpFilterDataMismatch(res.RequestMessage, res, response, WRLoginHandler.CookieJar, encodedFilterForm);
                        }
                        catch
                        {
                            /* ignore logging failures */
                        }
                        retrycounter++;
                        if (retrycounter >= 3)
                            return null;
                        await wrloginhandler.TryRefreshTripsNuCsrfAsync(tripDate);
                        await Task.Delay(500);
                        return await PostTripBatchDetails(longdate, day, year, wrloginhandler);
                    }

                    if (responseUri.IndexOf("login", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        WellRydeLog.WriteLine("WellRyde: filterdata redirected toward login. Request URI=" + responseUri);
                        retrycounter++;
                        if (retrycounter >= 3)
                            return null;
                        await wrloginhandler.TryRefreshTripsNuCsrfAsync(tripDate);
                        await Task.Delay(500);
                        return await PostTripBatchDetails(longdate, day, year, wrloginhandler);
                    }

                    if (!res.IsSuccessStatusCode)
                    {
                        WellRydeLog.WriteLine("WellRyde: filterdata HTTP " + status + " prefix=" + TrimForLog(response, 350));
                        retrycounter++;
                        if (retrycounter >= 3)
                            return null;
                        await wrloginhandler.TryRefreshTripsNuCsrfAsync(tripDate);
                        await Task.Delay(500);
                        return await PostTripBatchDetails(longdate, day, year, wrloginhandler);
                    }

                    lastDecoded = JsonConvert.DeserializeObject<JObject>(response);
                    if (lastDecoded == null)
                    {
                        WellRydeLog.WriteLine("WellRyde: filterdata JSON is not an object (wrong listDefId or API shape). prefix=" + TrimForLog(response, 400));
                        retrycounter++;
                        if (retrycounter >= 3)
                            return null;
                        await wrloginhandler.TryRefreshTripsNuCsrfAsync(tripDate);
                        await Task.Delay(500);
                        return await PostTripBatchDetails(longdate, day, year, wrloginhandler);
                    }

                    var pageValues = lastDecoded["values"] as JArray;
                    if (pageValues == null)
                        pageValues = new JArray();

                    foreach (var row in pageValues)
                        allValues.Add(row);

                    if (page == 1)
                        totalRecords = WellRydeTripParsing.ParseWellRydeTotalRecords(lastDecoded["totalRecords"]);

                    bool fullPage = pageValues.Count >= WellRydeConfig.FilterDataPageSize;
                    if (!fullPage)
                        break;
                    if (totalRecords > 0 && allValues.Count >= totalRecords)
                        break;
                }

                var merged = new JObject
                {
                    ["values"] = allValues,
                    ["totalRecords"] = totalRecords.ToString(CultureInfo.InvariantCulture),
                };
                if (lastDecoded != null && lastDecoded["entityName"] != null)
                    merged["entityName"] = lastDecoded["entityName"];

                return merged.ToString(Formatting.None);
            }
            catch (Exception ex)
            {
                WellRydeLog.WriteLine("WellRyde: PostTripBatchDetails exception: " + ex.GetType().Name + ": " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// <c>application/x-www-form-urlencoded</c> for <c>filterdata</c> — <see cref="HttpUtility.UrlEncode"/> uses <c>+</c> for spaces in values,
        /// matching Chrome &quot;Copy as cURL&quot; bodies (e.g. <c>April+27%2c+2026</c> inside <c>filterList</c>).
        /// </summary>
        private static string EncodePhpStyleFilterForm(IEnumerable<KeyValuePair<string, string>> pairs)
        {
            var sb = new StringBuilder(2048);
            var first = true;
            foreach (var kv in pairs)
            {
                if (!first)
                    sb.Append('&');
                first = false;
                sb.Append(HttpUtility.UrlEncode(kv.Key, Encoding.UTF8))
                    .Append('=')
                    .Append(HttpUtility.UrlEncode(kv.Value ?? "", Encoding.UTF8));
            }
            return sb.ToString();
        }

        private static HttpContent CreatePhpStyleFilterFormContent(string encodedBody)
        {
            var content = new StringContent(encodedBody, Encoding.UTF8, "application/x-www-form-urlencoded");
            content.Headers.ContentType.CharSet = "utf-8";
            return content;
        }

        private static string TrimForLog(string s, int maxLen)
        {
            if (string.IsNullOrEmpty(s))
                return "(empty)";
            s = s.Replace('\r', ' ').Replace('\n', ' ');
            return s.Length <= maxLen ? s : s.Substring(0, maxLen) + "...";
        }
        private List<WRDownloadedTrip> BuildWRTripList(WRBatchData wrBatchObj)
        {
            List<WRDownloadedTrip> triprcdlist = new List<WRDownloadedTrip>();
            //Console.WriteLine("BuildWRTripList");
            if (wrBatchObj == null)
                return triprcdlist;
            try
            {
                WellRydeLog.WriteLine("Wellryde Downloaded Trips: " + wrBatchObj.totalRecords);

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
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    if (string.IsNullOrEmpty(loginhandler._CsrfToken))
                        await loginhandler.TryRefreshPortalCsrfAsync();

                    HttpResponseMessage res = await loginhandler.Client.GetAsync(WellRydeConfig.TripGetAllDriversForTripAssignmentUrl);
                    var response = await res.Content.ReadAsStringAsync();

                    if (!res.IsSuccessStatusCode || WellRydeTripParsing.ResponseIndicatesPortalLogin(res)
                        || WellRydeTripParsing.LooksLikeNonJsonPayload(response) || !WellRydeTripParsing.LooksLikeJsonArray(response))
                    {
                        WellRydeLog.WriteLine("getAllDrivers: retry after status=" + res.StatusCode + " bodyPrefix=" + (response?.Length > 200 ? response.Substring(0, 200) : response));
                        await loginhandler.TryRefreshPortalCsrfAsync();
                        await Task.Delay(400);
                        continue;
                    }

                    var wrdrivers = JsonConvert.DeserializeObject<List<WRDrivers>>(response);
                    if (wrdrivers == null)
                    {
                        await loginhandler.TryRefreshPortalCsrfAsync();
                        await Task.Delay(400);
                        continue;
                    }

                    foreach (WRDrivers driver in wrdrivers)
                        WellRydeLog.WriteLine("Driver Name: " + driver.text + " Driver ID: " + driver.value);

                    return wrdrivers;
                }
                catch (JsonException)
                {
                    await loginhandler.TryRefreshPortalCsrfAsync();
                    await Task.Delay(400);
                }
                catch (HttpRequestException)
                {
                    await loginhandler.TryRefreshPortalCsrfAsync();
                    await Task.Delay(400);
                }
            }

            return null;
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
