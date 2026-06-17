using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
    internal class EmployeeProductionStats
    {
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string FullName { get; set; }

        private const decimal DefaultGasPricePerGallon = 4.14m;
        private const decimal DefaultGrandCaravanMpg = 19m;
        private const decimal DefaultDriverHourlyPay = 16m;
        private const decimal DefaultDeadheadMilesBetweenTrips = 2.5m;
        private const decimal DefaultTakeHomeMilesPerDriverPerDay = 18m;
        private const decimal DefaultAvgDeadheadMinutesBetweenTrips = 7m;
        private const decimal DefaultPerTripServiceBufferMinutes = 6m;
        private const decimal DefaultMonthlyInsurancePerVehicle = 210m;
        private const decimal DefaultMonthlyMaintenancePerVehicle = 120m;
        private const int DefaultFleetVehicleCount = 9;
        private const decimal DaysPerMonthForOverhead = 30m;
        private const decimal DefaultManagerPaidHoursPerDay = 8m;
        private const decimal ManagerHourlyPay = 23m;
        private const int ProfitBarMaximum = 200; // 100 = break-even, >100 = profit zone
        private static readonly HashSet<string> ManagerDriverNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "REMIE",
            "CHERIE",
            "BOBBY"
        };
        public Label ProfitLabel { get; set; }
        public Label AccuracyLabel { get; set; }
        public Label WorkloadLabel { get; set; }
        public ProgressBar ProfitProgressBar { get; set; }
        public ProgressBar AccuracyProgressBar { get; set; }
        public ProgressBar WorkloadProgressBar { get; set; }
        public List<WRDownloadedTrip> DriverWRTripList { get; set; }

        private static bool IsControlAlive(Control c) => c != null && !c.IsDisposed;

        public void GenerateEmployeeStats(List<WRDownloadedTrip> wrtriplist, List<MCDownloadedTrip> mctriplist, int numofemployees)
        {
            GenerateProfitStat(numofemployees);
            GenerateAccuracyStat(wrtriplist, mctriplist, numofemployees);
            GenerateWorkloadStat(wrtriplist, numofemployees);
        }

        private static string NormalizeTripNumber(string tripNumber)
        {
            if (string.IsNullOrWhiteSpace(tripNumber))
                return string.Empty;
            return tripNumber.Replace(" ", "").Trim().ToUpperInvariant();
        }

        private static decimal ParseMoneyOrZero(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return 0m;

            string cleaned = raw.Trim()
                .Replace("$", "")
                .Replace(",", "")
                .Trim();
            cleaned = cleaned.Replace("USD", "").Replace("usd", "");

            if (decimal.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out var inv))
                return inv;
            if (decimal.TryParse(cleaned, NumberStyles.Float, CultureInfo.CurrentCulture, out var cur))
                return cur;
            return 0m;
        }

        private static decimal ParseMilesOrZero(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return 0m;
            string cleaned = raw.Trim()
                .Replace(",", "")
                .Trim();
            string lower = cleaned.ToLowerInvariant();
            lower = lower.Replace("miles", "").Replace("mile", "").Replace("mi", "");
            cleaned = lower.Trim();
            if (decimal.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out var inv))
                return Math.Max(0m, inv);
            if (decimal.TryParse(cleaned, NumberStyles.Float, CultureInfo.CurrentCulture, out var cur))
                return Math.Max(0m, cur);
            return 0m;
        }

        private static string NormalizeDriverName(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return string.Empty;
            return raw.Trim().ToUpperInvariant();
        }

        private bool IsManagerDriver()
        {
            string full = NormalizeDriverName(FullName);
            if (full.Length > 0)
            {
                foreach (var manager in ManagerDriverNames)
                {
                    if (full.Contains(manager))
                        return true;
                }
            }

            string first = NormalizeDriverName(FirstName);
            return first.Length > 0 && ManagerDriverNames.Contains(first);
        }

        private decimal GetDriverHourlyPay()
        {
            return IsManagerDriver() ? ManagerHourlyPay : DefaultDriverHourlyPay;
        }

        private decimal GetMinimumPaidHoursForDriver()
        {
            return IsManagerDriver() ? DefaultManagerPaidHoursPerDay : 0m;
        }

        private static bool IsBillableTrip(WRDownloadedTrip trip)
        {
            if (trip == null)
                return false;
            return trip.Status == "Completed" || trip.Status == "Billed";
        }

        private static decimal GetTripServiceMinutes(WRDownloadedTrip trip)
        {
            if (trip == null)
                return 0m;
            string pu = string.IsNullOrWhiteSpace(trip.ActualPUTime) ? trip.PUTime : trip.ActualPUTime;
            string d0 = string.IsNullOrWhiteSpace(trip.ActualDOTime) ? trip.DOTime : trip.ActualDOTime;
            if (!TryParseClockTime(pu, out DateTime puTime) || !TryParseClockTime(d0, out DateTime doTime))
                return 0m;

            var diff = (decimal)(doTime - puTime).TotalMinutes;
            if (diff < 0)
                diff += 24m * 60m;
            if (diff > 8m * 60m)
                return 0m;
            return diff;
        }

        private static decimal GetDailyFixedFleetCostPerDriver(int activeDriverCount)
        {
            int drivers = Math.Max(1, activeDriverCount);
            decimal monthlyFleet = DefaultFleetVehicleCount * (DefaultMonthlyInsurancePerVehicle + DefaultMonthlyMaintenancePerVehicle);
            decimal dailyFleet = monthlyFleet / DaysPerMonthForOverhead;
            return dailyFleet / drivers;
        }

        private static decimal GetDailyTakeHomeFuelCost()
        {
            return (DefaultTakeHomeMilesPerDriverPerDay / DefaultGrandCaravanMpg) * DefaultGasPricePerGallon;
        }

        private decimal GetEstimatedDriverHoursForDay(List<WRDownloadedTrip> completedTrips)
        {
            decimal minimumHours = GetMinimumPaidHoursForDriver();
            if (completedTrips == null || completedTrips.Count == 0)
                return minimumHours;

            decimal serviceMinutes = completedTrips.Sum(GetTripServiceMinutes);
            decimal deadheadMinutes = Math.Max(0, completedTrips.Count - 1) * DefaultAvgDeadheadMinutesBetweenTrips;
            decimal bufferMinutes = completedTrips.Count * DefaultPerTripServiceBufferMinutes;
            decimal taskHours = (serviceMinutes + deadheadMinutes + bufferMinutes) / 60m;

            var timePoints = new List<DateTime>();
            foreach (var trip in completedTrips)
            {
                if (trip == null)
                    continue;
                string pu = string.IsNullOrWhiteSpace(trip.ActualPUTime) ? trip.PUTime : trip.ActualPUTime;
                string d0 = string.IsNullOrWhiteSpace(trip.ActualDOTime) ? trip.DOTime : trip.ActualDOTime;
                if (TryParseClockTime(pu, out DateTime puTime))
                    timePoints.Add(puTime);
                if (TryParseClockTime(d0, out DateTime doTime))
                    timePoints.Add(doTime);
            }

            decimal spanHours = 0m;
            if (timePoints.Count >= 2)
            {
                var minTime = timePoints.Min();
                var maxTime = timePoints.Max();
                spanHours = (decimal)(maxTime - minTime).TotalHours;
                if (spanHours < 0m)
                    spanHours += 24m;
                if (spanHours > 16m)
                    spanHours = 16m;
            }

            decimal estimatedHours = Math.Max(taskHours, spanHours);
            return Math.Max(minimumHours, estimatedHours);
        }

        private decimal GetDriverOperatingCost(List<WRDownloadedTrip> driverTrips, int activeDriverCount)
        {
            var completedTrips = (driverTrips ?? new List<WRDownloadedTrip>())
                .Where(IsBillableTrip)
                .ToList();

            decimal loadedMiles = completedTrips.Sum(t => ParseMilesOrZero(t.Miles));
            decimal deadheadMiles = Math.Max(0, completedTrips.Count - 1) * DefaultDeadheadMilesBetweenTrips;
            decimal totalFuelMiles = loadedMiles + deadheadMiles;

            decimal fuelCost = (totalFuelMiles / DefaultGrandCaravanMpg) * DefaultGasPricePerGallon;
            decimal takeHomeFuelCost = GetDailyTakeHomeFuelCost();

            decimal laborHours = GetEstimatedDriverHoursForDay(completedTrips);
            decimal laborCost = laborHours * GetDriverHourlyPay();

            decimal fixedCostShare = GetDailyFixedFleetCostPerDriver(activeDriverCount);
            return fuelCost + takeHomeFuelCost + laborCost + fixedCostShare;
        }

        private decimal GetDriverBaselineDailyCost(int activeDriverCount, List<WRDownloadedTrip> driverTrips)
        {
            decimal fixedCostShare = GetDailyFixedFleetCostPerDriver(activeDriverCount);
            decimal takeHomeFuelCost = GetDailyTakeHomeFuelCost();
            var completedTrips = (driverTrips ?? new List<WRDownloadedTrip>())
                .Where(IsBillableTrip)
                .ToList();
            decimal baselineLaborCost = GetEstimatedDriverHoursForDay(completedTrips) * GetDriverHourlyPay();
            return fixedCostShare + takeHomeFuelCost + baselineLaborCost;
        }

        private void GenerateProfitStat(int activeDriverCount)
        {
            if (!IsControlAlive(ProfitLabel) || !IsControlAlive(ProfitProgressBar))
                return;

            var driverTrips = DriverWRTripList ?? new List<WRDownloadedTrip>();
            decimal revenue = 0;
            foreach (WRDownloadedTrip trip in driverTrips)
            {
                if (IsBillableTrip(trip))
                    revenue += ParseMoneyOrZero(trip.Price);
            }

            decimal baselineCost = GetDriverBaselineDailyCost(activeDriverCount, driverTrips);
            decimal operatingCost = GetDriverOperatingCost(driverTrips, activeDriverCount);
            decimal finalprofit = revenue - operatingCost;
            if (!IsControlAlive(ProfitLabel) || !IsControlAlive(ProfitProgressBar))
                return;

            int net = (int)Math.Truncate(finalprofit);
            int baseline = (int)Math.Truncate(baselineCost);
            if (revenue <= 0m)
            {
                ProfitLabel.Text = "-$" + baseline + " start cost";
            }
            else
            {
                ProfitLabel.Text = "$" + net;
            }
            GenerateProfitBarValue(finalprofit, baselineCost);
        }

        private void GenerateProfitBarValue(decimal netProfit, decimal baselineCost)
        {
            if (!IsControlAlive(ProfitProgressBar))
                return;
            decimal safeBaseline = Math.Max(1m, baselineCost);
            // 0 = deeply negative day, 100 = break-even, 200 = strong profit.
            decimal netPosition = 100m + ((netProfit / safeBaseline) * 100m);
            int clamped = (int)Math.Round(Math.Max(0m, Math.Min(ProfitBarMaximum, netPosition)));
            ProfitProgressBar.Maximum = ProfitBarMaximum;
            ProfitProgressBar.Value = clamped;
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

            int putimediff = DateTime.Compare(driverputime, schedputime);
            int dotimediff = DateTime.Compare(driverdotime, scheddotime);

            if (mctrip.TripNumber.Contains("A"))
            {
                switch (putimediff)
                {
                    case 0:
                        accuracies += 1;
                        break;
                    case -1:
                        if ((schedputime - driverputime).TotalMinutes <= 30)
                            accuracies += 1;
                        break;
                    case 1:
                        if ((driverputime - schedputime).TotalMinutes <= 15)
                            accuracies += 1;
                        break;
                }

                switch (dotimediff)
                {
                    case 0:
                        accuracies += 1;
                        break;
                    case -1:
                        if ((scheddotime - driverdotime).TotalMinutes <= 30)
                            accuracies += 1;
                        break;
                    case 1:
                        break;
                }
            }
            else
            {
                switch (putimediff)
                {
                    case 0:
                        accuracies += 1;
                        break;
                    case -1:
                        break;
                    case 1:
                        if ((driverputime - schedputime).TotalMinutes <= 30)
                            accuracies += 1;
                        break;
                }

                if (scheddotime.TimeOfDay.Ticks == 0)
                {
                    if (driverdotime > schedputime)
                        accuracies += 1;
                    return accuracies;
                }

                switch (dotimediff)
                {
                    case 0:
                        accuracies += 1;
                        break;
                    case -1:
                        if ((scheddotime - driverdotime).TotalMinutes <= 30)
                            accuracies += 1;
                        break;
                    case 1:
                        break;
                }
            }

            return accuracies;
        }

        private void GenerateAccuracyStat(List<WRDownloadedTrip> wrdtlist, List<MCDownloadedTrip> mcdledtriplist, int numofworkers)
        {
            if (DriverWRTripList == null)
                return;
            if (!DriverWRTripList.Any())
                return;
            if (!IsControlAlive(AccuracyLabel) || !IsControlAlive(AccuracyProgressBar))
                return;

            var mcTripsById = new Dictionary<string, MCDownloadedTrip>(StringComparer.OrdinalIgnoreCase);
            foreach (var mctrip in mcdledtriplist ?? Enumerable.Empty<MCDownloadedTrip>())
            {
                if (mctrip == null)
                    continue;
                string key = NormalizeTripNumber(mctrip.TripNumber);
                if (key.Length == 0)
                    continue;
                if (!mcTripsById.ContainsKey(key))
                    mcTripsById[key] = mctrip;
            }

            int tripcounter = 0;
            int accuraciescounter = 0;
            foreach (WRDownloadedTrip wrtrip in DriverWRTripList)
            {
                if (wrtrip == null)
                    continue;
                if (wrtrip.Status != "Completed" && wrtrip.Status != "Billed")
                    continue;

                string key = NormalizeTripNumber(wrtrip.TripNumber);
                if (key.Length == 0 || !mcTripsById.TryGetValue(key, out MCDownloadedTrip mctrip))
                    continue;

                tripcounter += 2;
                accuraciescounter += CheckIfDriversTimesAreAccurate(wrtrip, mctrip);
            }

            if (tripcounter == 0)
                return;

            double result = Math.Round((double)accuraciescounter / tripcounter * 100);
            if (!IsControlAlive(AccuracyLabel) || !IsControlAlive(AccuracyProgressBar))
                return;

            AccuracyLabel.Text = result + "%";
            GenerateAccuracyBarValue((int)result);
        }

        private void GenerateAccuracyBarValue(int accuracy)
        {
            if (!IsControlAlive(AccuracyProgressBar))
                return;

            int max = AccuracyProgressBar.Maximum <= 0 ? 100 : AccuracyProgressBar.Maximum;
            int clampedAccuracy = Math.Max(0, Math.Min(max, accuracy));
            AccuracyProgressBar.Maximum = max;
            AccuracyProgressBar.Value = clampedAccuracy;
        }

        private void GenerateWorkloadStat(List<WRDownloadedTrip> wrdtlist, int numofworkers)
        {
            if (DriverWRTripList == null)
                return;
            if (!DriverWRTripList.Any())
                return;
            if (!IsControlAlive(WorkloadLabel) || !IsControlAlive(WorkloadProgressBar))
                return;

            double grouptotalrevenue = 0;
            foreach (WRDownloadedTrip trip in wrdtlist)
            {
                if (IsBillableTrip(trip))
                    grouptotalrevenue += Convert.ToDouble(ParseMoneyOrZero(trip.Price));
            }

            double profit = 0;
            foreach (WRDownloadedTrip trip in DriverWRTripList)
            {
                if (IsBillableTrip(trip))
                    profit += Convert.ToDouble(ParseMoneyOrZero(trip.Price));
            }

            if (grouptotalrevenue <= 0 || numofworkers <= 0)
            {
                if (IsControlAlive(WorkloadLabel) && IsControlAlive(WorkloadProgressBar))
                {
                    WorkloadLabel.Text = "0%";
                    WorkloadProgressBar.Value = 0;
                }
                return;
            }

            double result = (profit / grouptotalrevenue) * 100;
            if (!IsControlAlive(WorkloadLabel) || !IsControlAlive(WorkloadProgressBar))
                return;

            double fairsharepercent = 100 / (double)numofworkers;
            WorkloadProgressBar.Maximum = Math.Max(1, (int)Math.Round(fairsharepercent));

            WorkloadLabel.Text = Math.Round(result) + "%";
            if (Math.Round(result) > WorkloadProgressBar.Maximum)
                result = WorkloadProgressBar.Maximum;

            GenerateWorkloadBarValue((int)Math.Round(result));
        }

        private void GenerateWorkloadBarValue(int workload)
        {
            if (!IsControlAlive(WorkloadProgressBar))
                return;

            int max = WorkloadProgressBar.Maximum <= 0 ? 1 : WorkloadProgressBar.Maximum;
            int clamped = Math.Max(0, Math.Min(max, workload));

            WorkloadProgressBar.Value = clamped;
            if (clamped == 0)
                return;
        }
    }
}
