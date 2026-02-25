using Microsoft.VisualStudio.TestTools.UnitTesting;
using OcrAgent.Plugins.Enhancement;
using OpenCvSharp;

namespace UnitTests.Plugins;

[TestClass]
public sealed class InvertPluginTests : PluginTestBase
{
    private InvertPlugin _plugin = null!;

    [TestInitialize]
    public void Setup()
    {
        _plugin = new InvertPlugin(Store);
    }

    [TestMethod]
    public async Task InvertImage_Always_ProducesOutput()
    {
        var result = await _plugin.InvertImage(TestImageRef, "always");

        AssertImageValid(result);
        var meta = Store.GetMetadata(result);
        Assert.AreEqual("invert", meta["op"]);
        Assert.AreEqual("True", meta["wasInverted"]);
    }
}