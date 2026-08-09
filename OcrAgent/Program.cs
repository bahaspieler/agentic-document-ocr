using Microsoft.Extensions.Configuration;
using OcrAgent.Agent;
using OcrAgent.Configuration;
using OcrAgent.Plugins;
using OcrAgent.Runs;
using OcrAgent.Storage;

namespace OcrAgent;

/// <summary>
/// Application entry: loads configuration, builds storage and plugins, then runs the CLI or interactive chat.
/// </summary>
internal class Program
{
    static async Task<int> Main(string[] args)
    {
        var (config, configBasePath) = LoadConfiguration();
        
        var storageConfig = config.GetSection("Storage").Get<StorageConfiguration>() 
            ?? new StorageConfiguration();

        // Resolve relative storage paths against the config base (not cwd which may differ when double-clicking the binary).
        if (!Path.IsPathRooted(storageConfig.Local.RootPath))
            storageConfig.Local.RootPath = Path.GetFullPath(
                Path.Combine(configBasePath, storageConfig.Local.RootPath));

        if (!Path.IsPathRooted(storageConfig.AzureBlob.CacheRootPath))
            storageConfig.AzureBlob.CacheRootPath = Path.GetFullPath(
                Path.Combine(configBasePath, storageConfig.AzureBlob.CacheRootPath));

        var runStore = RunStoreFactory.CreateOrNull(
            config.GetSection("Runs").Get<RunStoreConfiguration>() ?? new RunStoreConfiguration());

        // Chat is the default when no command is given; the CLI branch below overrides it.
        RunScope.Source = args.Length > 0 && !args[0].Equals("chat", StringComparison.OrdinalIgnoreCase)
            ? "cli"
            : "chat";

        IImageStore store;
        try
        {
            store = StorageFactory.Create(storageConfig);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: Failed to initialize storage - {ex.Message}");
            Console.WriteLine("Please check your appsettings.json configuration.");
            return 1;
        }

        var aggregator = new PluginAggregator(store, config, runStore);

        try
        {
            // First arg other than "chat" selects CLI (workflow, tool, register, runs, list-*, help).
            if (args.Length > 0 && !args[0].Equals("chat", StringComparison.OrdinalIgnoreCase))
            {
                var cli = new CliRunner(store, aggregator, runStore);
                return await cli.RunAsync(args);
            }

            Console.WriteLine("OcrAgent - AI-Powered Image Preprocessing for OCR");
            Console.WriteLine("Type 'exit' to quit. Run with '--help' for CLI mode.");
            Console.WriteLine();

            var agent = AgentRunner.CreateAgent(aggregator.GetAllTools());
            var runner = new AgentRunner(agent);
            await runner.RunConversationLoopAsync();
            return 0;
        }
        finally
        {
            aggregator.Dispose();
            if (store is IDisposable disposable)
                disposable.Dispose();
        }
    }

    private static (IConfiguration Config, string BasePath) LoadConfiguration()
    {
        EnvFile.Load();

        var basePath = File.Exists(Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json"))
            ? Directory.GetCurrentDirectory()
            : AppContext.BaseDirectory;

        var builder = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production"}.json", 
                optional: true, reloadOnChange: false)
            .AddEnvironmentVariables("OCR_");

        return (builder.Build(), basePath);
    }
}
