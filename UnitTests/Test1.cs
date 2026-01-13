using Microsoft.VisualStudio.TestTools.UnitTesting;
using OcrAgent.Storage;
using System;
using System.Collections.Generic;
using System.IO;

namespace UnitTests;

[TestClass]
public sealed class Test1
{
    private string _tempRoot = null!;
    private LocalFileImageStore _store = null!;

    [TestInitialize]
    public void Setup()
    {
        // Create a unique temp folder for each test run
        _tempRoot = Path.Combine(Path.GetTempPath(), "OcrAgentTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);

        _store = new LocalFileImageStore(_tempRoot);
    }

    [TestCleanup]
    public void Cleanup()
    {
        // Cleanup temp files after test
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
        // Arrange: fake "image bytes" (doesn't need to be real PNG for storage test)
        var bytes = new byte[] { 1, 2, 3, 4, 5,6,7 };
        var meta = new Dictionary<string, string>
        {
            ["testKey"] = "testValue"
        };

        // Act
        var reference = _store.SaveBytes(bytes, "png", meta);

        // Assert: exists
        Assert.IsTrue(_store.Exists(reference), "Saved reference should exist.");

        // Assert: image file exists and bytes match
        var imagePath = _store.GetImagePath(reference);
        Assert.IsTrue(File.Exists(imagePath), "Image file should exist on disk.");
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(imagePath), "Saved bytes should match.");

        // Assert: metadata exists and includes our keys + auto keys
        var loadedMeta = _store.GetMetadata(reference);

        Assert.AreEqual("testValue", loadedMeta["testKey"]);
        Assert.AreEqual(reference, loadedMeta["reference"]);
        Assert.AreEqual("png", loadedMeta["extension"]);
        Assert.IsTrue(loadedMeta.ContainsKey("createdUtc"));
    }

    [TestMethod]
    public void SaveFromFile_SavesFileAndMetadata_AndReturnsReference()
    {
        // Arrange: create a temporary input file
        var inputPath = Path.Combine(_tempRoot, "input.jpg");
        var inputBytes = new byte[] { 10, 20, 30, 40 ,50};
        File.WriteAllBytes(inputPath, inputBytes);

        // Act
        var reference = _store.SaveFromFile(inputPath);

        // Assert: exists
        Assert.IsTrue(_store.Exists(reference));

        // Stored bytes should match
        var storedPath = _store.GetImagePath(reference);
        Assert.IsTrue(File.Exists(storedPath));

        var storedBytes = File.ReadAllBytes(storedPath);
        CollectionAssert.AreEqual(inputBytes, storedBytes);

        // Metadata should contain source + originalPath
        var meta = _store.GetMetadata(reference);
        Assert.AreEqual("file", meta["source"]);
        Assert.AreEqual(inputPath, meta["originalPath"]);
    }

    [TestMethod]
    public void SaveBytes_WithSameDesiredReference_Throws()
    {
        // Arrange
        var bytes = new byte[] { 7, 7, 7 ,7};
        var desired = "MY_REF_001";

        // First save should work
        _store.SaveBytes(bytes, "png", desiredReference: desired);

        // Second save with same reference should throw
        try
        {
            _store.SaveBytes(bytes, "png", desiredReference: desired);
            Assert.Fail("Expected InvalidOperationException, but no exception was thrown.");
        }
        catch (InvalidOperationException)
        {
            // ✅ expected
        }
    }

    [TestMethod]
    public void GetImagePath_ForMissingReference_ThrowsFileNotFound()
    {
        try
        {
            _store.GetImagePath("does-not-exist");
            Assert.Fail("Expected FileNotFoundException, but no exception was thrown.");
        }
        catch (FileNotFoundException)
        {
            // ✅ expected
        }
    }

    [TestMethod]
    public void GetMetadata_ForMissingReference_ReturnsEmptyDictionary()
    {
        var meta = _store.GetMetadata("does-not-exist");

        Assert.IsNotNull(meta);
        Assert.AreEqual(0, meta.Count);
    }
}
