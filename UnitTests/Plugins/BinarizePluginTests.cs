using Microsoft.VisualStudio.TestTools.UnitTesting;
using OcrAgent.Plugins.Segmentation;

namespace UnitTests.Plugins;

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
}
