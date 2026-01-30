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

    [Description("Resizes image to normalize dimensions.")]
    public Task<string> ResizeImage(string imageReference)
    {
        throw new NotImplementedException();
    }

    public IEnumerable<AITool> GetAITools()
    {
        yield return AIFunctionFactory.Create(this.ResizeImage);
    }
}
