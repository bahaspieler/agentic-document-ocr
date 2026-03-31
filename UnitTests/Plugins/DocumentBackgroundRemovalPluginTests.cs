using Microsoft.VisualStudio.TestTools.UnitTesting;
using OcrAgent.Plugins.Segmentation;
using OpenCvSharp;

namespace UnitTests.Plugins;

/// <summary>
/// Tests DocumentBackgroundRemovalPlugin removal presets and outputs.
/// </summary>
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
    
    [TestMethod]
    public async Task RemoveBackground_WithFeather_ProducesOutput()
    {
        var docRef = CreateDocumentOnDeskImage();

        var result = await _plugin.RemoveDocumentBackground(docRef, "on_white", feather: 5);

        AssertImageValid(result);
        var meta = Store.GetMetadata(result);
        Assert.AreEqual("5", meta["feather"]);
    }

    [TestMethod]
    public async Task RemoveBackground_InvalidMode_ThrowsArgumentException()
    {
        bool thrown = false;
        try { await _plugin.RemoveDocumentBackground(TestImageRef, "invalid_mode"); }
        catch (ArgumentException) { thrown = true; }
        Assert.IsTrue(thrown, "Expected ArgumentException for invalid mode");
    }

    [TestMethod]
    public async Task RemoveBackground_InvalidSensitivity_ThrowsArgumentException()
    {
        bool thrown = false;
        try { await _plugin.RemoveDocumentBackground(TestImageRef, sensitivity: "invalid_sens"); }
        catch (ArgumentException) { thrown = true; }
        Assert.IsTrue(thrown, "Expected ArgumentException for invalid sensitivity");
    }

    [TestMethod]
    public async Task RemoveBackground_TransparentMode_ProducesRgbaOutput()
    {
        var docRef = CreateDocumentOnDeskImage();

        var result = await _plugin.RemoveDocumentBackground(docRef, "transparent", feather: 0);

        AssertImageValid(result);
        var meta = Store.GetMetadata(result);
        Assert.AreEqual("transparent", meta["mode"]);

        var path = Store.GetImagePath(result);
        using var img = Cv2.ImRead(path, ImreadModes.Unchanged);
        Assert.IsFalse(img.Empty());
        Assert.AreEqual(4, img.Channels(), "Transparent mode should produce 4-channel RGBA image");
    }

    [TestMethod]
    public async Task RemoveBackground_TransparentMode_HasTransparentBackground()
    {
        var docRef = CreateDocumentOnDeskImage();

        var result = await _plugin.RemoveDocumentBackground(docRef, "transparent", feather: 0);

        var path = Store.GetImagePath(result);
        using var img = Cv2.ImRead(path, ImreadModes.Unchanged);
        
        Assert.AreEqual(4, img.Channels(), "Should be RGBA");

        // Check corner pixels - alpha channel should be 0 (transparent)
        var topLeft = img.At<Vec4b>(0, 0);
        var topRight = img.At<Vec4b>(0, img.Width - 1);

        Assert.IsTrue(topLeft.Item3 < 50, "Top-left corner should have low alpha (transparent background)");
        Assert.IsTrue(topRight.Item3 < 50, "Top-right corner should have low alpha (transparent background)");
    }

    [TestMethod]
    public async Task RemoveBackground_TieredStrategy_RecordsCorrectMethod()
    {
        var docRef = CreateDocumentOnDeskImage();

        var result = await _plugin.RemoveDocumentBackground(docRef, "on_white");

        var meta = Store.GetMetadata(result);
        Assert.IsTrue(meta.ContainsKey("detectionMethod"), "Should have detectionMethod");
        
        // Method should be either U2NETP-based or CV-based
        var method = meta["detectionMethod"];
        bool isValidMethod = 
            method.Contains("u2netp") || 
            method.Contains("otsu") || 
            method.Contains("hsv") || 
            method.Contains("edges") || 
            method.Contains("grabcut");
        
        Assert.IsTrue(isValidMethod, $"Method '{method}' should be valid strategy name");
    }

    [TestMethod]
    public async Task RemoveBackground_WithFeather_TransparentMode_WorksCorrectly()
    {
        var docRef = CreateDocumentOnDeskImage();

        var result = await _plugin.RemoveDocumentBackground(docRef, "transparent", feather: 3);

        AssertImageValid(result);
        var meta = Store.GetMetadata(result);
        Assert.AreEqual("transparent", meta["mode"]);
        Assert.AreEqual("3", meta["feather"]);
    }

    [TestMethod]
    public async Task RemoveBackground_DifferentSensitivities_ProduceValidOutput()
    {
        var docRef = CreateDocumentOnDeskImage();
        var sensitivities = new[] { "auto", "aggressive", "conservative" };

        foreach (var sensitivity in sensitivities)
        {
            var result = await _plugin.RemoveDocumentBackground(docRef, "on_white", sensitivity: sensitivity);
            
            AssertImageValid(result);
            var meta = Store.GetMetadata(result);
            Assert.IsTrue(meta.ContainsKey("maskCoverage"), 
                $"Should have maskCoverage for sensitivity '{sensitivity}'");
        }
    }

    [TestMethod]
    public async Task RemoveBackground_DebugMode_CreatesDebugFolder()
    {
        var docRef = CreateDocumentOnDeskImage();

        var result = await _plugin.RemoveDocumentBackground(docRef, "on_white", debug: true);

        AssertImageValid(result);

        var imagePath = Store.GetImagePath(docRef);
        var storageDir = Path.GetDirectoryName(imagePath)!;
        var debugDir = Path.Combine(storageDir, "bgremoval_debug", docRef);
        Assert.IsTrue(Directory.Exists(debugDir), $"Debug folder should exist at {debugDir}");

        var debugFiles = Directory.GetFiles(debugDir, "*.png");
        Assert.IsTrue(debugFiles.Length >= 3, $"Debug folder should have at least 3 images, found {debugFiles.Length}");
    }
    
    [TestMethod]
    public async Task RemoveBackground_MaskCoverage_IsReasonable()
    {
        var docRef = CreateDocumentOnDeskImage();

        var result = await _plugin.RemoveDocumentBackground(docRef, "on_white");

        var meta = Store.GetMetadata(result);
        Assert.IsTrue(meta.ContainsKey("maskCoverage"), "Should have maskCoverage");

        bool parsed = double.TryParse(meta["maskCoverage"],
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out double coverage);
        if (!parsed)
            parsed = double.TryParse(meta["maskCoverage"], out coverage);

        Assert.IsTrue(parsed, $"maskCoverage should be parseable, got '{meta["maskCoverage"]}'");
        Assert.IsTrue(coverage >= 0.1 && coverage <= 0.95,
            $"maskCoverage should be between 0.1 and 0.95, got {coverage}");
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
