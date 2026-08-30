namespace OcrAgent.Mcp.Api;

/// <summary>Submits one idempotent message to a durable conversation.</summary>
/// <param name="Message">Natural-language instruction for the agent.</param>
/// <param name="IdempotencyKey">Required key used to replay a completed turn without rerunning it.</param>
/// <param name="Attachments">Uploaded images available to the turn.</param>
public sealed record ChatMessageRequest(
    string Message,
    string? IdempotencyKey,
    IReadOnlyList<ChatAttachment>? Attachments = null);
