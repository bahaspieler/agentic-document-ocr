namespace OcrAgent.Sql;

/// <summary>Disabled session store used by local/tool-only configurations.</summary>
public sealed class NullSessionStore : ISessionStore
{
    public static NullSessionStore Instance { get; } = new();
    public bool IsEnabled => false;
    private NullSessionStore() { }

    public Task<ConversationRecord> CreateAsync(
        string conversationId, string? ownerUserId, string title, CancellationToken cancellationToken = default) =>
        Task.FromException<ConversationRecord>(Disabled());

    public Task<ConversationRecord?> GetAsync(string conversationId, CancellationToken cancellationToken = default) =>
        Task.FromResult<ConversationRecord?>(null);

    public Task<IReadOnlyList<ConversationRecord>> ListAsync(
        string ownerUserId, int top = 100, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ConversationRecord>>([]);

    public Task RenameAsync(
        string conversationId, string title, string? ownerUserId, CancellationToken cancellationToken = default) =>
        Task.FromException(Disabled());

    public Task DeleteAsync(
        string conversationId, string? ownerUserId, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task ClaimAsync(
        IEnumerable<string> conversationIds, string ownerUserId, CancellationToken cancellationToken = default) =>
        Task.FromException(Disabled());

    public Task<ConversationTurnRecord?> GetTurnAsync(
        string conversationId, string idempotencyKey, CancellationToken cancellationToken = default) =>
        Task.FromResult<ConversationTurnRecord?>(null);

    public Task<IReadOnlyList<ConversationTurnRecord>> ListTurnsAsync(
        string conversationId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ConversationTurnRecord>>([]);

    public Task<ConversationRecord> SaveTurnAsync(
        ConversationRecord expected, string payloadJson, string idempotencyKey,
        string userMessage, string answer, CancellationToken cancellationToken = default) =>
        Task.FromException<ConversationRecord>(Disabled());

    private static InvalidOperationException Disabled() =>
        new("Durable conversations require Sql:Enabled=true.");
}
