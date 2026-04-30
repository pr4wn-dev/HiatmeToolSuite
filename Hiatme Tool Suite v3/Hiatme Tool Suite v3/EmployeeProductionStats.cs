using GMap.NET.MapProviders;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using static System.Net.Mime.MediaTypeNames;

namespace Hiatme_Tool_Suite_v3
{
    internal class EmployeeProductionStats
    {
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string FullName { get; set; }

        private int Overhead = 400;
        public Label ProfitLabel { get; set; }
        public Label AccuracyLabel { get; set; }
        public Label WorkloadLabel { get; set; }
        public ProgressBar ProfitProgressBar { get; set; }
        public ProgressBar AccuracyProgressBar { get; set; }
        public ProgressBar WorkloadProgressBar { get; set; }
        public List<WRDownloadedTrip> DriverWRTripList { get; set; }

        public void GenerateEmployeeStats(List<WRDownloadedTrip> wrtriplist, List<MCDownloadedTrip> mctriplist, int numofemployees)
        {
            GenerateProfitStat();
            GenerateAccuracyStat(wrtriplist, mctriplist, numofemployees);
            GenerateWorkloadStat(wrtriplist, numofemployees);
        }
        private void GenerateProfitStat()
        {
            if (DriverWRTripList == null)
            {
                return;
            }
            if (!DriverWRTripList.Any())
            {
                return;
            }

            decimal profit = 0;
            foreach (WRDownloadedTrip trip in DriverWRTripList)
            {
                if (trip.Status == "Completed" || trip.Status == "Billed")
                {
                    profit += Convert.ToDecimal(trip.Price);
                }
            }
            decimal finalprofit = profit - Overhead;
            ProfitLabel.Text = "$" + Math.Truncate(finalprofit).ToString();
            GenerateProfitBarValue((int)Math.Truncate(finalprofit));
        }
        private void GenerateProfitBarValue(int profitnumber)
        {
            if (profitnumber <= 0)
            {
                ProfitProgressBar.Value = (400 - Math.Abs(profitnumber)) / 8;
                ProfitProgressBar.SetState(2);
                return;
            }

            if (profitnumber > 0)
            {
                if (((400 + profitnumber) / 8) > 100) {
                    ProfitProgressBar.Value = 100;
                }
                else
                {
                    ProfitProgressBar.Value = (400 + profitnumber) / 8;
                }

                ProfitProgressBar.SetState(1);
                return;
            }
        }






        /// <summary>Parses clock strings from Modivcare or WellRyde (may be empty, include seconds, or use AM/PM).</summary>
        private static bool TryParseClockTime(string raw, out DateTime time)
        {
            time = default;
            if (string.IsNullOrWhiteSpace(raw))
                return false;
            var s = raw.Trim().Replace("&nbsp;", "").Trim();
            if (s.Length == 0)
                return false;

            string[] formats =
            {
                "HH:mm", "H:mm", "HH:mm:ss", "H:mm:ss",
                "hh:mm tt", "h:mm tt", "hh:mm:ss tt", "h:mm:ss tt",
            };
            foreach (string fmt in formats)
            {
                if (DateTime.TryParseExact(s, fmt, CultureInfo.InvariantCulture, DateTimeStyles.None, out time))
                    return true;
            }

            return DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.NoCurrentDateDefault, out time);
        }

        private int CheckIfDriversTimesAreAccurate(WRDownloadedTrip driverwrtrip, MCDownloadedTrip mctrip)
        {
            int accuracies = 0;

            if (!TryParseClockTime(mctrip.PUTime, out DateTime schedputime) ||
                !TryParseClockTime(mctrip.DOTime, out DateTime scheddotime))
                return 0;

            string wrPu = string.IsNullOrWhiteSpace(driverwrtrip.ActualPUTime)
                ? driverwrtrip.PUTime
                : driverwrtrip.ActualPUTime;
            string wrDo = string.IsNullOrWhiteSpace(driverwrtrip.ActualDOTime)
                ? driverwrtrip.DOTime
                : driverwrtrip.ActualDOTime;

            if (!TryParseClockTime(wrPu, out DateTime driverputime) ||
                !TryParseClockTime(wrDo, out DateTime driverdotime))
                return 0;


            //Console.WriteLine(trprcd.RiderCallTime);
            // if (scheddotime.TimeOfDay.Ticks != 0)
            //{
            //  accuracies += 1;
            //}



            int putimediff = DateTime.Compare(driverputime, schedputime);
            int dotimediff = DateTime.Compare(driverdotime, scheddotime);

            if (mctrip.TripNumber.Contains("A"))
            {
                switch (putimediff)
                {
                    case 0://times are same
                        accuracies += 1;
                        break;
                    case -1://driver is early
                        if ((schedputime - driverputime).TotalMinutes <= 30)//driver time is good
                        {
                            accuracies += 1;
                        }
                        else
                        {
                            //AddInaccuracy(dledtripsaddinfo, trprcd.Driver);
                        }
                        break; //up to 30 minutes early
                    case 1://driver is late
                        if ((driverputime - schedputime).TotalMinutes <= 15)//driver time is good
                        { 
                            accuracies += 1;
                        }
                        else
                        {
                            //AddInaccuracy(dledtripsaddinfo, trprcd.Driver);
                        }
                        break;//up to 15 minutes late
                }
                switch (dotimediff)
                {
                    case 0://times are same
                        accuracies += 1;
                        break;
                    case -1://driver is early
                        if ((scheddotime - driverdotime).TotalMinutes <= 30)//driver time is good
                        {
                            accuracies += 1;
                        }
                        else
                        {
                            //AddInaccuracy(dledtripsaddinfo, trprcd.Driver);
                        }
                        break;//up to 30 minutes early
                    case 1://driver is late
                        //AddInaccuracy(dledtripsaddinfo, trprcd.Driver);
                        break;//cant be late
                }

            }
            else
            {
                switch (putimediff)
                {
                    case 0://times are same
                        accuracies += 1;
                        break;
                    case -1://driver is early
                        //AddInaccuracy(dledtripsaddinfo, trprcd.Driver);
                        break; //up to 30 minutes late
                    case 1://driver is late
                        if ((driverputime - schedputime).TotalMinutes <= 30)//driver time is good
                        {
                            accuracies += 1;
                        }
                        else
                        {
                            //AddInaccuracy(dledtripsaddinfo, trprcd.Driver);
                        }
                        break;//up to 30 minutes late
                }

                if (scheddotime.TimeOfDay.Ticks == 0)
                {
                    if (driverdotime > schedputime)//driver time is good
                    {
                        accuracies += 1;
                    }
                    else
                    {
                        //AddInaccuracy(dledtripsaddinfo, trprcd.Driver);
                    }
                    return accuracies;
                }

                switch (dotimediff)
                {
                    case 0://times are same
                        accuracies += 1;
                        break;
                    case -1://driver is early
                        if ((scheddotime - driverdotime).TotalMinutes <= 30)//driver time is good
                        {
                            accuracies += 1;
                        }
                        else
                        {
                            //AddInaccuracy(dledtripsaddinfo, trprcd.Driver);
                        }
                        break;//up to 25 minutes early
                    case 1://driver is late
                        //AddInaccuracy(dledtripsaddinfo, trprcd.Driver);
                        break;//cant be late
                }
            }










            return accuracies;
        }
        private void GenerateAccuracyStat(List<WRDownloadedTrip> wrdtlist, List<MCDownloadedTrip> mcdledtriplist, int numofworkers)
        {
            if (DriverWRTripList == null)
            {
                return;
            }
            if (!DriverWRTripList.Any())
            {
                return;
            }

            int tripcounter = 0;
            int accuraciescounter = 0;
            foreach (WRDownloadedTrip wrtrip in DriverWRTripList)
            {
                if (wrtrip.Status == "Completed" || wrtrip.Status == "Billed")
                {
                    foreach (MCDownloadedTrip mctrip in mcdledtriplist)
                    {
                        if (mctrip.TripNumber == wrtrip.TripNumber)
                        {
                            tripcounter += 2;
                            int acc = CheckIfDriversTimesAreAccurate(wrtrip, mctrip);
                            accuraciescounter += acc;
                            Console.WriteLine(mctrip.TripNumber + ": " + acc);
                        }
                    }
                }
            }


            Console.WriteLine(tripcounter);

            if (tripcounter == 0)
                return;

            double result = Math.Round((double)accuraciescounter / tripcounter * 100);
            AccuracyLabel.Text = result.ToString() + "%";
            GenerateAccuracyBarValue((int)result);
        }

        private void GenerateAccuracyBarValue(int accuracy)
        {
            int minacceptableaccuracy = 70;
            if (accuracy < minacceptableaccuracy)
            {
                Console.WriteLine(accuracy);
                AccuracyProgressBar.Value = accuracy;
                AccuracyProgressBar.SetState(2);
                return;
            }
            if (accuracy >= minacceptableaccuracy)
            {
                AccuracyProgressBar.Value = accuracy;
                AccuracyProgressBar.SetState(1);
                return;
            }
        }



















        private void GenerateWorkloadStat(List<WRDownloadedTrip> wrdtlist, int numofworkers)
        {
            if (DriverWRTripList == null)
            {
                return;
            }
            if (!DriverWRTripList.Any())
            {
                return;
            }

            double grouptotalrevenue = 0;
            foreach (WRDownloadedTrip trip in wrdtlist)
            {
                if (trip.Status == "Completed" || trip.Status == "Billed")
                {
                    grouptotalrevenue += Convert.ToDouble(trip.Price);
                }
            }

            double profit = 0;
            foreach (WRDownloadedTrip trip in DriverWRTripList)
            {
                if (trip.Status == "Completed" || trip.Status == "Billed")
                {
                    profit += Convert.ToDouble(trip.Price);
                }
            }

            if (grouptotalrevenue <= 0 || numofworkers <= 0)
            {
                WorkloadLabel.Text = "0%";
                WorkloadProgressBar.Value = 0;
                WorkloadProgressBar.SetState(1);
                return;
            }

            double result = ((double)profit / grouptotalrevenue) * 100;

            double fairsharepercent = 100 / (double)numofworkers;
            WorkloadProgressBar.Maximum = (int)Math.Round(fairsharepercent);

            WorkloadLabel.Text = Math.Round(result).ToString() + "%";

            if (Math.Round(result) > WorkloadProgressBar.Maximum)
            {
                result = WorkloadProgressBar.Maximum;
            }

            GenerateWorkloadBarValue((int)Math.Round(result), numofworkers);

        }
        private void GenerateWorkloadBarValue(int workload, int numofemployees)
        {
            WorkloadProgressBar.Value = workload;

            int minacceptableworkload = 100 / numofemployees;
            
            if (workload == 0)
            {
                WorkloadProgressBar.Value = 0;
                WorkloadProgressBar.SetState(1);
                return;
            }
            if (workload < minacceptableworkload)
            {
                WorkloadProgressBar.Value = workload;
                WorkloadProgressBar.SetState(2);
                return;
            }
            if (workload >= minacceptableworkload)
            {
                WorkloadProgressBar.Value = workload;
                WorkloadProgressBar.SetState(1);
                return;
            }
        }














    }
}
