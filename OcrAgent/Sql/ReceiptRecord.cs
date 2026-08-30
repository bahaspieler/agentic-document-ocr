namespace OcrAgent.Sql;

/// <summary>Current durable receipt state and its optimistic-concurrency token.</summary>
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
