using OcrAgent.Storage;

namespace OcrAgent.Tests;

public sealed class ArtifactStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"ocr-artifacts-{Guid.NewGuid():N}");

    [Fact]
    public async Task ListsAndOpensOnlyFilesInRequestedScope()
    {
        var store = new LocalFileImageStore(_root);
        store.SetActiveSubdirectory("wf_test/run-1");
        var reference = store.SaveBytes([1, 2, 3], "png", desiredReference: "output");
        File.WriteAllText(Path.Combine(store.GetWriteDirectory(), "receipt.csv"), "a,b");
        store.SetActiveSubdirectory(null);

        var artifacts = (IArtifactStore)store;
        var list = await artifacts.ListAsync("wf_test/run-1");
        await using var stream = await artifacts.OpenReadAsync("wf_test/run-1", "receipt.csv");

        Assert.Equal("wf_test/run-1/output", reference);
        Assert.Contains(list, item => item.Name == "receipt.csv");
        Assert.Equal(3, list.Count);
        Assert.Equal(3, stream.Length);
    }

    [Theory]
    [InlineData("../secret")]
    [InlineData("sub/file.json")]
    [InlineData(@"sub\file.json")]
    public async Task RejectsArtifactNameTraversal(string name)
    {
        var store = (IArtifactStore)new LocalFileImageStore(_root);
        await Assert.ThrowsAsync<ArgumentException>(
            () => store.OpenReadAsync("wf_test/run-1", name));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
