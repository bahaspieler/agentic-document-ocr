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
    Task<ReceiptRecord> UpdateAsync(
        string receiptId,
        ReceiptData corrected,
        byte[] expectedRowVersion,
        string editorIdentity,
        string? changeSummary = null,
        bool verified = false,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ReceiptRevisionRecord>> ListRevisionsAsync(
        string receiptId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ReceiptRecord>> QueryAsync(
        string? merchant = null,
        DateTime? fromUtc = null,
        DateTime? toUtc = null,
        int top = 100,
        CancellationToken cancellationToken = default);
}
