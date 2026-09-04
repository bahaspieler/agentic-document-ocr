using System.ComponentModel.DataAnnotations;

namespace OcrAgent.Storage;

/// <summary>Minimum and maximum clients plus connection health settings.</summary>
public sealed class ConnectionPoolConfig
{
    /// <summary>Minimum number of connections kept by the pool.</summary>
    [Range(1, 100)]
    public int MinConnections { get; set; } = 1;

    /// <summary>Maximum number of simultaneous pooled connections.</summary>
    [Range(1, 100)]
    public int MaxConnections { get; set; } = 10;

    /// <summary>Time before an idle connection may be removed, in seconds.</summary>
    [Range(10, 3600)]
    public int ConnectionIdleTimeout { get; set; } = 300;

    /// <summary>Interval between SSH keep-alive messages, in seconds.</summary>
    [Range(10, 300)]
    public int KeepAliveInterval { get; set; } = 60;

    /// <summary>Whether idle connections are checked for validity.</summary>
    public bool EnableHealthChecks { get; set; } = true;

    /// <summary>Interval between connection health checks, in seconds.</summary>
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
