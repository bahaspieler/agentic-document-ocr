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
}
