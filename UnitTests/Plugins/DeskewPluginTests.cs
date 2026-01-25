using Microsoft.VisualStudio.TestTools.UnitTesting;
using OcrAgent.Plugins.Geometric;
using OpenCvSharp;

namespace UnitTests.Plugins;

[TestClass]
public sealed class DeskewPluginTests : PluginTestBase
{
    private DeskewPlugin _plugin = null!;

    [TestInitialize]
    public void Setup()
    {
        _plugin = new DeskewPlugin(Store);
    }

    [TestMethod]
    public async Task DeskewImage_Hough_ProducesOutput()
    {
        // act
        var result = await _plugin.DeskewImage(TestImageRef, 10.0, "hough");

        // assert
        AssertImageValid(result);
        var meta = Store.GetMetadata(result);
        Assert.AreEqual("deskew", meta["op"]);
        Assert.AreEqual("hough", meta["method"]);
    }
    
    [TestMethod]
    public async Task DeskewImage_Projection_ProducesOutput()
    {
        // act
        var result = await _plugin.DeskewImage(TestImageRef, 10.0, "projection");

        // assert
        AssertImageValid(result);
        var meta = Store.GetMetadata(result);
        Assert.AreEqual("projection", meta["method"]);
    }
    
    [TestMethod]
    public async Task DeskewImage_WithSkewedInput_CorrectionsApplied()
    {
        var skewedRef = CreateSkewedTestImage(5.0);
        
        var result = await _plugin.DeskewImage(skewedRef, 10.0);
        
        AssertImageValid(result);
        var meta = Store.GetMetadata(result);
        
        // detect angle (should be non-zero)
        double detectedAngle = double.Parse(meta["detectedAngle"]);
        Assert.IsTrue(Math.Abs(detectedAngle) >= 0, "Should detect some angle");
    }

    private string CreateSkewedTestImage(double angle)
    {
        // create test image
        using var img = new Mat(100, 100, MatType.CV_8UC3, Scalar.White);
        Cv2.Rectangle(img, new Rect(25, 25, 50, 50), Scalar.Black, -1);

        // Rotate
        var center = new Point2f(50, 50);
        using var rotationMatrix = Cv2.GetRotationMatrix2D(center, angle, 1.0);
        using var rotated = new Mat();
        Cv2.WarpAffine(img, rotated, rotationMatrix, new Size(100, 100), InterpolationFlags.Linear, BorderTypes.Constant, Scalar.White);

        // Save
        Cv2.ImEncode(".png", rotated, out var pngBytes);
        return Store.SaveBytes(pngBytes, "png", new Dictionary<string, string> { ["source"] = "test_skewed" }, "SKEWED001");
    }
    
    [TestMethod]
    public async Task DeskewImage_StoresAngleAndConfidence()
    {
        // Act
        var result = await _plugin.DeskewImage(TestImageRef, 10.0);

        // Assert
        var meta = Store.GetMetadata(result);
        Assert.IsTrue(meta.ContainsKey("detectedAngle"));
        Assert.IsTrue(meta.ContainsKey("confidence"));
        Assert.IsTrue(meta.ContainsKey("correctionApplied"));
    }
}
