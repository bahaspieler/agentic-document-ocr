using Microsoft.Extensions.AI;
using System.ComponentModel;
using OcrAgent.Storage;
using OpenCvSharp;

namespace OcrAgent.Plugins.Segmentation;

public sealed class BinarizePlugin : IImageProcessingTool
{
    private readonly IImageStore _store;

    public BinarizePlugin(IImageStore store)
    {
        _store = store;
    }

    [Description("Converts image to binary (black/white) for OCR.")]
    public Task<string> BinarizeImage(
        [Description("The reference number")] string imageReference
        )
    {
        return Task.FromResult(string.Empty);
    }
    

    public IEnumerable<AITool> GetAITools()
    {
        yield return AIFunctionFactory.Create(this.BinarizeImage);
    }
}
