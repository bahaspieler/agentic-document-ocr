using Microsoft.Extensions.AI;
using System.ComponentModel;
using OcrAgent.Storage;

namespace OcrAgent.Plugins;

/// <summary>
/// Aggregates all image processing plugins and exposes tools to the AI agent.
/// Works as a registry for all preprocessing tools.
/// </summary>
public sealed class PluginAggregator
{
    private readonly IImageStore _store;
    private readonly List<IImageProcessingTool> _plugins;

    public PluginAggregator(IImageStore store)
    {
        _store = store;
        _plugins = new List<IImageProcessingTool>();
        
        // Register all plugins here
        
        // Geometric transformations
        RegisterPlugin(new Geometric.ZoomInPlugin(store));
        RegisterPlugin(new Geometric.RotatePlugin(store));
        RegisterPlugin(new Geometric.PerspectivePlugin(store));  // Apply before deskew
        RegisterPlugin(new Geometric.DeskewPlugin(store));
        
        // Enhancement operations
        RegisterPlugin(new Enhancement.ContrastPlugin(store));
        RegisterPlugin(new Enhancement.BrightnessPlugin(store));
        RegisterPlugin(new Enhancement.DenoisePlugin(store));
        RegisterPlugin(new Enhancement.ColorCorrectionPlugin(store));
        RegisterPlugin(new Enhancement.InvertPlugin(store));
        
        // Segmentation operations
        RegisterPlugin(new Segmentation.BinarizePlugin(store));
        RegisterPlugin(new Segmentation.MorphologyPlugin(store));
        RegisterPlugin(new Segmentation.DocumentBackgroundRemovalPlugin(store));
    }

    /// <summary>
    /// Registers an image into the storage system and returns its reference ID.
    /// This tool is exposed directly by the aggregator.
    /// </summary>
    [Description("Registers an image into the storage system and returns its reference ID. The image is provided as a local file path.")]
    public string RegisterImage(
        [Description("Local file path of the image to register.")] string filePath,
        [Description("Optional reference ID to use (e.g., 123ABC). If empty, a new reference is generated.")] string? desiredReference = null)
    {
        return _store.SaveFromFile(filePath, desiredReference);
    }

    /// <summary>
    /// Returns all AI tools from all registered plugins and also the RegisterImage plugin.
    /// </summary>
    public IEnumerable<AITool> GetAllTools()
    {
        // Yield the RegisterImage plugin
        yield return AIFunctionFactory.Create(this.RegisterImage);
        
        // yield all plugins from registered plugins
        foreach (var plugin in _plugins)
        {
            foreach (var tool in plugin.GetAITools())
            {
                yield return tool;
            }
        }
    }

    /// <summary>
    /// Registers a plugin with the aggregator.
    /// </summary>
    internal void RegisterPlugin(IImageProcessingTool plugin)
    {
        _plugins.Add(plugin);
    }
}
