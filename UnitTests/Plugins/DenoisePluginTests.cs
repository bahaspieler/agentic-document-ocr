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
        _store = store;
    }

    [System.ComponentModel.Description("Removes noise from the given image using the specified method. Returns the reference of the generated image.")]
    public Task<string> DenoiseImage(
        [System.ComponentModel.Description("The reference number of the image to denoise.")] string imageReference,
        [System.ComponentModel.Description("The denoising method: 'median', 'bilateral', or 'nlm' (Non-Local Means).")] string method = "bilateral",
        [System.ComponentModel.Description("The denoising strength: 'low' (3), 'medium' (5), or 'high' (7). For NLM, this is the filter strength.")] string strength = "medium")
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

        // 2) Placeholder output (real denoising comes in commit 3)
        using var denoised = src.Clone();

        // 3) Encode as PNG
        Cv2.ImEncode(".png", denoised, out var pngBytes);

        // 4) Metadata for traceability
        var meta = new Dictionary<string, string>
        {
            ["parent"] = imageReference,
            ["op"] = "denoise",
            ["method"] = method,
            ["strength"] = strength
        };

        // 5) Create a unique reference and save
        var baseRef = $"{imageReference}_denoise_{method}_{strength}";
        var desiredRef = PluginHelpers.MakeUniqueRef(_store, baseRef);

        var newRef = _store.SaveBytes(pngBytes, "png", meta, desiredRef);
        return Task.FromResult(newRef);
    }

    // Expose this method as an AI tool
    public IEnumerable<AITool> GetAITools()
    {
        yield return AIFunctionFactory.Create(this.DenoiseImage);
    }
}
