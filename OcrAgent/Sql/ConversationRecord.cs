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
