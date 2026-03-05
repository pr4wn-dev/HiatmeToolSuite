using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Hiatme_Tool_Suite_v3
{
    public class WRDownloadedTrip
    {
        public string PUTime { get; set; }
        public string DOTime { get; set; }
        public string ActualPUTime { get; set; }
        public string ActualDOTime { get; set; }
        public string Status { get; set; }
        public string TripUUID { get; set; }
        public string TripNumber { get; set; }
        public string ClientName { get; set; }
        public string Miles { get; set; }        
        public string DriverName { get; set; }
        public List<string> Alerts { get; set; }
        public string Price { get; set; }
        public string PUStreet { get; set; }
        public string PUCity { get; set; }
        public string DOStreet { get; set; }
        public string DOCITY { get; set; }
        public string DOB { get; set; }
        public string Age { get; set; }
        public string PUPhone { get; set; }
        public string DOPhone { get; set; }
        public string Comments { get; set; }
        public string Escorts { get; set; }
        public string ScheduleLocation { get; set; }
        public string References { get; set; }

        public WRDownloadedTrip()
        {
            Alerts = new List<string>();
        }
    }

}
