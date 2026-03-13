using Microsoft.Extensions.AI;
using System.ComponentModel;
using OcrAgent.Storage;
using OcrAgent.Inference;
using OcrAgent.Workflows.Engine;
using OcrAgent.Workflows.Tools;

namespace OcrAgent.Plugins;

/// <summary>
/// Aggregates all image processing plugins and exposes tools to the AI agent.
/// Works as a registry for all preprocessing tools.
/// </summary>
public sealed class PluginAggregator
{
    private readonly IImageStore _store;
    private readonly List<IImageProcessingTool> _plugins;
    private readonly ToolBindingRegistry _toolRegistry;
    private readonly WorkflowTool? _workflowTool;

    public PluginAggregator(IImageStore store)
    {
        _store = store;
        _plugins = new List<IImageProcessingTool>();
        _toolRegistry = new ToolBindingRegistry();

        var u2netPredictor = U2NetpOnnxPredictor.TryCreateDefault();

        // Geometric transformations
        RegisterPlugin(new Geometric.ZoomInPlugin(store));
        RegisterPlugin(new Geometric.RotatePlugin(store));
        RegisterPlugin(new Geometric.PerspectivePlugin(store, u2netPredictor));
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
        RegisterPlugin(new Segmentation.DocumentBackgroundRemovalPlugin(store, u2netPredictor));

        foreach (var plugin in _plugins)
        {
            foreach (var tool in plugin.GetAITools())
            {
                if (tool is AIFunction func)
                    _toolRegistry.Register(func);
            }
        }

        var definitionsPath = Path.Combine(AppContext.BaseDirectory, "Workflows", "Definitions");
        var catalog = new WorkflowCatalog(definitionsPath);
        var compiler = new WorkflowCompiler(_toolRegistry);
        var runner = new WorkflowRunner(catalog, compiler);
        _workflowTool = new WorkflowTool(runner, catalog);
    }

    public ToolBindingRegistry GetToolRegistry() => _toolRegistry;

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
        yield return AIFunctionFactory.Create(this.RegisterImage);

        foreach (var plugin in _plugins)
            foreach (var tool in plugin.GetAITools())
                yield return tool;

        if (_workflowTool != null)
            foreach (var tool in _workflowTool.GetAITools())
                yield return tool;
    }

    /// <summary>
    /// Registers a plugin with the aggregator.
    /// </summary>
    internal void RegisterPlugin(IImageProcessingTool plugin)
    {
        _plugins.Add(plugin);
    }
}
