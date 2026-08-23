using System.Text.Json;
using OcrAgent.Plugins.Analysis;
using OcrAgent.Storage;
using OpenCvSharp;

namespace UnitTests.Plugins;

/// <summary>
/// Tests QualityAssessmentPlugin JSON quality metrics output.
/// </summary>
[TestClass]
public class QualityAssessmentPluginTests : PluginTestBase
{
    [TestMethod]
    public async Task AssessQuality_ReturnsValidJson()
    {
        var plugin = new QualityAssessmentPlugin(Store);
        var result = await plugin.AssessQuality(TestImageRef);

        Assert.IsFalse(string.IsNullOrEmpty(result));
        using var doc = JsonDocument.Parse(result);
        Assert.IsTrue(doc.RootElement.TryGetProperty("blurScore", out _));
        Assert.IsTrue(doc.RootElement.TryGetProperty("noiseLevel", out _));
        Assert.IsTrue(doc.RootElement.TryGetProperty("meanBrightness", out _));
        Assert.IsTrue(doc.RootElement.TryGetProperty("contrast", out _));
        Assert.IsTrue(doc.RootElement.TryGetProperty("illuminationUniformity", out _));
        Assert.IsTrue(doc.RootElement.TryGetProperty("dimensions", out _));
    }

    [TestMethod]
    public async Task AssessQuality_DetectsBlurriness()
    {
        using var blurry = new Mat(200, 200, MatType.CV_8UC3, Scalar.White);
        Cv2.GaussianBlur(blurry, blurry, new Size(31, 31), 10);
        Cv2.ImEncode(".png", blurry, out var bytes);
        var blurRef = Store.SaveBytes(bytes, "png", null, "BLUR001");

        var plugin = new QualityAssessmentPlugin(Store);
        var result = await plugin.AssessQuality(blurRef);

        using var doc = JsonDocument.Parse(result);
        Assert.IsTrue(doc.RootElement.GetProperty("isBlurry").GetBoolean());
    }

    [TestMethod]
    public async Task AssessQuality_DimensionsCorrect()
    {
        var plugin = new QualityAssessmentPlugin(Store);
        var result = await plugin.AssessQuality(TestImageRef);

        using var doc = JsonDocument.Parse(result);
        Assert.AreEqual("100x100", doc.RootElement.GetProperty("dimensions").GetString());
    }

    [TestMethod]
    public async Task AssessQuality_ResolutionAdequate_FalseForSmallImage()
    {
        var plugin = new QualityAssessmentPlugin(Store);
        var result = await plugin.AssessQuality(TestImageRef);

        using var doc = JsonDocument.Parse(result);
        Assert.IsFalse(doc.RootElement.GetProperty("resolutionAdequate").GetBoolean());
    }

    [TestMethod]
    public async Task AssessQuality_DetectsDarkBackground()
    {
        using var dark = new Mat(200, 200, MatType.CV_8UC3, new Scalar(10, 10, 10));
        Cv2.ImEncode(".png", dark, out var bytes);
        var darkRef = Store.SaveBytes(bytes, "png", null, "DARK001");

        var plugin = new QualityAssessmentPlugin(Store);
        var result = await plugin.AssessQuality(darkRef);

        using var doc = JsonDocument.Parse(result);
        Assert.IsTrue(doc.RootElement.GetProperty("isDarkBackground").GetBoolean());
    }

    [TestMethod]
    public async Task AssessQuality_NoiseLevelCategories_FromJson()
    {
        var plugin = new QualityAssessmentPlugin(Store);
        var result = await plugin.AssessQuality(TestImageRef);

        using var doc = JsonDocument.Parse(result);
        var noiseLevel = doc.RootElement.GetProperty("noiseLevel").GetString();
        Assert.IsTrue(noiseLevel is "low" or "medium" or "high",
            $"Expected noiseLevel to be low/medium/high, got {noiseLevel}");
    }
}
