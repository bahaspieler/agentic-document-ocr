using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Server;
using OcrAgent.Plugins;
using OcrAgent.Storage;

namespace OcrAgent.Mcp;

/// <summary>
/// Bootstraps the OcrAgent engine (configuration, image store, plugin aggregator) once and
/// exposes its tool surface as MCP server tools. Mirrors the startup done in OcrAgent's Program.cs
/// but hosts the engine in-process behind an MCP server instead of the console.
/// </summary>
public sealed class OcrAgentToolHost : IDisposable
{
    /// <summary>Backing image store shared by every tool.</summary>
    public IImageStore Store { get; }

    /// <summary>Registry of all preprocessing, OCR, extraction, and workflow tools.</summary>
    public PluginAggregator Aggregator { get; }

    private OcrAgentToolHost(IImageStore store, PluginAggregator aggregator)
    {
        Store = store;
        Aggregator = aggregator;
    }

    /// <summary>
    /// Loads configuration, initializes the image store, and builds the plugin aggregator.
    /// </summary>
    /// <returns>A ready-to-use host whose tools can be exposed over MCP.</returns>
    public static OcrAgentToolHost Create()
    {
        var (config, configBasePath) = LoadConfiguration();

        var storageConfig = config.GetSection("Storage").Get<StorageConfiguration>()
            ?? new StorageConfiguration();

        // Resolve relative storage paths against the config base, not the current directory.
        if (!Path.IsPathRooted(storageConfig.Local.RootPath))
            storageConfig.Local.RootPath = Path.GetFullPath(
                Path.Combine(configBasePath, storageConfig.Local.RootPath));

        var store = StorageFactory.Create(storageConfig);
        var aggregator = new PluginAggregator(store, config);

        return new OcrAgentToolHost(store, aggregator);
    }

    /// <summary>
    /// Wraps every <see cref="AIFunction"/> exposed by the aggregator as an MCP server tool.
    /// This surfaces RegisterImage, all preprocessing/OCR/extraction plugins, and the
    /// workflow tools with their existing names, descriptions, and JSON schemas.
    /// </summary>
    /// <returns>MCP tools to register with the server.</returns>
    public IEnumerable<McpServerTool> CreateMcpTools() =>
        Aggregator.GetAllTools()
            .OfType<AIFunction>()
            .Select(function => McpServerTool.Create(function));

    private static (IConfiguration Config, string BasePath) LoadConfiguration()
    {
        // Content (appsettings.json, Workflows/Definitions, Assets) is copied next to the
        // built assembly, so resolve against AppContext.BaseDirectory first.
        var basePath = File.Exists(Path.Combine(AppContext.BaseDirectory, "appsettings.json"))
            ? AppContext.BaseDirectory
            : Directory.GetCurrentDirectory();

        var builder = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .AddEnvironmentVariables("OCR_");

        return (builder.Build(), basePath);
    }

    /// <summary>Disposes the aggregator and store.</summary>
    public void Dispose()
    {
        Aggregator.Dispose();
        if (Store is IDisposable disposable)
            disposable.Dispose();
    }
}
