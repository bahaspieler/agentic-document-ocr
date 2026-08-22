using Microsoft.Extensions.Configuration;
using OcrAgent.Configuration;
using OcrAgent.Plugins;
using OcrAgent.Runs;
using OcrAgent.Sql;
using OcrAgent.Storage;

namespace OcrAgent.Hosting;

/// <summary>
/// The engine every entry point starts from: configuration, image store, run store, and the tool
/// surface built over them.
/// </summary>
/// <remarks>
/// The console, the MCP server, and later the queue worker all need the same six steps in the same
/// order, and each host having its own copy is how the two that existed first drifted apart on where
/// configuration is read from. Hosts differ in what they do with the tools, not in how the engine is
/// assembled, so only the former belongs to them.
/// </remarks>
public sealed class EngineHost : IDisposable
{
    /// <summary>Merged configuration: appsettings, the environment overlay, then <c>OCR_</c> variables.</summary>
    public IConfiguration Configuration { get; }

    /// <summary>Directory the configuration was read from; relative storage paths resolve against it.</summary>
    public string ConfigBasePath { get; }

    /// <summary>Backing image store shared by every tool.</summary>
    public IImageStore Store { get; }

    /// <summary>Registry of all preprocessing, OCR, extraction, and workflow tools.</summary>
    public PluginAggregator Aggregator { get; }

    /// <summary>Store that receives one record per job executed through this host.</summary>
    public IRunStore RunStore { get; }

    /// <summary>Durable web conversations, disabled when SQL is not configured.</summary>
    public ISessionStore SessionStore { get; }

    /// <summary>Microsoft identity profiles, disabled when SQL is not configured.</summary>
    public IUserStore UserStore { get; }

    /// <summary>Queryable structured receipt data, disabled when SQL is not configured.</summary>
    public IReceiptStore ReceiptStore { get; }

    private EngineHost(
        IConfiguration configuration,
        string configBasePath,
        IImageStore store,
        PluginAggregator aggregator,
        IRunStore runStore,
        SqlStores sqlStores)
    {
        Configuration = configuration;
        ConfigBasePath = configBasePath;
        Store = store;
        Aggregator = aggregator;
        RunStore = runStore;
        SessionStore = sqlStores.Sessions;
        UserStore = sqlStores.Users;
        ReceiptStore = sqlStores.Receipts;
    }

    /// <summary>
    /// Loads configuration, initializes the image and run stores, and builds the plugin aggregator.
    /// </summary>
    /// <param name="source">
    /// Entry point being started, recorded on every run this host produces: <c>cli</c>, <c>chat</c>,
    /// <c>mcp</c>, or <c>queue</c>.
    /// </param>
    /// <returns>A ready-to-use engine. The caller owns it and must dispose it.</returns>
    /// <exception cref="Exception">
    /// Propagated from storage initialization, which a host may want to report in its own words.
    /// </exception>
    public static EngineHost Create(string source)
    {
        var (config, configBasePath) = LoadConfiguration();

        RunScope.Source = source;

        var storageConfig = config.GetSection("Storage").Get<StorageConfiguration>()
            ?? new StorageConfiguration();

        // Relative paths in appsettings.json are relative to that file, not to the working directory,
        // which differs between a shell, a double-clicked binary, and the container.
        if (!Path.IsPathRooted(storageConfig.Local.RootPath))
            storageConfig.Local.RootPath = Path.GetFullPath(
                Path.Combine(configBasePath, storageConfig.Local.RootPath));

        if (!Path.IsPathRooted(storageConfig.AzureBlob.CacheRootPath))
            storageConfig.AzureBlob.CacheRootPath = Path.GetFullPath(
                Path.Combine(configBasePath, storageConfig.AzureBlob.CacheRootPath));

        var runStore = RunStoreFactory.CreateOrNull(
            config.GetSection("Runs").Get<RunStoreConfiguration>() ?? new RunStoreConfiguration());
        var sqlStores = SqlStoreFactory.CreateOrNull(
            config.GetSection("Sql").Get<SqlConfiguration>() ?? new SqlConfiguration());

        var store = StorageFactory.Create(storageConfig);
        var aggregator = new PluginAggregator(store, config, runStore, sqlStores.Receipts);

        return new EngineHost(config, configBasePath, store, aggregator, runStore, sqlStores);
    }

    /// <summary>Disposes the aggregator and the image store.</summary>
    public void Dispose()
    {
        Aggregator.Dispose();
        if (Store is IDisposable disposable)
            disposable.Dispose();
    }

    /// <summary>
    /// Builds configuration and reports the directory it came from.
    /// </summary>
    /// <remarks>
    /// A working directory holding an appsettings.json is a deliberate choice by whoever started the
    /// process, so it wins. Otherwise the copy beside the assembly is the one that ships, which is
    /// the case in the container, where the working directory is the filesystem root.
    /// </remarks>
    private static (IConfiguration Config, string BasePath) LoadConfiguration()
    {
        EnvFile.Load();

        var basePath = File.Exists(Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json"))
            ? Directory.GetCurrentDirectory()
            : AppContext.BaseDirectory;

        var environment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production";

        var builder = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .AddJsonFile($"appsettings.{environment}.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables("OCR_");

        return (builder.Build(), basePath);
    }
}
