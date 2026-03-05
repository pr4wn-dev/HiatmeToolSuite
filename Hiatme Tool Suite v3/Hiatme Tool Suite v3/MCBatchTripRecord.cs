using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Hiatme_Tool_Suite_v3
{
    internal class MCBatchTripRecord
    {
        public string TripToken { get; set; }
        public string TripFull { get; set; }
        public string Trip { get; set; }
        public string Date { get; set; }
        public string Alerts { get; set; }
        public string Driver { get; set; }
        public string PUTime { get; set; }
        public string DOTime { get; set; }
        public string RiderCallTime { get; set; }
        public string Vehicle { get; set; }
        public string SignatureReceived { get; set; }
        public string CoPay { get; set; }
        public string BilledAmount { get; set; }
        public string RequiresAttention { get; set; }
        public string ScheduledPUTime { get; set; }
        public string ScheduledDOTime { get; set; }
        public string SuggestedPUTime { get; set; }
        public string SuggestedDOTime { get; set; }
        public string Status { get; set; }
        public string BatchLink { get; set; }
        public bool TripErrors { get; set; }
        public MCAssignedVehicle AssignedVehicle { get; set; }

        public MCBatchTripRecord()
        {
            AssignedVehicle = new MCAssignedVehicle();
        }
    }
}
