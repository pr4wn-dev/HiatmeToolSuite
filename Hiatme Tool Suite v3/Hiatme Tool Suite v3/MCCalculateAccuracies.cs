using System;
using System.Collections.Generic;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Driver and company accuracy from actual trip times vs scheduled scoreboard rules.
    /// Independent of load mode (Standard / Lenient / Data only). After a trip is corrected on the portal (Passed),
    /// suggested submit times are used so the chart reflects fixes in progress.
    /// </summary>
    internal class MCCalculateAccuracies
    {
        private void AddInaccuracy(MCBatchAdditionalInfo addinfo, string drivername)
        {
            foreach (MCDriver driver in addinfo.MCDrivers)
            {
                if (driver != null)
                {
                    if (driver.Driver.Replace(" ", "") == drivername.Replace(" ", ""))
                    {
                        driver.Inaccuracies += 1;
                    }
                }
            }
        }

        private void AddAccuracy(MCBatchAdditionalInfo addinfo, string drivername)
        {
            foreach (MCDriver driver in addinfo.MCDrivers)
            {
                if (driver != null)
                {
                    if (driver.Driver.Replace(" ", "") == drivername.Replace(" ", ""))
                    {
                        driver.Accuracies += 1;
                    }
                }
            }
        }

        private void AddToTripCount(MCBatchAdditionalInfo addinfo, string drivername)
        {
            foreach (MCDriver driver in addinfo.MCDrivers)
            {
                if (driver != null)
                {
                    if (driver.Driver.Replace(" ", "") == drivername.Replace(" ", ""))
                    {
                        driver.Triplegs += 2;
                    }
                }
            }
        }

        public List<MCDriver> ReturnAccuracies(List<MCBatchAdditionalInfo> mcbatchinfo)
        {
            List<MCDriver> newdriverslist = new List<MCDriver>();

            foreach (MCBatchAdditionalInfo batchinfo in mcbatchinfo)
            {
                foreach (MCDriver driver in batchinfo.MCDrivers)
                {
                    bool driverfound = false;
                    if (driver != null)
                    {
                        foreach (MCDriver newdriver in newdriverslist)
                        {
                            if (newdriver.Driver.Replace(" ", "") == driver.Driver.Replace(" ", ""))
                            {
                                newdriver.Accuracies += driver.Accuracies;
                                newdriver.Inaccuracies += driver.Inaccuracies;
                                newdriver.Triplegs += driver.Triplegs;
                                driverfound = true;
                                break;
                            }
                        }
                        if (!driverfound)
                        {
                            newdriverslist.Add(driver);
                        }
                    }
                }
            }
            return newdriverslist;
        }

        /// <summary>
        /// Driver-reported times unless the trip was successfully submitted (Passed), then portal times.
        /// </summary>
        private static bool TryGetTimesForAccuracy(MCBatchTripRecord trprcd, out DateTime pu, out DateTime dotime)
        {
            pu = default;
            dotime = default;

            bool useSubmitted = string.Equals(trprcd.Status, "Passed", StringComparison.OrdinalIgnoreCase);

            string puText = useSubmitted && MCTimeCorrection.TryParseBatchTime(trprcd.SuggestedPUTime, out _)
                ? trprcd.SuggestedPUTime
                : trprcd.PUTime;

            string doText = useSubmitted && MCTimeCorrection.TryParseBatchTime(trprcd.SuggestedDOTime, out _)
                ? trprcd.SuggestedDOTime
                : trprcd.DOTime;

            return MCTimeCorrection.TryParseBatchTime(puText, out pu) &&
                   MCTimeCorrection.TryParseBatchTime(doText, out dotime);
        }

        public void CalculateAccuracies(List<MCBatchTripRecord> batchtrips, MCBatchAdditionalInfo dledtripsaddinfo)
        {
            foreach (MCBatchTripRecord trprcd in batchtrips)
            {
                if (trprcd.Date != dledtripsaddinfo.MCBatchDate)
                    continue;

                AddToTripCount(dledtripsaddinfo, trprcd.Driver);

                if (!MCTimeCorrection.TryParseBatchTime(trprcd.ScheduledPUTime, out DateTime schedputime) ||
                    !MCTimeCorrection.TryParseBatchTime(trprcd.ScheduledDOTime, out DateTime scheddotime) ||
                    !TryGetTimesForAccuracy(trprcd, out DateTime actualputime, out DateTime actualdotime))
                    continue;

                if (!trprcd.RiderCallTime.Contains("nbsp") &&
                    MCTimeCorrection.TryParseBatchTime(trprcd.RiderCallTime, out DateTime riderCallPu))
                {
                    if (scheddotime.TimeOfDay.Ticks != 0)
                        AddAccuracy(dledtripsaddinfo, trprcd.Driver);
                    schedputime = riderCallPu;
                }

                if (McTripTimingRules.PuLateMinutesOk(trprcd.Trip, actualputime, schedputime) &&
                    McTripTimingRules.PuEarlyMinutesOk(trprcd.Trip, actualputime, schedputime))
                    AddAccuracy(dledtripsaddinfo, trprcd.Driver);
                else
                    AddInaccuracy(dledtripsaddinfo, trprcd.Driver);

                if (scheddotime.TimeOfDay.Ticks == 0)
                {
                    if (actualdotime > schedputime)
                        AddAccuracy(dledtripsaddinfo, trprcd.Driver);
                    else
                        AddInaccuracy(dledtripsaddinfo, trprcd.Driver);
                    continue;
                }

                if (McTripTimingRules.DoLateMinutesOk(actualdotime, scheddotime) &&
                    McTripTimingRules.MinutesEarly(scheddotime, actualdotime) <= McTripTimingRules.ALegPuEarlyMaxMinutes)
                    AddAccuracy(dledtripsaddinfo, trprcd.Driver);
                else
                    AddInaccuracy(dledtripsaddinfo, trprcd.Driver);
            }
        }
    }
}
