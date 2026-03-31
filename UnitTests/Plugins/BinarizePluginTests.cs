using Microsoft.VisualStudio.TestTools.UnitTesting;
using OcrAgent.Plugins.Segmentation;

namespace UnitTests.Plugins;

/// <summary>
/// Tests BinarizePlugin for Otsu and adaptive binarization paths.
/// </summary>
[TestClass]
public sealed class BinarizePluginTests : PluginTestBase
{
    private BinarizePlugin _plugin = null!;

    [TestInitialize]
    public void Setup()
    {
        _plugin = new BinarizePlugin(Store);
    }

    [TestMethod]
    public async Task BinarizeImage_Otsu_ProducesOutput()
    {
        var result = await _plugin.BinarizeImage(TestImageRef, "otsu");
        AssertImageValid(result);
        var meta = Store.GetMetadata(result);
        Assert.AreEqual("binarize", meta["op"]);
        Assert.AreEqual("otsu", meta["method"]);
    }

    [TestMethod]
    public async Task BinarizeImage_AdaptiveMean_ProducesOutput()
    {
        var result = await _plugin.BinarizeImage(TestImageRef, "adaptive_mean", 11, 2);
        AssertImageValid(result);
        var meta = Store.GetMetadata(result);
        Assert.AreEqual("adaptive_mean", meta["method"]);
        Assert.AreEqual("11", meta["blockSize"]);
        Assert.AreEqual("2", meta["constant"]);
    }

    [TestMethod]
    public async Task BinarizeImage_AdaptiveGaussian_ProducesOutput()
    {
        var result = await _plugin.BinarizeImage(TestImageRef, "adaptive_gaussian", 15, 3);
        AssertImageValid(result);
        var meta = Store.GetMetadata(result);
        Assert.AreEqual("adaptive_gaussian", meta["method"]);
        Assert.AreEqual("15", meta["blockSize"]);
    }

    [TestMethod]
    public async Task BinarizeImage_WithInvert_ProducesOutput()
    {
        var result = await _plugin.BinarizeImage(TestImageRef, "otsu", invertForDarkBackground: true);
        AssertImageValid(result);
        var meta = Store.GetMetadata(result);
        Assert.AreEqual("True", meta["inverted"]);
    }

    [TestMethod]
    public async Task BinarizeImage_StoresMetadata()
    {
        var result = await _plugin.BinarizeImage(TestImageRef, "otsu");
        var meta = Store.GetMetadata(result);
        Assert.AreEqual(TestImageRef, meta["parent"]);
    }

    [TestMethod]
    public async Task BinarizeImage_Sauvola_ProducesOutput()
    {
        var result = await _plugin.BinarizeImage(TestImageRef, "sauvola");
        AssertImageValid(result);
        var meta = Store.GetMetadata(result);
        Assert.AreEqual("sauvola", meta["method"]);
    }

    [TestMethod]
    public async Task BinarizeImage_Auto_ProducesOutput()
    {
        var result = await _plugin.BinarizeImage(TestImageRef, "auto");
        AssertImageValid(result);
    }

    [TestMethod]
    public async Task BinarizeImage_Auto_WithQualityProfile_SelectsMethod()
    {
        var profile = """{"contrast": 60.0, "illuminationUniformity": 0.95}""";
        var result = await _plugin.BinarizeImage(TestImageRef, "auto", qualityProfile: profile);
        AssertImageValid(result);
        var meta = Store.GetMetadata(result);
        Assert.AreEqual("otsu", meta["method"], "High uniformity + good contrast should select Otsu");
    }

    [TestMethod]
    public async Task BinarizeImage_Auto_LowUniformity_SelectsSauvola()
    {
        var profile = """{"contrast": 50.0, "illuminationUniformity": 0.60}""";
        var result = await _plugin.BinarizeImage(TestImageRef, "auto", qualityProfile: profile);
        AssertImageValid(result);
        var meta = Store.GetMetadata(result);
        Assert.AreEqual("sauvola", meta["method"], "Low uniformity should select Sauvola");
    }
}
