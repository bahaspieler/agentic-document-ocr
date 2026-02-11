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
}
