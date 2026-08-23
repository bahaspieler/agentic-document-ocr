namespace MyExperiment
{
    /// <summary>
    /// This is the class which describe the project configuration.
    /// </summary>
    public class MyConfig
    {
        public string StorageConnectionString { get; set; }

        /// <summary>
        /// The name of the container in the blob storage, where training files are located.
        /// </summary>
        public string TrainingContainer { get; set; }

        /// <summary>
        /// The name of the container in the blob storage, where result files will be uploaded.
        /// </summary>
        public string ResultContainer { get; set; }

        /// <summary>
        /// The name of the table where result information will be uploaded.
        /// </summary>
        public string ResultTable { get; set; }

        /// <summary>
        /// The name of the queue used to trigger the computation.
        /// </summary>
        public string Queue{ get; set; }

        /// <summary>
        /// Set here the name of your group.
        /// </summary>
        public string GroupId { get; set; }

        /// <summary>Root for temporary command, image, and output files.</summary>
        public string WorkRoot { get; set; } =
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hidden-layer-worker");

        /// <summary>How long a received queue message remains hidden while a job executes.</summary>
        public int MessageVisibilityTimeoutSeconds { get; set; } = 900;

        /// <summary>Whether configured queue/blob/table resources may be created when absent.</summary>
        public bool EnsureStorageResources { get; set; } = true;

    }
}


