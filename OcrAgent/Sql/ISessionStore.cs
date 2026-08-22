namespace OcrAgent.Sql;

/// <summary>Durable conversation metadata, model session payloads, and completed turns.</summary>
public interface ISessionStore
{
    bool IsEnabled { get; }
    Task<ConversationRecord> CreateAsync(
        string conversationId, string? ownerUserId, string title,
        CancellationToken cancellationToken = default);
    Task<ConversationRecord?> GetAsync(string conversationId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ConversationRecord>> ListAsync(
        string ownerUserId, int top = 100, CancellationToken cancellationToken = default);
    Task RenameAsync(
        string conversationId, string title, string? ownerUserId,
        CancellationToken cancellationToken = default);
    Task DeleteAsync(
        string conversationId, string? ownerUserId, CancellationToken cancellationToken = default);
    Task ClaimAsync(
        IEnumerable<string> conversationIds, string ownerUserId,
        CancellationToken cancellationToken = default);
    Task<ConversationTurnRecord?> GetTurnAsync(
        string conversationId, string idempotencyKey,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ConversationTurnRecord>> ListTurnsAsync(
        string conversationId, CancellationToken cancellationToken = default);
    Task<ConversationRecord> SaveTurnAsync(
        ConversationRecord expected,
        string payloadJson,
        string idempotencyKey,
        string userMessage,
        string answer,
        string eventsJson,
        string runIdsJson,
        string attachmentsJson,
        CancellationToken cancellationToken = default);
}
