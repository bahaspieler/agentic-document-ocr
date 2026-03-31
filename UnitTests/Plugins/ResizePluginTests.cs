using Microsoft.VisualStudio.TestTools.UnitTesting;
using OcrAgent.Plugins.Geometric;
using OpenCvSharp;

namespace UnitTests.Plugins;

/// <summary>
/// Tests ResizePlugin width, height, and scale resize behavior.
/// </summary>
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

    [TestMethod]
    public async Task ResizeImage_FormPreset_UsesCorrectDimensions()
    {
        var result = await _plugin.ResizeImage(TestImageRef, documentType: "form");

        AssertImageValid(result);
        var meta = Store.GetMetadata(result);
        Assert.AreEqual("form", meta["documentType"]);
        Assert.AreEqual("2550x3300", meta["newSize"]);
    }

    [TestMethod]
    public async Task ResizeImage_InterpolationMethods_Work()
    {
        // test linear method
        var result1 = await _plugin.ResizeImage(TestImageRef, 50, 50, interpolation: "linear");
        AssertImageValid(result1);

        // test cubic method
        var result2 = await _plugin.ResizeImage(TestImageRef, 50, 50, interpolation: "cubic");
        AssertImageValid(result2);

        // test lanczos4 method
        var result3 = await _plugin.ResizeImage(TestImageRef, 50, 50, interpolation: "lanczos4");
        AssertImageValid(result3);
    }

    [TestMethod]
    public async Task ResizeImage_StoresMetadata()
    {
        var result = await _plugin.ResizeImage(TestImageRef, 60, 80);

        var meta = Store.GetMetadata(result);
        Assert.AreEqual(TestImageRef, meta["parent"]);
        Assert.AreEqual("resize", meta["op"]);
        Assert.IsTrue(meta.ContainsKey("originalSize"));
        Assert.IsTrue(meta.ContainsKey("interpolation"));
    }
}
