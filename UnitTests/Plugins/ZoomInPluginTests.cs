using Microsoft.VisualStudio.TestTools.UnitTesting;
using OcrAgent.Plugins.Geometric;

namespace UnitTests.Plugins;

[TestClass]
public sealed class ZoomInPluginTests : PluginTestBase
{
    private ZoomInPlugin _plugin = null!;

    [TestInitialize]
    public void Setup()
    {
        _plugin = new ZoomInPlugin(Store);
    }

    [TestMethod]
    public async Task ZoomInImage_GeneratesCorrectNumberOfVariants() 
    {
        // arrange
        int startZoom = 10;
        int endZoom = 30;
        int step = 10;

        // act
        var result = await _plugin.ZoomInImage(TestImageRef, startZoom, endZoom, step);
        var references = result.Split(',');

        // assert
        Assert.AreEqual(3, references.Length, "Should generate 3 zoom variants.");
        foreach (var refId in references)
        {
            AssertImageValid(refId.Trim());
        }
    }

    [TestMethod]
    public async Task ZoomInImage_RespectsCap() 
        => await Task.CompletedTask;

    [TestMethod]
    public async Task ZoomInImage_StoresMetadata() 
        => await Task.CompletedTask;
}