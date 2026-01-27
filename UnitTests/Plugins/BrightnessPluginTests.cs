using Microsoft.VisualStudio.TestTools.UnitTesting;
using OcrAgent.Plugins.Enhancement;

namespace UnitTests.Plugins;

[TestClass]
public sealed class BrightnessPluginTests : PluginTestBase
{
    private BrightnessPlugin _plugin = null!;

    [TestInitialize]
    public void Setup()
    {
        _plugin = new BrightnessPlugin(Store);
    }

}