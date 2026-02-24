using Microsoft.Extensions.AI;
using System.ComponentModel;
using OcrAgent.Storage;
using OpenCvSharp;

namespace OcrAgent.Plugins.Enhancement;

public sealed class InvertPlugin : IImageProcessingTool
{
    private readonly IImageStore _store;

    public InvertPlugin(IImageStore store)
    {
        _store = store;
    }

    public IEnumerable<AITool> GetAITools()
    {
        yield break;
    }
}