using System;
using System.Collections.Generic;

namespace Hiatme_Tool_Suite_v3
{
    internal sealed class FsWellRydeAssignResult
    {
        public DateTime ServiceDate { get; set; }
        public int SentSlots { get; set; }
        public int Skipped { get; set; }
        public int NoWellRydeMatch { get; set; }
        public int DriversSent { get; set; }
        public int AssignedOnWellRyde { get; set; }
        public int ReservedOnWellRyde { get; set; }
        public bool PortalWritesEnabled { get; set; } = true;
        public List<FsWellRydeAssignDriverRow> Drivers { get; } = new List<FsWellRydeAssignDriverRow>();
        public List<FsWellRydeAssignSkipRow> Skips { get; } = new List<FsWellRydeAssignSkipRow>();
    }

    internal sealed class FsWellRydeAssignDriverRow
    {
        public string DriverName { get; set; }
        public int Sent { get; set; }
    }

    internal sealed class FsWellRydeAssignSkipRow
    {
        public string TripNumber { get; set; }
        public string DriverName { get; set; }
        public string Reason { get; set; }
    }
}
