using System.ComponentModel.DataAnnotations;

namespace OcrAgent.Storage;

/// <summary>Host, authentication, path, pool, retry, timeout, and behavior settings for SFTP.</summary>
public sealed class SftpStorageConfig
{
    /// <summary>SFTP server host name or address.</summary>
    [Required]
    public string Host { get; set; } = "localhost";

    /// <summary>SFTP server port.</summary>
    [Range(1, 65535)]
    public int Port { get; set; } = 22;

    /// <summary>User name used to authenticate with the server.</summary>
    [Required]
    public string Username { get; set; } = "";

    /// <summary>Authentication mode: password, privatekey, or agent.</summary>
    public string AuthenticationMethod { get; set; } = "password";
    /// <summary>Password used by password authentication.</summary>
    public string? Password { get; set; }
    /// <summary>Path to the key used by private-key authentication.</summary>
    public string? PrivateKeyPath { get; set; }
    /// <summary>Optional passphrase for the private key.</summary>
    public string? PrivateKeyPassphrase { get; set; }

    /// <summary>Remote directory under which files are stored.</summary>
    [Required]
    public string RemoteBasePath { get; set; } = "/upload/ocr";

    /// <summary>Connection-pool settings.</summary>
    public ConnectionPoolConfig ConnectionPool { get; set; } = new();
    /// <summary>Retry settings for SFTP operations.</summary>
    public RetryConfig Retry { get; set; } = new();
    /// <summary>Connection and operation timeout settings.</summary>
    public TimeoutsConfig Timeouts { get; set; } = new();
    /// <summary>Transfer and remote-directory behavior settings.</summary>
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
