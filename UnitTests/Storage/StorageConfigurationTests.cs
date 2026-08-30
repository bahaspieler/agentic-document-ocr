using System.ComponentModel.DataAnnotations;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OcrAgent.Storage;

namespace UnitTests.Storage;

/// <summary>Tests SFTP and supporting storage configuration validation.</summary>
[TestClass]
public sealed class StorageConfigurationTests
{
    [TestMethod]
    public void SftpConfig_Validate_WithPasswordAuth_Succeeds()
    {
        var config = new SftpStorageConfig
        {
            Host = "localhost",
            Username = "user",
            AuthenticationMethod = "password",
            Password = "pass",
            RemoteBasePath = "/upload",
        };

        config.Validate();
    }

    [TestMethod]
    public void SftpConfig_Validate_WithMissingPassword_ThrowsValidationException()
    {
        var config = new SftpStorageConfig
        {
            Host = "localhost",
            Username = "user",
            AuthenticationMethod = "password",
            Password = null,
            RemoteBasePath = "/upload",
        };

        Assert.ThrowsExactly<ValidationException>(config.Validate);
    }

    [TestMethod]
    public void SftpConfig_Validate_WithPrivateKeyAuth_Succeeds()
    {
        var config = new SftpStorageConfig
        {
            Host = "localhost",
            Username = "user",
            AuthenticationMethod = "privatekey",
            PrivateKeyPath = "/path/to/key",
            RemoteBasePath = "/upload",
        };

        config.Validate();
    }

    [TestMethod]
    public void ConnectionPoolConfig_Validate_WithInvalidRange_ThrowsValidationException()
    {
        var config = new ConnectionPoolConfig
        {
            MinConnections = 10,
            MaxConnections = 5,
        };

        Assert.ThrowsExactly<ValidationException>(config.Validate);
    }

    [TestMethod]
    public void RetryConfig_Validate_WithInvalidDelays_ThrowsValidationException()
    {
        var config = new RetryConfig
        {
            InitialDelayMs = 5000,
            MaxDelayMs = 1000,
        };

        Assert.ThrowsExactly<ValidationException>(config.Validate);
    }
}
