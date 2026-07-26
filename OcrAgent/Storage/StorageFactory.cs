namespace OcrAgent.Storage;

/// <summary>
/// Builds <see cref="IImageStore"/> from <see cref="StorageConfiguration"/> (local folder, SFTP, or Azure Blob).
/// </summary>
public static class StorageFactory
{
    /// <summary>Selects the implementation named by the configuration and validates its settings.</summary>
    /// <param name="config">Provider name and nested options.</param>
    /// <returns>Configured store instance.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="config"/> is null.</exception>
    /// <exception cref="ArgumentException">Unknown <see cref="StorageConfiguration.Provider"/>.</exception>
    public static IImageStore Create(StorageConfiguration config)
    {
        if (config == null)
            throw new ArgumentNullException(nameof(config));

        var provider = config.Provider.ToLowerInvariant();

        return provider switch
        {
            "local" => new LocalFileImageStore(config.Local.RootPath),
            "sftp" => CreateSftpStore(config.Sftp),
            "azureblob" => CreateAzureBlobStore(config.AzureBlob),
            _ => throw new ArgumentException($"Unknown storage provider: {config.Provider}. Must be 'local', 'sftp', or 'azureblob'.")
        };
    }

    private static IImageStore CreateAzureBlobStore(AzureBlobStorageConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.ConnectionString))
        {
            config.ConnectionString = Environment.GetEnvironmentVariable("AZURE_STORAGE_CONNECTION_STRING") ?? "";
        }

        config.Validate();
        return new AzureBlobImageStore(config);
    }

    private static IImageStore CreateSftpStore(SftpStorageConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.Password))
        {
            config.Password = Environment.GetEnvironmentVariable("SFTP_PASSWORD");
        }

        config.Validate();
        return new SftpImageStore(config);
    }
}
