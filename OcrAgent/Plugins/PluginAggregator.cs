using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using System.ComponentModel;
using System.Diagnostics;
using OcrAgent.Storage;
using OcrAgent.Inference;
using OcrAgent.Ocr;
using OcrAgent.Workflows.Engine;
using OcrAgent.Workflows.Tools;

namespace OcrAgent.Plugins;

/// <summary>
/// Aggregates all image processing plugins and exposes tools to the AI agent.
/// Works as a registry for all preprocessing tools.
/// </summary>
public sealed class PluginAggregator : IDisposable
{
    private readonly IImageStore _store;
    private readonly List<IImageProcessingTool> _plugins;
    private readonly ToolBindingRegistry _toolRegistry;
    private readonly WorkflowTool? _workflowTool;
    private readonly IOcrEngine? _ocrEngine;

    /// <summary>
    /// Registers built-in plugins, binds their tools, and wires the workflow tool when tessdata is available.
    /// </summary>
    /// <param name="store">Image store shared by all plugins.</param>
    /// <param name="config">Optional configuration (e.g. Tesseract paths).</param>
    public PluginAggregator(IImageStore store, IConfiguration? config = null)
    {
        _store = store;
        _plugins = new List<IImageProcessingTool>();
        _toolRegistry = new ToolBindingRegistry();

        var u2netPredictor = U2NetpOnnxPredictor.TryCreateDefault();

        // Analysis operations
        RegisterPlugin(new Analysis.QualityAssessmentPlugin(store));
        RegisterPlugin(new Analysis.ContourExtractionPlugin(store, u2netPredictor));

        // Geometric transformations
        RegisterPlugin(new Geometric.ZoomInPlugin(store));
        RegisterPlugin(new Geometric.RotatePlugin(store));
        RegisterPlugin(new Geometric.PerspectivePlugin(store, u2netPredictor));
        RegisterPlugin(new Geometric.DeskewPlugin(store));
        RegisterPlugin(new Geometric.AutoOrientPlugin(store));
        RegisterPlugin(new Geometric.ResizePlugin(store));
        RegisterPlugin(new Geometric.BorderPaddingPlugin(store));
        RegisterPlugin(new Geometric.AutoCropPlugin(store));

        // Enhancement operations
        RegisterPlugin(new Enhancement.ContrastPlugin(store));
        RegisterPlugin(new Enhancement.BrightnessPlugin(store));
        RegisterPlugin(new Enhancement.DenoisePlugin(store));
        RegisterPlugin(new Enhancement.ColorCorrectionPlugin(store));
        RegisterPlugin(new Enhancement.InvertPlugin(store));
        RegisterPlugin(new Enhancement.SharpenPlugin(store));
        RegisterPlugin(new Enhancement.IlluminationNormalizePlugin(store));

        // Segmentation operations
        RegisterPlugin(new Segmentation.BinarizePlugin(store));
        RegisterPlugin(new Segmentation.MorphologyPlugin(store));
        RegisterPlugin(new Segmentation.DocumentBackgroundRemovalPlugin(store, u2netPredictor));

        // OCR (Tesseract with built-in variant/PSM support)
        _ocrEngine = TryCreateOcrEngine(config);
        if (_ocrEngine != null)
        {
            RegisterPlugin(new Ocr.OcrPlugin(store, _ocrEngine));
            RegisterPlugin(new Ocr.OcrScoringPlugin());
        }

        // Receipt extraction (rule-based)
        RegisterPlugin(new Extraction.ParseReceiptPlugin(store));
        RegisterPlugin(new Extraction.ExportReceiptPlugin(store));

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
        var runner = new WorkflowRunner(catalog, compiler, store);
        _workflowTool = new WorkflowTool(runner, catalog);
    }

    /// <summary>
    /// Returns the registry used to resolve workflow step names to AI functions.
    /// </summary>
    /// <returns>Tool binding registry.</returns>
    public ToolBindingRegistry GetToolRegistry() => _toolRegistry;

    /// <summary>
    /// Saves a file into storage and returns its reference id.
    /// </summary>
    /// <param name="filePath">Path to the image file on disk.</param>
    /// <param name="desiredReference">Optional id; if null or empty, one is generated.</param>
    /// <returns>New image reference id.</returns>
    [Description("Registers an image into the storage system and returns its reference ID. The image is provided as a local file path.")]
    public string RegisterImage(
        [Description("Local file path of the image to register.")] string filePath,
        [Description("Optional reference ID to use (e.g., 123ABC). If empty, a new reference is generated.")] string? desiredReference = null)
    {
        return _store.SaveFromFile(filePath, desiredReference);
    }

    /// <summary>
    /// Yields register-image, every plugin tool, and workflow tools.
    /// </summary>
    /// <returns>All tools exposed to the agent.</returns>
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

    internal void RegisterPlugin(IImageProcessingTool plugin)
    {
        _plugins.Add(plugin);
    }

    /// <summary>
    /// Disposes the OCR engine when it implements <see cref="IDisposable"/>.
    /// </summary>
    public void Dispose()
    {
        if (_ocrEngine is IDisposable d)
            d.Dispose();
    }

    private static IOcrEngine? TryCreateOcrEngine(IConfiguration? config)
    {
        var tessdataPaths = new List<string>();

        // Priority 1: configured path from appsettings.json
        var configuredPath = config?.GetValue<string>("Tesseract:TessdataPath");
        if (!string.IsNullOrEmpty(configuredPath))
            tessdataPaths.Add(configuredPath);

        // Priority 2: standard discovery paths
        tessdataPaths.Add(Path.Combine(AppContext.BaseDirectory, "Assets", "tessdata"));
        tessdataPaths.Add(Path.Combine(AppContext.BaseDirectory, "tessdata"));
        tessdataPaths.Add(Path.Combine(Directory.GetCurrentDirectory(), "Assets", "tessdata"));

        foreach (var path in tessdataPaths)
        {
            if (!Directory.Exists(path) || Directory.GetFiles(path, "*.traineddata").Length == 0)
                continue;

            try
            {
                var engine = new TesseractOcrEngine(path);
                Debug.WriteLine($"[OK] Tesseract OCR loaded from {path}");
                return engine;
            }
            catch (Exception ex)
            {
                var inner = ex.InnerException?.Message ?? ex.Message;
                Debug.WriteLine($"[WARN] Tesseract failed to load from {path}: {inner}");
                Debug.WriteLine("       On macOS Apple Silicon, run: scripts/setup-native-libs.sh");
            }
        }

        Debug.WriteLine("[INFO] Tesseract OCR not available. RunOcr tool disabled.");
        Debug.WriteLine("       Install tessdata: place eng.traineddata in Assets/tessdata/");
        Debug.WriteLine("       On macOS ARM, also run: scripts/setup-native-libs.sh");
        return null;
    }
}
