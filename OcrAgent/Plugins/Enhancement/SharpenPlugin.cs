using Microsoft.Extensions.AI;
using System.ComponentModel;
using OcrAgent.Storage;

namespace OcrAgent.Plugins.Enhancement;

/// <summary>
/// Plugin for sharpening images to enhance text edges and reduce blur.
/// Particularly useful for blurry documents.
/// </summary>
public sealed class SharpenPlugin : IImageProcessingTool
{
    private readonly IImageStore _store;

    public SharpenPlugin(IImageStore store)
    {
        _store = store;
    }

    // Temporary stub, will implement step-by-step in next commits.
    [Description("Enhances text edges and reduces blur. Returns sharpened image reference.")]
    public Task<string> SharpenImage(string imageReference)
    {
        return Task.FromResult(imageReference);
    }

    public IEnumerable<AITool> GetAITools()
    {
        yield return AIFunctionFactory.Create(this.SharpenImage);
    }
}
