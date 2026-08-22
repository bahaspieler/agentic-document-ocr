namespace OcrAgent.Agent;

/// <summary>
/// Interactive console session with the OCR agent.
/// </summary>
/// <remarks>
/// Only the console part is here: reading a line, echoing the reply as it streams, and knowing when to
/// stop. The turn itself belongs to <see cref="AgentService"/>, which the REST and queue entry points
/// will call the same way. Holding one <see cref="AgentConversation"/> for the life of the loop is
/// what makes the console multi-turn.
/// </remarks>
public sealed class AgentRunner
{
    private readonly AgentService _service;

    /// <summary>Creates a runner over the service that executes each turn.</summary>
    /// <param name="service">Service holding the agent and its tools.</param>
    public AgentRunner(AgentService service)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
    }

    /// <summary>Prompts on stdin until "exit" or EOF; streams the model reply each turn.</summary>
    /// <returns>Task that finishes when the loop ends.</returns>
    public async Task RunConversationLoopAsync()
    {
        var conversation = await _service.StartConversationAsync();

        while (true)
        {
            Console.WriteLine();
            Console.Write("User> ");
            var userInput = Console.ReadLine();

            if (userInput == null)
                break;

            if (userInput.Trim().Equals("exit", StringComparison.OrdinalIgnoreCase))
                break;

            if (string.IsNullOrWhiteSpace(userInput))
                continue;

            Console.WriteLine();
            Console.Write("Agent> ");
            await _service.RunTurnAsync(userInput, conversation, Console.Write);
            Console.WriteLine();
        }
    }
}
