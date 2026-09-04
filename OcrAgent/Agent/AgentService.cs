using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OcrAgent.Runs;
using OpenAI;
using System.ClientModel;
using System.Text.Json;

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

    /// <summary>Builds an isolated agent with task-specific instructions and tools.</summary>
    /// <param name="tools">Tools available to the specialized agent.</param>
    /// <param name="instructions">System instructions for the specialized task.</param>
    /// <returns>Service ready to run isolated turns.</returns>
    public static AgentService Create(
        IEnumerable<AITool> tools,
        string instructions) =>
        new(CreateAgent(tools, instructions));

    #region Conversation Lifecycle

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

    /// <summary>Restores a conversation persisted by <see cref="SerializeConversationAsync"/>.</summary>
    public async Task<AgentConversation> RestoreConversationAsync(
        string conversationId,
        string payloadJson,
        CancellationToken cancellationToken = default)
    {
        using var document = JsonDocument.Parse(payloadJson);
        var session = await _agent.DeserializeSessionAsync(
            document.RootElement.Clone(), cancellationToken: cancellationToken).ConfigureAwait(false);
        return new AgentConversation(conversationId, session);
    }

    /// <summary>Serializes all model-side conversation state for durable storage.</summary>
    public async Task<string> SerializeConversationAsync(
        AgentConversation conversation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        var element = await _agent.SerializeSessionAsync(
            conversation.Session, cancellationToken: cancellationToken).ConfigureAwait(false);
        return element.GetRawText();
    }

    #endregion

    #region Turn Execution

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
        => await RunTurnStreamingAsync(
            message,
            conversation,
            onUpdate is null
                ? null
                : streamEvent =>
                {
                    if (streamEvent.Type == "token" && streamEvent.Text is not null)
                        onUpdate(streamEvent.Text);
                },
            cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Runs an isolated agent request without assigning its tool calls to a durable conversation.
    /// </summary>
    public Task<string> RunOneShotAsync(
        string message,
        Action<AgentStreamEvent>? onEvent = null,
        CancellationToken cancellationToken = default) =>
        RunStreamingCoreAsync(message, conversation: null, onEvent, cancellationToken);

    /// <summary>Runs a turn while exposing model text, tool calls, and workflow steps.</summary>
    public async Task<string> RunTurnStreamingAsync(
        string message,
        AgentConversation? conversation = null,
        Action<AgentStreamEvent>? onEvent = null,
        CancellationToken cancellationToken = default) =>
        await RunStreamingCoreAsync(message, conversation, onEvent, cancellationToken)
            .ConfigureAwait(false);

    private async Task<string> RunStreamingCoreAsync(
        string message,
        AgentConversation? conversation,
        Action<AgentStreamEvent>? onEvent,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(message))
            throw new ArgumentException("Message is required.", nameof(message));

        var session = conversation?.Session ??
            await _agent.CreateSessionAsync(cancellationToken).ConfigureAwait(false);

        // Tools are invoked below this line, and the recorder reads the id from here rather than
        // taking it as an argument, which is what keeps every tool and workflow signature unchanged.
        using var scope = conversation is null
            ? null
            : RunScope.EnterConversation(conversation.Id);
        using var progressScope = AgentProgressScope.Enter(onEvent);
        using var runEvents = RunEventScope.Enter(
            run => onEvent?.Invoke(AgentStreamEvent.RunCompleted(run)));

        var answer = new System.Text.StringBuilder();
        var announcedCalls = new HashSet<string>(StringComparer.Ordinal);

        await foreach (var update in _agent
            .RunStreamingAsync(message, session, cancellationToken: cancellationToken)
            .ConfigureAwait(false))
        {
            foreach (var functionCall in update.Contents.OfType<FunctionCallContent>())
            {
                var key = $"{functionCall.CallId}:{functionCall.Name}";
                if (announcedCalls.Add(key))
                    onEvent?.Invoke(AgentStreamEvent.Tool(functionCall.Name));
            }

            if (string.IsNullOrEmpty(update.Text))
                continue;

            answer.Append(update.Text);
            onEvent?.Invoke(AgentStreamEvent.Token(update.Text));
        }

        return answer.ToString();
    }

    #endregion

    #region Agent Configuration

    /// <summary>
    /// Builds the chat agent from OPENAI_API_KEY and optional OPENAI_CHATCOMPLETION_DEPLOYMENT.
    /// </summary>
    /// <param name="tools">Tools registered on the agent.</param>
    /// <param name="instructions">Optional task-specific system instructions.</param>
    /// <returns>Configured agent with the shared system instructions.</returns>
    private static AIAgent CreateAgent(
        IEnumerable<AITool> tools,
        string? instructions = null)
    {
        string apiKey = GetEnvOrThrow("OPENAI_API_KEY");
        string deployment = Environment.GetEnvironmentVariable("OPENAI_CHATCOMPLETION_DEPLOYMENT")
            ?? "gpt-4o-mini";

        var client = new OpenAIClient(new ApiKeyCredential(apiKey));
        var chatClient = client.GetChatClient(deployment);

        return chatClient.AsIChatClient().AsAIAgent(
            instructions: instructions ?? AgentInstructions.ForAgent,
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

    #endregion
}
