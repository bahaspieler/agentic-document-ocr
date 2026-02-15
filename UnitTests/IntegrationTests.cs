using Microsoft.VisualStudio.TestTools.UnitTesting;
using OcrAgent.Plugins;
using OcrAgent.Storage;
using OpenCvSharp;


namespace UnitTests; 

[TestClass] 
public sealed class IntegrationTests
{
    private string _tempRoot = null!;
    private LocalFileImageStore _store = null!;
    private PluginAggregator _aggregator = null!;

    [TestInitialize]
    public void Setup()
    {
        // unique temp folder for each test
        _tempRoot = Path.Combine(Path.GetTempPath(), "OcrAgentIntegrationTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);

        _store = new LocalFileImageStore(_tempRoot);
        _aggregator = new PluginAggregator(_store);
    }

    [TestCleanup]
    public void Cleanup()
    {
        // cleanup temp files after test
        try
        {
            if (Directory.Exists(_tempRoot))
                Directory.Delete(_tempRoot, recursive: true);
        }
        catch
        {
            // ignore cleanup failures
        }
    }

    [TestMethod]
    public void PluginAggregator_RegistersAllPlugins()
    {
        var tools = _aggregator.GetAllTools().ToList();

        Assert.IsGreaterThanOrEqualTo(tools.Count, 6, $"Should have at least 6 tools registered, but found {tools.Count}");
    }

    [TestMethod]
    public void EndToEnd_RegisterAndProcessImage()
    {
        // arrange: create a test image file
        var testImagePath = Path.Combine(_tempRoot, "test_input.png");
        using (var img = new Mat(200, 200, MatType.CV_8UC3, Scalar.White))
        {
            Cv2.Rectangle(img, new Rect(50, 50, 100, 100), Scalar.Black, -1);
            Cv2.ImWrite(testImagePath, img);
        }

        // act 1: register the image
        var originalRef = _aggregator.RegisterImage(testImagePath);

        // assert 1: image was registered
        Assert.IsTrue(_store.Exists(originalRef));
        var originalMeta = _store.GetMetadata(originalRef);
        Assert.AreEqual("file", originalMeta["source"]);

        // act 2: get all tools and verify they can be invoked
        var tools = _aggregator.GetAllTools().ToList();
        
        // assert 2: multiple tools are available
        Assert.IsTrue(tools.Count >= 6, "Should have multiple preprocessing tools available.");

        // act 3: verify image can be read
        var imagePath = _store.GetImagePath(originalRef);
        using var loadedImage = Cv2.ImRead(imagePath);
    }
}