using System;
using System.Collections.Generic;
using System.ComponentModel;
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
        _store = store;
    }

    [Description("Removes noise from the given image using the specified method. Returns the reference of the generated image.")]
    public Task<string> DenoiseImage(
        [Description("The reference number of the image to denoise.")] string imageReference,
        [Description("The denoising method: 'median', 'bilateral', or 'nlm' (Non-Local Means).")] string method = "bilateral",
        [Description("The denoising strength: 'low' (3), 'medium' (5), or 'high' (7). For NLM, this is the filter strength.")] string strength = "medium")
    {
        method = method.ToLowerInvariant();
        strength = strength.ToLowerInvariant();

        if (method != "median" && method != "bilateral" && method != "nlm")
            throw new ArgumentException("method must be 'median', 'bilateral', or 'nlm'");

        if (strength != "low" && strength != "medium" && strength != "high")
            throw new ArgumentException("strength must be 'low', 'medium', or 'high'");

        var inputPath = _store.GetImagePath(imageReference);
        using var src = Cv2.ImRead(inputPath, ImreadModes.Color);

        if (src.Empty())
            throw new InvalidOperationException($"Could not read image for reference {imageReference} from {inputPath}");

        // Still not wired to methods yet (next commit)
        using var denoised = src.Clone();

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

    private void ApplyMedianBlur(Mat src, Mat dst, string strength)
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

    private void ApplyBilateralFilter(Mat src, Mat dst, string strength)
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

        double sigmaSpace = strength switch
        {
            "low" => 50,
            "medium" => 75,
            "high" => 100,
            _ => 75
        };

        Cv2.BilateralFilter(src, dst, d, sigmaColor, sigmaSpace);
    }

    private void ApplyNonLocalMeans(Mat src, Mat dst, string strength)
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
