using Microsoft.VisualStudio.TestTools.UnitTesting;
using OcrAgent.Plugins.Enhancement;

namespace UnitTests.Plugins;

[TestClass]
public sealed class ContrastPluginTests : PluginTestBase
{
    private ContrastPlugin _plugin = null!;

    [TestInitialize]
    public void Setup()
    {
        _plugin = new ContrastPlugin(Store);
    }
}
