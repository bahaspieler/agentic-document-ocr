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

        // Test method to check resizing by width
        [TestMethod]
        public async Task ResizeImage_ByWidth_ProducesCorrectSize()
        {
            var result = await _plugin.ResizeImage(TestImageRef, targetWidth: 50, targetHeight: 0);

            AssertImageValid(result);  
            var meta = Store.GetMetadata(result);  
            Assert.StartsWith(meta["newSize"], "50x");  
        }
    }
}
