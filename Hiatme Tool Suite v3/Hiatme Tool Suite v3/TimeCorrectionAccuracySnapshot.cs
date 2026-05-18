using System.Collections.Generic;

namespace Hiatme_Tool_Suite_v3
{
    internal sealed class TimeCorrectionAccuracySnapshot
    {
        public List<MCDriver> Drivers { get; set; } = new List<MCDriver>();

        public int TotalLegs { get; set; }

        public int AccurateLegs { get; set; }

        public double CompanyAccuracyPercent { get; set; }

        public string ServiceDateLabel { get; set; } = "";

        public int PassedTrips { get; set; }

        public int FixableTrips { get; set; }

        public int FailedTrips { get; set; }

        public int TotalTrips { get; set; }
    }
}
