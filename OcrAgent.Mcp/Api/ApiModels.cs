using System.Text.Json;
using OcrAgent.Extraction.Models;

namespace OcrAgent.Mcp.Api;

public sealed record CreateConversationRequest(string? Title);
public sealed record RenameConversationRequest(string Title);
public sealed record ClaimConversationsRequest(string[] ConversationIds);
public sealed record ChatAttachment(string Reference, string? Name, string? ContentType);
public sealed record ChatMessageRequest(
    string Message,
    string? IdempotencyKey,
    IReadOnlyList<ChatAttachment>? Attachments = null);
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
public sealed record ReceiptUpdateRequest(
    ReceiptData Receipt,
    string RowVersion,
    string? ConversationId,
    string? ChangeSummary,
    bool Verified = false);
