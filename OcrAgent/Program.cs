using OcrAgent.Agent;
using OcrAgent.Hosting;

namespace OcrAgent;

/// <summary>
/// Application entry: builds the engine, then runs the CLI or interactive chat.
/// </summary>
internal class Program
{
    static async Task<int> Main(string[] args)
    {
        // First arg other than "chat" selects CLI (workflow, tool, register, runs, list-*, help).
        bool isCli = args.Length > 0 && !args[0].Equals("chat", StringComparison.OrdinalIgnoreCase);

        EngineHost engine;
        try
        {
            engine = EngineHost.Create(isCli ? "cli" : "chat");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: Failed to start the engine - {ex.Message}");
            Console.WriteLine("Please check your appsettings.json configuration.");
            return 1;
        }

        using (engine)
        {
            if (isCli)
            {
                var cli = new CliRunner(engine.Store, engine.Aggregator, engine.RunStore);
                return await cli.RunAsync(args);
            }

            Console.WriteLine("OcrAgent - AI-Powered Image Preprocessing for OCR");
            Console.WriteLine("Type 'exit' to quit. Run with '--help' for CLI mode.");
            Console.WriteLine();

                var service = AgentService.Create(engine.Aggregator.GetAllTools());
                var runner = new AgentRunner(service);
                await runner.RunConversationLoopAsync();
            return 0;
        }
    }
}
