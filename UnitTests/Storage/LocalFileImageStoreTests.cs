using Microsoft.VisualStudio.TestTools.UnitTesting;
using OcrAgent.Storage;
using System;
using System.Collections.Generic;
using System.IO;

namespace UnitTests.Storage;

/// <summary>
/// Tests LocalFileImageStore save, load, and metadata operations.
/// </summary>
[TestClass]
public sealed class LocalFileImageStoreTests
{
    private string _tempRoot = null!;
    private LocalFileImageStore _store = null!;

    [TestInitialize]
    public void Setup()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "OcrAgentTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);

        _store = new LocalFileImageStore(_tempRoot);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
                Directory.Delete(_tempRoot, recursive: true);
        }
        catch
        {
            // ignore cleanup failures (e.g., file lock)
        }
    }

    [TestMethod]
    public void SaveBytes_CreatesImageAndMetadata_AndCanBeLoaded()
    {
        var bytes = new byte[] { 1, 2, 3, 4, 5, 6, 7 };
        var meta = new Dictionary<string, string>
        {
            ["testKey"] = "testValue"
        };

        var reference = _store.SaveBytes(bytes, "png", meta);

        Assert.IsTrue(_store.Exists(reference), "Saved reference should exist.");

        var imagePath = _store.GetImagePath(reference);
        Assert.IsTrue(File.Exists(imagePath), "Image file should exist on disk.");
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(imagePath), "Saved bytes should match.");

        var loadedMeta = _store.GetMetadata(reference);
        Assert.AreEqual("testValue", loadedMeta["testKey"]);
        Assert.AreEqual(reference, loadedMeta["reference"]);
        Assert.AreEqual("png", loadedMeta["extension"]);
        Assert.IsTrue(loadedMeta.ContainsKey("createdUtc"));
    }

    [TestMethod]
    public void SaveFromFile_SavesFileAndMetadata_AndReturnsReference()
    {
        var inputPath = Path.Combine(_tempRoot, "input.jpg");
        var inputBytes = new byte[] { 10, 20, 30, 40, 50 };
        File.WriteAllBytes(inputPath, inputBytes);

        var reference = _store.SaveFromFile(inputPath);

        Assert.IsTrue(_store.Exists(reference));

        var storedPath = _store.GetImagePath(reference);
        Assert.IsTrue(File.Exists(storedPath));

        var storedBytes = File.ReadAllBytes(storedPath);
        CollectionAssert.AreEqual(inputBytes, storedBytes);

        var meta = _store.GetMetadata(reference);
        Assert.AreEqual("file", meta["source"]);
        Assert.AreEqual(inputPath, meta["originalPath"]);
    }

    [TestMethod]
    public void SaveBytes_WithSameDesiredReference_Throws()
    {
        var bytes = new byte[] { 7, 7, 7, 7 };
        var desired = "MY_REF_001";

        _store.SaveBytes(bytes, "png", desiredReference: desired);

        Assert.ThrowsExactly<InvalidOperationException>(
            () => _store.SaveBytes(bytes, "png", desiredReference: desired));
    }

    [TestMethod]
    public void GetImagePath_ForMissingReference_ThrowsFileNotFound()
    {
        Assert.ThrowsExactly<FileNotFoundException>(
            () => _store.GetImagePath("does-not-exist"));
    }

    [TestMethod]
    public void GetMetadata_ForMissingReference_ReturnsEmptyDictionary()
    {
        var meta = _store.GetMetadata("does-not-exist");

        Assert.IsNotNull(meta);
        Assert.IsEmpty(meta);
    }
}
