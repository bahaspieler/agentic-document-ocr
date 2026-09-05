using Azure;
using Azure.Data.Tables;
using OcrAgent.Worker.Contracts;
using System;
using System.Runtime.Serialization;

namespace OcrAgent.Worker.Azure
{
    /// <summary>Worker result row plus OCR-specific execution evidence.</summary>
    public class ExperimentResult : ITableEntity, IExperimentResult
    {
        /// <summary>Initializes an empty result row for table deserialization.</summary>
        public ExperimentResult()
        {
        }

        /// <summary>Initializes a result row with its table keys.</summary>
        /// <param name="partitionKey">Table partition key.</param>
        /// <param name="rowKey">Table row key.</param>
        public ExperimentResult(string partitionKey, string rowKey)
        {
            PartitionKey = partitionKey;
            RowKey = rowKey;
        }

        /// <summary>Gets or sets the Azure Table Storage partition key.</summary>
        public string PartitionKey { get; set; } = "";

        /// <summary>Gets or sets the Azure Table Storage row key.</summary>
        public string RowKey { get; set; } = "";

        /// <summary>Gets or sets the timestamp maintained by Azure Table Storage.</summary>
        public DateTimeOffset? Timestamp { get; set; }

        /// <summary>Gets or sets the entity tag used for table concurrency control.</summary>
        public ETag ETag { get; set; }

        /// <summary>Gets or sets the identifier of the experiment execution.</summary>
        public string ExperimentId { get; set; } = "";

        /// <summary>Gets or sets the natural-language command executed by the agent.</summary>
        public string Name { get; set; } = "";

        /// <summary>Gets or sets the experiment description.</summary>
        public string Description { get; set; } = "";

        /// <summary>Gets or sets the UTC time when execution started.</summary>
        public DateTime? StartTimeUtc { get; set; }

        /// <summary>Gets or sets the UTC time when execution ended.</summary>
        public DateTime? EndTimeUtc { get; set; }

        /// <summary>Gets or sets the rounded execution duration in seconds.</summary>
        public long DurationSec { get; set; }

        /// <summary>Gets or sets the source training Blob name.</summary>
        public string InputFileUrl { get; set; } = "";

        /// <summary>Gets or sets the URLs of uploaded result artifacts.</summary>
        [IgnoreDataMember]
        public string[] OutputFiles { get; set; } = Array.Empty<string>();

        /// <summary>Gets or sets the accuracy value required by the experiment contract.</summary>
        [IgnoreDataMember]
        public float Accuracy { get; set; }

        /// <summary>Gets or sets the precise in-memory execution duration.</summary>
        [IgnoreDataMember]
        public TimeSpan Duration { get; set; }

        /// <summary>Gets or sets the execution status.</summary>
        public string Status { get; set; } = "failed";

        /// <summary>Gets or sets whether execution completed successfully.</summary>
        public bool Success { get; set; }

        /// <summary>Gets or sets the execution mode used by the OCR engine.</summary>
        public string Mode { get; set; } = "";

        /// <summary>Gets or sets the failure message, if execution failed.</summary>
        public string? Error { get; set; }

        /// <summary>Gets or sets the agent's final response.</summary>
        public string? Answer { get; set; }

        /// <summary>Gets or sets the primary OCR engine run identifier.</summary>
        public string? PrimaryRunId { get; set; }

        /// <summary>Gets or sets the URL of the primary uploaded artifact.</summary>
        public string? PrimaryOutputFileUrl { get; set; }

        /// <summary>Gets or sets the serialized URLs of uploaded result artifacts.</summary>
        public string OutputFilesJson { get; set; } = "[]";

        /// <summary>Gets or sets local artifact paths awaiting upload.</summary>
        [IgnoreDataMember]
        public string[] LocalOutputFiles { get; set; } = Array.Empty<string>();

        /// <summary>Gets or sets temporary directories removed after durable upload.</summary>
        [IgnoreDataMember]
        public string[] CleanupDirectories { get; set; } = Array.Empty<string>();

        /// <summary>Gets or sets the result kind: single, batch, or batch-item.</summary>
        public string ResultKind { get; set; } = "single";

        /// <summary>Gets or sets the parent batch identifier.</summary>
        public string? BatchId { get; set; }

        /// <summary>Gets or sets the deterministic identifier of a batch item.</summary>
        public string? BatchItemId { get; set; }

        /// <summary>Gets or sets the source Blob name for a batch item.</summary>
        public string? SourceBlobName { get; set; }

        /// <summary>Gets or sets the dataset folder selected for a batch.</summary>
        public string? DatasetName { get; set; }

        /// <summary>Gets or sets the validated serialized plan shared by all batch items.</summary>
        public string? PlanJson { get; set; }

        /// <summary>Gets or sets the number of images selected by a batch.</summary>
        public int ItemCount { get; set; }
    }
}
