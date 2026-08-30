using System.ComponentModel.DataAnnotations;

namespace OcrAgent.Storage;

/// <summary>Minimum and maximum clients plus connection health settings.</summary>
public sealed class ConnectionPoolConfig
{
    [Range(1, 100)]
    public int MinConnections { get; set; } = 1;

    [Range(1, 100)]
    public int MaxConnections { get; set; } = 10;

    [Range(10, 3600)]
    public int ConnectionIdleTimeout { get; set; } = 300;

    [Range(10, 300)]
    public int KeepAliveInterval { get; set; } = 60;

    public bool EnableHealthChecks { get; set; } = true;

    [Range(10, 300)]
    public int HealthCheckInterval { get; set; } = 30;

    /// <summary>Ensures the minimum does not exceed the maximum.</summary>
    public void Validate()
    {
        if (MinConnections > MaxConnections)
        {
            throw new ValidationException(
                $"MinConnections ({MinConnections}) cannot be greater than MaxConnections ({MaxConnections})");
        }
    }
}
