namespace ProcesadorXmlPedidos
{
    /// <summary>
    /// Represents configuration values read from appsettings.json under the "Worker" section.
    /// </summary>
    public class WorkerConfig
    {
        /// <summary>Interval between polls, in seconds. Defaults to 30.</summary>
        public int IntervalSeconds { get; set; } = 30;

        /// <summary>Path to the input directory (relative or absolute).</summary>
        public string InputFolder { get; set; } = "input";

        /// <summary>Path to the folder where successfully processed files are moved.</summary>
        public string ProcessedFolder { get; set; } = "processed";

        /// <summary>Path to the folder where files that generated errors are moved.</summary>
        public string ErrorFolder { get; set; } = "error";
    }
}