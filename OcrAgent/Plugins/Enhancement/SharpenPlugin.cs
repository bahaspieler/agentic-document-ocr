using OpenCvSharp;
using System.ComponentModel;
using Microsoft.Extensions.AI;
using OcrAgent.Storage;

namespace OcrAgent.Plugins.Enhancement;

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
            throw new InvalidOperationException($"Could not read image for reference {imageReference} from {inputPath}");

        // Still stub: real processing comes next commits
        return Task.FromResult(imageReference);
    }

    public IEnumerable<AITool> GetAITools()
    {
        yield return AIFunctionFactory.Create(this.SharpenImage);
    }
}
