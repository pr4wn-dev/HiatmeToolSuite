using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Newtonsoft.Json;

namespace Hiatme_Tool_Suite_v3
{
    internal class WRBillingTool
    {
        public List<WRDownloadedTrip> WRTripList { get; set; }
        public WRCalculations WRCalculations { get; set; }

        /// <summary>
        /// Reloads <see cref="WRTripList"/> from <c>POST /portal/filterdata</c> (same path as billing tab Load).
        /// On failure, existing list and calculations are left unchanged. <paramref name="portalTotalRecords"/> is 0 unless the reload and parse succeed.
        /// </summary>
        public async Task<(WellRydePortalFilterDataResult result, int portalTotalRecords)> ReloadTripsFromPortalAsync(
            WellRydePortalSession portalSession, DateTime tripDate, CancellationToken cancellationToken = default)
        {
            if (portalSession == null)
                return (WellRydePortalFilterDataResult.Fail(null, "WellRyde portal session is not available."), 0);

            WellRydePortalFilterDataResult fd;
            try
            {
                fd = await portalSession.PostTripFilterDataAsync(tripDate,
                    maxResults: WellRydePortalSession.DefaultTripFilterMaxResult, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                return (WellRydePortalFilterDataResult.Fail(null, ex.Message ?? "filterdata request failed."), 0);
            }

            if (!fd.IsSuccess)
                return (fd, 0);

            int portalTotalRecords;
            try
            {
                var trips = WellRydeFilterDataParser.ParseTrips(fd.JsonBody, out portalTotalRecords);
                WRTripList = trips ?? new List<WRDownloadedTrip>();
                WRCalculations = new WRCalculations(WRTripList);
            }
            catch (Exception ex)
            {
                return (WellRydePortalFilterDataResult.Fail(fd.StatusCode,
                    "Failed to parse trip list: " + (ex.Message ?? "unknown error."), fd.JsonBody), 0);
            }

            return (WellRydePortalFilterDataResult.Ok(
                fd.StatusCode.GetValueOrDefault(HttpStatusCode.OK),
                fd.JsonBody ?? string.Empty), portalTotalRecords);
        }

        public Dictionary<WRDownloadedTrip, WRDownloadedTrip> FindTripPriceMismatches()
        {
            return WRCalculations?.GetTripPriceMismatches() ?? new Dictionary<WRDownloadedTrip, WRDownloadedTrip>();
        }

        /// <summary>
        /// Builds billable trips from <see cref="WRCalculations.BillableTrips"/> (legacy status rules: e.g. Completed, Dropoff Completed, In Progress, etc.)
        /// and POSTs them to <c>/portal/trip/saveBillData</c> as JSON in <c>formData</c>.
        /// </summary>
        public async Task<List<BillableTrip>> SendBill(WellRydePortalSession portalSession,
            System.Windows.Forms.CheckState sendmismatchtrips, System.Windows.Forms.CheckState sendalltrips)
        {
            if (portalSession == null)
                throw new ArgumentNullException(nameof(portalSession));

            if (WRCalculations == null)
                WRCalculations = new WRCalculations(WRTripList ?? new List<WRDownloadedTrip>());

            List<BillableTrip> billable = WRCalculations.BillableTrips(sendmismatchtrips, sendalltrips);
            if (billable.Count == 0)
                return billable;

            string json = JsonConvert.SerializeObject(billable);
            WellRydePortalSaveBillResult result = await portalSession.PostSaveBillDataAsync(json).ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                string msg = result.ErrorMessage ?? "saveBillData failed.";
                if (!string.IsNullOrEmpty(result.ResponseBody) && result.ResponseBody != result.ErrorMessage)
                    msg = msg + Environment.NewLine + result.ResponseBody;
                throw new InvalidOperationException(msg);
            }

            return billable;
        }
    }

    internal class BillableTrip
    {
        public string tripUUID { get; set; }
        public string billedAmount { get; set; }
    }
}
