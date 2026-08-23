using Microsoft.VisualStudio.TestTools.UnitTesting;
using OcrAgent.Plugins.Segmentation;

namespace UnitTests.Plugins;

/// <summary>
/// Tests MorphologyPlugin erode, dilate, and related operations.
/// </summary>
[TestClass]
public sealed class MorphologyPluginTests : PluginTestBase
{
    private MorphologyPlugin _plugin = null!;

    [TestInitialize]
    public void Setup()
    {
        _plugin = new MorphologyPlugin(Store);
    }

    [TestMethod]
    public async Task ApplyMorphology_Erode_ProducesOutput()
    {
        var result = await _plugin.ApplyMorphology(TestImageRef, "erode");

        AssertImageValid(result);
        var meta = Store.GetMetadata(result);
        Assert.AreEqual("morphology", meta["op"]);
        Assert.AreEqual("erode", meta["operation"]);
    }

    [TestMethod]
    public async Task ApplyMorphology_Dilate_ProducesOutput()
    {
        var result = await _plugin.ApplyMorphology(TestImageRef, "dilate");

        AssertImageValid(result);
        var meta = Store.GetMetadata(result);
        Assert.AreEqual("dilate", meta["operation"]);
    }

    [TestMethod]
    public async Task ApplyMorphology_Open_ProducesOutput()
    {
        var result = await _plugin.ApplyMorphology(TestImageRef, "open");

        AssertImageValid(result);
        var meta = Store.GetMetadata(result);
        Assert.AreEqual("open", meta["operation"]);
    }

    [TestMethod]
    public async Task ApplyMorphology_Close_ProducesOutput()
    {
        var result = await _plugin.ApplyMorphology(TestImageRef, "close");

        AssertImageValid(result);
        var meta = Store.GetMetadata(result);
        Assert.AreEqual("close", meta["operation"]);
    }

    [TestMethod]
    public async Task ApplyMorphology_Gradient_ProducesOutput()
    {
        var result = await _plugin.ApplyMorphology(TestImageRef, "gradient");

        AssertImageValid(result);
        var meta = Store.GetMetadata(result);
        Assert.AreEqual("gradient", meta["operation"]);
    }

    [TestMethod]
    public async Task ApplyMorphology_CustomKernel_ProducesOutput()
    {
        var result = await _plugin.ApplyMorphology(TestImageRef, "close", kernelSize: 5, iterations: 2);

        AssertImageValid(result);
        var meta = Store.GetMetadata(result);
        Assert.AreEqual("5", meta["kernelSize"]);
        Assert.AreEqual("2", meta["iterations"]);
    }

    [TestMethod]
    public async Task ApplyMorphology_EllipseKernel_ProducesOutput()
    {
        var result = await _plugin.ApplyMorphology(TestImageRef, "dilate", kernelShape: "ellipse");

        AssertImageValid(result);
        var meta = Store.GetMetadata(result);
        Assert.AreEqual("ellipse", meta["kernelShape"]);
    }

    [TestMethod]
    public async Task ApplyMorphology_CrossKernel_ProducesOutput()
    {
        var result = await _plugin.ApplyMorphology(TestImageRef, "erode", kernelShape: "cross");

        AssertImageValid(result);
        var meta = Store.GetMetadata(result);
        Assert.AreEqual("cross", meta["kernelShape"]);
    }

    [TestMethod]
    public async Task ApplyMorphology_InvalidOperation_ThrowsArgumentException()
    {
        bool thrown = false;
        try { await _plugin.ApplyMorphology(TestImageRef, "invalid"); }
        catch (ArgumentException) { thrown = true; }
        Assert.IsTrue(thrown, "Expected ArgumentException for invalid operation");
    }

    [TestMethod]
    public async Task ApplyMorphology_EvenKernelSize_ThrowsArgumentException()
    {
        bool thrown = false;
        try { await _plugin.ApplyMorphology(TestImageRef, "close", kernelSize: 4); }
        catch (ArgumentException) { thrown = true; }
        Assert.IsTrue(thrown, "Expected ArgumentException for even kernel size");
    }
    [TestMethod]
    public async Task ApplyMorphology_StoresMetadata()
    {
        var result = await _plugin.ApplyMorphology(TestImageRef, "open", kernelSize: 7, iterations: 3, kernelShape: "rect");

        var meta = Store.GetMetadata(result);
        Assert.AreEqual(TestImageRef, meta["parent"]);
        Assert.AreEqual("morphology", meta["op"]);
        Assert.AreEqual("open", meta["operation"]);
        Assert.AreEqual("7", meta["kernelSize"]);
        Assert.AreEqual("3", meta["iterations"]);
        Assert.AreEqual("rect", meta["kernelShape"]);
    }

    [TestMethod]
    public async Task ApplyMorphology_RemoveSpeckles_ProducesOutput()
    {
        var result = await _plugin.ApplyMorphology(TestImageRef, "remove_speckles");

        AssertImageValid(result);
        var meta = Store.GetMetadata(result);
        Assert.AreEqual("remove_speckles", meta["operation"]);
        Assert.AreEqual("20", meta["minArea"]);
    }

    [TestMethod]
    public async Task ApplyMorphology_RemoveSpeckles_CustomMinArea()
    {
        var result = await _plugin.ApplyMorphology(TestImageRef, "remove_speckles", minArea: 50);

        AssertImageValid(result);
        var meta = Store.GetMetadata(result);
        Assert.AreEqual("50", meta["minArea"]);
    }
}


