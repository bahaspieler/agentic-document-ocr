using System.ComponentModel.DataAnnotations;

namespace OcrAgent.Storage;

/// <summary>SSH connection, SFTP operation, and upload timeouts in seconds.</summary>
public sealed class TimeoutsConfig
{
    /// <summary>Maximum time allowed to establish an SSH connection, in seconds.</summary>
    [Range(5, 300)]
    public int ConnectionTimeout { get; set; } = 30;

    /// <summary>Maximum time allowed for an SFTP operation, in seconds.</summary>
    [Range(10, 3600)]
    public int OperationTimeout { get; set; } = 300;

    /// <summary>Maximum time allowed for an upload, in seconds.</summary>
    [Range(30, 7200)]
    public int UploadTimeout { get; set; } = 600;
}
