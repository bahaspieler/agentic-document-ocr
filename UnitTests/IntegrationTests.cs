using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OcrAgent.Plugins;
using OcrAgent.Storage;
using OpenCvSharp;


namespace UnitTests; 

/// <summary>
/// Runs end-to-end checks for PluginAggregator with local image storage.
/// </summary>
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
        _aggregator?.Dispose();

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

        Assert.IsGreaterThanOrEqualTo(22, tools.Count, $"Should have at least 22 tools registered, but found {tools.Count}");
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

        // assert 3: image is valid
        Assert.IsFalse(loadedImage.Empty(), "Loaded image should not be empty.");
        Assert.AreEqual(200, loadedImage.Width);
        Assert.AreEqual(200, loadedImage.Height);
    }

    [TestMethod]
    public async Task EndToEnd_ProcessingChain()
    {
        // arrange: create a test image
        var testImagePath = Path.Combine(_tempRoot, "test_chain.png");
        using (var img = new Mat(150, 150, MatType.CV_8UC3, Scalar.White))
        {
            Cv2.Circle(img, new Point(75, 75), 50, Scalar.Black, -1);
            Cv2.ImWrite(testImagePath, img);
        }

        // register the image
        var originalRef = _aggregator.RegisterImage(testImagePath);
        // act 1: apply zoom
        var zoomPlugin = new OcrAgent.Plugins.Geometric.ZoomInPlugin(_store);
        var zoomResult = await zoomPlugin.ZoomInImage(originalRef, 10, 10, 10);
        var zoomedRef = zoomResult.Split(',')[0];

        // assert 1: zoom created a new image
        Assert.IsTrue(_store.Exists(zoomedRef));
        var zoomMeta = _store.GetMetadata(zoomedRef);
        Assert.AreEqual(originalRef, zoomMeta["parent"]);
        Assert.AreEqual("zoom_in", zoomMeta["op"]);

        // act 2: apply contrast enhancement to the zoomed image
        var contrastPlugin = new OcrAgent.Plugins.Enhancement.ContrastPlugin(_store);
        var contrastRef = await contrastPlugin.EnhanceContrast(zoomedRef, "auto");

        // assert 2: contrast created another variant
        Assert.IsTrue(_store.Exists(contrastRef));
        var contrastMeta = _store.GetMetadata(contrastRef);
        Assert.AreEqual(zoomedRef, contrastMeta["parent"]);
        Assert.AreEqual("contrast", contrastMeta["op"]);

        // act 3: apply denoising to the contrast-enhanced image
        var denoisePlugin = new OcrAgent.Plugins.Enhancement.DenoisePlugin(_store);
        var denoiseRef = await denoisePlugin.DenoiseImage(contrastRef, "bilateral", "low");

        // assert 3: denoise created final variant
        Assert.IsTrue(_store.Exists(denoiseRef));
        var denoiseMeta = _store.GetMetadata(denoiseRef);
        Assert.AreEqual(contrastRef, denoiseMeta["parent"]);        Assert.AreEqual("denoise", denoiseMeta["op"]);

        // assert 4: all images in the chain are valid
        foreach (var refId in new[] { originalRef, zoomedRef, contrastRef, denoiseRef })
        {
            var path = _store.GetImagePath(refId);
            using var img = Cv2.ImRead(path);
            Assert.IsFalse(img.Empty(), $"Image {refId} should be valid.");
        }
    }

    [TestMethod]
    public void EndToEnd_MetadataChain()
    {
        // arrange: create and register an image
        var testImagePath = Path.Combine(_tempRoot, "test_metadata.png");
        using (var img = new Mat(100, 100, MatType.CV_8UC3, Scalar.White))
        {
            Cv2.ImWrite(testImagePath, img);
        }

        var originalRef = _aggregator.RegisterImage(testImagePath, "ORIGINAL");

        // act: get metadata
        var meta = _store.GetMetadata(originalRef);
        // assert: metadata is complete
        Assert.AreEqual("ORIGINAL", meta["reference"]);
        Assert.AreEqual("file", meta["source"]);
        Assert.AreEqual("png", meta["extension"]);
        Assert.IsTrue(meta.ContainsKey("createdUtc"));
        Assert.AreEqual(testImagePath, meta["originalPath"]);
    }

    [TestMethod]
    public void PluginAggregator_Dispose_CanBeCalledMultipleTimes()
    {
        var store = new LocalFileImageStore(_tempRoot);
        var aggregator = new PluginAggregator(store);

        aggregator.Dispose();
        aggregator.Dispose();
    }

    [TestMethod]
    public void PluginAggregator_ImplementsIDisposable()
    {
        Assert.IsInstanceOfType<IDisposable>(_aggregator);
    }

    [TestMethod]
    public void PluginAggregator_WithConfig_DoesNotThrow()
    {
        var configBuilder = new ConfigurationBuilder();
        configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Tesseract:TessdataPath"] = null
        });
        var config = configBuilder.Build();

        var store = new LocalFileImageStore(_tempRoot);
        using var aggregator = new PluginAggregator(store, config);

        var tools = aggregator.GetAllTools().ToList();
        Assert.IsTrue(tools.Count > 0, "Should register tools even with null tessdata path");
    }

    [TestMethod]
    public void PluginAggregator_WithNullConfig_DoesNotThrow()
    {
        var store = new LocalFileImageStore(_tempRoot);
        using var aggregator = new PluginAggregator(store, config: null);

        var tools = aggregator.GetAllTools().ToList();
        Assert.IsTrue(tools.Count > 0, "Should register tools with null config");
    }

    [TestMethod]
    public void PluginAggregator_GetToolRegistry_ReturnsNonNull()
    {
        var registry = _aggregator.GetToolRegistry();
        Assert.IsNotNull(registry);
    }

    [TestMethod]
    public void PluginAggregator_GetAllTools_IncludesRegisterImage()
    {
        var tools = _aggregator.GetAllTools().ToList();
        var toolNames = tools.Select(t => t.Name).ToList();

        Assert.IsTrue(toolNames.Contains("RegisterImage"),
            "RegisterImage should be one of the tools");
    }
}