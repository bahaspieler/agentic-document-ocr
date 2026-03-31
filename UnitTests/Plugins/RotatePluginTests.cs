using Microsoft.VisualStudio.TestTools.UnitTesting;
using OcrAgent.Plugins.Geometric;

namespace UnitTests.Plugins;

/// <summary>
/// Tests RotatePlugin angle variants and stored outputs.
/// </summary>
[TestClass]
public sealed class RotatePluginTests : PluginTestBase
{
    private RotatePlugin _plugin = null!;

    [TestInitialize]
    public void Setup()
    {
        _plugin = new RotatePlugin(Store);
    }

    [TestMethod]
    public async Task RotateImage_GeneratesCorrectNumberOfVariants()
    {
        int startAngle = 0;
        int endAngle = 45;
        int step = 15;
        
        var result = await _plugin.RotateImage(TestImageRef, startAngle, endAngle, step);
        var references = result.Split(',');
        
        Assert.AreEqual(4, references.Length, "Should generate 4 rotation variants.");
        foreach (var refId in references)
        {
            AssertImageValid(refId.Trim());
        }
    }

    [TestMethod]
    public async Task RotateImage_NormalizesAngles()
    {
        int startAngle = 370;
        int endAngle = 370;
        int step = 10;
        
        var result = await _plugin.RotateImage(TestImageRef, startAngle, endAngle, step);
        var reference = result.Split(',')[0].Trim();
        
        var meta = Store.GetMetadata(reference);
        Assert.AreEqual("10", meta["angleDegrees"], "370° should normalize to 10°");
    }
    
    [TestMethod]
    public async Task RotateImage_StoresMetadata()
    {
        int angle = 90;
        
        var result = await _plugin.RotateImage(TestImageRef, angle, angle, 10);
        var reference = result.Split(',')[0].Trim();
        
        var meta = Store.GetMetadata(reference);
        Assert.AreEqual(TestImageRef, meta["parent"]);
        Assert.AreEqual("rotate", meta["op"]);
        Assert.AreEqual("90", meta["angleDegrees"]);
    }
}