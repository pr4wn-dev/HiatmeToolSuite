using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Hiatme_Tool_Suite_v3
{
    internal class MCDriverTab
    {
        public string driverName {  get; set; }
        public List<MCDownloadedTrip> scheduledTrips {get; set;}

        public MCDriverTab()
        {
            scheduledTrips = new List<MCDownloadedTrip>();
            driverName = string.Empty;
        }
    }
}
