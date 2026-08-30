namespace OcrAgent.Storage;

/// <summary>Root directory for <see cref="LocalFileImageStore"/>.</summary>
public sealed class LocalStorageConfig
{
    /// <summary>Filesystem path; created on use if missing.</summary>
    public string RootPath { get; set; } = "./Storage";
}
