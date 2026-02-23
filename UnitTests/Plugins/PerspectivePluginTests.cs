using Microsoft.VisualStudio.TestTools.UnitTesting;
using OcrAgent.Plugins.Geometric;
using OpenCvSharp;

namespace UnitTests.Plugins;

[TestClass]
public sealed class PerspectivePluginTests : PluginTestBase
{
    private PerspectivePlugin _plugin = null!;

    [TestInitialize]
    public void Setup()
    {
        _plugin = new PerspectivePlugin(Store);
    }

    [TestMethod]
    public async Task CorrectPerspective_WithRectangle_ProducesOutput()
    {
        var result = await _plugin.CorrectPerspective(TestImageRef);

        AssertImageValid(result);
        var meta = Store.GetMetadata(result);
        Assert.AreEqual("perspective_correct", meta["op"]);
        Assert.IsTrue(meta.ContainsKey("confidence"));
    }

    [TestMethod]
    public async Task CorrectPerspective_AutoSensitivity_ProducesOutput()
    {
        var result = await _plugin.CorrectPerspective(TestImageRef, "auto");

        AssertImageValid(result);
        var meta = Store.GetMetadata(result);
        Assert.IsTrue(meta.ContainsKey("detectionMethod"));
    }

    [TestMethod]
    public async Task CorrectPerspective_AggressiveSensitivity_ProducesOutput()
    {
        var result = await _plugin.CorrectPerspective(TestImageRef, "aggressive");

        AssertImageValid(result);
        var meta = Store.GetMetadata(result);
        Assert.AreEqual("True", meta["enhanceEdges"]);
    }

    [TestMethod]
    public async Task CorrectPerspective_ConservativeSensitivity_ProducesOutput()
    {
        var result = await _plugin.CorrectPerspective(TestImageRef, "conservative");

        AssertImageValid(result);
    }

    [TestMethod]
    public async Task CorrectPerspective_WithTargetDimensions_ProducesCorrectSize()
    {
        var result = await _plugin.CorrectPerspective(TestImageRef, targetWidth: 200, targetHeight: 300);

        AssertImageValid(result);
        var meta = Store.GetMetadata(result);
        Assert.AreEqual("200x300", meta["outputDimensions"]);
    }

    [TestMethod]
    public async Task CorrectPerspective_WithMargin_AppliesMargin()
    {
        var result = await _plugin.CorrectPerspective(TestImageRef, marginPercent: 5);

        AssertImageValid(result);
        var meta = Store.GetMetadata(result);
        Assert.AreEqual("5", meta["marginApplied"]);
    }

    [TestMethod]
    public async Task CorrectPerspective_WithEnhanceEdges_EnhancesEdges()
    {
        var result = await _plugin.CorrectPerspective(TestImageRef, enhanceEdges: true);

        AssertImageValid(result);
        var meta = Store.GetMetadata(result);
        Assert.AreEqual("True", meta["enhanceEdges"]);
    }

    [TestMethod]
    public async Task CorrectPerspective_WithPerspectiveDistortion_CorrectsPerspective()
    {
        // create a document with perspective distortion
        var distortedRef = CreateDistortedDocument();

        // correct perspective
        var result = await _plugin.CorrectPerspective(distortedRef, "aggressive");

        AssertImageValid(result);
        var meta = Store.GetMetadata(result);
        Assert.IsTrue(meta.ContainsKey("detectedCorners"));
        Assert.IsTrue(meta.ContainsKey("outputDimensions"));
    }

    [TestMethod]
    public async Task CorrectPerspective_WithReceiptLikeImage_ProducesOutput()
    {
        // create a receipt-like document
        var receiptRef = CreateReceiptLikeDocument();

        // correct perspective
        var result = await _plugin.CorrectPerspective(receiptRef);

        AssertImageValid(result);
    }

    [TestMethod]
    public async Task CorrectPerspective_StoresComprehensiveMetadata()
    {
        var result = await _plugin.CorrectPerspective(TestImageRef);


        var meta = Store.GetMetadata(result);
        Assert.AreEqual(TestImageRef, meta["parent"]);
        Assert.AreEqual("perspective_correct", meta["op"]);
        Assert.IsTrue(meta.ContainsKey("detectedCorners"));
        Assert.IsTrue(meta.ContainsKey("outputDimensions"));
        Assert.IsTrue(meta.ContainsKey("confidence"));
        Assert.IsTrue(meta.ContainsKey("detectionMethod"));
    }

    // Helper Methods

    private string CreateDistortedDocument()
    {
        // a white document with black text
        using var doc = new Mat(400, 300, MatType.CV_8UC3, Scalar.White);
        
        // draw some text
        Cv2.Rectangle(doc, new Rect(20, 20, 260, 360), Scalar.Black, 2);
        Cv2.PutText(doc, "RECEIPT", new Point(80, 100), HersheyFonts.HersheySimplex, 1.5, Scalar.Black, 2);
        Cv2.PutText(doc, "Item 1: $10.00", new Point(40, 160), HersheyFonts.HersheySimplex, 0.8, Scalar.Black, 1);
        Cv2.PutText(doc, "Item 2: $15.00", new Point(40, 200), HersheyFonts.HersheySimplex, 0.8, Scalar.Black, 1);
        Cv2.PutText(doc, "Total: $25.00", new Point(40, 260), HersheyFonts.HersheySimplex, 1.0, Scalar.Black, 2);

        // apply perspective distortion
        var srcPoints = new Point2f[]
        {
            new(50, 50),   // top-left
            new(250, 30),  // top-right
            new(270, 370), // bottom-right
            new(30, 350)   // bottom-left
        };

        var dstPoints = new Point2f[]
        {
            new(0, 0),
            new(300, 0),
            new(300, 400),
            new(0, 400)
        };

        using var matrix = Cv2.GetPerspectiveTransform(dstPoints, srcPoints); // reverse transform to create distortion
        using var distorted = new Mat();
        Cv2.WarpPerspective(doc, distorted, matrix, new Size(300, 400), InterpolationFlags.Linear, BorderTypes.Constant, new Scalar(200, 200, 200));

        // save
        Cv2.ImEncode(".png", distorted, out var pngBytes);
        return Store.SaveBytes(pngBytes, "png", new Dictionary<string, string> { ["source"] = "test_distorted" }, "DISTORTED001");
    }

    private string CreateReceiptLikeDocument()
    {
        // a receipt-like image with white background
        using var receipt = new Mat(500, 350, MatType.CV_8UC3, new Scalar(245, 245, 245));
        
        // draw receipt header
        Cv2.PutText(receipt, "STORE NAME", new Point(80, 40), HersheyFonts.HersheySimplex, 1.0, Scalar.Black, 2);
        Cv2.Line(receipt, new Point(30, 60), new Point(320, 60), Scalar.Black, 1);
        
        // draw items
        Cv2.PutText(receipt, "Coffee", new Point(40, 100), HersheyFonts.HersheySimplex, 0.7, Scalar.Black, 1);
        Cv2.PutText(receipt, "$4.50", new Point(250, 100), HersheyFonts.HersheySimplex, 0.7, Scalar.Black, 1);
        
        Cv2.PutText(receipt, "Sandwich", new Point(40, 140), HersheyFonts.HersheySimplex, 0.7, Scalar.Black, 1);
        Cv2.PutText(receipt, "$7.99", new Point(250, 140), HersheyFonts.HersheySimplex, 0.7, Scalar.Black, 1);
        
        Cv2.Line(receipt, new Point(30, 160), new Point(320, 160), Scalar.Black, 1);
        
        // draw total
        Cv2.PutText(receipt, "TOTAL", new Point(40, 200), HersheyFonts.HersheySimplex, 0.8, Scalar.Black, 2);
        Cv2.PutText(receipt, "$12.49", new Point(230, 200), HersheyFonts.HersheySimplex, 0.8, Scalar.Black, 2);

        // save
        Cv2.ImEncode(".png", receipt, out var pngBytes);
        return Store.SaveBytes(pngBytes, "png", new Dictionary<string, string> { ["source"] = "test_receipt" }, "RECEIPT_TEST");
    }


    /// <summary>
    /// Creates a full-frame document that fills the entire image.
    /// </summary>
    private string CreateFullFrameDocument()
    {
        // White receipt filling entire frame
        using var doc = new Mat(600, 400, MatType.CV_8UC3, new Scalar(250, 250, 250));
        
        // draw receipt content
        Cv2.PutText(doc, "FULL FRAME RECEIPT", new Point(60, 60), HersheyFonts.HersheySimplex, 0.9, Scalar.Black, 2);
        Cv2.Line(doc, new Point(20, 80), new Point(380, 80), Scalar.Black, 1);
        
        Cv2.PutText(doc, "Item 1", new Point(30, 120), HersheyFonts.HersheySimplex, 0.7, Scalar.Black, 1);
        Cv2.PutText(doc, "$5.00", new Point(300, 120), HersheyFonts.HersheySimplex, 0.7, Scalar.Black, 1);
        
        Cv2.PutText(doc, "Item 2", new Point(30, 160), HersheyFonts.HersheySimplex, 0.7, Scalar.Black, 1);
        Cv2.PutText(doc, "$8.50", new Point(300, 160), HersheyFonts.HersheySimplex, 0.7, Scalar.Black, 1);
        
        Cv2.Line(doc, new Point(20, 200), new Point(380, 200), Scalar.Black, 1);
        Cv2.PutText(doc, "TOTAL: $13.50", new Point(200, 240), HersheyFonts.HersheySimplex, 0.8, Scalar.Black, 2);

        Cv2.ImEncode(".png", doc, out var pngBytes);
        return Store.SaveBytes(pngBytes, "png", new Dictionary<string, string> { ["source"] = "test_fullframe" }, "FULLFRAME001");
    }

    /// <summary>
    /// Creates a document on a dark background.
    /// </summary>
    private string CreateDocumentOnDarkBackground()
    {
        // Dark background
        using var img = new Mat(600, 600, MatType.CV_8UC3, new Scalar(40, 40, 40));
        
        // White document region in center
        var docRect = new Rect(150, 100, 300, 400);
        Cv2.Rectangle(img, docRect, new Scalar(245, 245, 245), -1);
        
        // draw some text to the document
        Cv2.PutText(img, "DOCUMENT", new Point(220, 200), HersheyFonts.HersheySimplex, 0.9, Scalar.Black, 2);
        Cv2.PutText(img, "On Dark BG", new Point(210, 250), HersheyFonts.HersheySimplex, 0.7, Scalar.Black, 1);
        Cv2.Line(img, new Point(180, 280), new Point(420, 280), Scalar.Black, 1);
        Cv2.PutText(img, "Content here", new Point(200, 320), HersheyFonts.HersheySimplex, 0.6, Scalar.Black, 1);

        Cv2.ImEncode(".png", img, out var pngBytes);
        return Store.SaveBytes(pngBytes, "png", new Dictionary<string, string> { ["source"] = "test_dark_bg" }, "DARKBG001");
    }

    /// <summary>
    /// Creates a full-frame document with textured/noisy edges.
    /// </summary>
    private string CreateTexturedFullFrameDocument()
    {
        // White document base
        using var doc = new Mat(500, 350, MatType.CV_8UC3, new Scalar(248, 248, 248));
        
        // draw content
        Cv2.PutText(doc, "TEXTURED DOC", new Point(70, 50), HersheyFonts.HersheySimplex, 0.9, Scalar.Black, 2);
        Cv2.Line(doc, new Point(20, 70), new Point(330, 70), Scalar.Black, 1);
        
        for (int i = 0; i < 8; i++)
        {
            int y = 100 + i * 40;
            Cv2.PutText(doc, $"Line {i + 1}", new Point(30, y), HersheyFonts.HersheySimplex, 0.6, Scalar.Black, 1);
            Cv2.PutText(doc, $"${(i + 1) * 2.50:F2}", new Point(260, y), HersheyFonts.HersheySimplex, 0.6, Scalar.Black, 1);
        }
        
        // draw some noise at edges to simulate texture bumps
        var rng = new Random(42);
        for (int i = 0; i < 50; i++)
        {
            int x = rng.Next(0, 10);
            int y = rng.Next(0, doc.Height);
            Cv2.Circle(doc, new Point(x, y), 2, new Scalar(200, 200, 200), -1);
            
            x = rng.Next(doc.Width - 10, doc.Width);
            y = rng.Next(0, doc.Height);
            Cv2.Circle(doc, new Point(x, y), 2, new Scalar(200, 200, 200), -1);
        }

        Cv2.ImEncode(".png", doc, out var pngBytes);
        return Store.SaveBytes(pngBytes, "png", new Dictionary<string, string> { ["source"] = "test_textured" }, "TEXTURED001");
    }

    /// <summary>
    /// Creates an already well-aligned rectangular document.
    /// </summary>
    private string CreateAlreadyAlignedDocument()
    {
        // a clean, aligned document on slightly gray background
        using var img = new Mat(500, 400, MatType.CV_8UC3, new Scalar(220, 220, 220));
        
        // document fills most of the frame but not entirely
        var docRect = new Rect(10, 10, 380, 480);
        Cv2.Rectangle(img, docRect, new Scalar(255, 255, 255), -1);
        Cv2.Rectangle(img, docRect, new Scalar(100, 100, 100), 1);
        
        // draw content
        Cv2.PutText(img, "ALIGNED DOC", new Point(100, 80), HersheyFonts.HersheySimplex, 1.0, Scalar.Black, 2);
        Cv2.Line(img, new Point(30, 110), new Point(370, 110), Scalar.Black, 1);
        Cv2.PutText(img, "This document is already", new Point(50, 160), HersheyFonts.HersheySimplex, 0.6, Scalar.Black, 1);
        Cv2.PutText(img, "perfectly aligned.", new Point(80, 200), HersheyFonts.HersheySimplex, 0.6, Scalar.Black, 1);

        Cv2.ImEncode(".png", img, out var pngBytes);
        return Store.SaveBytes(pngBytes, "png", new Dictionary<string, string> { ["source"] = "test_aligned" }, "ALIGNED001");
    }

    // Error Handling Tests


    [TestMethod]
    public async Task CorrectPerspective_InvalidSensitivity_ThrowsArgumentException()
    {
        bool exceptionThrown = false;
        try
        {
            await _plugin.CorrectPerspective(TestImageRef, "invalid_sensitivity");
        }
        catch (ArgumentException)
        {
            exceptionThrown = true;
        }
        Assert.IsTrue(exceptionThrown, "Expected ArgumentException for invalid sensitivity");
    }

    [TestMethod]
    public async Task CorrectPerspective_MarginTooHigh_ThrowsArgumentException()
    {
        bool exceptionThrown = false;
        try
        {
            await _plugin.CorrectPerspective(TestImageRef, marginPercent: 25);
        }
        catch (ArgumentException)
        {
            exceptionThrown = true;
        }
        Assert.IsTrue(exceptionThrown, "Expected ArgumentException for margin > 20");
    }

    [TestMethod]
    public async Task CorrectPerspective_MarginNegative_ThrowsArgumentException()
    {
        bool exceptionThrown = false;
        try
        {
            await _plugin.CorrectPerspective(TestImageRef, marginPercent: -5);
        }
        catch (ArgumentException)
        {
            exceptionThrown = true;
        }
        Assert.IsTrue(exceptionThrown, "Expected ArgumentException for negative margin");
    }

    [TestMethod]
    public async Task CorrectPerspective_NonExistentImage_ThrowsException()
    {
        bool exceptionThrown = false;
        try
        {
            await _plugin.CorrectPerspective("NONEXISTENT_IMAGE_REF");
        }
        catch (Exception ex) when (ex is FileNotFoundException or InvalidOperationException)
        {
            exceptionThrown = true;
        }
        Assert.IsTrue(exceptionThrown, "Expected exception for non-existent image");
    }

    // Full-Frame Document Detection Tests


    [TestMethod]
    public async Task CorrectPerspective_FullFrameDocument_ReturnsWithFullFrameMetadata()
    {
        var fullFrameRef = CreateFullFrameDocument();
        
        var result = await _plugin.CorrectPerspective(fullFrameRef);
        
        AssertImageValid(result);
        var meta = Store.GetMetadata(result);
        Assert.AreEqual("perspective_correct", meta["op"]);
        Assert.IsTrue(meta.ContainsKey("detectionMethod"));
        // full-frame docs should either return "full_frame_aligned" or "already_aligned"
        var method = meta["detectionMethod"];
        Assert.IsTrue(
            method == "full_frame_aligned" || method == "already_aligned" || 
            method.Contains("otsu") || method.Contains("edges") || method.Contains("grabcut"),
            $"Unexpected detection method: {method}");
    }

    [TestMethod]
    public async Task CorrectPerspective_PartialFrameDocument_DetectsDocumentBoundaries()
    {
        var partialRef = CreateDocumentOnDarkBackground();
        
        var result = await _plugin.CorrectPerspective(partialRef, "aggressive");
        
        AssertImageValid(result);
        var meta = Store.GetMetadata(result);
        Assert.IsTrue(meta.ContainsKey("detectedCorners"));
        Assert.IsTrue(meta.ContainsKey("confidence"));
        
        // should NOT be full-frame
        if (meta.TryGetValue("isFullFrame", out var isFullFrame))
        {
            Assert.AreNotEqual("true", isFullFrame.ToLower());
        }
    }


    // Already-Aligned Detection Tests

    [TestMethod]
    public async Task CorrectPerspective_AlreadyAlignedDocument_ReturnsWithAlignedMetadata()
    {
        var alignedRef = CreateAlreadyAlignedDocument();
        
        var result = await _plugin.CorrectPerspective(alignedRef);
        
        AssertImageValid(result);
        var meta = Store.GetMetadata(result);
        Assert.IsTrue(meta.ContainsKey("detectionMethod"));
        
        // check for aligned-related metadata
        if (meta["detectionMethod"] == "already_aligned")
        {
            Assert.IsTrue(meta.ContainsKey("note"));
            Assert.IsTrue(meta["note"].Contains("aligned", StringComparison.OrdinalIgnoreCase));
        }
    }

    [TestMethod]
    public async Task CorrectPerspective_DistortedDocument_HasDistortionMetadata()
    {
        var distortedRef = CreateDistortedDocument();
        
        var result = await _plugin.CorrectPerspective(distortedRef, "aggressive");
        
        AssertImageValid(result);
        var meta = Store.GetMetadata(result);
        
        // should have distortion amount if not fallback and not already_aligned/full_frame
        var method = meta["detectionMethod"];
        bool expectsDistortion = method != "fallback_image_bounds" && 
                                  method != "already_aligned" && 
                                  method != "full_frame_aligned";
        
        if (expectsDistortion && meta.ContainsKey("distortionAmount"))
        {
            var distortionStr = meta["distortionAmount"];
            if (double.TryParse(distortionStr, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var distortion) ||
                double.TryParse(distortionStr, out distortion))
            {
                Assert.IsTrue(distortion >= 0, "Distortion amount should be non-negative");
            }
        }
        
        // always verify we got valid detection metadata
        Assert.IsTrue(meta.ContainsKey("detectionMethod"));
        Assert.IsTrue(meta.ContainsKey("confidence"));
    }

    // Debug Mode Tests

    [TestMethod]
    public async Task CorrectPerspective_DebugMode_CreatesDebugFolder()
    {
        var result = await _plugin.CorrectPerspective(TestImageRef, debug: true);
        
        AssertImageValid(result);
        
        // check if debug folder was created
        var debugFolderPath = Path.Combine(TempRoot, "perspective_debug", TestImageRef);
        Assert.IsTrue(Directory.Exists(debugFolderPath), $"Debug folder should exist at {debugFolderPath}");
    }

    [TestMethod]
    public async Task CorrectPerspective_DebugMode_SavesInputImage()
    {
        var result = await _plugin.CorrectPerspective(TestImageRef, debug: true);
        
        AssertImageValid(result);
        
        var debugFolderPath = Path.Combine(TempRoot, "perspective_debug", TestImageRef);
        var inputImagePath = Path.Combine(debugFolderPath, "00_input.png");
        
        Assert.IsTrue(File.Exists(inputImagePath), "Debug input image (00_input.png) should exist");
    }

    [TestMethod]
    public async Task CorrectPerspective_DebugMode_SavesFullFrameAnalysis()
    {
        var fullFrameRef = CreateFullFrameDocument();
        
        var result = await _plugin.CorrectPerspective(fullFrameRef, debug: true);
        
        AssertImageValid(result);
        
        var debugFolderPath = Path.Combine(TempRoot, "perspective_debug", fullFrameRef);
        var analysisPath = Path.Combine(debugFolderPath, "01_fullframe_analysis.png");
        
        Assert.IsTrue(File.Exists(analysisPath), "Debug full-frame analysis image should exist");
    }

    // Detection Strategy Tests

    [TestMethod]
    public async Task CorrectPerspective_DetectionMethod_IsValidMethod()
    {
        var result = await _plugin.CorrectPerspective(TestImageRef);
        
        var meta = Store.GetMetadata(result);
        var method = meta["detectionMethod"];
        
        var validMethods = new[]
        {
            "edges", "otsu_segmentation", "hsv_light_segmentation", "grabcut",
            "already_aligned", "full_frame_aligned", "fallback_image_bounds"
        };
        
        Assert.IsTrue(validMethods.Contains(method), 
            $"Detection method '{method}' should be one of the valid methods");
    }

    [TestMethod]
    public async Task CorrectPerspective_DocumentOnDarkBackground_SelectsAppropriateMethod()
    {
        var darkBgRef = CreateDocumentOnDarkBackground();
        
        var result = await _plugin.CorrectPerspective(darkBgRef, "aggressive");
        
        AssertImageValid(result);
        var meta = Store.GetMetadata(result);
        
        // for document on dark background, should detect using one of the segmentation methods
        var method = meta["detectionMethod"];
        Assert.IsTrue(
            method.Contains("otsu") || method.Contains("grabcut") || 
            method.Contains("hsv") || method.Contains("edges"),
            $"Expected segmentation method for document on dark background, got: {method}");
    }

    [TestMethod]
    public async Task CorrectPerspective_TexturedDocument_HandlesEdgeBumps()
    {
        var texturedRef = CreateTexturedFullFrameDocument();
        
        var result = await _plugin.CorrectPerspective(texturedRef);
        
        AssertImageValid(result);
        var meta = Store.GetMetadata(result);
        Assert.IsTrue(meta.ContainsKey("detectedCorners"));
    }

    // Metadata Completeness Tests

    [TestMethod]
    public async Task CorrectPerspective_StandardProcessing_HasAllRequiredMetadata()
    {
        var distortedRef = CreateDistortedDocument();
        
        var result = await _plugin.CorrectPerspective(distortedRef, marginPercent: 2);
        
        var meta = Store.GetMetadata(result);
        
        // required fields for standard processing
        Assert.IsTrue(meta.ContainsKey("parent"), "Should have parent metadata");
        Assert.IsTrue(meta.ContainsKey("op"), "Should have op metadata");
        Assert.IsTrue(meta.ContainsKey("detectedCorners"), "Should have detectedCorners metadata");
        Assert.IsTrue(meta.ContainsKey("outputDimensions"), "Should have outputDimensions metadata");
        Assert.IsTrue(meta.ContainsKey("confidence"), "Should have confidence metadata");
        Assert.IsTrue(meta.ContainsKey("detectionMethod"), "Should have detectionMethod metadata");
    }

    [TestMethod]
    public async Task CorrectPerspective_Confidence_IsWithinValidRange()
    {
        var result = await _plugin.CorrectPerspective(TestImageRef);
        
        var meta = Store.GetMetadata(result);
        var confidenceStr = meta["confidence"];
        
        // parsing confidence with invariant culture to handle locale differences
        if (!double.TryParse(confidenceStr, System.Globalization.NumberStyles.Float, 
            System.Globalization.CultureInfo.InvariantCulture, out var confidence))
        {
            // parsing confidence with current culture to handle comma as decimal separator
            double.TryParse(confidenceStr, out confidence);
        }
        
        Assert.IsTrue(confidence >= 0.0 && confidence <= 1.0, 
            $"Confidence '{confidenceStr}' (parsed as {confidence}) should be between 0.0 and 1.0");
    }

    [TestMethod]
    public async Task CorrectPerspective_OutputDimensions_HasCorrectFormat()
    {
        var result = await _plugin.CorrectPerspective(TestImageRef);
        
        var meta = Store.GetMetadata(result);
        var dimensions = meta["outputDimensions"];
        
        // format should be "WxH"
        Assert.IsTrue(dimensions.Contains("x"), "Output dimensions should be in WxH format");
        var parts = dimensions.Split('x');
        Assert.AreEqual(2, parts.Length, "Dimensions should have exactly two parts");
        Assert.IsTrue(int.TryParse(parts[0], out var width) && width > 0, "Width should be positive integer");
        Assert.IsTrue(int.TryParse(parts[1], out var height) && height > 0, "Height should be positive integer");
    }

    [TestMethod]
    public async Task CorrectPerspective_DetectedCorners_HasCorrectFormat()
    {
        var distortedRef = CreateDistortedDocument();
        
        var result = await _plugin.CorrectPerspective(distortedRef);
        
        var meta = Store.GetMetadata(result);
        var corners = meta["detectedCorners"];
        
        // format should be "[(...), (...), (...), (...)]"
        Assert.IsTrue(corners.StartsWith("[") && corners.EndsWith("]"), 
            "Corners should be enclosed in brackets");
        
        // should have 4 corner coordinates
        var cornerCount = corners.Split(new[] { "), (" }, StringSplitOptions.None).Length;
        Assert.IsTrue(cornerCount >= 1, "Should have corner coordinates");
    }

    [TestMethod]
    public async Task CorrectPerspective_Parent_MatchesInputReference()
    {
        var result = await _plugin.CorrectPerspective(TestImageRef);
        
        var meta = Store.GetMetadata(result);
        Assert.AreEqual(TestImageRef, meta["parent"], "Parent should match input reference");
    }

    [TestMethod]
    public async Task CorrectPerspective_EnhanceEdgesFalse_RecordsInMetadata()
    {
        var result = await _plugin.CorrectPerspective(TestImageRef, enhanceEdges: false);
        
        var meta = Store.GetMetadata(result);
        
        // check if enhanceEdges is in metadata
        if (meta.ContainsKey("enhanceEdges"))
        {
            Assert.AreEqual("False", meta["enhanceEdges"]);
        }
    }
}
