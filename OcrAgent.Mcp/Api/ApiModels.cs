using System.Text.Json;

namespace OcrAgent.Mcp.Api;

public sealed record CreateConversationRequest(string? Title);
public sealed record RenameConversationRequest(string Title);
public sealed record ClaimConversationsRequest(string[] ConversationIds);
public sealed record ChatMessageRequest(string Message, string? IdempotencyKey);
public sealed record JobRequest(
    string Mode,
    string Name,
    string? ImageReference,
    string? OverridesJson,
    Dictionary<string, JsonElement>? Arguments);
