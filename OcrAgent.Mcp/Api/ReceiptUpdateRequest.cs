using OcrAgent.Extraction.Models;

namespace OcrAgent.Mcp.Api;

/// <summary>Stores a corrected receipt as a new revision.</summary>
/// <remarks>The rowversion must match the current receipt to prevent a lost update.</remarks>
public sealed record ReceiptUpdateRequest(
    ReceiptData Receipt,
    string RowVersion,
    string? ConversationId,
    string? ChangeSummary,
    bool Verified = false);
