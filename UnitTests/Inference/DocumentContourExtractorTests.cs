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
}