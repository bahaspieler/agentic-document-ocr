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

public sealed class ReceiptRecord
{
    public string ReceiptId { get; set; } = "";
    public string? RunId { get; set; }
    public string? ConversationId { get; set; }
    public string? Merchant { get; set; }
    public DateTime? TransactionUtc { get; set; }
    public string Currency { get; set; } = "";
    public decimal? Subtotal { get; set; }
    public decimal? Tax { get; set; }
    public decimal? Total { get; set; }
    public double OcrConfidence { get; set; }
    public bool IsValid { get; set; }
    public int ValidationScore { get; set; }
    public DateTime CreatedUtc { get; set; }
    public string RawJson { get; set; } = "";
    public string CurrentJson { get; set; } = "";
    public DateTime UpdatedUtc { get; set; }
    public string ReviewStatus { get; set; } = "needs_review";
    public int CurrentRevision { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed record ReceiptRevisionRecord(
    string ReceiptId,
    int RevisionNumber,
    string EditorIdentity,
    DateTime CreatedUtc,
    string CorrectedJson,
    string? ChangeSummary);

public sealed class ReceiptConcurrencyException(string receiptId)
    : InvalidOperationException($"Receipt '{receiptId}' changed before this edit could be saved.");
