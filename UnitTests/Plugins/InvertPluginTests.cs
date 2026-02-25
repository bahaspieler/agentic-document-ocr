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
        // Default TestImageRef is a white image with a black rectangle (bright overall)
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

    [TestMethod]
    public async Task InvertImage_Smart_ProducesOutput()
    {
        var result = await _plugin.InvertImage(TestImageRef, "smart");

        AssertImageValid(result);
        var meta = Store.GetMetadata(result);
        Assert.AreEqual("smart", meta["mode"]);
        Assert.IsTrue(meta.ContainsKey("darkPixelRatio"), "Smart mode should include darkPixelRatio");
    }

    [TestMethod]
    public async Task InvertImage_Smart_DarkBackground_Inverts()
    {
        var darkRef = CreateDarkBackgroundImage();

        var result = await _plugin.InvertImage(darkRef, "smart");

        AssertImageValid(result);
        var meta = Store.GetMetadata(result);
        Assert.AreEqual("True", meta["wasInverted"]);
        Assert.IsTrue(meta.ContainsKey("darkPixelRatio"));
    }

    [TestMethod]
    public async Task InvertImage_InvalidMode_ThrowsArgumentException()
    {
        bool thrown = false;
        try { await _plugin.InvertImage(TestImageRef, "invalid"); }
        catch (ArgumentException) { thrown = true; }
        Assert.IsTrue(thrown, "Expected ArgumentException for invalid mode");
    }

    [TestMethod]
    public async Task InvertImage_StoresMetadata()
    {
        var result = await _plugin.InvertImage(TestImageRef, "always");

        var meta = Store.GetMetadata(result);
        Assert.AreEqual(TestImageRef, meta["parent"]);
        Assert.AreEqual("invert", meta["op"]);
        Assert.AreEqual("always", meta["mode"]);
        Assert.IsTrue(meta.ContainsKey("wasInverted"));
        Assert.IsTrue(meta.ContainsKey("meanBrightness"));
    }

    private string CreateDarkBackgroundImage()
    {
        // Dark background with light text (mean brightness well below 127)
        using var img = new Mat(200, 200, MatType.CV_8UC3, new Scalar(20, 20, 20));
        Cv2.PutText(img, "DARK", new Point(30, 100), HersheyFonts.HersheySimplex, 1.5, new Scalar(220, 220, 220), 2);
        Cv2.PutText(img, "DOC", new Point(50, 150), HersheyFonts.HersheySimplex, 1.2, new Scalar(200, 200, 200), 2);

        Cv2.ImEncode(".png", img, out var pngBytes);
        return Store.SaveBytes(pngBytes, "png", new Dictionary<string, string> { ["source"] = "test_dark" }, "DARK001");
    }
}
