using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Hiatme_Tool_Suite_v3
{
    internal class MCDownloadedTrip
    {
        public string TripNumber { get; set; }
        public string Date { get; set; }
        public List<string> Alerts { get; set; }
        public string ClientFirstName { get; set; }
        public string ClientLastName { get; set; }
        public string ClientFullName { get; set; }
        public string DriverNameParsed { get; set; }
        public string PUStreet { get; set; }
        public string PUCity { get; set; }
        public string PUTelephone { get; set; }
        public string PUTime { get; set; }
        public string DOStreet { get; set; }
        public string DOCITY { get; set; }
        public string DOTelephone { get; set; }
        public string DOTime { get; set; }
        public string Age { get; set; }
        public string Miles { get; set; }
        public string Comments { get; set; }
        public bool Assignable { get; set; }
        public string GetAlerts()
        {
            string alertsstr = string.Empty;
            if (Alerts != null)
            {
                bool firstalert = true;
                foreach (string alert in Alerts)
                { 
                    if (firstalert)
                    {
                        alertsstr = alert;
                        firstalert = false;
                    }
                    else
                    {
                        alertsstr += " " + alert;
                    }
                }
            }

            if (alertsstr.Contains("Date"))
            {
                return "Date";
            }

            if (alertsstr.Contains("Cancelled"))
            {
                return "Cancelled";
            }

            if (alertsstr.Contains("Dupe"))
            {
                return "Dupe";
            }

            if (alertsstr.Contains("WC Not in reserves!"))
            {
                return "WC Not in reserves!";
            }

            if (alertsstr.Contains("Hidden"))
            {
                return "Hidden";
            }

            return alertsstr;
        }
        public int GetAlertCount()
        {
            int count = 0;
            foreach (string alert in Alerts)
            {
                if (DriverNameParsed == "Reserves")
                {
                    if (alert == "Cancelled")
                    {
                        return 0;
                    }
                }
                if (alert == "Date")
                {
                    return 1;
                }
                if (alert == "Dupe")
                {
                    return 1;
                }
                if (alert == "Cancelled")
                {
                    return 1;
                }
                if (alert == "WC Not in reserves!")
                {
                    return 1;
                }
                if (alert == "Hidden")
                {
                    return 1;
                }
                count++;
            }
            return count;
        }
        public Color GetColor()
        {
            foreach (string alert in Alerts)
            {
                if (alert.Contains("Date"))
                {
                    return Color.DarkRed;
                }

                if (alert.Contains("Hidden"))
                {
                    return Color.RoyalBlue;
                }

                if (alert.Contains("Cancelled"))
                {
                    return Color.FromArgb(80, 80, 80);
                }

                if (alert.Contains("Dupe"))
                {
                    return Color.DarkRed;
                }

                if (alert.Contains("WC Not in reserves!"))
                {
                    return Color.DarkRed;
                }

                if (alert.Contains("Time"))
                {
                    return Color.DarkRed;
                }

                if (alert.Contains("Address"))
                {
                    return Color.DarkRed;
                }

                if (alert.Contains("MWC"))
                {
                    return Color.OrangeRed;
                }

                if (alert.Contains("Child"))
                {
                    return Color.OrangeRed;
                }

                if (alert.Contains("Escort"))
                {
                    return Color.OrangeRed;
                }

                if (alert.Contains("LBS"))
                {
                    return Color.OrangeRed;
                }

                if (alert.Contains("Service Dog"))
                {
                    return Color.OrangeRed;
                }

                if (alert.Contains("Scooter"))
                {
                    return Color.OrangeRed;
                }

                if (alert.Contains("Mass Transit"))
                {
                    return Color.OrangeRed;
                }
            }
            return Color.FromArgb(80, 80, 80);
        }
        public Font GetFontStyle ()
        {
            foreach (string alert in Alerts)
            {
                if (alert.Contains("Date"))
                {
                    return new Font("Arial", 10, FontStyle.Regular);
                }

                if (alert.Contains("Hidden"))
                {
                    return new Font("Arial", 10, FontStyle.Bold);
                }

                if (alert.Contains("Cancelled"))
                {
                    return new Font("Arial", 10, FontStyle.Regular);
                }

                if (alert.Contains("Dupe"))
                {
                    return new Font("Arial", 10, FontStyle.Regular);
                }

                if (alert.Contains("WC Not in reserves!"))
                {
                    new Font("Arial", 10, FontStyle.Regular);
                }

                if (alert.Contains("Time"))
                {
                    new Font("Arial", 10, FontStyle.Regular);
                }

                if (alert.Contains("Address"))
                {
                    new Font("Arial", 10, FontStyle.Regular);
                }

                if (alert.Contains("MWC"))
                {
                    new Font("Arial", 10, FontStyle.Regular);
                }

                if (alert.Contains("Child"))
                {
                    new Font("Arial", 10, FontStyle.Regular);
                }

                if (alert.Contains("Escort"))
                {
                    new Font("Arial", 10, FontStyle.Regular);
                }

                if (alert.Contains("LBS"))
                {
                    new Font("Arial", 10, FontStyle.Regular);
                }

                if (alert.Contains("Service Dog"))
                {
                    new Font("Arial", 10, FontStyle.Regular);
                }

                if (alert.Contains("Scooter"))
                {
                    new Font("Arial", 10, FontStyle.Regular);
                }

                if (alert.Contains("Mass Transit"))
                {
                    new Font("Arial", 10, FontStyle.Regular);
                }
            }
            return new Font("Arial", 10, FontStyle.Regular);
        }
        public MCDownloadedTrip()
        {
            Alerts = new List<string>();
            Assignable = true;
        }
    }
}
