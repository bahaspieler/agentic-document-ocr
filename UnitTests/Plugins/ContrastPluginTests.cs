using Microsoft.VisualStudio.TestTools.UnitTesting;
using OcrAgent.Plugins.Enhancement;

namespace UnitTests.Plugins;

[TestClass]
public sealed class ContrastPluginTests : PluginTestBase
{
    private ContrastPlugin _plugin = null!;

    [TestInitialize]
    public void Setup()
    {
        _plugin = new ContrastPlugin(Store);
    }

    [TestMethod]
    public async Task EnhanceContrast_Auto_ProducesOutput()
    {
        var result = await _plugin.EnhanceContrast(TestImageRef, "auto");

        AssertImageValid(result);
        var meta = Store.GetMetadata(result);
        Assert.AreEqual("contrast", meta["op"]);
        Assert.AreEqual("auto", meta["method"]);
    }

    [TestMethod]
    public async Task EnhanceContrast_Histogram_ProducesOutput()
    {
        var result = await _plugin.EnhanceContrast(TestImageRef, "histogram");

        AssertImageValid(result);
        var meta = Store.GetMetadata(result);
        Assert.AreEqual("histogram", meta["method"]);
    }

    [TestMethod]
    public async Task EnhanceContrast_CLAHE_ProducesOutput()
    {
        var result = await _plugin.EnhanceContrast(TestImageRef, "clahe");

        AssertImageValid(result);
        var meta = Store.GetMetadata(result);
        Assert.AreEqual("clahe", meta["method"]);
    }

    [TestMethod]
    public async Task EnhanceContrast_DefaultMethod()
    {
        var result = await _plugin.EnhanceContrast(TestImageRef);

        AssertImageValid(result);
        var meta = Store.GetMetadata(result);
        Assert.AreEqual("auto", meta["method"]);
    }

    [TestMethod]
    public async Task EnhanceContrast_AutoLab_ProducesOutput()
    {
        var result = await _plugin.EnhanceContrast(TestImageRef, "auto_lab");

        AssertImageValid(result);
        var meta = Store.GetMetadata(result);
        Assert.AreEqual("auto_lab", meta["method"]);
    }

    [TestMethod]
    public async Task EnhanceContrast_AutoLab_SkipsWhenContrastAdequate()
    {
        var profile = """{"contrast": 60.0}""";
        var result = await _plugin.EnhanceContrast(TestImageRef, "auto_lab", qualityProfile: profile);

        Assert.AreEqual(TestImageRef, result, "Should skip when contrast > 55");
    }

    [TestMethod]
    public async Task EnhanceContrast_AutoLab_ProcessesWhenContrastLow()
    {
        var profile = """{"contrast": 20.0}""";
        var result = await _plugin.EnhanceContrast(TestImageRef, "auto_lab", qualityProfile: profile);

        Assert.AreNotEqual(TestImageRef, result, "Should process when contrast is low");
        AssertImageValid(result);
    }
}
