using OcrAgent.Execution;
using OcrAgent.Runs;
using OcrAgent.Storage;

namespace UnitTests.Cloud;

[TestClass]
public sealed class ExecutionCoreTests
{
    private string _root = null!;

    [TestInitialize]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), $"ocr-execution-{Guid.NewGuid():N}");
    }

    [TestMethod]
    public void SelectPrimaryRun_SelectsLastWorkflow()
    {
        var runs = new[]
        {
            new RunRecord { RunId = "tool-1", Mode = "tool" },
            new RunRecord { RunId = "workflow-1", Mode = "workflow" },
            new RunRecord { RunId = "tool-2", Mode = "tool" },
        };

        var primary = ExecutionCore.SelectPrimaryRun(runs);

        Assert.IsNotNull(primary);
        Assert.AreEqual("workflow-1", primary.RunId);
    }

    [TestMethod]
    public async Task DiscoverAsync_DiscoversOutputDirectoryArtifacts()
    {
        var store = new LocalFileImageStore(_root);
        store.SetActiveSubdirectory("wf/run-1");
        store.SaveBytes([1, 2, 3], "png", desiredReference: "processed");
        File.WriteAllText(Path.Combine(store.GetWriteDirectory(), "result.json"), "{}");
        store.SetActiveSubdirectory(null);
        var run = new RunRecord
        {
            RunId = "run-1",
            OutputDirectory = "wf/run-1",
            OutputReference = "missing",
        };

        var artifacts = await new ArtifactDiscoveryService(store).DiscoverAsync(run);

        Assert.IsTrue(artifacts.Any(item =>
            item.Name == "result.json" && item.ContentType == "application/json"));
    }

    [TestMethod]
    public async Task DiscoverAsync_FallsBackToSingleToolOutputReference()
    {
        var store = new LocalFileImageStore(_root);
        var reference = store.SaveBytes([1, 2, 3], "png", desiredReference: "tool-output");
        var run = new RunRecord
        {
            RunId = "tool-run",
            Mode = "tool",
            OutputReference = reference,
        };

        var artifacts = await new ArtifactDiscoveryService(store).DiscoverAsync(run);

        Assert.HasCount(1, artifacts);
        var artifact = artifacts[0];
        Assert.AreEqual("tool-run", artifact.RunId);
        Assert.AreEqual("image/png", artifact.ContentType);
        Assert.IsTrue(artifact.Previewable);
    }

    [TestCleanup]
    public void TearDown()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
