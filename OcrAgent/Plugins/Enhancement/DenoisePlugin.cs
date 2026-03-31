using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.AI;
using OcrAgent.Storage;
using OcrAgent.Plugins.Analysis;
using OpenCvSharp;

namespace OcrAgent.Plugins.Enhancement;

/// <summary>
/// Plugin for removing noise from images using various denoising methods.
/// Supports median blur, bilateral filtering, and Non-Local Means denoising.
/// </summary>
public sealed class DenoisePlugin : IImageProcessingTool
{
    private readonly IImageStore _store;

    /// <summary>
    /// Creates the plugin with the image store used to load inputs and save outputs.
    /// </summary>
    /// <param name="store">Image storage implementation.</param>
    public DenoisePlugin(IImageStore store)
    {
        _store = store;
    }

    /// <summary>
    /// Runs median, bilateral, NLM, or auto mode; auto may return the input reference when noise is low.
    /// </summary>
    /// <param name="imageReference">Input image reference.</param>
    /// <param name="method">auto, median, bilateral, or nlm.</param>
    /// <param name="strength">low, medium, or high; ignored for auto.</param>
    /// <param name="qualityProfile">Optional JSON with noiseSigma for auto mode.</param>
    /// <returns>New denoised image reference, or unchanged input in auto when skipped.</returns>
    [Description("Removes noise from the given image using the specified method. 'auto' analyzes the image and selects the best method/strength automatically, skipping if noise is negligible. Returns the reference of the generated image.")]
    public Task<string> DenoiseImage(
        [Description("The reference number of the image to denoise.")] string imageReference,
        [Description("The denoising method: 'auto' (recommended), 'median', 'bilateral', or 'nlm' (Non-Local Means).")] string method = "bilateral",
        [Description("The denoising strength: 'low', 'medium', or 'high'. Ignored when method is 'auto'.")] string strength = "medium",
        [Description("Optional quality profile JSON from AssessQuality. When provided in auto mode, uses pre-computed noise data instead of re-analyzing.")] string? qualityProfile = null)
    {
        method = method.ToLowerInvariant();
        strength = strength.ToLowerInvariant();

        if (method != "auto" && method != "median" && method != "bilateral" && method != "nlm")
            throw new ArgumentException("method must be 'auto', 'median', 'bilateral', or 'nlm'");

        if (method != "auto" && strength != "low" && strength != "medium" && strength != "high")
            throw new ArgumentException("strength must be 'low', 'medium', or 'high'");

        var inputPath = _store.GetImagePath(imageReference);
        using var src = Cv2.ImRead(inputPath, ImreadModes.Color);

        if (src.Empty())
            throw new InvalidOperationException($"Could not read image for reference {imageReference} from {inputPath}");

        if (method == "auto")
        {
            double sigma = GetNoiseSigma(src, qualityProfile);

            if (sigma < 5)
                return Task.FromResult(imageReference);

            if (sigma < 15) { method = "bilateral"; strength = "low"; }
            else if (sigma < 30) { method = "bilateral"; strength = "medium"; }
            else { method = "nlm"; strength = "high"; }
        }

        using var denoised = new Mat();
        switch (method)
        {
            case "median":
                ApplyMedianBlur(src, denoised, strength);
                break;
            case "bilateral":
                ApplyBilateralFilter(src, denoised, strength);
                break;
            case "nlm":
                ApplyNonLocalMeans(src, denoised, strength);
                break;
        }

        Cv2.ImEncode(".png", denoised, out var pngBytes);

        var meta = new Dictionary<string, string>
        {
            ["parent"] = imageReference,
            ["op"] = "denoise",
            ["method"] = method,
            ["strength"] = strength
        };

        var baseRef = $"{imageReference}_denoise_{method}_{strength}";
        var desiredRef = PluginHelpers.MakeUniqueRef(_store, baseRef);
        var newRef = _store.SaveBytes(pngBytes, "png", meta, desiredRef);
        return Task.FromResult(newRef);
    }

    private static double GetNoiseSigma(Mat src, string? qualityProfile)
    {
        if (!string.IsNullOrEmpty(qualityProfile))
        {
            try
            {
                using var doc = JsonDocument.Parse(qualityProfile);
                if (doc.RootElement.TryGetProperty("noiseSigma", out var ns))
                    return ns.GetDouble();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WARN] quality profile parse (noiseSigma): {ex.Message}");
            }
        }

        using var gray = new Mat();
        Cv2.CvtColor(src, gray, ColorConversionCodes.BGR2GRAY);
        return QualityAssessmentPlugin.EstimateNoiseSigma(gray);
    }

    private static void ApplyMedianBlur(Mat src, Mat dst, string strength)
    {
        int ksize = strength switch { "low" => 3, "high" => 7, _ => 5 };
        Cv2.MedianBlur(src, dst, ksize);
    }

    private static void ApplyBilateralFilter(Mat src, Mat dst, string strength)
    {
        int d = strength switch { "low" => 5, "high" => 15, _ => 9 };
        double sigma = strength switch { "low" => 50, "high" => 100, _ => 75 };
        Cv2.BilateralFilter(src, dst, d, sigma, sigma);
    }

    private static void ApplyNonLocalMeans(Mat src, Mat dst, string strength)
    {
        float h = strength switch { "low" => 3.0f, "high" => 10.0f, _ => 7.0f };
        Cv2.FastNlMeansDenoisingColored(src, dst, h, h, 7, 21);
    }

    /// <summary>
    /// Registers denoising as an AI-callable tool.
    /// </summary>
    /// <returns>Tool bindings for this plugin.</returns>
    public IEnumerable<AITool> GetAITools()
    {
        yield return AIFunctionFactory.Create(this.DenoiseImage);
    }
}
