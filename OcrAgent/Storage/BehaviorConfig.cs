using System.ComponentModel.DataAnnotations;

namespace OcrAgent.Storage;

/// <summary>Remote-directory, transfer-buffer, and debug-artifact behavior.</summary>
public sealed class BehaviorConfig
{
    public bool CreateRemoteDirectoryIfNotExists { get; set; } = true;
    public bool UseCompression { get; set; } = true;

    [Range(4096, 1048576)]
    public int BufferSize { get; set; } = 32768;

    public bool EnableDebugFolders { get; set; } = true;
}
