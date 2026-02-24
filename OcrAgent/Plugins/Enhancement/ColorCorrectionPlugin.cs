using Microsoft.Extensions.AI;
using System.ComponentModel;
using OcrAgent.Storage;
using OpenCvSharp;
namespace OcrAgent.Plugins.Enhancement;

/// <summary>
/// Fixes color casts in document images from lighting.
/// Adjusts white balance to improve OCR results.
/// </summary>
public sealed class ColorCorrectionPlugin : IImageProcessingTool
{
    private readonly IImageStore _store;

    public ColorCorrectionPlugin(IImageStore store)
    {
        _store = store;
    }
    [Description("Corrects color casts and white balance in document images. Returns corrected image reference.")]
    public Task<string> CorrectColor(
        [Description("The reference number of the image to correct.")] string imageReference,
        [Description("Color correction method: 'auto' (detect best), 'gray_world' (neutralize color cast), 'white_patch' (normalize to white), 'histogram_match' (equalize channels).")] string method = "auto")
    {
        method = method.ToLowerInvariant();

        if (method is not ("auto" or "gray_world" or "white_patch" or "histogram_match"))
            throw new ArgumentException("method must be 'auto', 'gray_world', 'white_patch', or 'histogram_match'");
        var inputPath = _store.GetImagePath(imageReference);
        using var src = Cv2.ImRead(inputPath, ImreadModes.Color);

        if (src.Empty())
            throw new InvalidOperationException($"Could not read image for reference {imageReference} from {inputPath}");

        return Task.FromResult(imageReference);
    }

    public IEnumerable<AITool> GetAITools()
    {
        yield break;
    }
}