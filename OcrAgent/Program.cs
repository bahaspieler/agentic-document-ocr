using Microsoft.Extensions.Configuration;
using OcrAgent.Agent;
using OcrAgent.Plugins;
using OcrAgent.Storage;

namespace OcrAgent;

/// <summary>
/// Application entry: loads configuration, builds storage and plugins, then runs the CLI or interactive chat.
/// </summary>
internal class Program
{
    static async Task<int> Main(string[] args)
    {
        // JSON appsettings, optional env-specific file, OCR_ env vars; .env loaded from cwd or parents.
        var config = LoadConfiguration();
        
        var storageConfig = config.GetSection("Storage").Get<StorageConfiguration>() 
            ?? new StorageConfiguration();

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

        var aggregator = new PluginAggregator(store, config);

        try
        {
            // First arg other than "chat" selects CLI (workflow, tool, register, list-*, help).
            if (args.Length > 0 && !args[0].Equals("chat", StringComparison.OrdinalIgnoreCase))
            {
                var cli = new CliRunner(store, aggregator);
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

    private static IConfiguration LoadConfiguration()
    {
        LoadEnvFile();
        
        var builder = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production"}.json", 
                optional: true, reloadOnChange: false)
            .AddEnvironmentVariables("OCR_");

        return builder.Build();
    }

    private static void LoadEnvFile()
    {
        var startDir = Directory.GetCurrentDirectory();
        var dir = startDir;
        
        while (dir != null)
        {
            var envPath = Path.Combine(dir, ".env");
            if (File.Exists(envPath))
            {
                foreach (var line in File.ReadAllLines(envPath))
                {
                    var trimmed = line.Trim();
                    if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#')) continue;
                    
                    var eqIndex = trimmed.IndexOf('=');
                    if (eqIndex <= 0) continue;
                    
                    var key = trimmed[..eqIndex].Trim();
                    var value = trimmed[(eqIndex + 1)..].Trim();
                    
                    if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key)))
                        Environment.SetEnvironmentVariable(key, value);
                }
                
                return;
            }
            
            var parentDir = Directory.GetParent(dir);
            if (parentDir == null) break;
            dir = parentDir.FullName;
        }
    }
}
