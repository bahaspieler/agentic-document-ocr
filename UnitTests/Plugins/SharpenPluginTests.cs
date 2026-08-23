using Microsoft.VisualStudio.TestTools.UnitTesting;
using OcrAgent.Plugins.Enhancement;

namespace UnitTests.Plugins;

/// <summary>
/// Tests SharpenPlugin strength presets and output images.
/// </summary>
[TestClass]
public sealed class SharpenPluginTests : PluginTestBase
{
    private SharpenPlugin _plugin = null!;

    [TestInitialize]
    public void Setup()
    {
        _plugin = new SharpenPlugin(Store);
    }

    [TestMethod]
    public async Task SharpenImage_LowStrength_ProducesOutput()
    {
        var result = await _plugin.SharpenImage(TestImageRef, "low");

        AssertImageValid(result);
        var meta = Store.GetMetadata(result);
        Assert.AreEqual("sharpen", meta["op"]);
        Assert.AreEqual("low", meta["strength"]);
    }

    [TestMethod]
    public async Task SharpenImage_MediumStrength_ProducesOutput()
    {
        var result = await _plugin.SharpenImage(TestImageRef, "medium");

        AssertImageValid(result);
        var meta = Store.GetMetadata(result);
        Assert.AreEqual("medium", meta["strength"]);
    }

    [TestMethod]
    public async Task SharpenImage_HighStrength_ProducesOutput()
    {
        var result = await _plugin.SharpenImage(TestImageRef, "high");

        AssertImageValid(result);
        var meta = Store.GetMetadata(result);
        Assert.AreEqual("high", meta["strength"]);
    }

    [TestMethod]
    public async Task SharpenImage_UnsharpMethod_ProducesOutput()
    {
        var result = await _plugin.SharpenImage(TestImageRef, "medium", "unsharp");

        AssertImageValid(result);
        var meta = Store.GetMetadata(result);
        Assert.AreEqual("unsharp", meta["method"]);
    }

    [TestMethod]
    public async Task SharpenImage_LaplacianMethod_ProducesOutput()
    {
        var result = await _plugin.SharpenImage(TestImageRef, "medium", "laplacian");

        AssertImageValid(result);
        var meta = Store.GetMetadata(result);
        Assert.AreEqual("laplacian", meta["method"]);
    }

    [TestMethod]
    public async Task SharpenImage_AdaptiveMode_DetectsBlur()
    {
        var result = await _plugin.SharpenImage(TestImageRef, adaptiveStrength: true);

        AssertImageValid(result);
        var meta = Store.GetMetadata(result);
        Assert.AreEqual("True", meta["adaptiveStrength"]);
        Assert.IsTrue(meta.ContainsKey("blurDetected"));
    }
}
