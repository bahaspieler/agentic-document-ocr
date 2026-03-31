using System.ComponentModel.DataAnnotations;

namespace OcrAgent.Storage;

/// <summary>
/// Top-level storage binding: provider id plus local and SFTP option objects.
/// </summary>
public sealed class StorageConfiguration
{
    /// <summary><c>local</c> or <c>sftp</c> (case-insensitive in <see cref="StorageFactory"/>).</summary>
    public string Provider { get; set; } = "local";

    /// <summary>Settings when <see cref="Provider"/> is local.</summary>
    public LocalStorageConfig Local { get; set; } = new();

    /// <summary>Settings when <see cref="Provider"/> is SFTP.</summary>
    public SftpStorageConfig Sftp { get; set; } = new();
}

/// <summary>Root directory for <see cref="LocalFileImageStore"/>.</summary>
public sealed class LocalStorageConfig
{
    /// <summary>Filesystem path; created on use if missing.</summary>
    public string RootPath { get; set; } = "./Storage";
}

/// <summary>Host, auth, remote path, pool, retry, timeout, and behavior flags for SFTP storage.</summary>
public sealed class SftpStorageConfig
{
    [Required]
    public string Host { get; set; } = "localhost";

    [Range(1, 65535)]
    public int Port { get; set; } = 22;

    [Required]
    public string Username { get; set; } = "";

    public string AuthenticationMethod { get; set; } = "password";

    public string? Password { get; set; }

    public string? PrivateKeyPath { get; set; }

    public string? PrivateKeyPassphrase { get; set; }

    [Required]
    public string RemoteBasePath { get; set; } = "/upload/ocr";

    public ConnectionPoolConfig ConnectionPool { get; set; } = new();

    public RetryConfig Retry { get; set; } = new();

    public TimeoutsConfig Timeouts { get; set; } = new();

    public BehaviorConfig Behavior { get; set; } = new();

    /// <summary>Throws if host, user, or auth-specific fields are invalid.</summary>
    /// <exception cref="ValidationException">Required field missing or unknown auth method.</exception>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Host))
            throw new ValidationException("SFTP Host is required");

        if (string.IsNullOrWhiteSpace(Username))
            throw new ValidationException("SFTP Username is required");

        var authMethod = AuthenticationMethod.ToLowerInvariant();
        if (authMethod == "password" && string.IsNullOrWhiteSpace(Password))
            throw new ValidationException("SFTP Password is required when using password authentication");

        if (authMethod == "privatekey" && string.IsNullOrWhiteSpace(PrivateKeyPath))
            throw new ValidationException("SFTP PrivateKeyPath is required when using private key authentication");

        if (authMethod != "password" && authMethod != "privatekey" && authMethod != "agent")
            throw new ValidationException($"Unknown authentication method: {AuthenticationMethod}. Must be 'password', 'privatekey', or 'agent'");
    }
}

/// <summary>Min/max clients, idle timeout, keep-alive, and health-check cadence.</summary>
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

    /// <summary>Ensures min does not exceed max.</summary>
    /// <exception cref="ValidationException">Invalid min/max pair.</exception>
    public void Validate()
    {
        if (MinConnections > MaxConnections)
            throw new ValidationException($"MinConnections ({MinConnections}) cannot be greater than MaxConnections ({MaxConnections})");
    }
}

/// <summary>Retry count, backoff, and which exception type names may trigger retry.</summary>
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

    public List<string> RetryableExceptions { get; set; } = new()
    {
        "SshConnectionException",
        "SftpPathNotFoundException"
    };

    /// <summary>Ensures initial delay is not above max delay.</summary>
    /// <exception cref="ValidationException">Invalid delay range.</exception>
    public void Validate()
    {
        if (InitialDelayMs > MaxDelayMs)
            throw new ValidationException($"InitialDelayMs ({InitialDelayMs}) cannot be greater than MaxDelayMs ({MaxDelayMs})");
    }
}

/// <summary>SSH connect, SFTP operation, and upload timeouts in seconds.</summary>
public sealed class TimeoutsConfig
{
    [Range(5, 300)]
    public int ConnectionTimeout { get; set; } = 30;

    [Range(10, 3600)]
    public int OperationTimeout { get; set; } = 300;

    [Range(30, 7200)]
    public int UploadTimeout { get; set; } = 600;
}

/// <summary>Remote mkdir, compression hint, buffer size, and debug folder uploads.</summary>
public sealed class BehaviorConfig
{
    public bool CreateRemoteDirectoryIfNotExists { get; set; } = true;

    public bool UseCompression { get; set; } = true;

    [Range(4096, 1048576)]
    public int BufferSize { get; set; } = 32768;

    public bool EnableDebugFolders { get; set; } = true;
}
