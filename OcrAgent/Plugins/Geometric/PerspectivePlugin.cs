using Microsoft.Extensions.AI;
using System.ComponentModel;
using OcrAgent.Storage;
using OpenCvSharp;
using System.Globalization;

namespace OcrAgent.Plugins.Geometric;

/// <summary>
/// Auto perspective correction for documents/receipts.
/// </summary>
public sealed class PerspectivePlugin : IImageProcessingTool
{
    private readonly IImageStore _store;
    
    private const int MaxSearchContours = 15;
    private const double MinAreaRatio = 0.10;
    private const double MaxAreaRatio = 0.995;

    public PerspectivePlugin(IImageStore store)
    {
        _store = store;
    }

    [Description("Auto-detects document boundaries and corrects perspective distortion. Returns corrected image reference.")]
    public Task<string> CorrectPerspective(
        [Description("The reference number of the image to correct.")] string imageReference,
        [Description("Detection sensitivity: 'auto' (recommended), 'aggressive', 'conservative'.")] string sensitivity = "auto",
        [Description("Target output width in pixels (0 = auto).")] int targetWidth = 0,
        [Description("Target output height in pixels (0 = auto).")] int targetHeight = 0,
        [Description("Add margin padding around detected document.")] int marginPercent = 0,
        [Description("Enhance edges before detection.")] bool enhanceEdges = true)
    {
        sensitivity = sensitivity.ToLowerInvariant();
        if (sensitivity is not ("auto" or "aggressive" or "conservative"))
            throw new ArgumentException("sensitivity must be 'auto', 'aggressive', or 'conservative'");

        if (marginPercent < 0 || marginPercent > 20)
            throw new ArgumentException("marginPercent must be between 0 and 20");

        var inputPath = _store.GetImagePath(imageReference);
        using var src = Cv2.ImRead(inputPath, ImreadModes.Color);
        if (src.Empty())
            throw new InvalidOperationException($"Could not read image for reference {imageReference} from {inputPath}");

        
        return Task.FromResult(string.Empty);
    }
    
    public IEnumerable<AITool> GetAITools()
    {
        yield return AIFunctionFactory.Create(this.CorrectPerspective);
    }
}
