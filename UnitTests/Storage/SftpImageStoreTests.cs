using Microsoft.VisualStudio.TestTools.UnitTesting;
using OcrAgent.Storage;
using OpenCvSharp;
using System.Diagnostics;
using System.Security;

namespace UnitTests.Storage;

[TestClass]
[TestCategory("Integration")]
public sealed class SftpImageStoreTests
{
    private static Process? _dockerProcess;
    private SftpStorageConfig _config = null!;

    [AssemblyInitialize]
    public static void StartSftpServer(TestContext context)
    {
        Console.WriteLine("Starting SFTP server for integration tests...");
        
        try
        {
            var dockerComposeDir = FindDockerComposeDirectory();
            if (dockerComposeDir == null)
            {
                Console.WriteLine("Warning: docker/sftp directory not found. Assuming SFTP server is already running.");
                return;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = "docker-compose",
                Arguments = "up -d",
                WorkingDirectory = dockerComposeDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };

            _dockerProcess = Process.Start(startInfo);
            _dockerProcess?.WaitForExit();

            Console.WriteLine("Waiting for SFTP server to be ready...");
            Thread.Sleep(8000);

            Console.WriteLine("SFTP server should be ready for tests");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to start SFTP server: {ex.Message}");
            Console.WriteLine("Tests may fail if SFTP server is not running manually.");
        }
    }

    [AssemblyCleanup]
    public static void StopSftpServer()
    {
        try
        {
            var dockerComposeDir = FindDockerComposeDirectory();
            if (dockerComposeDir == null) return;

            Console.WriteLine("Stopping SFTP server...");
            
            var stopInfo = new ProcessStartInfo
            {
                FileName = "docker-compose",
                Arguments = "down",
                WorkingDirectory = dockerComposeDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };

            var process = Process.Start(stopInfo);
            process?.WaitForExit();

            Console.WriteLine("SFTP server stopped");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to stop SFTP server: {ex.Message}");
        }
    }

    private static string? FindDockerComposeDirectory()
    {
        var currentDir = Directory.GetCurrentDirectory();
        
        while (currentDir != null)
        {
            var dockerSftpPath = Path.Combine(currentDir, "docker", "sftp");
            if (Directory.Exists(dockerSftpPath))
            {
                return dockerSftpPath;
            }
            
            currentDir = Directory.GetParent(currentDir)?.FullName;
        }
        
        return null;
    }
    
    [TestInitialize]
    public void Setup()
    {
        _config = new SftpStorageConfig
        {
            Host = "localhost",
            Port = 2222,
            Username = "ocragent",
            AuthenticationMethod = "password",
            Password = "password",
            RemoteBasePath = "/upload/ocr_test_" + Guid.NewGuid().ToString("N").Substring(0, 8),
            ConnectionPool = new ConnectionPoolConfig
            {
                MinConnections = 1,
                MaxConnections = 5,
                ConnectionIdleTimeout = 60,
                KeepAliveInterval = 30,
                EnableHealthChecks = false
            },
            Retry = new RetryConfig
            {
                MaxRetries = 2,
                InitialDelayMs = 100,
                MaxDelayMs = 1000,
                BackoffMultiplier = 2.0
            },
            Timeouts = new TimeoutsConfig
            {
                ConnectionTimeout = 30,
                OperationTimeout = 60,
                UploadTimeout = 120
            },
            Behavior = new BehaviorConfig
            {
                CreateRemoteDirectoryIfNotExists = true,
                UseCompression = true,
                BufferSize = 32768,
                EnableDebugFolders = true
            }
        };

    }
    
    [TestMethod]
    public async Task SaveBytes_CreatesFileAndMetadata()
    {
        using var store = new SftpImageStore(_config);
        
        var testData = new byte[] { 1, 2, 3, 4, 5 };
        var metadata = new Dictionary<string, string> { ["test"] = "value" };

        var reference = await store.SaveBytesAsync(testData, "png", metadata, "TEST001");

        Assert.AreEqual("TEST001", reference);
        Assert.IsTrue(await store.ExistsAsync(reference));
        
        var retrievedMeta = await store.GetMetadataAsync(reference);
        Assert.AreEqual("value", retrievedMeta["test"]);
        Assert.AreEqual("TEST001", retrievedMeta["reference"]);
        Assert.AreEqual("png", retrievedMeta["extension"]);
    }

    [TestMethod]
    public async Task SaveBytes_WithRandomReference_GeneratesUniqueReference()
    {
        using var store = new SftpImageStore(_config);
        
        var testData = new byte[] { 1, 2, 3, 4, 5 };

        var ref1 = await store.SaveBytesAsync(testData, "png");
        var ref2 = await store.SaveBytesAsync(testData, "png");

        Assert.AreNotEqual(ref1, ref2);
        Assert.IsTrue(await store.ExistsAsync(ref1));
        Assert.IsTrue(await store.ExistsAsync(ref2));
    }
}
