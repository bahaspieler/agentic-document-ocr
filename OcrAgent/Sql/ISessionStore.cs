namespace OcrAgent.Sql;

/// <summary>Durable conversation metadata, model session payloads, and completed turns.</summary>
public interface ISessionStore
{
    /// <summary>Gets whether durable conversation storage is available.</summary>
    bool IsEnabled { get; }

    /// <summary>Creates a conversation with its initial metadata.</summary>
    Task<ConversationRecord> CreateAsync(
        string conversationId, string? ownerUserId, string title,
        CancellationToken cancellationToken = default);

    /// <summary>Gets a conversation by its identifier.</summary>
    Task<ConversationRecord?> GetAsync(string conversationId, CancellationToken cancellationToken = default);

    /// <summary>Lists an owner's most recently active conversations.</summary>
    Task<IReadOnlyList<ConversationRecord>> ListAsync(
        string ownerUserId, int top = 100, CancellationToken cancellationToken = default);

    /// <summary>Renames a conversation owned by the specified user.</summary>
    Task RenameAsync(
        string conversationId, string title, string? ownerUserId,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes a conversation owned by the specified user.</summary>
    Task DeleteAsync(
        string conversationId, string? ownerUserId, CancellationToken cancellationToken = default);

    /// <summary>Assigns unowned conversations to a user.</summary>
    Task ClaimAsync(
        IEnumerable<string> conversationIds, string ownerUserId,
        CancellationToken cancellationToken = default);

    /// <summary>Gets a completed turn by its conversation and idempotency key.</summary>
    Task<ConversationTurnRecord?> GetTurnAsync(
        string conversationId, string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>Lists a conversation's completed turns in turn order.</summary>
    Task<IReadOnlyList<ConversationTurnRecord>> ListTurnsAsync(
        string conversationId, CancellationToken cancellationToken = default);

    /// <summary>Atomically saves updated conversation state and its completed turn.</summary>
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
