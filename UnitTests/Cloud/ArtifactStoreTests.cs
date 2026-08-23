using OcrAgent.Storage;

namespace UnitTests.Cloud;

[TestClass]
public sealed class ArtifactStoreTests
{
    private string _root = null!;

    [TestInitialize]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), $"ocr-artifacts-{Guid.NewGuid():N}");
    }

    [TestMethod]
    public async Task ListAndOpen_StayWithinRequestedScope()
    {
        var store = new LocalFileImageStore(_root);
        store.SetActiveSubdirectory("wf_test/run-1");
        var reference = store.SaveBytes([1, 2, 3], "png", desiredReference: "output");
        File.WriteAllText(Path.Combine(store.GetWriteDirectory(), "receipt.csv"), "a,b");
        store.SetActiveSubdirectory(null);

        var artifacts = (IArtifactStore)store;
        var list = await artifacts.ListAsync("wf_test/run-1");
        await using var stream = await artifacts.OpenReadAsync("wf_test/run-1", "receipt.csv");

        Assert.AreEqual("wf_test/run-1/output", reference);
        Assert.IsTrue(list.Any(item => item.Name == "receipt.csv"));
        Assert.HasCount(3, list);
        Assert.AreEqual(3, stream.Length);
    }

    [TestMethod]
    [DataRow("../secret")]
    [DataRow("sub/file.json")]
    [DataRow(@"sub\file.json")]
    public async Task OpenReadAsync_RejectsArtifactNameTraversal(string name)
    {
        var store = (IArtifactStore)new LocalFileImageStore(_root);

        await Assert.ThrowsExactlyAsync<ArgumentException>(
            () => store.OpenReadAsync("wf_test/run-1", name));
    }

    [TestCleanup]
    public void TearDown()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
