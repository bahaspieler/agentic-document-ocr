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

    [TestMethod]
    public async Task ResizeImage_WithDPI_CalculatesDimensions()
    {
        var result = await _plugin.ResizeImage(TestImageRef, targetDPI: 300);

        AssertImageValid(result);
        var meta = Store.GetMetadata(result);
        Assert.AreEqual("300", meta["targetDPI"]);
        Assert.IsTrue(meta.ContainsKey("newSize"));
    }

    [TestMethod]
    public async Task ResizeImage_ReceiptPreset_UsesCorrectDimensions()
    {
        var result = await _plugin.ResizeImage(TestImageRef, documentType: "receipt");

        AssertImageValid(result);
        var meta = Store.GetMetadata(result);
        Assert.AreEqual("receipt", meta["documentType"]);
        Assert.AreEqual("1200x1600", meta["newSize"]);
    }
}
