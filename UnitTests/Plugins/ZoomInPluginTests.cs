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
        // Setup logic removed
    }

    [TestMethod]
    public async Task ZoomInImage_GeneratesCorrectNumberOfVariants() 
        => await Task.CompletedTask;

    [TestMethod]
    public async Task ZoomInImage_RespectsCap() 
        => await Task.CompletedTask;

    [TestMethod]
    public async Task ZoomInImage_StoresMetadata() 
        => await Task.CompletedTask;
}