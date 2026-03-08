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
        Assert.IsTrue(thrown, "Should throw FileNotFoundException for missing files");
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
}