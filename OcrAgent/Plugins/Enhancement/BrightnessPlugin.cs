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

    [Description("Adjusts the brightness of the given image.")]
    public Task<string> AdjustBrightness(
        [Description("The reference number of the image to adjust.")] string imageReference)
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