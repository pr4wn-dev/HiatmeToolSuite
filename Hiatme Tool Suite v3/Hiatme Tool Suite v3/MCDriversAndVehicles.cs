using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Hiatme_Tool_Suite_v3
{
    internal class MCDriversAndVehicles
    {
        //store list of drivers and vehicles for this day
        public IDictionary<string, string> DriversAndVehicles { get; set; }

        //store list of drivers and website ids
        public IDictionary<string, string> DriversAndIDs { get; set; }

        //store list of vehicles and website ids
        public IDictionary<string, string> VehiclesAndIDs { get; set; }

        public MCDriversAndVehicles()
        {
            DriversAndVehicles = new Dictionary<string, string>();
            VehiclesAndIDs = new Dictionary<string, string>();
            DriversAndIDs = new Dictionary<string, string>();
        }
    }
}
