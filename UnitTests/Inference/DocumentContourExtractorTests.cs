using Microsoft.VisualStudio.TestTools.UnitTesting;
using OcrAgent.Inference;
using OpenCvSharp;

namespace UnitTests.Inference;

/// <summary>
/// Tests for DocumentContourExtractor utility methods.
/// </summary>
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
        // Create a probability mask with a clear rectangular foreground region
        using var probMask = new Mat(400, 300, MatType.CV_32FC1, new Scalar(0.0));

        // High probability rectangle in center (simulating document)
        Cv2.Rectangle(probMask, new Rect(50, 50, 200, 250), new Scalar(0.9), -1);

        bool result = DocumentContourExtractor.TryExtractDocument(
            probMask, out var docMask, out var quad, out var coverage, out var confidence);
        using var maskOwned = docMask;

        Assert.IsTrue(result, "Should successfully extract document");
        Assert.IsNotNull(docMask, "Document mask should not be null");
        Assert.IsFalse(docMask.Empty(), "Document mask should not be empty");
        Assert.AreEqual(MatType.CV_8UC1, docMask.Type(), "Document mask should be 8-bit");
        Assert.HasCount(4, quad, "Quad should have 4 corners");
        Assert.IsTrue(coverage > 0.1 && coverage < 0.98, $"Coverage should be reasonable, got {coverage}");
        Assert.IsTrue(confidence > 0.0 && confidence <= 1.0, $"Confidence should be in [0,1], got {confidence}");
    }

    [TestMethod]
    public void TryExtractDocument_WithTooSmallRegion_ReturnsFalse()
    {
        using var probMask = new Mat(400, 300, MatType.CV_32FC1, new Scalar(0.0));

        // Very small region (below minAreaPercent threshold)
        Cv2.Rectangle(probMask, new Rect(150, 140, 10, 10), new Scalar(0.9), -1);

        bool result = DocumentContourExtractor.TryExtractDocument(
            probMask, out var docMask, out var quad, out var coverage, out var confidence);
        using var maskOwned = docMask;

        Assert.IsFalse(result, "Should reject too-small regions");
        Assert.IsEmpty(quad, "Quad should be empty on failure");
        Assert.AreEqual(0.0, confidence, "Confidence should be 0 on failure");
    }

    [TestMethod]
    public void TryExtractDocument_WithFullImageMask_ReturnsFalse()
    {
        // Full-image mask (above maxAreaPercent threshold)
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

        // Verify it contains both 0 and 255 values
        Cv2.MinMaxLoc(binaryMask, out double minVal, out double maxVal);
        Assert.AreEqual(0.0, minVal, "Should have background (0)");
        Assert.AreEqual(255.0, maxVal, "Should have foreground (255)");
    }

    [TestMethod]
    public void OrderCorners_ReturnsCorrectOrder()
    {
        // Create corners in random order
        var corners = new[]
        {
            new Point2f(100, 100), // TL
            new Point2f(200, 100), // TR
            new Point2f(200, 200), // BR
            new Point2f(100, 200)  // BL
        };

        var shuffled = new[] { corners[2], corners[0], corners[3], corners[1] }; // BR, TL, BL, TR

        var ordered = DocumentContourExtractor.OrderCorners(shuffled);

        Assert.HasCount(4, ordered);
        // Should be TL, TR, BR, BL
        Assert.IsLessThan(ordered[1].X, ordered[0].X, "TL should be left of TR");
        Assert.IsLessThan(ordered[3].Y, ordered[0].Y, "TL should be above BL");
        Assert.IsGreaterThan(ordered[0].X, ordered[1].X, "TR should be right of TL");
        Assert.IsGreaterThan(ordered[3].X, ordered[2].X, "BR should be right of BL");
    }

    [TestMethod]
    public void Dist_CalculatesCorrectDistance()
    {
        var p1 = new Point2f(0, 0);
        var p2 = new Point2f(3, 4);

        double distance = DocumentContourExtractor.Dist(p1, p2);

        Assert.AreEqual(5.0, distance, 0.001, "Distance should be 5 (3-4-5 triangle)");
    }

    [TestMethod]
    public void Dist_WithSamePoint_ReturnsZero()
    {
        var p = new Point2f(100, 100);

        double distance = DocumentContourExtractor.Dist(p, p);

        Assert.AreEqual(0.0, distance, 0.001, "Distance to same point should be 0");
    }

    [TestMethod]
    public void TryExtractDocument_QuadCornersAreOrdered()
    {
        using var probMask = new Mat(400, 300, MatType.CV_32FC1, new Scalar(0.0));
        Cv2.Rectangle(probMask, new Rect(50, 60, 180, 240), new Scalar(0.9), -1);

        bool result = DocumentContourExtractor.TryExtractDocument(
            probMask, out var docMask, out var quad, out _, out _);
        using var maskOwned = docMask;

        Assert.IsTrue(result);
        Assert.HasCount(4, quad);

        // Verify ordering: TL, TR, BR, BL
        Assert.IsLessThan(quad[1].X, quad[0].X, "TL should be left of TR");
        Assert.IsLessThan(quad[3].Y, quad[0].Y, "TL should be above BL");
        Assert.IsLessThan(quad[2].Y, quad[1].Y, "TR should be above BR");
        Assert.IsGreaterThan(quad[3].X, quad[2].X, "BR should be right of BL");
    }

    [TestMethod]
    public void TryExtractDocument_CustomAreaThresholds_AreRespected()
    {
        using var probMask = new Mat(400, 300, MatType.CV_32FC1, new Scalar(0.0));

        // Create a region that covers 15% of image
        Cv2.Rectangle(probMask, new Rect(50, 50, 150, 120), new Scalar(0.9), -1);

        // Should pass with default thresholds (8% min, 98% max)
        bool result1 = DocumentContourExtractor.TryExtractDocument(
            probMask, out var mask1, out _, out _, out _, minAreaPercent: 8, maxAreaPercent: 98);
        using var m1 = mask1;
        Assert.IsTrue(result1, "Should pass with default thresholds");

        // Should fail with stricter min threshold
        bool result2 = DocumentContourExtractor.TryExtractDocument(
            probMask, out var mask2, out _, out _, out _, minAreaPercent: 20, maxAreaPercent: 98);
        using var m2 = mask2;
        Assert.IsFalse(result2, "Should fail with min threshold above coverage");
    }
}
