using Microsoft.VisualStudio.TestTools.UnitTesting;
using OcrAgent.Plugins.Segmentation;
using OpenCvSharp;

namespace UnitTests.Plugins;

[TestClass]
public sealed class DocumentBackgroundRemovalPluginTests : PluginTestBase
{
    private DocumentBackgroundRemovalPlugin _plugin = null!;

    [TestInitialize]
    public void Setup()
    {
        _plugin = new DocumentBackgroundRemovalPlugin(Store);
    }

    [TestMethod]
    public async Task RemoveBackground_OnWhite_ProducesOutput()
    {
        var docRef = CreateDocumentOnDeskImage();

        var result = await _plugin.RemoveDocumentBackground(docRef, "on_white");

        AssertImageValid(result);
        var meta = Store.GetMetadata(result);
        Assert.AreEqual("remove_background", meta["op"]);
        Assert.AreEqual("on_white", meta["mode"]);
    }

    [TestMethod]
    public async Task RemoveBackground_MaskOnly_ProducesOutput()
    {
        var docRef = CreateDocumentOnDeskImage();

        var result = await _plugin.RemoveDocumentBackground(docRef, "mask_only");

        AssertImageValid(result);
        var meta = Store.GetMetadata(result);
        Assert.AreEqual("mask_only", meta["mode"]);

        var path = Store.GetImagePath(result);
        using var maskImg = Cv2.ImRead(path, ImreadModes.Grayscale);
        Assert.IsFalse(maskImg.Empty());
    }
    
    [TestMethod]
    public async Task RemoveBackground_OnWhite_HasWhiteBackground()
    {
        var docRef = CreateDocumentOnDeskImage();

        var result = await _plugin.RemoveDocumentBackground(docRef, "on_white", feather: 0);

        var path = Store.GetImagePath(result);
        using var img = Cv2.ImRead(path);

        var topLeft = img.At<Vec3b>(0, 0);
        var topRight = img.At<Vec3b>(0, img.Width - 1);
        var bottomLeft = img.At<Vec3b>(img.Height - 1, 0);
        var bottomRight = img.At<Vec3b>(img.Height - 1, img.Width - 1);

        AssertPixelIsWhiteish(topLeft, "top-left");
        AssertPixelIsWhiteish(topRight, "top-right");
        AssertPixelIsWhiteish(bottomLeft, "bottom-left");
        AssertPixelIsWhiteish(bottomRight, "bottom-right");
    }

    [TestMethod]
    public async Task RemoveBackground_StoresMetadata()
    {
        var docRef = CreateDocumentOnDeskImage();

        var result = await _plugin.RemoveDocumentBackground(docRef, "on_white");

        var meta = Store.GetMetadata(result);
        Assert.AreEqual(docRef, meta["parent"]);
        Assert.AreEqual("remove_background", meta["op"]);
        Assert.AreEqual("on_white", meta["mode"]);
        Assert.IsTrue(meta.ContainsKey("detectionMethod"), "Should have detectionMethod");
        var validMethods = new[] { "otsu", "hsv", "edges", "grabcut" };
        bool hasValidMethod = validMethods.Any(m => meta["detectionMethod"].Contains(m));
        Assert.IsTrue(hasValidMethod,
            $"detectionMethod '{meta["detectionMethod"]}' should contain at least one valid method name");
        Assert.IsTrue(meta.ContainsKey("maskCoverage"), "Should have maskCoverage");
        Assert.IsTrue(meta.ContainsKey("confidence"), "Should have confidence");
        Assert.IsTrue(meta.ContainsKey("feather"), "Should have feather");
        Assert.IsTrue(meta.ContainsKey("fillHoles"), "Should have fillHoles");
    }

    /// <summary>
    /// Creates an image of a white document on a dark desk-colored background.
    /// </summary>
    private string CreateDocumentOnDeskImage()
    {
        using var img = new Mat(400, 300, MatType.CV_8UC3, new Scalar(45, 35, 30));

        // white document region in the center
        Cv2.Rectangle(img, new Rect(40, 50, 220, 300), new Scalar(250, 250, 250), -1);

        // text lines on the document
        Cv2.Line(img, new Point(60, 100), new Point(240, 100), new Scalar(30, 30, 30), 1);
        Cv2.Line(img, new Point(60, 130), new Point(220, 130), new Scalar(30, 30, 30), 1);
        Cv2.Line(img, new Point(60, 160), new Point(230, 160), new Scalar(30, 30, 30), 1);
        Cv2.Line(img, new Point(60, 190), new Point(200, 190), new Scalar(30, 30, 30), 1);

        Cv2.ImEncode(".png", img, out var pngBytes);
        return Store.SaveBytes(pngBytes, "png",
            new Dictionary<string, string> { ["source"] = "test_desk_doc" }, "DESKDOC001");
    }

    /// <summary>
    /// Creates an image of a white document on a gray textured surface.
    /// </summary>
    private string CreateDocumentOnTexturedBackground()
    {
        using var img = new Mat(400, 300, MatType.CV_8UC3, new Scalar(120, 115, 110));

        // add noise to simulate texture
        using var noise = new Mat(400, 300, MatType.CV_8UC3);
        Cv2.Randn(noise, new Scalar(0, 0, 0), new Scalar(15, 15, 15));
        Cv2.Add(img, noise, img);

        // white document
        Cv2.Rectangle(img, new Rect(35, 45, 230, 310), new Scalar(245, 245, 245), -1);

        Cv2.ImEncode(".png", img, out var pngBytes);
        return Store.SaveBytes(pngBytes, "png",
            new Dictionary<string, string> { ["source"] = "test_textured" }, "TEXTDOC001");
    }

    private static void AssertPixelIsWhiteish(Vec3b pixel, string location)
    {
        bool isWhiteish = pixel.Item0 >= 220 && pixel.Item1 >= 220 && pixel.Item2 >= 220;
        Assert.IsTrue(isWhiteish,
            $"Pixel at {location} should be white-ish but was ({pixel.Item0}, {pixel.Item1}, {pixel.Item2})");
    }
}
