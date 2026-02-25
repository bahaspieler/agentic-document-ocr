using Microsoft.VisualStudio.TestTools.UnitTesting;
using OcrAgent.Plugins.Enhancement;
using OpenCvSharp;

namespace UnitTests.Plugins;

[TestClass]
public sealed class InvertPluginTests : PluginTestBase
{
    private InvertPlugin _plugin = null!;

    [TestInitialize]
    public void Setup()
    {
        _plugin = new InvertPlugin(Store);
    }

    [TestMethod]
    public async Task InvertImage_Always_ProducesOutput()
    {
        var result = await _plugin.InvertImage(TestImageRef, "always");

        AssertImageValid(result);
        var meta = Store.GetMetadata(result);
        Assert.AreEqual("invert", meta["op"]);
        Assert.AreEqual("True", meta["wasInverted"]);
    }

    [TestMethod]
    public async Task InvertImage_Auto_WhiteBackground_SkipsInversion()
    {
        var result = await _plugin.InvertImage(TestImageRef, "auto");

        AssertImageValid(result);
        var meta = Store.GetMetadata(result);
        Assert.AreEqual("auto", meta["mode"]);
        Assert.AreEqual("False", meta["wasInverted"]);
    }

    [TestMethod]
    public async Task InvertImage_Auto_DarkBackground_Inverts()
    {
        var darkRef = CreateDarkBackgroundImage();

        var result = await _plugin.InvertImage(darkRef, "auto");

        AssertImageValid(result);
        var meta = Store.GetMetadata(result);
        Assert.AreEqual("True", meta["wasInverted"]);
    }

    private string CreateDarkBackgroundImage()
    {
        using var img = new Mat(200, 200, MatType.CV_8UC3, new Scalar(20, 20, 20));
        Cv2.PutText(img, "DARK", new Point(30, 100), HersheyFonts.HersheySimplex, 1.5, new Scalar(220, 220, 220), 2);
        Cv2.PutText(img, "DOC", new Point(50, 150), HersheyFonts.HersheySimplex, 1.2, new Scalar(200, 200, 200), 2);

        Cv2.ImEncode(".png", img, out var pngBytes);
        return Store.SaveBytes(
            pngBytes,
            "png",
            new Dictionary<string, string> { ["source"] = "test_dark" },
            "DARK001");
    }
}