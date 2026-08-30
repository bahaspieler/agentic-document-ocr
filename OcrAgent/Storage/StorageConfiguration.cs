namespace OcrAgent.Storage;

/// <summary>
/// Top-level storage binding: provider id plus local and SFTP option objects.
/// </summary>
public sealed class StorageConfiguration
{
    /// <summary><c>local</c>, <c>sftp</c>, or <c>azureblob</c> (case-insensitive in <see cref="StorageFactory"/>).</summary>
    public string Provider { get; set; } = "local";

    /// <summary>Settings when <see cref="Provider"/> is local.</summary>
    public LocalStorageConfig Local { get; set; } = new();

    /// <summary>Settings when <see cref="Provider"/> is SFTP.</summary>
    public SftpStorageConfig Sftp { get; set; } = new();

    /// <summary>Settings when <see cref="Provider"/> is Azure Blob Storage.</summary>
    public AzureBlobStorageConfig AzureBlob { get; set; } = new();
}
