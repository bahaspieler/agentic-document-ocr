using System.Collections.Generic;
using Microsoft.Extensions.AI;
using OcrAgent.Storage;

namespace OcrAgent.Plugins.Enhancement;

/// <summary>
/// Plugin for removing noise from images using various denoising methods.
/// Supports median blur, bilateral filtering, and Non-Local Means denoising.
/// </summary>
public sealed class DenoisePlugin : IImageProcessingTool
{
    private readonly IImageStore _store;

    public DenoisePlugin(IImageStore store)
    {
        _store = store;
    }

    // We'll expose tools in a later commit
    public IEnumerable<AITool> GetAITools()
    {
        yield break;
    }
}
