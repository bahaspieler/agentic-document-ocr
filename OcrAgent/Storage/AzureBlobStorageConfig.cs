using System.ComponentModel.DataAnnotations;

namespace OcrAgent.Storage;

/// <summary>Container, credentials, and local cache location for <see cref="AzureBlobImageStore"/>.</summary>
public sealed class AzureBlobStorageConfig
{
    /// <summary>Storage connection string supplied through configuration or a deployment secret.</summary>
    public string ConnectionString { get; set; } = "";

    /// <summary>Blob container that holds images, sidecars, and run artifacts.</summary>
    [Required]
    public string ContainerName { get; set; } = "ocr-images";

    /// <summary>Optional prefix inside the container.</summary>
    public string BlobPrefix { get; set; } = "";

    /// <summary>Local directory used to stage downloads and pending uploads.</summary>
    public string CacheRootPath { get; set; } = "./blob-cache";

    /// <summary>Creates the container on first use when it does not exist.</summary>
    public bool CreateContainerIfNotExists { get; set; } = true;

    /// <summary>Throws if the connection string or container name is missing.</summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            throw new ValidationException(
                "Azure Blob ConnectionString is required. Set Storage:AzureBlob:ConnectionString or the AZURE_STORAGE_CONNECTION_STRING environment variable.");
        }

        if (string.IsNullOrWhiteSpace(ContainerName))
            throw new ValidationException("Azure Blob ContainerName is required");
    }
}
