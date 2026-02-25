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
}