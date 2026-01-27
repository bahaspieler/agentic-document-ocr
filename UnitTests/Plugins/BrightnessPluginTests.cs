using Microsoft.VisualStudio.TestTools.UnitTesting;
using OcrAgent.Plugins.Enhancement;

namespace UnitTests.Plugins;

[TestClass]
public sealed class BrightnessPluginTests : PluginTestBase
{
    private BrightnessPlugin _plugin = null!;

    [TestInitialize]
    public void Setup()
    {
        _plugin = new BrightnessPlugin(Store);
    }
    [TestMethod]
    public async Task AdjustBrightness_GeneratesCorrectNumberOfVariants()
    {
        int start = -20;
        int end = 20;
        int step = 10;
        
        var result = await _plugin.AdjustBrightness(TestImageRef, start, end, step);
        var references = result.Split(',');

        Assert.HasCount(5, (System.Collections.IEnumerable)references, "Should generate 5 brightness variants.");
        foreach (var refId in references)
        {
            AssertImageValid(refId.Trim());
        }
    }
    [TestMethod]
    public async Task AdjustBrightness_StoresMetadata()
    {
        int brightness = 50;

        var result = await _plugin.AdjustBrightness(TestImageRef, brightness, brightness, 10);
        var reference = result.Split(',')[0].Trim();

        var meta = Store.GetMetadata(reference);
        Assert.AreEqual(TestImageRef, meta["parent"]);
        Assert.AreEqual("brightness", meta["op"]);
        Assert.AreEqual("50", meta["adjustment"]);
    }

}