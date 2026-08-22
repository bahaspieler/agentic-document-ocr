using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OcrAgent.Runs;
using OpenAI;
using System.ClientModel;

namespace OcrAgent.Agent;

/// <summary>
/// Runs one natural-language turn against the OCR tool surface.
/// </summary>
/// <remarks>
/// Everything a caller needs to hold a conversation with the agent, and nothing about how the turn
/// arrived. The console reads it from stdin, Phase 7 takes it from an HTTP request, and Phase 9 takes
/// it from a queue message; keeping the transport out means those three cannot end up with three
/// slightly different agents, three sets of instructions, or three ways of recording what ran.
/// <para>
/// A turn is either part of a conversation or on its own. A queue message such as "binarize RCPT001"
/// is complete in itself and passes no conversation, which is what makes it safe to process on any
/// replica; an interactive caller passes the same <see cref="AgentConversation"/> each time so the
/// model can still resolve "that image".
/// </para>
/// </remarks>
public sealed class AgentService
{
    private readonly AIAgent _agent;

    /// <summary>Wraps an already-built agent.</summary>
    /// <param name="agent">Agent holding the tool surface.</param>
    public AgentService(AIAgent agent)
    {
        _agent = agent ?? throw new ArgumentNullException(nameof(agent));
    }

    /// <summary>
    /// Builds the service over an agent configured from the environment.
    /// </summary>
    /// <param name="tools">Tools the model may call, normally the whole engine surface.</param>
    /// <returns>Service ready to run turns.</returns>
    /// <exception cref="InvalidOperationException">No model credentials in the environment.</exception>
    public static AgentService Create(IEnumerable<AITool> tools) => new(CreateAgent(tools));

    /// <summary>Starts a conversation the caller can carry across turns.</summary>
    /// <param name="conversationId">
    /// Id to record the turns under. A new one is generated when null, which is the usual case; a
    /// caller that already identifies the exchange, such as a web front end with its own session ids,
    /// passes that instead.
    /// </param>
    /// <param name="cancellationToken">Cancels session creation.</param>
    /// <returns>The conversation to pass back to <see cref="RunTurnAsync"/>.</returns>
    public async Task<AgentConversation> StartConversationAsync(
        string? conversationId = null, CancellationToken cancellationToken = default)
    {
        var session = await _agent.CreateSessionAsync(cancellationToken).ConfigureAwait(false);
        return new AgentConversation(conversationId ?? RunId.NewId(), session);
    }

    /// <summary>
    /// Runs one turn: the model reads the message, calls whichever tools it needs, and answers.
    /// </summary>
    /// <param name="message">What the user asked for, in their own words.</param>
    /// <param name="conversation">
    /// Conversation to continue. Null runs the turn on its own, with no history before it and nothing
    /// kept after it.
    /// </param>
    /// <param name="onUpdate">
    /// Called with each fragment of the answer as it arrives, for a caller that shows the reply while
    /// it is still being written. Invoked on the thread draining the model response.
    /// </param>
    /// <param name="cancellationToken">Cancels the turn.</param>
    /// <returns>The complete answer.</returns>
    public async Task<string> RunTurnAsync(
        string message,
        AgentConversation? conversation = null,
        Action<string>? onUpdate = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(message))
            throw new ArgumentException("Message is required.", nameof(message));

        conversation ??= await StartConversationAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        // Tools are invoked below this line, and the recorder reads the id from here rather than
        // taking it as an argument, which is what keeps every tool and workflow signature unchanged.
        using var scope = RunScope.EnterConversation(conversation.Id);

        var answer = new System.Text.StringBuilder();

        await foreach (var update in _agent
            .RunStreamingAsync(message, conversation.Session, cancellationToken: cancellationToken)
            .ConfigureAwait(false))
        {
            if (string.IsNullOrEmpty(update.Text))
                continue;

            answer.Append(update.Text);
            onUpdate?.Invoke(update.Text);
        }

        return answer.ToString();
    }

    /// <summary>
    /// Builds the chat agent from OPENAI_API_KEY and optional OPENAI_CHATCOMPLETION_DEPLOYMENT.
    /// </summary>
    /// <param name="tools">Tools registered on the agent.</param>
    /// <returns>Configured agent with the shared system instructions.</returns>
    private static AIAgent CreateAgent(IEnumerable<AITool> tools)
    {
        string apiKey = GetEnvOrThrow("OPENAI_API_KEY");
        string deployment = Environment.GetEnvironmentVariable("OPENAI_CHATCOMPLETION_DEPLOYMENT")
            ?? "gpt-4o-mini";

        var client = new OpenAIClient(new ApiKeyCredential(apiKey));
        var chatClient = client.GetChatClient(deployment);

        return chatClient.AsIChatClient().AsAIAgent(
            instructions: AgentInstructions.ForAgent,
            name: AgentInstructions.Name,
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
