using Microsoft.VisualStudio.TestTools.UnitTesting;  
using OcrAgent.Plugins.Geometric;  
using OpenCvSharp;

namespace UnitTests.Plugins
{
    [TestClass]  
    public sealed class ResizePluginTests : PluginTestBase
    {
        private ResizePlugin _plugin = null!;  

        // Setup method to initialize the plugin before each test
        [TestInitialize]  
        public void Setup()
        {
            _plugin = new ResizePlugin(Store); 
        }
    }
}
