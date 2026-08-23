using OcrAgent.Plugins.Enhancement;
using OpenCvSharp;

namespace UnitTests.Plugins;

/// <summary>
/// Tests IlluminationNormalizePlugin output and quality profile skips.
/// </summary>
[TestClass]
public class IlluminationNormalizePluginTests : PluginTestBase
{
    [TestMethod]
    public async Task NormalizeIllumination_CreatesNewImage()
    {
        var plugin = new IlluminationNormalizePlugin(Store);
        var result = await plugin.NormalizeIllumination(TestImageRef);

        Assert.IsFalse(string.IsNullOrEmpty(result));
        AssertImageValid(result);
    }

    [TestMethod]
    public async Task NormalizeIllumination_SkipsWhenUniform_WithQualityProfile()
    {
        var plugin = new IlluminationNormalizePlugin(Store);
        var profile = """{"illuminationUniformity": 0.95}""";

        var result = await plugin.NormalizeIllumination(TestImageRef, qualityProfile: profile);

        Assert.AreEqual(TestImageRef, result, "Should skip when uniformity is high");
    }

    [TestMethod]
    public async Task NormalizeIllumination_ProcessesWhenNonUniform_WithQualityProfile()
    {
        var plugin = new IlluminationNormalizePlugin(Store);
        var profile = """{"illuminationUniformity": 0.60}""";

        var result = await plugin.NormalizeIllumination(TestImageRef, qualityProfile: profile);

        Assert.AreNotEqual(TestImageRef, result, "Should process when uniformity is low");
        AssertImageValid(result);
    }

    [TestMethod]
    public async Task NormalizeIllumination_CustomKernelSize()
    {
        var plugin = new IlluminationNormalizePlugin(Store);
        var result = await plugin.NormalizeIllumination(TestImageRef, kernelSize: 31);

        Assert.IsFalse(string.IsNullOrEmpty(result));
    }

    [TestMethod]
    public async Task NormalizeIllumination_EvenKernelSize_Throws()
    {
        var plugin = new IlluminationNormalizePlugin(Store);
        bool thrown = false;
        try { await plugin.NormalizeIllumination(TestImageRef, kernelSize: 10); }
        catch (ArgumentException) { thrown = true; }
        Assert.IsTrue(thrown, "Expected ArgumentException for even kernel size");
    }
}
