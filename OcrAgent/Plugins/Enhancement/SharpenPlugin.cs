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

    /// <summary>
    /// Creates the plugin with the image store used to load inputs and save outputs.
    /// </summary>
    /// <param name="store">Image storage implementation.</param>
    public SharpenPlugin(IImageStore store)
    {
        _store = store;
    }

    /// <summary>
    /// Sharpens with unsharp mask or Laplacian; optional adaptive mode picks strength from blur variance.
    /// </summary>
    /// <param name="imageReference">Input image reference.</param>
    /// <param name="strength">low, medium, or high.</param>
    /// <param name="method">unsharp or laplacian.</param>
    /// <param name="adaptiveStrength">When true, overrides strength from detected blur.</param>
    /// <returns>Reference of the saved sharpened image.</returns>
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
        using var src = Cv2.ImDecode(File.ReadAllBytes(inputPath), ImreadModes.Color);

        if (src.Empty())
            throw new InvalidOperationException($"Could not read image for reference {imageReference} from {inputPath}");

        // Detect blur level if adaptive
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

        Cv2.ImEncode(".png", sharpened, out var pngBytes);

        var meta = new Dictionary<string, string>
        {
            ["parent"] = imageReference,
            ["op"] = "sharpen",
            ["strength"] = strength,
            ["method"] = method,
            ["adaptiveStrength"] = adaptiveStrength.ToString(),
            ["blurDetected"] = detectedBlur
        };

        var baseRef = $"{imageReference}_sharpen_{strength}";
        var desiredRef = PluginHelpers.MakeUniqueRef(_store, baseRef);

        var newRef = _store.SaveBytes(pngBytes, "png", meta, desiredRef);
        return Task.FromResult(newRef);
    }

    private void ApplyUnsharpMask(Mat src, Mat dst, string strength)
    {
        // unsharp masking

        var (kernelSize, sigma, amount) = strength switch
        {
            "low" => (3, 1.0, 0.5),
            "medium" => (3, 1.0, 1.0),
            "high" => (5, 1.5, 1.5),
            _ => (3, 1.0, 1.0)
        };

        // blurred
        using var blurred = new Mat();
        Cv2.GaussianBlur(src, blurred, new Size(kernelSize, kernelSize), sigma);

        // unsharp mask: src - blurred
        using var unsharp = new Mat();
        Cv2.Subtract(src, blurred, unsharp);

        // weighted unsharp mask
        using var sharpened = new Mat();
        Cv2.AddWeighted(src, 1.0, unsharp, amount, 0, sharpened);

        sharpened.CopyTo(dst);
    }

    private void ApplyLaplacianSharpening(Mat src, Mat dst, string strength)
    {
        // Laplacian-based sharpening

        double amount = strength switch
        {
            "low" => 0.5,
            "medium" => 1.0,
            "high" => 1.5,
            _ => 1.0
        };

        // float
        using var srcFloat = new Mat();
        src.ConvertTo(srcFloat, MatType.CV_32F);

        // apply Laplacian
        using var laplacian = new Mat();
        Cv2.Laplacian(srcFloat, laplacian, MatType.CV_32F, ksize: 3);

        // weighted Laplacian
        using var sharpened = new Mat();
        Cv2.AddWeighted(srcFloat, 1.0, laplacian, amount, 0, sharpened);

        // convert back to 8-bit
        sharpened.ConvertTo(dst, MatType.CV_8U);
    }

    private double DetectBlurLevel(Mat src)
    {
        // Laplacian variance
        // Lower variance = more blur
        using var gray = new Mat();
        Cv2.CvtColor(src, gray, ColorConversionCodes.BGR2GRAY);

        using var laplacian = new Mat();
        Cv2.Laplacian(gray, laplacian, MatType.CV_64F);

        Cv2.MeanStdDev(laplacian, out var mean, out var stddev);

        // variance
        double variance = stddev.Val0 * stddev.Val0;
        return variance;
    }

    private string CategorizeBlur(double blurLevel)
    {
        // thresholds
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
            return "low";   // moderate sharp
        else if (blurLevel > 100)
            return "medium"; // moderate blur
        else
            return "high";   // very blurry
    }

    /// <summary>
    /// Registers sharpening as an AI-callable tool.
    /// </summary>
    /// <returns>Tool bindings for this plugin.</returns>
    public IEnumerable<AITool> GetAITools()
    {
        yield return AIFunctionFactory.Create(this.SharpenImage);
    }
}
