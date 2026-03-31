using System.Text.Json.Serialization;

namespace OcrAgent.Extraction.Models;

/// <summary>
/// Full receipt payload: merchant, transaction, totals, line items, and validation.
/// </summary>
public sealed class ReceiptData
{
    /// <summary>Stable id for this receipt in exports.</summary>
    [JsonPropertyName("receiptId")]
    public string ReceiptId { get; set; } = "";

    /// <summary>Provenance: image ref, workflow, OCR engine, mode.</summary>
    [JsonPropertyName("source")]
    public SourceInfo Source { get; set; } = new();

    /// <summary>Merchant block from the header.</summary>
    [JsonPropertyName("merchant")]
    public MerchantInfo Merchant { get; set; } = new();

    /// <summary>Date, time, and receipt metadata.</summary>
    [JsonPropertyName("transaction")]
    public TransactionInfo Transaction { get; set; } = new();

    /// <summary>Money summary: subtotal, tax, total, payment.</summary>
    [JsonPropertyName("totals")]
    public TotalsInfo Totals { get; set; } = new();

    /// <summary>Purchased lines from the body zone.</summary>
    [JsonPropertyName("items")]
    public List<ReceiptLineItem> Items { get; set; } = new();

    /// <summary>Cross-checks and score after extraction.</summary>
    [JsonPropertyName("validation")]
    public ValidationInfo Validation { get; set; } = new();
}
