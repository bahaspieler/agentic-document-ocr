using Microsoft.VisualStudio.TestTools.UnitTesting;
using OcrAgent.Plugins.Segmentation;

namespace UnitTests.Plugins;

[TestClass]
public sealed class MorphologyPluginTests : PluginTestBase
{
    private MorphologyPlugin _plugin = null!;

    [TestInitialize]
    public void Setup()
    {
        _plugin = new MorphologyPlugin(Store);
    }

    [TestMethod]
    public async Task ApplyMorphology_Erode_ProducesOutput()
    {
        var result = await _plugin.ApplyMorphology(TestImageRef, "erode");

        AssertImageValid(result);
        var meta = Store.GetMetadata(result);
        Assert.AreEqual("morphology", meta["op"]);
        Assert.AreEqual("erode", meta["operation"]);
    }
}