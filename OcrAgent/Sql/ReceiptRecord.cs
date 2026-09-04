namespace OcrAgent.Sql;

/// <summary>Current durable receipt state and its optimistic-concurrency token.</summary>
public sealed class ReceiptRecord
{
    /// <summary>Gets or sets the immutable receipt identifier.</summary>
    public string ReceiptId { get; set; } = "";

    /// <summary>Gets or sets the extraction run identifier.</summary>
    public string? RunId { get; set; }

    /// <summary>Gets or sets the conversation associated with the receipt.</summary>
    public string? ConversationId { get; set; }

    /// <summary>Gets or sets the merchant name.</summary>
    public string? Merchant { get; set; }

    /// <summary>Gets or sets the transaction time in UTC.</summary>
    public DateTime? TransactionUtc { get; set; }

    /// <summary>Gets or sets the receipt currency code.</summary>
    public string Currency { get; set; } = "";

    /// <summary>Gets or sets the receipt subtotal.</summary>
    public decimal? Subtotal { get; set; }

    /// <summary>Gets or sets the receipt tax amount.</summary>
    public decimal? Tax { get; set; }

    /// <summary>Gets or sets the receipt total.</summary>
    public decimal? Total { get; set; }

    /// <summary>Gets or sets the OCR confidence score.</summary>
    public double OcrConfidence { get; set; }

    /// <summary>Gets or sets whether the extracted receipt passed validation.</summary>
    public bool IsValid { get; set; }

    /// <summary>Gets or sets the validation score.</summary>
    public int ValidationScore { get; set; }

    /// <summary>Gets or sets when the receipt was created in UTC.</summary>
    public DateTime CreatedUtc { get; set; }

    /// <summary>Gets or sets the original extracted receipt JSON.</summary>
    public string RawJson { get; set; } = "";

    /// <summary>Gets or sets the current receipt JSON after corrections.</summary>
    public string CurrentJson { get; set; } = "";

    /// <summary>Gets or sets when the receipt was last updated in UTC.</summary>
    public DateTime UpdatedUtc { get; set; }

    /// <summary>Gets or sets the current review status.</summary>
    public string ReviewStatus { get; set; } = "needs_review";

    /// <summary>Gets or sets the latest correction revision number.</summary>
    public int CurrentRevision { get; set; }

    /// <summary>Gets or sets the optimistic-concurrency row version.</summary>
    public byte[] RowVersion { get; set; } = [];
}
