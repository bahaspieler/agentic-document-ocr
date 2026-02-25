using Microsoft.VisualStudio.TestTools.UnitTesting;
using OcrAgent.Plugins.Enhancement;

namespace UnitTests.Plugins;

[TestClass]
public sealed class ColorCorrectionPluginTests : PluginTestBase
{
    private ColorCorrectionPlugin _plugin = null!;

    [TestInitialize]
    public void Setup()
    {
        _plugin = new ColorCorrectionPlugin(Store);
    }
}