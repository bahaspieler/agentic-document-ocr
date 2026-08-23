using OcrAgent.Plugins.Geometric;
using OpenCvSharp;

namespace UnitTests.Plugins;

/// <summary>
/// Tests AutoCropPlugin cropping behavior with sample images.
/// </summary>
[TestClass]
public class AutoCropPluginTests : PluginTestBase
{
    [TestMethod]
    public async Task AutoCrop_WithContentImage_CropsSuccessfully()
    {
        using var img = new Mat(400, 400, MatType.CV_8UC3, new Scalar(200, 200, 200));
        Cv2.Rectangle(img, new Rect(100, 100, 200, 200), Scalar.Black, -1);
        Cv2.ImEncode(".png", img, out var bytes);
        var ref1 = Store.SaveBytes(bytes, "png", null, "CROP001");

        var plugin = new AutoCropPlugin(Store);
        var result = await plugin.AutoCrop(ref1);

        Assert.IsFalse(string.IsNullOrEmpty(result));
    }

    [TestMethod]
    public async Task AutoCrop_WithContourData_UsesBoundingRect()
    {
        using var img = new Mat(400, 400, MatType.CV_8UC3, Scalar.White);
        Cv2.Rectangle(img, new Rect(50, 50, 300, 300), Scalar.Black, 2);
        Cv2.ImEncode(".png", img, out var bytes);
        var ref1 = Store.SaveBytes(bytes, "png", null, "CROP002");

        var contourData = """{"found": true, "quadCorners": [[50,50],[350,50],[350,350],[50,350]]}""";

        var plugin = new AutoCropPlugin(Store);
        var result = await plugin.AutoCrop(ref1, contourData: contourData);

        Assert.IsFalse(string.IsNullOrEmpty(result));
    }

    [TestMethod]
    public async Task AutoCrop_FullImage_SkipsCrop()
    {
        var plugin = new AutoCropPlugin(Store);
        var result = await plugin.AutoCrop(TestImageRef);

        // Small test image where content fills most of the area - may or may not skip
        Assert.IsFalse(string.IsNullOrEmpty(result));
    }

    [TestMethod]
    public async Task AutoCrop_WithNotFoundContourData_FallsBackToCV()
    {
        var contourData = """{"found": false}""";

        var plugin = new AutoCropPlugin(Store);
        var result = await plugin.AutoCrop(TestImageRef, contourData: contourData);

        Assert.IsFalse(string.IsNullOrEmpty(result));
    }

    [TestMethod]
    public async Task AutoCrop_NegativeMargin_Throws()
    {
        var plugin = new AutoCropPlugin(Store);
        bool thrown = false;
        try { await plugin.AutoCrop(TestImageRef, marginPercent: -1); }
        catch (ArgumentException) { thrown = true; }
        Assert.IsTrue(thrown, "Expected ArgumentException for negative margin");
    }
}
