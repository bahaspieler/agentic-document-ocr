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
}