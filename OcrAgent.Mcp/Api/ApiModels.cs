using System.Text.Json;
using OcrAgent.Extraction.Models;

namespace OcrAgent.Mcp.Api;

/// <summary>Creates a durable Web conversation.</summary>
public sealed record CreateConversationRequest(string? Title);

/// <summary>Changes the title of an accessible conversation.</summary>
public sealed record RenameConversationRequest(string Title);

/// <summary>Assigns anonymous conversations to the authenticated user.</summary>
public sealed record ClaimConversationsRequest(string[] ConversationIds);

/// <summary>References an uploaded image attached to a conversation turn.</summary>
public sealed record ChatAttachment(string Reference, string? Name, string? ContentType);

/// <summary>Submits one idempotent message to a durable conversation.</summary>
/// <param name="Message">Natural-language instruction for the agent.</param>
/// <param name="IdempotencyKey">Required key used to replay a completed turn without rerunning it.</param>
/// <param name="Attachments">Uploaded images available to the turn.</param>
public sealed record ChatMessageRequest(
    string Message,
    string? IdempotencyKey,
    IReadOnlyList<ChatAttachment>? Attachments = null);

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

/// <summary>Stores a corrected receipt as a new revision.</summary>
/// <remarks>The rowversion must match the current receipt to prevent a lost update.</remarks>
public sealed record ReceiptUpdateRequest(
    ReceiptData Receipt,
    string RowVersion,
    string? ConversationId,
    string? ChangeSummary,
    bool Verified = false);
