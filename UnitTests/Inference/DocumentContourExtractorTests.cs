using Microsoft.VisualStudio.TestTools.UnitTesting;
using OcrAgent.Inference;
using OpenCvSharp;

namespace UnitTests.Inference;

[TestClass]
public sealed class DocumentContourExtractorTests
{
    [TestMethod]
    public void TryExtractDocument_WithEmptyMask_ThrowsArgumentException()
    {
        using var emptyMask = new Mat();

        bool thrown = false;
        try
        {
            DocumentContourExtractor.TryExtractDocument(
                emptyMask, out _, out _, out _, out _);
        }
        catch (ArgumentException)
        {
            thrown = true;
        }
        Assert.IsTrue(thrown, "Should throw ArgumentException for empty mask");
    }

    [TestMethod]
    public void TryExtractDocument_WithWrongType_ThrowsArgumentException()
    {
        using var wrongType = new Mat(100, 100, MatType.CV_8UC1);

        bool thrown = false;
        try
        {
            DocumentContourExtractor.TryExtractDocument(
                wrongType, out _, out _, out _, out _);
        }
        catch (ArgumentException)
        {
            thrown = true;
        }
        Assert.IsTrue(thrown, "Should throw ArgumentException for wrong type");
    }

    [TestMethod]
    public void TryExtractDocument_WithValidMask_ReturnsTrue()
    {
        using var probMask = new Mat(400, 300, MatType.CV_32FC1, new Scalar(0.0));
        Cv2.Rectangle(probMask, new Rect(50, 50, 200, 250), new Scalar(0.9), -1);

        bool result = DocumentContourExtractor.TryExtractDocument(
            probMask, out var docMask, out var quad, out var coverage, out var confidence);
        using var maskOwned = docMask;

        Assert.IsTrue(result, "Should successfully extract document");
        Assert.IsNotNull(docMask, "Document mask should not be null");
        Assert.IsFalse(docMask.Empty(), "Document mask should not be empty");
        Assert.AreEqual(MatType.CV_8UC1, docMask.Type(), "Document mask should be 8-bit");
        Assert.AreEqual(4, quad.Length, "Quad should have 4 corners");
        Assert.IsTrue(coverage > 0.1 && coverage < 0.98, $"Coverage should be reasonable, got {coverage}");
        Assert.IsTrue(confidence > 0.0 && confidence <= 1.0, $"Confidence should be in [0,1], got {confidence}");
    }

    [TestMethod]
    public void TryExtractDocument_WithTooSmallRegion_ReturnsFalse()
    {
        using var probMask = new Mat(400, 300, MatType.CV_32FC1, new Scalar(0.0));
        Cv2.Rectangle(probMask, new Rect(150, 140, 10, 10), new Scalar(0.9), -1);

        bool result = DocumentContourExtractor.TryExtractDocument(
            probMask, out var docMask, out var quad, out var coverage, out var confidence);
        using var maskOwned = docMask;

        Assert.IsFalse(result, "Should reject too-small regions");
        Assert.AreEqual(0, quad.Length, "Quad should be empty on failure");
        Assert.AreEqual(0.0, confidence, "Confidence should be 0 on failure");
    }

    [TestMethod]
    public void TryExtractDocument_WithFullImageMask_ReturnsFalse()
    {
        using var probMask = new Mat(400, 300, MatType.CV_32FC1, new Scalar(0.95));

        bool result = DocumentContourExtractor.TryExtractDocument(
            probMask, out var docMask, out var quad, out var coverage, out var confidence);
        using var maskOwned = docMask;

        Assert.IsFalse(result, "Should reject full-image masks");
    }

    [TestMethod]
    public void ToBinaryMask_WithEmptyMask_ReturnsNull()
    {
        using var emptyMask = new Mat();

        var result = DocumentContourExtractor.ToBinaryMask(emptyMask);

        Assert.IsNull(result, "Should return null for empty mask");
    }

    [TestMethod]
    public void ToBinaryMask_WithWrongType_ReturnsNull()
    {
        using var wrongType = new Mat(100, 100, MatType.CV_8UC1);

        var result = DocumentContourExtractor.ToBinaryMask(wrongType);

        Assert.IsNull(result, "Should return null for wrong type");
    }

    [TestMethod]
    public void ToBinaryMask_WithValidProbMask_ReturnsBinaryMask()
    {
        using var probMask = new Mat(400, 300, MatType.CV_32FC1, new Scalar(0.0));
        Cv2.Rectangle(probMask, new Rect(50, 50, 200, 250), new Scalar(0.85), -1);

        using var binaryMask = DocumentContourExtractor.ToBinaryMask(probMask);

        Assert.IsNotNull(binaryMask, "Binary mask should not be null");
        Assert.IsFalse(binaryMask.Empty(), "Binary mask should not be empty");
        Assert.AreEqual(MatType.CV_8UC1, binaryMask.Type(), "Should be 8-bit mask");
        Assert.AreEqual(probMask.Width, binaryMask.Width, "Width should match");
        Assert.AreEqual(probMask.Height, binaryMask.Height, "Height should match");

        Cv2.MinMaxLoc(binaryMask, out double minVal, out double maxVal);
        Assert.AreEqual(0.0, minVal, "Should have background (0)");
        Assert.AreEqual(255.0, maxVal, "Should have foreground (255)");
    }
}