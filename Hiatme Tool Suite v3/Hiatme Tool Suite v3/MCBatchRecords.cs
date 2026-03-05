using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Hiatme_Tool_Suite_v3
{
    internal class MCBatchRecords
    {
        public List<MCBatchLink> MCBatchLinks { get; set; }
        public string ActiveBatchLink { get; set; }
        public List<MCBatchTripRecord> MCBatchTrips { get; set; }
        public List<MCBatchAdditionalInfo> MCBatchAdditionalInfo { get; set; }

        public MCBatchRecords()
        {
            MCBatchLinks = new List<MCBatchLink>();
            ActiveBatchLink = "";
            MCBatchTrips = new List<MCBatchTripRecord>();
            MCBatchAdditionalInfo = new List<MCBatchAdditionalInfo>();
        }
    }
}
