using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Hiatme_Tool_Suite_v3
{
    internal class MCAssignedVehicle
    {
        public string Driver { get; set; }
        public string DriverTag { get; set; }
        public string Vehicle { get; set; }
        public string VehicleTag { get; set;}
        public bool updated { get; set; }
    }
    internal class MCAvailableVehicle
    {
        public string Vehicle { get; set; }
        public string VehicleTag { get; set; }
    }
    internal class MCDriver
    {
        public string Driver { get; set; }
        public string DriverTag { get; set; }
        public int Accuracies { get; set; }
        public int Inaccuracies { get; set; }
        public int Triplegs { get; set; }
        public double AccuracyPercent { get; set; }
    }
}
