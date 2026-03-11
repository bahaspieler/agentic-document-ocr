namespace OcrAgent.Storage;

public static class StorageFactory
{
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
