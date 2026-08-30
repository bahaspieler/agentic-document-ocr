using System.Text.Json;

namespace OcrAgent.Mcp.Api;

/// <summary>Requests direct execution of one tool or workflow.</summary>
/// <remarks>
/// A conversation id links the run to durable history. When present, the idempotency key identifies
/// the stored turn; otherwise the run id is used.
/// </remarks>
public sealed record JobRequest(
    string Mode,
    string Name,
    string? ImageReference,
    string? OverridesJson,
    Dictionary<string, JsonElement>? Arguments,
    string? ConversationId = null,
    string? IdempotencyKey = null,
    string? UserMessage = null,
    string? AttachmentName = null,
    string? AttachmentContentType = null);
