using Microsoft.VisualStudio.TestTools.UnitTesting;
using OcrAgent.Storage;
using OpenCvSharp;

namespace UnitTests.Plugins;

/// <summary>
/// Base class for plugin tests with common test utilities.
/// </summary>
public abstract class PluginTestBase
{
    protected string TempRoot = null!;
    protected LocalFileImageStore Store = null!;
    protected string TestImageRef = null!;
}
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OcrAgent.Storage;
using OpenCvSharp;

namespace UnitTests.Plugins;

public abstract class PluginTestBase
{
    protected string TempRoot = null!;
    protected LocalFileImageStore Store = null!;
    protected string TestImageRef = null!;

    [TestInitialize]
    public void BaseSetup()
    {
        // A unique temp folder for each test run
        TempRoot = Path.Combine(Path.GetTempPath(), "OcrAgentPluginTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(TempRoot);

        Store = new LocalFileImageStore(TempRoot);
    }

    [TestCleanup]
    public void BaseCleanup()
    {
        // Cleanup temp files after test
        try
        {
            if (Directory.Exists(TempRoot))
                Directory.Delete(TempRoot, recursive: true);
        }
        catch
        {
            // ignore cleanup failures for now
        }
    }
}
