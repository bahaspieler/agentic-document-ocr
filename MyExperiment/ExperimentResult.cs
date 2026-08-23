using Azure;
using Azure.Data.Tables;
using MyCloudProject.Common;
using System;
using System.Runtime.Serialization;

namespace MyExperiment
{
    /// <summary>Course result row plus OCR-specific execution evidence.</summary>
    public class ExperimentResult : ITableEntity, IExperimentResult
    {
        public ExperimentResult()
        {
        }

        public ExperimentResult(string partitionKey, string rowKey)
        {
            PartitionKey = partitionKey;
            RowKey = rowKey;
        }

        public string PartitionKey { get; set; } = "";

        public string RowKey { get; set; } = "";

        public DateTimeOffset? Timestamp { get; set; }

        public ETag ETag { get; set; }

        public string ExperimentId { get; set; } = "";

        public string Name { get; set; } = "";

        public string Description { get; set; } = "";

        public DateTime? StartTimeUtc { get; set; }

        public DateTime? EndTimeUtc { get; set; }

        public long DurationSec { get; set; }

        public string InputFileUrl { get; set; } = "";

        [IgnoreDataMember]
        public string[] OutputFiles { get; set; } = Array.Empty<string>();

        [IgnoreDataMember]
        public float Accuracy { get; set; }

        [IgnoreDataMember]
        public TimeSpan Duration { get; set; }

        public string Status { get; set; } = "failed";
        public bool Success { get; set; }
        public string Mode { get; set; } = "";
        public string Error { get; set; }
        public string Answer { get; set; }
        public string PrimaryRunId { get; set; }
        public string PrimaryOutputFileUrl { get; set; }
        public string OutputFilesJson { get; set; } = "[]";

        [IgnoreDataMember]
        public string[] LocalOutputFiles { get; set; } = Array.Empty<string>();

        [IgnoreDataMember]
        public string[] CleanupDirectories { get; set; } = Array.Empty<string>();
    }
}
