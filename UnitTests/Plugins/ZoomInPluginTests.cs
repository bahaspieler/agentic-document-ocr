using Microsoft.VisualStudio.TestTools.UnitTesting;
using OcrAgent.Plugins.Geometric;

namespace UnitTests.Plugins;

/// <summary>
/// Tests ZoomInPlugin zoom factor variants and outputs.
/// </summary>
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
        Assert.HasCount(3, (System.Collections.IEnumerable)references, "Should generate 3 zoom variants.");
        foreach (var refId in references)
        {
            AssertImageValid(refId.Trim());
        }
    }

    [TestMethod]
    public async Task ZoomInImage_RespectsCap() 
    {
        // arrange
        int startZoom = 0;
        int endZoom = 990;
        int step = 10;

        // act
        var result = await _plugin.ZoomInImage(TestImageRef, startZoom, endZoom, step);
        var references = result.Split(',');

        // assert
        Assert.IsLessThanOrEqualTo(20, references.Length, "Should respect maximum output cap of 20.");
    }

    [TestMethod]
    public async Task ZoomInImage_StoresMetadata() 
    {
        // arrange
        int startZoom = 15;
        int endZoom = 15;
        int step = 10;

        // act
        var result = await _plugin.ZoomInImage(TestImageRef, startZoom, endZoom, step);
        var reference = result.Split(',')[0].Trim();

        // assert
        var meta = Store.GetMetadata(reference);
        Assert.AreEqual(TestImageRef, meta["parent"]);
        Assert.AreEqual("zoom_in", meta["op"]);
        Assert.AreEqual("15", meta["zoomPercent"]);
    }
}