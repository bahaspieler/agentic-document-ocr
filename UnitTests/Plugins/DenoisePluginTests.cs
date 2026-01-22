using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using OcrAgent.Storage;
using OpenCvSharp;

namespace OcrAgent.Plugins.Enhancement;

/// <summary>
/// Plugin for removing noise from images using various denoising methods.
/// Supports median blur, bilateral filtering, and Non-Local Means denoising.
/// </summary>
public sealed class DenoisePlugin : IImageProcessingTool
{
    private readonly IImageStore _store;

    public DenoisePlugin(IImageStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    [System.ComponentModel.Description("Removes noise from the given image using the specified method. Returns the reference of the generated image.")]
    public Task<string> DenoiseImage(
        [System.ComponentModel.Description("The reference number of the image to denoise.")] string imageReference,
        [System.ComponentModel.Description("The denoising method: 'median', 'bilateral', or 'nlm' (Non-Local Means).")] string method = "bilateral",
        [System.ComponentModel.Description("The denoising strength: 'low', 'medium', or 'high'. For NLM, this is the filter strength.")] string strength = "medium")
    {
        method = method.ToLowerInvariant();
        strength = strength.ToLowerInvariant();

        if (method != "median" && method != "bilateral" && method != "nlm")
            throw new ArgumentException("method must be 'median', 'bilateral', or 'nlm'");

        if (strength != "low" && strength != "medium" && strength != "high")
            throw new ArgumentException("strength must be 'low', 'medium', or 'high'");

        // 1) Load image from store
        var inputPath = _store.GetImagePath(imageReference);
        using var src = Cv2.ImRead(inputPath, ImreadModes.Color);

        if (src.Empty())
            throw new InvalidOperationException($"Could not read image for reference {imageReference} from {inputPath}");

        // 2) Denoise
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

        // 3) Encode as PNG
        Cv2.ImEncode(".png", denoised, out var pngBytes);

        // 4) Metadata
        var meta = new Dictionary<string, string>
        {
            ["parent"] = imageReference,
            ["op"] = "denoise",
            ["method"] = method,
            ["strength"] = strength
        };

        // 5) Unique output reference (no PluginHelpers dependency)
        var baseRef = $"{imageReference}_denoise_{method}_{strength}";
        var desiredRef = $"{baseRef}_{Guid.NewGuid():N}";

        var newRef = _store.SaveBytes(pngBytes, "png", meta, desiredRef);
        return Task.FromResult(newRef);
    }

    private static void ApplyMedianBlur(Mat src, Mat dst, string strength)
    {
        int ksize = strength switch
        {
            "low" => 3,
            "medium" => 5,
            "high" => 7,
            _ => 5
        };

        Cv2.MedianBlur(src, dst, ksize);
    }

    private static void ApplyBilateralFilter(Mat src, Mat dst, string strength)
    {
        int d = strength switch
        {
            "low" => 5,
            "medium" => 9,
            "high" => 15,
            _ => 9
        };

        double sigmaColor = strength switch
        {
            "low" => 50,
            "medium" => 75,
            "high" => 100,
            _ => 75
        };

        double sigmaSpace = sigmaColor;

        Cv2.BilateralFilter(src, dst, d, sigmaColor, sigmaSpace);
    }

    private static void ApplyNonLocalMeans(Mat src, Mat dst, string strength)
    {
        float h = strength switch
        {
            "low" => 3.0f,
            "medium" => 7.0f,
            "high" => 10.0f,
            _ => 7.0f
        };

        Cv2.FastNlMeansDenoisingColored(src, dst, h, h, 7, 21);
    }

    public IEnumerable<AITool> GetAITools()
    {
        yield return AIFunctionFactory.Create(this.DenoiseImage);
    }
}
