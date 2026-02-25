using Microsoft.VisualStudio.TestTools.UnitTesting;
using OcrAgent.Plugins.Enhancement;

namespace UnitTests.Plugins;

[TestClass]
public sealed class ColorCorrectionPluginTests : PluginTestBase
{
    private ColorCorrectionPlugin _plugin = null!;

    [TestInitialize]
    public void Setup()
    {
        _plugin = new ColorCorrectionPlugin(Store);
    }
    [TestMethod]
    public async Task CorrectColor_Auto_ProducesOutput()
    {
        var result = await _plugin.CorrectColor(TestImageRef, "auto");

        AssertImageValid(result);
        var meta = Store.GetMetadata(result);
        Assert.AreEqual("color_correction", meta["op"]);
        Assert.AreEqual("auto", meta["method"]);
        Assert.IsTrue(meta.ContainsKey("appliedMethod"));
    }
    [TestMethod]
    public async Task CorrectColor_GrayWorld_ProducesOutput()
    {
        var result = await _plugin.CorrectColor(TestImageRef, "gray_world");

        AssertImageValid(result);
        var meta = Store.GetMetadata(result);
        Assert.AreEqual("gray_world", meta["method"]);
        Assert.AreEqual("gray_world", meta["appliedMethod"]);
    }
    [TestMethod]
    public async Task CorrectColor_WhitePatch_ProducesOutput()
    {
        var result = await _plugin.CorrectColor(TestImageRef, "white_patch");

        AssertImageValid(result);
        var meta = Store.GetMetadata(result);
        Assert.AreEqual("white_patch", meta["method"]);
    }
    [TestMethod]
    public async Task CorrectColor_HistogramMatch_ProducesOutput()
    {
        var result = await _plugin.CorrectColor(TestImageRef, "histogram_match");

        AssertImageValid(result);
        var meta = Store.GetMetadata(result);
        Assert.AreEqual("histogram_match", meta["method"]);
    }
}