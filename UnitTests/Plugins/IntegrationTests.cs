using Microsoft.VisualStudio.TestTools.UnitTesting;
using OcrAgent.Plugins;
using OcrAgent.Storage;
using OpenCvSharp;

namespace UnitTests; [TestClass] public sealed class IntegrationTests
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
}