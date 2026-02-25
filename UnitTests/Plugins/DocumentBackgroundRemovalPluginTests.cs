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
}
