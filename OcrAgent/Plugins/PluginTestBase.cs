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

        // A simple test image (100x100 white image with black rectangle)
        TestImageRef = CreateTestImage();
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

    /// <summary>
    /// A simple test image and returns its reference ID.
    /// </summary>
    protected string CreateTestImage()
    {
        // A 100x100 white image with a black rectangle in the center
        using var img = new Mat(100, 100, MatType.CV_8UC3, Scalar.White);
        Cv2.Rectangle(img, new Rect(25, 25, 50, 50), Scalar.Black, -1);

        // Encode to PNG
        Cv2.ImEncode(".png", img, out var pngBytes);

        // Save to store
        return Store.SaveBytes(pngBytes, "png", new Dictionary<string, string>
        {
            ["source"] = "test"
        }, "TEST001");
    }
}
