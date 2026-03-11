using Microsoft.VisualStudio.TestTools.UnitTesting;
using OcrAgent.Inference;
using OpenCvSharp;

namespace UnitTests.Inference;

/// <summary>
/// Tests for U2NetpOnnxPredictor. These tests verify graceful degradation
/// when the model file is missing, as well as basic functionality when present.
/// </summary>
[TestClass]
public sealed class U2NetpOnnxPredictorTests
{
    private string _tempDir = null!;

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "OcrAgentU2NetTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
            // ignore cleanup failures
        }
    }

    [TestMethod]
    public void TryCreateDefault_WhenModelPresent_ReturnsPredictor()
    {
        var predictor = U2NetpOnnxPredictor.TryCreateDefault();
        
        if (predictor == null)
        {
            Assert.Inconclusive("U2NETP model not available - this is expected if model file is not present");
            return;
        }
        
        // If we get here, model is available
        Assert.IsNotNull(predictor, "Should return predictor when model is present");
        predictor.Dispose();
    }

    [TestMethod]
    public void FromModelPath_WhenPathEmpty_ThrowsArgumentException()
    {
        bool thrown = false;
        try
        {
            U2NetpOnnxPredictor.FromModelPath("");
        }
        catch (ArgumentException)
        {
            thrown = true;
        }
        Assert.IsTrue(thrown, "Should throw ArgumentException for empty path");
    }

    [TestMethod]
    public void FromModelPath_WhenFileNotFound_ThrowsFileNotFoundException()
    {
        var nonExistentPath = Path.Combine(_tempDir, "nonexistent.onnx");
        
        bool thrown = false;
        try
        {
            U2NetpOnnxPredictor.FromModelPath(nonExistentPath);
        }
        catch (FileNotFoundException)
        {
            thrown = true;
        }
        Assert.IsTrue(thrown, "Should throw FileNotFoundException for missing file");
    }

    [TestMethod]
    public void PredictProbabilityMask_WhenImageEmpty_ThrowsArgumentException()
    {
        var predictor = U2NetpOnnxPredictor.TryCreateDefault();
        if (predictor == null)
        {
            Assert.Inconclusive("U2NETP model not available, skipping test");
            return;
        }

        using var emptyMat = new Mat();
        
        bool thrown = false;
        try
        {
            predictor.PredictProbabilityMask(emptyMat);
        }
        catch (ArgumentException)
        {
            thrown = true;
        }
        
        predictor.Dispose();
        Assert.IsTrue(thrown, "Should throw ArgumentException for empty image");
    }

    [TestMethod]
    public void PredictProbabilityMask_WithValidImage_ReturnsValidMask()
    {
        var predictor = U2NetpOnnxPredictor.TryCreateDefault();
        if (predictor == null)
        {
            Assert.Inconclusive("U2NETP model not available, skipping test");
            return;
        }

        // Create a simple test image (white document on dark background)
        using var testImg = new Mat(400, 300, MatType.CV_8UC3, new Scalar(40, 40, 40));
        Cv2.Rectangle(testImg, new Rect(50, 50, 200, 200), new Scalar(250, 250, 250), -1);

        using var mask = predictor.PredictProbabilityMask(testImg);

        Assert.IsNotNull(mask, "Mask should not be null");
        Assert.IsFalse(mask.Empty(), "Mask should not be empty");
        Assert.AreEqual(MatType.CV_32FC1, mask.Type(), "Mask should be float single-channel");
        Assert.AreEqual(testImg.Width, mask.Width, "Mask width should match input");
        Assert.AreEqual(testImg.Height, mask.Height, "Mask height should match input");

        // Verify values are in [0,1] range
        Cv2.MinMaxLoc(mask, out double minVal, out double maxVal);
        Assert.IsTrue(minVal >= 0.0 && minVal <= 1.0, $"Min value should be in [0,1], got {minVal}");
        Assert.IsTrue(maxVal >= 0.0 && maxVal <= 1.0, $"Max value should be in [0,1], got {maxVal}");

        predictor.Dispose();
    }

    [TestMethod]
    public void PredictProbabilityMask_OutputDimensions_MatchInput()
    {
        var predictor = U2NetpOnnxPredictor.TryCreateDefault();
        if (predictor == null)
        {
            Assert.Inconclusive("U2NETP model not available, skipping test");
            return;
        }

        var testSizes = new[] { 
            (width: 100, height: 100),
            (width: 640, height: 480),
            (width: 800, height: 600),
            (width: 1024, height: 768)
        };

        foreach (var (width, height) in testSizes)
        {
            using var testImg = new Mat(height, width, MatType.CV_8UC3, new Scalar(128, 128, 128));
            using var mask = predictor.PredictProbabilityMask(testImg);

            Assert.AreEqual(width, mask.Width, $"Mask width should match input for {width}x{height}");
            Assert.AreEqual(height, mask.Height, $"Mask height should match input for {width}x{height}");
        }

        predictor.Dispose();
    }

    [TestMethod]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        var predictor = U2NetpOnnxPredictor.TryCreateDefault();
        if (predictor == null)
        {
            Assert.Inconclusive("U2NETP model not available, skipping test");
            return;
        }

        predictor.Dispose();
        predictor.Dispose(); // Should not throw
    }

    [TestMethod]
    public void InputSize_IsCorrect()
    {
        Assert.AreEqual(320, U2NetpOnnxPredictor.InputSize, 
            "InputSize should be 320 (U2NETP expects 320x320)");
    }
}
