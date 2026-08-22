namespace OcrAgent.Sql;

/// <summary>Durable metadata and serialized model state for one conversation.</summary>
public sealed record ConversationRecord(
    string Id,
    string? OwnerUserId,
    string Title,
    DateTime CreatedUtc,
    DateTime LastActiveUtc,
    int TurnCount,
    int SchemaVersion,
    string? PayloadJson,
    byte[] RowVersion);

/// <summary>A completed turn, also used as the response to an idempotent retry.</summary>
public sealed record ConversationTurnRecord(
    string ConversationId,
    string IdempotencyKey,
    int TurnIndex,
    DateTime CreatedUtc,
    string UserMessage,
    string Answer,
    string EventsJson,
    string RunIdsJson,
    string AttachmentsJson);

/// <summary>Signals that another request updated the conversation before this one saved.</summary>
public sealed class ConversationConcurrencyException(string conversationId)
    : Exception($"Conversation '{conversationId}' changed while this turn was running.");
