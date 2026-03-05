using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Hiatme_Tool_Suite_v3
{
    internal class MCBatchAdditionalInfo
    {
        //store list of drivers and vehicles for this day

        //store wellrydes records for this day
        public List<WRDownloadedTrip> wrDownloadedTrips { get; set; } //trips that are downloaded. Can be used to compare to batch trips for corrections
        public List<MCDownloadedTrip> mcDownloadedTrips { get; set; } //trips that are downloaded. Can be used to compare to batch trips for corrections
        public List<MCAvailableVehicle> MCAvailableVehicles { get; set; }
        public List<MCAssignedVehicle> MCAssignedVehicles { get; set; }
        public List<MCDriver> MCDrivers { get; set; }
        //store the date of this day
        public string MCBatchDate { get; set; }
        public MCBatchAdditionalInfo(){
            wrDownloadedTrips = new List<WRDownloadedTrip>();
            mcDownloadedTrips = new List<MCDownloadedTrip>();
            MCAvailableVehicles = new List<MCAvailableVehicle>();
            MCAssignedVehicles = new List<MCAssignedVehicle>();
            MCDrivers = new List<MCDriver>();
            MCBatchDate = "";
        }
    }
}
