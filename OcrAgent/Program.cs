using OcrAgent.Agent;
using OcrAgent.Plugins;
using OcrAgent.Storage;

namespace OcrAgent;
internal class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("OcrAgent");
        Console.WriteLine("Type 'exit' to quit.");

        // storage root: configurable
        var storageRoot = Environment.GetEnvironmentVariable("OCR_AGENT_STORAGE")
                          ?? Path.Combine(Environment.CurrentDirectory, "Storage");

        var store = new LocalFileImageStore(storageRoot);
        var aggregator = new PluginAggregator(store);

        var agent = AgentRunner.CreateAgent(aggregator.GetAllTools());
        var samples = new AgentRunner(agent);

        await samples.RunConversationLoopAsync();
    }
}