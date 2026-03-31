namespace OcrAgent.Storage;

/// <summary>
/// Builds <see cref="IImageStore"/> from <see cref="StorageConfiguration"/> (local folder or SFTP).
/// </summary>
public static class StorageFactory
{
    /// <summary>Selects local or SFTP implementation and validates SFTP settings.</summary>
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
            _ => throw new ArgumentException($"Unknown storage provider: {config.Provider}. Must be 'local' or 'sftp'.")
        };
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
