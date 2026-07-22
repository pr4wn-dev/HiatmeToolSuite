using System;
using System.Collections.Generic;

namespace Hiatme_Tool_Suite_v3
{
    /// <summary>
    /// Fields for one printable driver corrective-action / discipline write-up.
    /// </summary>
    internal sealed class DriverDisciplineRecord
    {
        public string CaseNumber { get; set; } = "";
        public DateTime NoticeDate { get; set; } = DateTime.Today;

        public string DriverName { get; set; } = "";
        public string EmployeeId { get; set; } = "";
        public string Vehicle { get; set; } = "";
        public string SupervisorName { get; set; } = "";
        public string Department { get; set; } = "Operations";

        public DateTime IncidentDate { get; set; } = DateTime.Today;
        public string IncidentTime { get; set; } = "";
        public string TripOrClientRef { get; set; } = "";
        public string Location { get; set; } = "";

        /// <summary>Checked violation labels (user-friendly names).</summary>
        public List<string> Violations { get; set; } = new List<string>();

        /// <summary>Coaching, Verbal Warning, Written Warning, Final Warning, Suspension, Termination recommended.</summary>
        public string ActionLevel { get; set; } = "Written Warning";

        public string FootageSummary { get; set; } = "";
        public string Narrative { get; set; } = "";
        public string PolicyCited { get; set; } = "";
        public string PriorHistory { get; set; } = "";
        public string CorrectiveAction { get; set; } = "";
        public string FollowUpDate { get; set; } = "";
        public string DriverStatement { get; set; } = "";

        public string FootageFolder { get; set; } = "";
        public List<string> ClipPaths { get; set; } = new List<string>();

        public string PreparedBy { get; set; } = "";
    }

    internal static class DriverDisciplineOptions
    {
        public static readonly string[] Violations =
        {
            "Speeding / unsafe speed",
            "Hard braking / aggressive driving",
            "Distracted driving (phone, eating, etc.)",
            "Failure to follow route or company policy",
            "Unprofessional conduct",
            "Client mistreatment / verbal abuse",
            "Threat to client safety / physical misconduct",
            "Failure to report an incident",
            "Equipment misuse / dashcam interference",
            "Other (describe in narrative)",
        };

        public static readonly string[] ActionLevels =
        {
            "Coaching / counseling",
            "Verbal warning",
            "Written warning",
            "Final written warning",
            "Suspension",
            "Termination recommended",
        };
    }
}
