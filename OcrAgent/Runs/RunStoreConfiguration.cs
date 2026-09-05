using System.ComponentModel.DataAnnotations;

namespace OcrAgent.Runs;

/// <summary>
/// Binding for the <c>Runs</c> configuration section: which run store to build and how to reach it.
/// </summary>
public sealed class RunStoreConfiguration
{
    /// <summary><c>none</c> or <c>azuretable</c> (case-insensitive in <see cref="RunStoreFactory"/>).</summary>
    public string Provider { get; set; } = "none";

    /// <summary>
    /// Storage account connection string. Left empty in committed configuration; supplied through the
    /// <c>AZURE_STORAGE_CONNECTION_STRING</c> environment variable or a deployment secret, the same
    /// way <see cref="Storage.AzureBlobStorageConfig"/> takes it.
    /// </summary>
    public string ConnectionString { get; set; } = "";

    /// <summary>Table that holds the run records.</summary>
    [Required]
    public string TableName { get; set; } = "OcrRuns";

    /// <summary>Partition key for every record this deployment writes.</summary>
    [Required]
    public string GroupId { get; set; } = "AgenticDocumentOcr";

    /// <summary>Creates the table on first use when it does not exist.</summary>
    public bool CreateTableIfNotExists { get; set; } = true;

    /// <summary>Throws if the connection string, table name, or group id is missing.</summary>
    /// <exception cref="ValidationException">Required setting missing.</exception>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
            throw new ValidationException(
                "Run store ConnectionString is required. Set Runs:ConnectionString or the AZURE_STORAGE_CONNECTION_STRING environment variable.");

        if (string.IsNullOrWhiteSpace(TableName))
            throw new ValidationException("Run store TableName is required");

        if (string.IsNullOrWhiteSpace(GroupId))
            throw new ValidationException("Run store GroupId is required");
    }
}
