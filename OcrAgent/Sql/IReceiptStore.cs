using OcrAgent.Extraction.Models;

namespace OcrAgent.Sql;

/// <summary>Queryable structured receipt data.</summary>
public interface IReceiptStore
{
    /// <summary>Gets whether durable receipt storage is available.</summary>
    bool IsEnabled { get; }

    /// <summary>Saves an extracted receipt unless its receipt identifier already exists.</summary>
    Task SaveAsync(
        ReceiptData receipt, string? runId, string? conversationId,
        CancellationToken cancellationToken = default);

    /// <summary>Gets a receipt by its identifier.</summary>
    Task<ReceiptRecord?> GetAsync(string receiptId, CancellationToken cancellationToken = default);

    /// <summary>Applies a reviewed correction when the expected receipt version still matches.</summary>
    Task<ReceiptRecord> UpdateAsync(
        string receiptId,
        ReceiptData corrected,
        byte[] expectedRowVersion,
        string editorIdentity,
        string? changeSummary = null,
        bool verified = false,
        CancellationToken cancellationToken = default);

    /// <summary>Lists the recorded corrections for a receipt in descending revision order.</summary>
    Task<IReadOnlyList<ReceiptRevisionRecord>> ListRevisionsAsync(
        string receiptId, CancellationToken cancellationToken = default);

    /// <summary>Queries recent receipts by optional merchant and transaction date filters.</summary>
    Task<IReadOnlyList<ReceiptRecord>> QueryAsync(
        string? merchant = null,
        DateTime? fromUtc = null,
        DateTime? toUtc = null,
        int top = 100,
        CancellationToken cancellationToken = default);
}
