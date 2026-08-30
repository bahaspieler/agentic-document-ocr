using System.ComponentModel.DataAnnotations;

namespace OcrAgent.Storage;

/// <summary>Host, authentication, path, pool, retry, timeout, and behavior settings for SFTP.</summary>
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

    /// <summary>Throws if host, user, or authentication-specific fields are invalid.</summary>
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
        {
            throw new ValidationException(
                $"Unknown authentication method: {AuthenticationMethod}. Must be 'password', 'privatekey', or 'agent'");
        }
    }
}
