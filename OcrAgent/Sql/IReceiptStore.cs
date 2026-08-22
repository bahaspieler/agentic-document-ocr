using OcrAgent.Extraction.Models;

namespace OcrAgent.Sql;

/// <summary>Queryable structured receipt data.</summary>
public interface IReceiptStore
{
    bool IsEnabled { get; }
    Task SaveAsync(
        ReceiptData receipt, string? runId, string? conversationId,
        CancellationToken cancellationToken = default);
    Task<ReceiptRecord?> GetAsync(string receiptId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ReceiptRecord>> QueryAsync(
        string? merchant = null,
        DateTime? fromUtc = null,
        DateTime? toUtc = null,
        int top = 100,
        CancellationToken cancellationToken = default);
}

public sealed record ReceiptRecord(
    string ReceiptId,
    string? RunId,
    string? ConversationId,
    string? Merchant,
    DateTime? TransactionUtc,
    string Currency,
    decimal? Subtotal,
    decimal? Tax,
    decimal? Total,
    bool IsValid,
    int ValidationScore,
    DateTime CreatedUtc,
    string RawJson);
