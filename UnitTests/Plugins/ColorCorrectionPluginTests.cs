using Microsoft.VisualStudio.TestTools.UnitTesting;
using OcrAgent.Plugins.Enhancement;

namespace UnitTests.Plugins;

/// <summary>
/// Tests ColorCorrectionPlugin auto and preset correction modes.
/// </summary>
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

    [TestMethod]
    public async Task CorrectColor_InvalidMethod_ThrowsArgumentException()
    {
        bool thrown = false;
        try { await _plugin.CorrectColor(TestImageRef, "invalid"); }
        catch (ArgumentException) { thrown = true; }
        Assert.IsTrue(thrown, "Expected ArgumentException for invalid method");
    }
    [TestMethod]
    public async Task CorrectColor_StoresMetadata()
    {
        var result = await _plugin.CorrectColor(TestImageRef, "gray_world");

        var meta = Store.GetMetadata(result);
        Assert.AreEqual(TestImageRef, meta["parent"]);
        Assert.AreEqual("color_correction", meta["op"]);
        Assert.AreEqual("gray_world", meta["method"]);
        Assert.AreEqual("gray_world", meta["appliedMethod"]);
    }
}