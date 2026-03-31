using Microsoft.VisualStudio.TestTools.UnitTesting;
using OcrAgent.Plugins.Enhancement;

namespace UnitTests.Plugins;

/// <summary>
/// Tests BrightnessPlugin variant generation and output validation.
/// </summary>
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

    [TestMethod]
    public async Task AutoAdjustBrightness_ProducesOutput()
    {
        var result = await _plugin.AutoAdjustBrightness(TestImageRef);

        Assert.IsFalse(string.IsNullOrEmpty(result));
    }

    [TestMethod]
    public async Task AutoAdjustBrightness_SkipsWhenInRange()
    {
        var profile = """{"meanBrightness": 150.0}""";
        var result = await _plugin.AutoAdjustBrightness(TestImageRef, qualityProfile: profile);

        Assert.AreEqual(TestImageRef, result, "Should skip when brightness is already in optimal range");
    }

    [TestMethod]
    public async Task AutoAdjustBrightness_AdjustsWhenTooLow()
    {
        var profile = """{"meanBrightness": 80.0}""";
        var result = await _plugin.AutoAdjustBrightness(TestImageRef, qualityProfile: profile);

        Assert.AreNotEqual(TestImageRef, result, "Should adjust when brightness is too low");
    }

    [TestMethod]
    public async Task AutoAdjustBrightness_AdjustsWhenTooHigh()
    {
        var profile = """{"meanBrightness": 220.0}""";
        var result = await _plugin.AutoAdjustBrightness(TestImageRef, qualityProfile: profile);

        Assert.AreNotEqual(TestImageRef, result, "Should adjust when brightness is too high");
    }
}