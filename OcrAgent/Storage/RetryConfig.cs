using System.ComponentModel.DataAnnotations;

namespace OcrAgent.Storage;

/// <summary>Retry count, backoff, and retryable exception type names.</summary>
public sealed class RetryConfig
{
    [Range(0, 10)]
    public int MaxRetries { get; set; } = 3;

    [Range(10, 10000)]
    public int InitialDelayMs { get; set; } = 100;

    [Range(100, 60000)]
    public int MaxDelayMs { get; set; } = 5000;

    [Range(1.0, 10.0)]
    public double BackoffMultiplier { get; set; } = 2.0;

    public List<string> RetryableExceptions { get; set; } =
    [
        "SshConnectionException",
        "SftpPathNotFoundException",
    ];

    /// <summary>Ensures the initial delay does not exceed the maximum.</summary>
    public void Validate()
    {
        if (InitialDelayMs > MaxDelayMs)
        {
            throw new ValidationException(
                $"InitialDelayMs ({InitialDelayMs}) cannot be greater than MaxDelayMs ({MaxDelayMs})");
        }
    }
}
