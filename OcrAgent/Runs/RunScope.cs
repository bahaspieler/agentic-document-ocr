namespace OcrAgent.Runs;

/// <summary>
/// Ambient information about the job currently executing.
/// </summary>
/// <remarks>
/// A workflow drives its steps through the same tool bindings a direct call uses, so without this
/// the recorder could not tell a user-issued call apart from a step inside a pipeline and a single
/// workflow would leave a row per step. <see cref="Enter"/> marks the boundary; the recorder skips
/// anything raised while a job is already running.
/// <para>
/// The depth lives in an <see cref="AsyncLocal{T}"/> so concurrent jobs in one process, which is the
/// normal case for the MCP server, cannot see each other's scope.
/// </para>
/// </remarks>
public static class RunScope
{
    private static readonly AsyncLocal<int> Depth = new();
    private static readonly AsyncLocal<string?> Conversation = new();
    private static readonly AsyncLocal<string?> SourceContext = new();
    private static string _defaultSource = "cli";

    /// <summary>Entry point that started this process: <c>cli</c>, <c>chat</c>, or <c>mcp</c>.</summary>
    public static string Source
    {
        get => SourceContext.Value ?? _defaultSource;
        set => _defaultSource = value;
    }

    /// <summary>True when a job is already running on this execution path.</summary>
    public static bool IsInsideJob => Depth.Value > 0;

    /// <summary>
    /// Conversation this job belongs to, or null when nothing raised it on behalf of one.
    /// </summary>
    /// <remarks>
    /// A model decides which tools to call and in what order, so the jobs one request produces are not
    /// known in advance and cannot be labelled by the caller. Carrying the id here labels them all
    /// without a single tool or workflow signature changing. A direct tool call or a CLI invocation has
    /// no conversation, and leaves this null.
    /// </remarks>
    public static string? ConversationId => Conversation.Value;

    /// <summary>Marks the start of a job and restores the previous state when disposed.</summary>
    /// <returns>Handle to dispose at the end of the job.</returns>
    public static IDisposable Enter()
    {
        Depth.Value++;
        return new Handle(() => Depth.Value--);
    }

    /// <summary>Attributes every job raised on this path to a conversation.</summary>
    /// <param name="conversationId">Id to record, from the caller holding the conversation.</param>
    /// <returns>Handle to dispose at the end of the turn.</returns>
    public static IDisposable EnterConversation(string conversationId)
    {
        var previous = Conversation.Value;
        Conversation.Value = conversationId;
        return new Handle(() => Conversation.Value = previous);
    }

    /// <summary>Attributes jobs on the current async request to a particular entry point.</summary>
    public static IDisposable EnterSource(string source)
    {
        var previous = SourceContext.Value;
        SourceContext.Value = source;
        return new Handle(() => SourceContext.Value = previous);
    }

    private sealed class Handle : IDisposable
    {
        private readonly Action _release;
        private bool _released;

        public Handle(Action release)
        {
            _release = release;
        }

        public void Dispose()
        {
            if (_released) return;
            _released = true;
            _release();
        }
    }
}
