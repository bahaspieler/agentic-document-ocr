using Microsoft.Extensions.AI;
using System.ComponentModel;
using OcrAgent.Storage;

namespace OcrAgent.Plugins.Enhancement;

public sealed class BrightnessPlugin : IImageProcessingTool
{
    private readonly IImageStore _store;

    public BrightnessPlugin(IImageStore store)
    {
        _store = store;
    }

    [Description("Adjusts the brightness of the given image. It returns the comma-separated list of references of generated images.")]
    public Task<string> AdjustBrightness(
        [Description("The reference number of the image to adjust.")] string imageReference,
        [Description("The starting brightness adjustment (-100 to +100).")] int startBrightness,
        [Description("The ending brightness adjustment (-100 to +100).")] int endBrightness,
        [Description("The brightness adjustment step.")] int step)
    {
        // Validation

        // Data Loading
        
        // Image Processing
        
        // Save image with proper reference

        // Return the reference
        return Task.FromResult(string.Empty);
    }

    public IEnumerable<AITool> GetAITools()
    {
        yield return AIFunctionFactory.Create(this.AdjustBrightness);
    }
}