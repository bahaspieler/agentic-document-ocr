namespace OcrAgent.Sql;

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
