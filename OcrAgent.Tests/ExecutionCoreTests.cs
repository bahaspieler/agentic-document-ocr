using OcrAgent.Execution;
using OcrAgent.Runs;
using OcrAgent.Storage;

namespace OcrAgent.Tests;

public sealed class ExecutionCoreTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"ocr-execution-{Guid.NewGuid():N}");

    [Fact]
    public void SelectsLastWorkflowAsPrimaryRun()
    {
        var runs = new[]
        {
            new RunRecord { RunId = "tool-1", Mode = "tool" },
            new RunRecord { RunId = "workflow-1", Mode = "workflow" },
            new RunRecord { RunId = "tool-2", Mode = "tool" },
        };

        var primary = ExecutionCore.SelectPrimaryRun(runs);

        Assert.Equal("workflow-1", primary?.RunId);
    }

    [Fact]
    public async Task DiscoversOutputDirectoryArtifacts()
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

        Assert.Contains(artifacts, item =>
            item.Name == "result.json" && item.ContentType == "application/json");
    }

    [Fact]
    public async Task FallsBackToSingleToolOutputReference()
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

        var artifact = Assert.Single(artifacts);
        Assert.Equal("tool-run", artifact.RunId);
        Assert.Equal("image/png", artifact.ContentType);
        Assert.True(artifact.Previewable);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
