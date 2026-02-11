using Microsoft.VisualStudio.TestTools.UnitTesting;
using OcrAgent.Plugins.Geometric;
using OpenCvSharp;

namespace UnitTests.Plugins;

[TestClass]
public sealed class PerspectivePluginTests : PluginTestBase
{
    private PerspectivePlugin _plugin = null!;

    [TestInitialize]
    public void Setup()
    {
        _plugin = new PerspectivePlugin(Store);
    }

    [TestMethod]
    public async Task CorrectPerspective_WithRectangle_ProducesOutput()
    {
        var result = await _plugin.CorrectPerspective(TestImageRef);

        AssertImageValid(result);
        var meta = Store.GetMetadata(result);
        Assert.AreEqual("perspective_correct", meta["op"]);
        Assert.IsTrue(meta.ContainsKey("confidence"));
    }

    [TestMethod]
    public async Task CorrectPerspective_AutoSensitivity_ProducesOutput()
    {
        var result = await _plugin.CorrectPerspective(TestImageRef, "auto");

        AssertImageValid(result);
        var meta = Store.GetMetadata(result);
        Assert.IsTrue(meta.ContainsKey("detectionMethod"));
    }

    [TestMethod]
    public async Task CorrectPerspective_AggressiveSensitivity_ProducesOutput()
    {
        var result = await _plugin.CorrectPerspective(TestImageRef, "aggressive");

        AssertImageValid(result);
        var meta = Store.GetMetadata(result);
        Assert.AreEqual("True", meta["enhanceEdges"]);
    }

    [TestMethod]
    public async Task CorrectPerspective_ConservativeSensitivity_ProducesOutput()
    {
        var result = await _plugin.CorrectPerspective(TestImageRef, "conservative");

        AssertImageValid(result);
    }

    [TestMethod]
    public async Task CorrectPerspective_WithTargetDimensions_ProducesCorrectSize()
    {
        var result = await _plugin.CorrectPerspective(TestImageRef, targetWidth: 200, targetHeight: 300);

        AssertImageValid(result);
        var meta = Store.GetMetadata(result);
        Assert.AreEqual("200x300", meta["outputDimensions"]);
    }

    [TestMethod]
    public async Task CorrectPerspective_WithMargin_AppliesMargin()
    {
        var result = await _plugin.CorrectPerspective(TestImageRef, marginPercent: 5);

        AssertImageValid(result);
        var meta = Store.GetMetadata(result);
        Assert.AreEqual("5", meta["marginApplied"]);
    }
}
