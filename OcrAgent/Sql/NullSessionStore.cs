namespace OcrAgent.Sql;

/// <summary>Disabled session store used by local/tool-only configurations.</summary>
public sealed class NullSessionStore : ISessionStore
{
    /// <summary>Gets the shared disabled session store.</summary>
    public static NullSessionStore Instance { get; } = new();

    /// <summary>Gets a value indicating that durable conversation storage is unavailable.</summary>
    public bool IsEnabled => false;
    private NullSessionStore() { }

    /// <summary>Rejects conversation creation because durable storage is disabled.</summary>
    public Task<ConversationRecord> CreateAsync(
        string conversationId, string? ownerUserId, string title, CancellationToken cancellationToken = default) =>
        Task.FromException<ConversationRecord>(Disabled());

    /// <summary>Returns no conversation because durable storage is disabled.</summary>
    public Task<ConversationRecord?> GetAsync(string conversationId, CancellationToken cancellationToken = default) =>
        Task.FromResult<ConversationRecord?>(null);

    /// <summary>Returns no conversations because durable storage is disabled.</summary>
    public Task<IReadOnlyList<ConversationRecord>> ListAsync(
        string ownerUserId, int top = 100, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ConversationRecord>>([]);

    /// <summary>Rejects conversation renaming because durable storage is disabled.</summary>
    public Task RenameAsync(
        string conversationId, string title, string? ownerUserId, CancellationToken cancellationToken = default) =>
        Task.FromException(Disabled());

    /// <summary>Completes without deleting data because durable storage is disabled.</summary>
    public Task DeleteAsync(
        string conversationId, string? ownerUserId, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    /// <summary>Rejects conversation claiming because durable storage is disabled.</summary>
    public Task ClaimAsync(
        IEnumerable<string> conversationIds, string ownerUserId, CancellationToken cancellationToken = default) =>
        Task.FromException(Disabled());

    /// <summary>Returns no completed turn because durable storage is disabled.</summary>
    public Task<ConversationTurnRecord?> GetTurnAsync(
        string conversationId, string idempotencyKey, CancellationToken cancellationToken = default) =>
        Task.FromResult<ConversationTurnRecord?>(null);

    /// <summary>Returns no completed turns because durable storage is disabled.</summary>
    public Task<IReadOnlyList<ConversationTurnRecord>> ListTurnsAsync(
        string conversationId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ConversationTurnRecord>>([]);

    /// <summary>Rejects turn persistence because durable storage is disabled.</summary>
    public Task<ConversationRecord> SaveTurnAsync(
        ConversationRecord expected, string payloadJson, string idempotencyKey,
        string userMessage, string answer, string eventsJson, string runIdsJson,
        string attachmentsJson, CancellationToken cancellationToken = default) =>
        Task.FromException<ConversationRecord>(Disabled());

    private static InvalidOperationException Disabled() =>
        new("Durable conversations require Sql:Enabled=true.");
}
