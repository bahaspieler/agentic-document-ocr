using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using OcrAgent.Storage;

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

        // Placeholder: real image processing and saving will be added in the next commits
        return Task.FromResult(imageReference);
    }

    public IEnumerable<AITool> GetAITools()
    {
        // We'll expose the tool in a later commit
        yield break;
    }
}
