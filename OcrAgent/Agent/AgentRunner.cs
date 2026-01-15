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
    public static AIAgent CreateAgent(IEnumerable<AITool> tools)
    {
        string apiKey = GetEnvOrThrow("OPENAI_API_KEY");
        string deployment = Environment.GetEnvironmentVariable("OPENAI_CHATCOMPLETION_DEPLOYMENT") ?? "gpt-3.5-turbo";

        var client = new OpenAIClient(new ApiKeyCredential(apiKey));
        var chatClient = client.GetChatClient(deployment);

        return chatClient.CreateAIAgent(
            instructions:
                "You help preprocess images for OCR.\n" +
                "If the user provides a local file path, call RegisterImage(filePath, desiredReference) to store it.\n" +
                "If the user requests zooming, call ZoomInImage(imageReference, startZoom, endZoom, zoomStep).\n" +
                "If the user requests rotating, call RotateImage(imageReference, startAngle, endAngle, angleStep).\n" +
                "Always return the generated reference IDs to the user.\n" +
                "If required parameters are missing, ask a short follow-up question.",
            name: "OCR Preprocessing Agent",
            tools: tools.ToArray()
        );
    }
    private static string GetEnvOrThrow(string key)
    {
        var value = Environment.GetEnvironmentVariable(key);
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"Missing {key} environment variable.");
        return value;
    }
}