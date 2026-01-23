using Microsoft.VisualStudio.TestTools.UnitTesting;
using OcrAgent.Plugins.Geometric;
using OpenCvSharp;

namespace UnitTests.Plugins;

[TestClass]
public sealed class DeskewPluginTests : PluginTestBase
{
    private DeskewPlugin _plugin = null!;

    [TestInitialize]
    public void Setup()
    {
        _plugin = new DeskewPlugin(Store);
    }

    [TestMethod]
    public async Task DeskewImage_Hough_ProducesOutput()
    {
        // act
        var result = await _plugin.DeskewImage(TestImageRef, 10.0, "hough");

        // assert
        AssertImageValid(result);
        var meta = Store.GetMetadata(result);
        Assert.AreEqual("deskew", meta["op"]);
        Assert.AreEqual("hough", meta["method"]);
    }

    [TestMethod]
    public async Task DeskewImage_Projection_ProducesOutput()
    {
        // act
        var result = await _plugin.DeskewImage(TestImageRef, 10.0, "projection");

        // assert
        AssertImageValid(result);
        var meta = Store.GetMetadata(result);
        Assert.AreEqual("projection", meta["method"]);
    }
}
