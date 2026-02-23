using Microsoft.Extensions.AI;
using System.ComponentModel;
using OcrAgent.Storage;

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

    public IEnumerable<AITool> GetAITools()
    {
        yield break;
    }
}