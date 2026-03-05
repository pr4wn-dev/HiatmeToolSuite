using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Hiatme_Tool_Suite_v3
{
    internal class MCBatchLink
    {
      //  public string TotalBatches { get; set; }
        public string BatchLinkToken { get; set; }
        public string BatchID { get; set; }
        public string CreateDate { get; set; }
        public string CreatedBy { get; set; }
        public string TripCount { get; set; }
        public string FailedTripCount { get; set; }
        public string RequiresAttention { get; set; }
        public string TotalBilledAmount { get; set; }

    }
}
