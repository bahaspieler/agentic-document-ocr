using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;
using System.ClientModel;

namespace OcrAgent.Agent;

public sealed class AgentRunner
{
    private readonly AIAgent _agent;

    public AgentRunner(AIAgent agent)
    {
        _agent = agent;
    }

    public async Task RunConversationLoopAsync()
    {
        var thread = _agent.GetNewThread();

        while (true)
        {
            Console.WriteLine();
            Console.Write("> ");
            var userInput = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(userInput) ||
                userInput.Trim().Equals("exit", StringComparison.OrdinalIgnoreCase))
                break;

            await foreach (var update in _agent.RunStreamingAsync(userInput, thread))
            {
                Console.Write(update);
            }
        }
    }