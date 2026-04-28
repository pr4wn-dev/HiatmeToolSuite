using Hiatme_Tools;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;

namespace Hiatme_Tool_Suite_v3
{
    internal class WRCalculations
    {
        private List<WRDownloadedTrip> WRTripList { get; set; }
        private Dictionary<WRDownloadedTrip, WRDownloadedTrip> WRPriceMismatchTripList { get; set; }
        public WRCalculations(List<WRDownloadedTrip> triplist) 
        {
            WRTripList = triplist ?? new List<WRDownloadedTrip>();
        }
        public List<BillableTrip> BillableTrips(System.Windows.Forms.CheckState sendmismatches, System.Windows.Forms.CheckState sendall)
        {
            List<BillableTrip> BillableTripsList = new List<BillableTrip>();

            foreach (WRDownloadedTrip trip in WRTripList) //RETURNS NULL
            {
                if (sendall == System.Windows.Forms.CheckState.Checked)
                {
                    if (VerifyTripForBilling(trip.Status) != false)
                    {
                        BillableTrip billable = new BillableTrip();
                        billable.tripUUID = trip.TripUUID;
                        billable.billedAmount = trip.Price;
                        BillableTripsList.Add(billable);
                    }
                }
                else
                {
                    if (VerifyNoobTripsForBilling(trip.Status) != false)
                    {
                        if (sendmismatches == System.Windows.Forms.CheckState.Checked)
                        {
                            BillableTrip billable = new BillableTrip();
                            billable.tripUUID = trip.TripUUID;
                            billable.billedAmount = trip.Price;
                            BillableTripsList.Add(billable);
                        }
                        else
                        {
                            bool mmtripfound = false;
                            foreach (KeyValuePair<WRDownloadedTrip, WRDownloadedTrip> mmtrip in WRPriceMismatchTripList)
                            {
                                if (mmtrip.Key.TripNumber == trip.TripNumber)
                                {
                                    mmtripfound = true;
                                }
                            }
                            if(!mmtripfound) 
                            {
                                BillableTrip billable = new BillableTrip();
                                billable.tripUUID = trip.TripUUID;
                                billable.billedAmount = trip.Price;
                                BillableTripsList.Add(billable);
                            }
                        }
                    }
                }

            }

           
            return BillableTripsList;
        }
        public int GetAlertCount()
        {
            int alertCount = 0;
            foreach (WRDownloadedTrip trip in WRTripList)
            {
                alertCount += trip.Alerts.Count;
                //Console.WriteLine(trip.Alerts.Count);
            }
            //Console.WriteLine(alertCount);
                return alertCount;
        }
        public Dictionary<WRDownloadedTrip, WRDownloadedTrip> GetTripPriceMismatches()
        {
            WRPriceMismatchTripList = new Dictionary<WRDownloadedTrip, WRDownloadedTrip>();

            if (WRPriceMismatchTripList != null)
            {
                foreach (WRDownloadedTrip trip in WRTripList) // crashes here
                {
                    PriceMismatchCheck(trip);
                }
            return WRPriceMismatchTripList;
            }
            return null;
        }
        private bool VerifyTripForMismatch(string tripstatus)
        {
            string status = tripstatus;

            switch (status)
            {
                case "Cancelled":
                    return false;

                case "Pickup Departed":
                    return true;

                case "Pickup Arrived":
                    return true;

                case "Suspended":
                    return false;

                case "Dropoff Completed":
                    return true;

                case "Dropoff Arrived":
                    return true;

                case "Reserved":
                    return true;

                case "Assigned":
                    return true;

                case "In Progress":
                    return true;

                case "Completed":
                    return true;

                case "Billed":
                    return true;

                default:
                    return false;
            }
        }
        private bool PriceMismatchCheck(WRDownloadedTrip wrtriprecord)
        {
            //Console.WriteLine("HEINO");
            if (!VerifyTripForMismatch(wrtriprecord.Status))
            {
                    return false;
            }
            
           //check each trip that isnt the searched trip
            bool mismatchalreadyfound = false;
            foreach (WRDownloadedTrip trip in WRTripList)
            {
                if (trip.TripNumber != wrtriprecord.TripNumber)
                {
                    //found a different trip 
                    foreach (KeyValuePair<WRDownloadedTrip, WRDownloadedTrip>  mmtrip in WRPriceMismatchTripList)
                    {
                        if (wrtriprecord.TripNumber == mmtrip.Key.TripNumber)
                        {
                            //Console.WriteLine("mismatch found for " + wrtriprecord.TripNumber);
                            return true;
                        }
                    }
                }

                if (wrtriprecord.PUStreet.Contains(trip.PUStreet) && wrtriprecord.DOStreet.Contains(trip.DOStreet))
                {
                    if (wrtriprecord.Miles != trip.Miles && Convert.ToDecimal(wrtriprecord.Price) < Convert.ToDecimal(trip.Price))
                    {
                        wrtriprecord.Alerts.Add("Price");
                        wrtriprecord.References = trip.TripNumber;
                        WRPriceMismatchTripList.Add(wrtriprecord, trip);
                        //Console.WriteLine(wrtriprecord.TripNumber + ":" + wrtriprecord.Price);
                        //Console.WriteLine(trip.TripNumber + ":" + trip.Price);
                        return true;
                    }
                }

                if (wrtriprecord.PUStreet.Contains(trip.DOStreet) && wrtriprecord.DOStreet.Contains(trip.PUStreet))
                {
                    if (wrtriprecord.Miles != trip.Miles && Convert.ToDecimal(wrtriprecord.Price) < Convert.ToDecimal(trip.Price))
                    {
                        wrtriprecord.Alerts.Add("Price");
                        wrtriprecord.References = trip.TripNumber;
                        WRPriceMismatchTripList.Add(wrtriprecord, trip);
                        //Console.WriteLine(wrtriprecord.TripNumber + ":" + wrtriprecord.Price);
                        //Console.WriteLine(trip.TripNumber + ":" + trip.Price);
                        return true;
                    }
                }

                if (wrtriprecord.DOStreet.Contains(trip.PUStreet) && wrtriprecord.PUStreet.Contains(trip.DOStreet))
                {
                    if (wrtriprecord.Miles != trip.Miles && Convert.ToDecimal(wrtriprecord.Price) < Convert.ToDecimal(trip.Price))
                    {
                        wrtriprecord.Alerts.Add("Price");
                        wrtriprecord.References = trip.TripNumber;
                        WRPriceMismatchTripList.Add(wrtriprecord, trip);
                        //Console.WriteLine(wrtriprecord.TripNumber + ":" + wrtriprecord.Price);
                        //Console.WriteLine(trip.TripNumber + ":" + trip.Price);
                        return true;
                    }
                }
            }
                return false;
        }
        public int CalculateBillableTripCount(System.Windows.Forms.CheckState sendmms, System.Windows.Forms.CheckState sendemall)
        {
            return BillableTrips(sendmms, sendemall).Count;
        }
        public decimal CalculateActualBillTotal(System.Windows.Forms.CheckState sendmms, System.Windows.Forms.CheckState sendemall)
        {
            List<BillableTrip> thisbilllist = new List<BillableTrip>();
            thisbilllist = BillableTrips(sendmms, sendemall);

            decimal billingTotal = 0;

            foreach (BillableTrip trip in thisbilllist)
            {
                    billingTotal += Convert.ToDecimal(trip.billedAmount);
            }
            return billingTotal;
        }
        public decimal CalculateBillableTotal()
        {
            decimal billingTotal = 0;

            foreach (WRDownloadedTrip trip in WRTripList)
            {
                if (VerifyTripForBilling(trip.Status) != false)
                {
                    billingTotal += Convert.ToDecimal(trip.Price);
                }
            }
            return billingTotal;
        }
        public void CheckIfAllTripsAreBeingBilled(System.Windows.Forms.CheckState sendmms, System.Windows.Forms.CheckState sendemall)
        {
            List<BillableTrip> actualbillabletrips = new List<BillableTrip>();
            List<BillableTrip> speculativebillabletrips = new List<BillableTrip>();

            actualbillabletrips = BillableTrips(sendmms, sendemall);
            speculativebillabletrips = BillableTrips(CheckState.Checked, CheckState.Checked);

            if (actualbillabletrips.Count < speculativebillabletrips.Count)
            {
                Console.WriteLine("Your selected settings will only bill " + actualbillabletrips.Count + " of " + speculativebillabletrips.Count + " total billable trips. To bill all trips check 'Bill Mismatches' and 'Bill All' then load the list again.");
            }

        }
        private bool VerifyTripForBilling(string tripstatus)
        {
            string status = tripstatus;

            switch (status)
            {
                case "Cancelled":
                    return false;

                case "Pickup Departed":
                    return true;

                case "Pickup Completed":
                    return true;

                case "Pickup Arrived":
                    return true;

                case "Suspended":
                    return false;

                case "Dropoff Completed":
                    return true;

                case "Dropoff Arrived":
                    return true;

                case "Reserved":
                    return false;

                case "Assigned":
                    return false;

                case "In Progress":
                    return true;

                case "Completed":
                    return true;

                case "Billed":
                    return false;

                default:
                    return false;
            }
        }
        public decimal CalculateNoobBilledTotal()
        {
            decimal billedTotal = 0;

            foreach (WRDownloadedTrip trip in WRTripList)
            {
                if (VerifyNoobTripsForBilling(trip.Status) != false)
                {
                    billedTotal += Convert.ToDecimal(trip.Price);
                }
            }
            return billedTotal;
        }
        private bool VerifyNoobTripsForBilling(string tripstatus)
        {
            string status = tripstatus;

            switch (status)
            {
                case "Cancelled":
                    return false;

                case "Pickup Departed":
                    return false;

                case "Pickup Arrived":
                    return false;

                case "Pickup Completed":
                    return false;

                case "Suspended":
                    return false;

                case "Dropoff Completed":
                    return true;

                case "Dropoff Arrived":
                    return true;

                case "Reserved":
                    return false;

                case "Assigned":
                    return false;

                case "In Progress":
                    return false;

                case "Completed":
                    return true;

                case "Billed":
                    return false;

                default:
                    return false;
            }
        }
        public decimal CalculateBilledTotal()
        {
            decimal billedTotal = 0;

            foreach (WRDownloadedTrip trip in WRTripList)
            {
                if (VerifyTripForBilled(trip.Status) != false)
                {
                    billedTotal += Convert.ToDecimal(trip.Price);
                }
            }
            return billedTotal;
        }
        private bool VerifyTripForBilled(string tripstatus)
        {
            string status = tripstatus;

            switch (status)
            {
                case "Billed":
                    return true;

                default:
                    return false;
            }
        }
        public decimal CalculateSimpleBillableTotal()
        {
            decimal billingTotal = 0;

            foreach (WRDownloadedTrip trip in WRTripList)
            {
                if (VerifyTripForSimpleBilling(trip.Status) != false)
                {
                    billingTotal += Convert.ToDecimal(trip.Price);
                }
            }
            return billingTotal;
        }
        private bool VerifyTripForSimpleBilling(string tripstatus)
        {
            string status = tripstatus;

            switch (status)
            {
                case "Billed":
                    return true;
                case "Completed":
                    return true;
                default:
                    return false;
            }
        }
        public decimal CalculateCancelsTotal()
        {
            decimal cancelsTotal = 0;

            foreach (WRDownloadedTrip trip in WRTripList)
            {
                if (VerifyTripForCancels(trip.Status) != false)
                {
                    cancelsTotal += Convert.ToDecimal(trip.Price);
                }
            }
            return cancelsTotal;
        }
        private bool VerifyTripForCancels(string tripstatus)
        {
            string status = tripstatus;

            switch (status)
            {
                case "Cancelled":
                    return true;

                default:
                    return false;
            }
        }
        public decimal CalculateBillAllTotal()
        {
            decimal billedTotal = 0;

            foreach (WRDownloadedTrip trip in WRTripList)
            {
                if (VerifyTripForBillAllTotal(trip.Status) != false)
                {
                    billedTotal += Convert.ToDecimal(trip.Price);
                }
            }
            return billedTotal;
        }
        private bool VerifyTripForBillAllTotal(string tripstatus)
        {
            string status = tripstatus;

            switch (status)
            {
                case "Cancelled":
                    return false;

                case "Pickup Departed":
                    return true;

                case "Pickup Arrived":
                    return true;

                case "Pickup Completed":
                    return true;

                case "Suspended":
                    return false;

                case "Dropoff Completed":
                    return true;

                case "Dropoff Arrived":
                    return true;

                case "Reserved":
                    return false;

                case "Assigned":
                    return false;

                case "In Progress":
                    return true;

                case "Completed":
                    return true;

                case "Billed":
                    return true;

                default:
                    return false;
            }
        }
        public IDictionary<decimal, int> CalculateBillablePriceGroups() 
        {
            IDictionary<decimal, int> keyValuePairs = new Dictionary<decimal, int>();

            foreach (WRDownloadedTrip trip in WRTripList)
            {
                if (VerifyTripForBillablePriceGroups(trip.Status))
                {
                    //Console.WriteLine(trip.Status);
                    bool pricefound = false;
                    decimal price = Convert.ToDecimal(trip.Price);
                    int amountofprices = 0;
                    foreach (KeyValuePair<decimal, int> priceandnumofthem in keyValuePairs)
                    {
                        if (priceandnumofthem.Key == price)
                        {
                            pricefound = true;
                            //amountofprices = priceandnumofthem.Value;
                        }
                    }

                    if (!pricefound)
                    {
                       // Console.WriteLine(trip.Price);
                        foreach (WRDownloadedTrip tripcheck in WRTripList)
                        {
                            if (VerifyTripForBillablePriceGroups(tripcheck.Status))
                            {
                                if (Convert.ToDecimal(tripcheck.Price) == Convert.ToDecimal(trip.Price))
                                {
                                    amountofprices++;
                                    //Console.WriteLine(amountofprices);
                                }
                            }
                        }
                        keyValuePairs.Add(price, amountofprices);
                    }











                }
            }

            foreach (KeyValuePair<decimal, int> priceandnumofthem in keyValuePairs)
            {
                //Console.WriteLine("price: " + priceandnumofthem.Key + " amount: " + priceandnumofthem.Value);
            }

            return keyValuePairs;
        }
        private bool VerifyTripForBillablePriceGroups(string tripstatus) //Must match billable verify conditions
        {
            string status = tripstatus;

            switch (status)
            {
                case "Cancelled":
                    return false;

                case "Pickup Departed":
                    return false;

                case "Pickup Arrived":
                    return false;

                case "Suspended":
                    return false;

                case "Dropoff Completed":
                    return false;

                case "Dropoff Arrived":
                    return false;

                case "Reserved":
                    return false;

                case "Assigned":
                    return false;

                case "In Progress":
                    return false;

                case "Completed":
                    return false;

                case "Billed":
                    return true;

                default:
                    return false;
            }
        }


    }
}
