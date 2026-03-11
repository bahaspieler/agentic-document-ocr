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
}
