using System.ComponentModel.DataAnnotations;

namespace OcrAgent.Storage;

/// <summary>SSH connection, SFTP operation, and upload timeouts in seconds.</summary>
public sealed class TimeoutsConfig
{
    [Range(5, 300)]
    public int ConnectionTimeout { get; set; } = 30;

    [Range(10, 3600)]
    public int OperationTimeout { get; set; } = 300;

    [Range(30, 7200)]
    public int UploadTimeout { get; set; } = 600;
}
