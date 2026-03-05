using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Hiatme_Tool_Suite_v3
{
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
                        //Console.WriteLine("inaccuracy added for: " + driver.Driver);
                    }
                }
            }
        }
        private void AddAccuracy(MCBatchAdditionalInfo addinfo, string drivername)
        {
            foreach(MCDriver driver in addinfo.MCDrivers)
            {
                if (driver != null)
                {
                    if (driver.Driver.Replace(" ","") == drivername.Replace(" ", ""))
                    {
                        driver.Accuracies += 1;
                        //Console.WriteLine("accuracy added for: " + driver.Driver);
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
                                driverfound = true; break;
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
        public void CalculateAccuracies(List<MCBatchTripRecord> batchtrips, MCBatchAdditionalInfo dledtripsaddinfo)
        {
            Random r = new Random();
            int rInt;

            foreach (MCBatchTripRecord trprcd in batchtrips)
            {
                if (trprcd.Date == dledtripsaddinfo.MCBatchDate)
                {
                    AddToTripCount(dledtripsaddinfo, trprcd.Driver);
                    //Console.WriteLine(trprcd.ScheduledPUTime);
                    DateTime schedputime = DateTime.ParseExact(trprcd.ScheduledPUTime, "HH:mm", CultureInfo.InvariantCulture);
                    DateTime scheddotime = DateTime.ParseExact(trprcd.ScheduledDOTime, "HH:mm", CultureInfo.InvariantCulture);
                    DateTime driverputime = DateTime.ParseExact(trprcd.PUTime, "HH:mm", CultureInfo.InvariantCulture);
                    DateTime driverdotime = DateTime.ParseExact(trprcd.DOTime, "HH:mm", CultureInfo.InvariantCulture);

                    if (trprcd.RiderCallTime.Contains("nbsp"))
                    {

                    }
                    else
                    {
                        //Console.WriteLine(trprcd.RiderCallTime);
                        if (scheddotime.TimeOfDay.Ticks != 0)
                        {
                            AddAccuracy(dledtripsaddinfo, trprcd.Driver);
                        }
                            
                        schedputime = DateTime.ParseExact(trprcd.RiderCallTime, "HH:mm", CultureInfo.InvariantCulture);
                        if (trprcd.PUTime != trprcd.RiderCallTime)
                        {
                            //trprcd.Status = "Fixable";
                        }

                    }

                    int putimediff = DateTime.Compare(driverputime, schedputime);
                    int dotimediff = DateTime.Compare(driverdotime, scheddotime);

                    if (trprcd.Trip.Contains("A"))
                    {
                        switch (putimediff)
                        {
                            case 0://times are same
                                AddAccuracy(dledtripsaddinfo, trprcd.Driver);
                                break;
                            case -1://driver is early
                                if ((schedputime - driverputime).TotalMinutes <= 30)//driver time is good
                                {
                                    AddAccuracy(dledtripsaddinfo, trprcd.Driver);
                                }
                                else
                                {
                                    AddInaccuracy(dledtripsaddinfo, trprcd.Driver);
                                }
                                break; //up to 30 minutes early
                            case 1://driver is late
                                if ((driverputime - schedputime).TotalMinutes <= 15)//driver time is good
                                {
                                    AddAccuracy(dledtripsaddinfo, trprcd.Driver);
                                }
                                else
                                {
                                    AddInaccuracy(dledtripsaddinfo, trprcd.Driver);
                                }
                                break;//up to 15 minutes late
                        }
                        switch (dotimediff)
                        {
                            case 0://times are same
                                AddAccuracy(dledtripsaddinfo, trprcd.Driver);
                                break;
                            case -1://driver is early
                                if ((scheddotime - driverdotime).TotalMinutes <= 30)//driver time is good
                                {
                                    AddAccuracy(dledtripsaddinfo, trprcd.Driver);
                                }
                                else
                                {
                                    AddInaccuracy(dledtripsaddinfo, trprcd.Driver);
                                }
                                break;//up to 30 minutes early
                            case 1://driver is late
                                AddInaccuracy(dledtripsaddinfo, trprcd.Driver);
                                break;//cant be late
                        }

                    }
                    else
                    {
                        switch (putimediff)
                        {
                            case 0://times are same
                                AddAccuracy(dledtripsaddinfo, trprcd.Driver);
                                break;
                            case -1://driver is early
                                AddInaccuracy(dledtripsaddinfo, trprcd.Driver);
                                break; //up to 30 minutes late
                            case 1://driver is late
                                if ((driverputime - schedputime).TotalMinutes <= 30)//driver time is good
                                {
                                    AddAccuracy(dledtripsaddinfo, trprcd.Driver);
                                }
                                else
                                {
                                    AddInaccuracy(dledtripsaddinfo, trprcd.Driver);
                                }
                                break;//up to 30 minutes late
                        }

                        if (scheddotime.TimeOfDay.Ticks == 0)
                        {
                            if (driverdotime > schedputime)//driver time is good
                            {
                                AddAccuracy(dledtripsaddinfo, trprcd.Driver);
                                continue;
                            }
                            else
                            {
                                AddInaccuracy(dledtripsaddinfo, trprcd.Driver);
                                continue;
                            }
                        }

                        switch (dotimediff)
                        {
                            case 0://times are same
                                AddAccuracy(dledtripsaddinfo, trprcd.Driver);
                                break;
                            case -1://driver is early
                                if ((scheddotime - driverdotime).TotalMinutes <= 30)//driver time is good
                                {
                                    AddAccuracy(dledtripsaddinfo, trprcd.Driver);
                                }
                                else
                                {
                                    AddInaccuracy(dledtripsaddinfo, trprcd.Driver);
                                }
                                break;//up to 25 minutes early
                            case 1://driver is late
                                AddInaccuracy(dledtripsaddinfo, trprcd.Driver);
                                break;//cant be late
                        }
                    }
                }
            } 
        }







    }
}
