using Microsoft.Extensions.AI;
using System.ComponentModel;
using OcrAgent.Storage;

namespace OcrAgent.Plugins;

public sealed class PluginAggregator
{
    private readonly IImageStore _store;
    private readonly List<IImageProcessingTool> _plugins;

    public PluginAggregator(IImageStore store)
    {
        _store = store;
    }
    
    [Description("Registers an image into the storage system and returns its reference ID. The image is provided as a local file path.")]
    public string RegisterImage(
        [Description("Local file path of the image to register.")] string filePath,
        [Description("Optional reference ID to use (e.g., 123ABC). If empty, a new reference is generated.")] string? desiredReference = null)
    {
        return _store.SaveFromFile(filePath, desiredReference);
    }
}
