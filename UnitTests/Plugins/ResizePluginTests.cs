using Microsoft.VisualStudio.TestTools.UnitTesting;
using OcrAgent.Plugins.Geometric;
using OpenCvSharp;

namespace UnitTests.Plugins;

[TestClass]
public sealed class ResizePluginTests : PluginTestBase
{
    private ResizePlugin _plugin = null!;

    [TestInitialize]
    public void Setup()
    {
        _plugin = new ResizePlugin(Store);
    }

    [TestMethod]
    public async Task ResizeImage_ByWidth_ProducesCorrectSize()
    {
        var result = await _plugin.ResizeImage(TestImageRef, targetWidth: 50, targetHeight: 0);

        AssertImageValid(result);
        var meta = Store.GetMetadata(result);
        Assert.IsTrue(meta["newSize"].StartsWith("50x"));
    }

    [TestMethod]
    public async Task ResizeImage_ByHeight_ProducesCorrectSize()
    {
        var result = await _plugin.ResizeImage(TestImageRef, targetWidth: 0, targetHeight: 50);

        AssertImageValid(result);
        var meta = Store.GetMetadata(result);
        Assert.IsTrue(meta["newSize"].EndsWith("x50"));
    }

    [TestMethod]
    public async Task ResizeImage_ExactDimensions_ProducesCorrectSize()
    {
        var result = await _plugin.ResizeImage(TestImageRef, 60, 80);

        AssertImageValid(result);
        var meta = Store.GetMetadata(result);
        Assert.AreEqual("60x80", meta["newSize"]);
    }
}
