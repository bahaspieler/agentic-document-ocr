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
        string deployment = Environment.GetEnvironmentVariable("OPENAI_CHATCOMPLETION_DEPLOYMENT") ?? "gpt-4o-mini";

        var client = new OpenAIClient(new ApiKeyCredential(apiKey));
        var chatClient = client.GetChatClient(deployment);

        return chatClient.CreateAIAgent(
            instructions:
                "You are an OCR Preprocessing Agent that helps improve document images before OCR extraction.\n\n" +
                "You will analyze the user prompt and map their demand to the available list of tools and utilize them\n\n" +
                "=== AVAILABLE TOOLS ===\n" +
                "1. RegisterImage(filePath, ref?) - Store an image and get a reference ID\n\n" +
                
                "GEOMETRIC:\n" +
                "2. ZoomInImage(ref, start%, end%, step%) - Zoom into images\n" +
                "3. RotateImage(ref, startAngle, endAngle, angleStep) - Rotate images\n" +
                "4. CorrectPerspective(ref, sensitivity?, targetW?, targetH?, margin?) - Fix angled photos (USE FIRST for mobile captures!)\n" +
                "5. DeskewImage(ref, maxAngle?, method?, preserveSize?) - Auto-correct skew/tilt\n" +
                
                "ENHANCEMENT:\n" +
                "7. EnhanceContrast(ref, method='auto'|'histogram'|'clahe') - Improve contrast\n" +
                "8. AdjustBrightness(ref, start, end, step) - Adjust brightness\n" +
                "9. DenoiseImage(ref, method='bilateral'|'median'|'nlm', strength?) - Remove noise\n" +
                
                "SEGMENTATION:\n" +
                "11. BinarizeImage(ref, method='otsu'|'adaptive_mean'|'adaptive_gaussian') - Convert to B&W\n\n" +
                "Always return the generated reference IDs to the user in numbered style.\n" +
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