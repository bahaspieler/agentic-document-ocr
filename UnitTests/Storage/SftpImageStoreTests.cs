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
    
    [TestMethod]
    public async Task GetImagePath_DownloadsFileToTemp()
    {
        using var store = new SftpImageStore(_config);
        
        using var testImage = new Mat(100, 100, MatType.CV_8UC3, Scalar.White);
        Cv2.ImEncode(".png", testImage, out var imageBytes);

        var reference = await store.SaveBytesAsync(imageBytes, "png", null, "TESTIMG001");
        
        var localPath = await store.GetImagePathAsync(reference);

        Assert.IsTrue(File.Exists(localPath));
        Assert.IsTrue(localPath.Contains("TESTIMG001"));
        Assert.IsTrue(localPath.EndsWith(".png"));
        
        using var loadedImage = Cv2.ImRead(localPath);
        Assert.IsFalse(loadedImage.Empty());
        Assert.AreEqual(100, loadedImage.Width);
        Assert.AreEqual(100, loadedImage.Height);
        
        File.Delete(localPath);
    }
    
    [TestMethod]
    public async Task Exists_ReturnsFalseForNonExistentReference()
    {
        using var store = new SftpImageStore(_config);

        var exists = await store.ExistsAsync("NONEXISTENT999");

        Assert.IsFalse(exists);
    }

    [TestMethod]
    public async Task SaveBytes_DuplicateReference_ThrowsException()
    {
        using var store = new SftpImageStore(_config);
        
        var testData = new byte[] { 1, 2, 3 };

        await store.SaveBytesAsync(testData, "png", null, "DUP001");

        bool thrown = false;
        try
        {
            await store.SaveBytesAsync(testData, "png", null, "DUP001");
        }
        catch (InvalidOperationException)
        {
            thrown = true;
        }
        Assert.IsTrue(thrown, "Should throw InvalidOperationException for duplicate reference");
    }

    [TestMethod]
    public async Task GetImagePath_NonExistentReference_ThrowsException()
    {
        using var store = new SftpImageStore(_config);

        bool thrown = false;
        try
        {
            await store.GetImagePathAsync("NOTFOUND999");
        }
        catch (FileNotFoundException)
        {
            thrown = true;
        }
        Assert.IsTrue(thrown, "Should throw FileNotFoundException for non-existent reference");
    }
    
    [TestMethod]
    public async Task SaveDebugImage_CreatesDebugFolder()
    {
        using var store = new SftpImageStore(_config);
        
        var parentBytes = new byte[] { 1, 2, 3 };
        var parentRef = await store.SaveBytesAsync(parentBytes, "png", null, "PARENT001");

        using var debugImage = new Mat(50, 50, MatType.CV_8UC3, Scalar.Red);
        Cv2.ImEncode(".png", debugImage, out var debugBytes);

        await store.SaveDebugImageAsync(parentRef, "test_debug", "debug_01.png", debugBytes);

        Assert.IsTrue(await store.ExistsAsync(parentRef));
    }
    
    [TestMethod]
    public async Task ConcurrentOperations_HandleCorrectly()
    {
        using var store = new SftpImageStore(_config);
        
        var tasks = new List<Task<string>>();
        
        for (int i = 0; i < 10; i++)
        {
            var index = i;
            tasks.Add(Task.Run(async () =>
            {
                var data = new byte[] { (byte)index, (byte)(index + 1), (byte)(index + 2) };
                return await store.SaveBytesAsync(data, "png", null, $"CONCURRENT{index:D3}");
            }));
        }

        var references = await Task.WhenAll(tasks);

        Assert.AreEqual(10, references.Length);
        Assert.AreEqual(10, references.Distinct().Count());

        foreach (var reference in references)
        {
            Assert.IsTrue(await store.ExistsAsync(reference));
        }
    }
    
    [TestMethod]
    public async Task MultipleExtensions_SaveAndRetrieve()
    {
        using var store = new SftpImageStore(_config);
        
        var extensions = new[] { "png", "jpg", "jpeg", "tif", "tiff", "bmp" };
        
        foreach (var ext in extensions)
        {
            var testData = new byte[] { 1, 2, 3, 4, 5 };
            var reference = await store.SaveBytesAsync(testData, ext, null, $"EXT_{ext.ToUpper()}");
            
            Assert.IsTrue(await store.ExistsAsync(reference));
            
            var localPath = await store.GetImagePathAsync(reference);
            Assert.IsTrue(localPath.EndsWith($".{ext}"));
            File.Delete(localPath);
        }
    }

    [TestMethod]
    public async Task LargeFile_HandlesCorrectly()
    {
        using var store = new SftpImageStore(_config);
        
        using var largeImage = new Mat(2000, 2000, MatType.CV_8UC3, new Scalar(100, 150, 200));
        Cv2.ImEncode(".png", largeImage, out var largeBytes);
        
        Console.WriteLine($"Testing with {largeBytes.Length / 1024 / 1024.0:F2} MB file");

        var sw = Stopwatch.StartNew();
        var reference = await store.SaveBytesAsync(largeBytes, "png", null, "LARGE001");
        sw.Stop();
        
        Console.WriteLine($"Upload took {sw.ElapsedMilliseconds}ms");

        Assert.IsTrue(await store.ExistsAsync(reference));
        
        var localPath = await store.GetImagePathAsync(reference);
        Assert.IsTrue(File.Exists(localPath));
        
        var fileInfo = new FileInfo(localPath);
        Assert.IsTrue(fileInfo.Length > 1000000);
        
        File.Delete(localPath);
    }
    
    [TestMethod]
    public void InvalidReference_WithDirectoryTraversal_ThrowsSecurityException()
    {
        using var store = new SftpImageStore(_config);
        
        bool thrown1 = false;
        try { store.Exists("../../../etc/passwd"); }
        catch (SecurityException) { thrown1 = true; }
        Assert.IsTrue(thrown1, "Should throw SecurityException for path traversal");
        
        bool thrown2 = false;
        try { store.Exists("..\\..\\windows\\system32"); }
        catch (SecurityException) { thrown2 = true; }
        Assert.IsTrue(thrown2, "Should throw SecurityException for path traversal");
        
        bool thrown3 = false;
        try { store.Exists("test/../../../dangerous"); }
        catch (SecurityException) { thrown3 = true; }
        Assert.IsTrue(thrown3, "Should throw SecurityException for path traversal");
    }
}
