using System.Collections.Generic;
using Newtonsoft.Json;

namespace Hiatme_Tool_Suite_v3
{
    internal sealed class HiatmeSchedulePatch
    {
        [JsonProperty("version")]
        public int Version { get; set; } = 1;

        [JsonProperty("message")]
        public string Message { get; set; }

        [JsonProperty("actions")]
        public List<HiatmeScheduleAction> Actions { get; set; } = new List<HiatmeScheduleAction>();

        [JsonProperty("confidence")]
        public string Confidence { get; set; }
    }

    internal sealed class HiatmeScheduleAction
    {
        [JsonProperty("op")]
        public string Op { get; set; }

        [JsonProperty("trip_number")]
        public string TripNumber { get; set; }

        [JsonProperty("driver_name")]
        public string DriverName { get; set; }

        [JsonProperty("selected")]
        public bool Selected { get; set; } = true;

        [JsonProperty("use_templates")]
        public bool UseTemplates { get; set; } = true;
    }

    internal sealed class HiatmePatchApplyResult
    {
        public bool Ok { get; set; }
        public string Summary { get; set; }
        public List<string> Errors { get; } = new List<string>();
        public bool ShouldRebuild { get; set; }
        public bool? RebuildUseTemplates { get; set; }
    }
}
