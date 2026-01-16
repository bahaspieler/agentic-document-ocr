using Microsoft.Extensions.AI;
using System.ComponentModel;
using OcrAgent.Storage;

namespace OcrAgent.Plugins.Enhancement;

public sealed class DenoisePlugin : IImageProcessingTool
{
    private readonly IImageStore _store;

    public DenoisePlugin(IImageStore store)
    {
        _store = store;
    }

    [Description("Removes noise")]
    public Task<string> DenoiseImage(
        [Description("The reference number of the image to denoise.")] string imageReference)
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
        yield return AIFunctionFactory.Create(this.DenoiseImage);
    }
}
