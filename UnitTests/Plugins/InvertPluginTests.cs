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
}