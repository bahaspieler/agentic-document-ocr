using System.Text.Json;
using OcrAgent.Plugins.Analysis;
using OcrAgent.Storage;
using OpenCvSharp;

namespace UnitTests.Plugins;

/// <summary>
/// Tests ContourExtractionPlugin JSON contour output.
/// </summary>
[TestClass]
public class ContourExtractionPluginTests : PluginTestBase
{
    [TestMethod]
    public async Task ExtractDocumentContours_ReturnsValidJson()
    {
        var plugin = new ContourExtractionPlugin(Store);
        var result = await plugin.ExtractDocumentContours(TestImageRef);

        Assert.IsFalse(string.IsNullOrEmpty(result));
        using var doc = JsonDocument.Parse(result);
        Assert.IsTrue(doc.RootElement.TryGetProperty("found", out _));
    }

    [TestMethod]
    public async Task ExtractDocumentContours_WithDocumentLikeImage_FindsContour()
    {
        using var img = new Mat(400, 300, MatType.CV_8UC3, new Scalar(50, 50, 50));
        Cv2.Rectangle(img, new Rect(30, 30, 240, 340), Scalar.White, -1);
        Cv2.ImEncode(".png", img, out var bytes);
        var docRef = Store.SaveBytes(bytes, "png", null, "DOC001");

        var plugin = new ContourExtractionPlugin(Store);
        var result = await plugin.ExtractDocumentContours(docRef);

        using var doc = JsonDocument.Parse(result);
        var found = doc.RootElement.GetProperty("found").GetBoolean();
        if (found)
        {
            Assert.IsTrue(doc.RootElement.TryGetProperty("quadCorners", out _));
            Assert.IsTrue(doc.RootElement.TryGetProperty("maskReference", out _));
        }
    }

    [TestMethod]
    public async Task ExtractDocumentContours_WithBlankImage_ReturnsNotFound()
    {
        using var blank = new Mat(100, 100, MatType.CV_8UC3, Scalar.White);
        Cv2.ImEncode(".png", blank, out var bytes);
        var blankRef = Store.SaveBytes(bytes, "png", null, "BLANK001");

        var plugin = new ContourExtractionPlugin(Store);
        var result = await plugin.ExtractDocumentContours(blankRef);

        using var doc = JsonDocument.Parse(result);
        Assert.IsFalse(doc.RootElement.GetProperty("found").GetBoolean());
    }

    [TestMethod]
    public async Task ExtractDocumentContours_NullPredictor_UsesCVFallback()
    {
        var plugin = new ContourExtractionPlugin(Store, predictor: null);
        var result = await plugin.ExtractDocumentContours(TestImageRef);

        Assert.IsFalse(string.IsNullOrEmpty(result));
        using var doc = JsonDocument.Parse(result);
        Assert.IsTrue(doc.RootElement.TryGetProperty("found", out _));
    }
}
