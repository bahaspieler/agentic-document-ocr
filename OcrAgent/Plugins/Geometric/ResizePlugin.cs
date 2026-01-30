using Microsoft.Extensions.AI;
using System.ComponentModel;
using OcrAgent.Storage;

namespace OcrAgent.Plugins.Geometric;


public sealed class ResizePlugin : IImageProcessingTool
{
    private readonly IImageStore _store;

    public ResizePlugin(IImageStore store)
    {
        _store = store;
    }

    [Description("Resizes image to normalize dimensions. Use targetDPI for document standardization (300 recommended for OCR). Can specify pixels or use document type presets.")]
    public Task<string> ResizeImage(
        [Description("The reference number of the image to resize.")] string imageReference,
        [Description("Target width in pixels (0 to auto-calculate from height/aspect ratio).")] int targetWidth = 0,
        [Description("Target height in pixels (0 to auto-calculate from width/aspect ratio).")] int targetHeight = 0,
        [Description("Target DPI for document (e.g., 300 for high-quality OCR, 200 for receipts). Overrides width/height if > 0.")] int targetDPI = 0,
        [Description("Maintain aspect ratio when one dimension is 0 (recommended: true).")] bool maintainAspectRatio = true,
        [Description("Interpolation method: 'linear' (fast), 'cubic' (balanced), 'lanczos4' (for upscaling).")] string interpolation = "lanczos4",
        [Description("Document type preset: 'auto', 'receipt', 'form', 'statement', 'invoice'. Overrides other dimensions if specified.")] string documentType = "auto")
    {
        throw new NotImplementedException();
    }

    public IEnumerable<AITool> GetAITools()
    {
        yield return AIFunctionFactory.Create(
            (Func<string, int, int, int, bool, string, string, Task<string>>)this.ResizeImage
        );
    }
}
