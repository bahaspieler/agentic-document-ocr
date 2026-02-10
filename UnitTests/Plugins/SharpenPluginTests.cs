using Microsoft.VisualStudio.TestTools.UnitTesting;
using OcrAgent.Plugins.Enhancement;

namespace UnitTests.Plugins;

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
}
