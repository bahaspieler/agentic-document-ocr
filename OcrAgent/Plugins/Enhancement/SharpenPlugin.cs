using Microsoft.Extensions.AI;
using System.ComponentModel;
using OcrAgent.Storage;
using OpenCvSharp;

namespace OcrAgent.Plugins.Enhancement;

/// <summary>
/// Plugin for sharpening images to enhance text edges and reduce blur.
/// Particularly useful for blurry documents.
/// </summary>
public sealed class SharpenPlugin : IImageProcessingTool
{
    private readonly IImageStore _store;

    public SharpenPlugin(IImageStore store)
    {
        _store = store;
    }

    [Description("Enhances text edges and reduces blur. Use 'low' for clean scans, 'medium' for mobile captures, 'high' for very blurry receipts. Returns sharpened image reference.")]
    public Task<string> SharpenImage(
        [Description("The reference number of the image to sharpen.")] string imageReference,
        [Description("Sharpening strength: 'low' (clean scans), 'medium' (mobile captures), 'high' (very blurry receipts).")] string strength = "medium",
        [Description("Sharpening method: 'unsharp' (gentle, preserves structure) or 'laplacian' (stronger, for very blurry images).")] string method = "unsharp",
        [Description("Auto-adjust strength based on detected blur level.")] bool adaptiveStrength = false)
    {
        strength = strength.ToLowerInvariant();
        method = method.ToLowerInvariant();

        if (strength != "low" && strength != "medium" && strength != "high")
            throw new ArgumentException("strength must be 'low', 'medium', or 'high'");

        if (method != "unsharp" && method != "laplacian")
            throw new ArgumentException("method must be 'unsharp' or 'laplacian'");

        var inputPath = _store.GetImagePath(imageReference);
        using var src = Cv2.ImRead(inputPath, ImreadModes.Color);

        if (src.Empty())
            throw new InvalidOperationException(
                $"Could not read image for reference {imageReference} from {inputPath}");

        // Detect blur and adapt strength if requested
        string detectedBlur = "unknown";
        if (adaptiveStrength)
        {
            double blurLevel = DetectBlurLevel(src);
            detectedBlur = CategorizeBlur(blurLevel);
            strength = AdjustStrengthForBlur(blurLevel);
        }

        using var sharpened = new Mat();

        if (method == "unsharp")
        {
            ApplyUnsharpMask(src, sharpened, strength);
        }
        else
        {
            ApplyLaplacianSharpening(src, sharpened, strength);
        }

        // Encode result
        Cv2.ImEncode(".png", sharpened, out var pngBytes);

        // Metadata
        var meta = new Dictionary<string, string>
        {
            ["parent"] = imageReference,
            ["op"] = "sharpen",
            ["strength"] = strength,
            ["method"] = method,
            ["adaptiveStrength"] = adaptiveStrength.ToString(),
            ["blurDetected"] = detectedBlur
        };

        // Save with unique reference
        var baseRef = $"{imageReference}_sharpen_{strength}";
        var desiredRef = PluginHelpers.MakeUniqueRef(_store, baseRef);

        var newRef = _store.SaveBytes(pngBytes, "png", meta, desiredRef);
        return Task.FromResult(newRef);
    }

    /// <summary>
    /// Unsharp masking: gentle sharpening that preserves structure.
    /// </summary>
    private void ApplyUnsharpMask(Mat src, Mat dst, string strength)
    {
        var (kernelSize, sigma, amount) = strength switch
        {
            "low" => (3, 1.0, 0.5),
            "medium" => (3, 1.0, 1.0),
            "high" => (5, 1.5, 1.5),
            _ => (3, 1.0, 1.0)
        };

        using var blurred = new Mat();
        Cv2.GaussianBlur(src, blurred, new Size(kernelSize, kernelSize), sigma);

        using var unsharp = new Mat();
        Cv2.Subtract(src, blurred, unsharp);

        using var sharpened = new Mat();
        Cv2.AddWeighted(src, 1.0, unsharp, amount, 0, sharpened);

        sharpened.CopyTo(dst);
    }

    /// <summary>
    /// Laplacian-based sharpening for stronger edge enhancement.
    /// </summary>
    private void ApplyLaplacianSharpening(Mat src, Mat dst, string strength)
    {
        double amount = strength switch
        {
            "low" => 0.5,
            "medium" => 1.0,
            "high" => 1.5,
            _ => 1.0
        };

        using var srcFloat = new Mat();
        src.ConvertTo(srcFloat, MatType.CV_32F);

        using var laplacian = new Mat();
        Cv2.Laplacian(srcFloat, laplacian, MatType.CV_32F, ksize: 3);

        using var sharpened = new Mat();
        Cv2.AddWeighted(srcFloat, 1.0, laplacian, amount, 0, sharpened);

        sharpened.ConvertTo(dst, MatType.CV_8U);
    }

    /// <summary>
    /// Detects blur using Laplacian variance.
    /// Lower variance means more blur.
    /// </summary>
    private double DetectBlurLevel(Mat src)
    {
        using var gray = new Mat();
        Cv2.CvtColor(src, gray, ColorConversionCodes.BGR2GRAY);

        using var laplacian = new Mat();
        Cv2.Laplacian(gray, laplacian, MatType.CV_64F);

        Cv2.MeanStdDev(laplacian, out _, out var stddev);
        return stddev.Val0 * stddev.Val0;
    }

    private string CategorizeBlur(double blurLevel)
    {
        if (blurLevel > 500)
            return "sharp";
        else if (blurLevel > 100)
            return "moderate";
        else
            return "blurry";
    }

    private string AdjustStrengthForBlur(double blurLevel)
    {
        if (blurLevel > 500)
            return "low";
        else if (blurLevel > 100)
            return "medium";
        else
            return "high";
    }

    public IEnumerable<AITool> GetAITools()
    {
        yield return AIFunctionFactory.Create(this.SharpenImage);
    }
}
