namespace Hiatme_Tool_Suite_v3
{
    /// <summary>How Time Correction evaluates trips when a batch is loaded.</summary>
    internal enum TimeCorrectionLoadMode
    {
        /// <summary>Scoreboard PU/DO windows (current behavior).</summary>
        StandardScoreboard,

        /// <summary>Only severe timing violations; always fix missing driver/vehicle/data.</summary>
        Lenient,

        /// <summary>No timing checks — only missing/invalid driver, vehicle, and related data.</summary>
        DataOnly,

        /// <summary>Standard scoreboard timing and driver/vehicle fixes only for portal red rows.</summary>
        ModivcareRedOnly,
    }
}
