namespace MyExperiment
{
    /// <summary>Azure queue, Blob, and Table settings bound from the MyConfig section.</summary>
    public class MyConfig
    {
        /// <summary>Connection string for the queue, Blob containers, and result table.</summary>
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

        /// <summary>Partition key for result rows and prefix for result Blob paths.</summary>
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


