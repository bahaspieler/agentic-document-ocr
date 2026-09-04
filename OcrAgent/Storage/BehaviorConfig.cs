using System.ComponentModel.DataAnnotations;

namespace OcrAgent.Storage;

/// <summary>Remote-directory, transfer-buffer, and debug-artifact behavior.</summary>
public sealed class BehaviorConfig
{
    /// <summary>Whether missing remote directories are created before upload.</summary>
    public bool CreateRemoteDirectoryIfNotExists { get; set; } = true;
    /// <summary>Whether transfer compression is requested.</summary>
    public bool UseCompression { get; set; } = true;

    /// <summary>Transfer buffer size in bytes.</summary>
    [Range(4096, 1048576)]
    public int BufferSize { get; set; } = 32768;

    /// <summary>Whether diagnostic images may be uploaded.</summary>
    public bool EnableDebugFolders { get; set; } = true;
}
