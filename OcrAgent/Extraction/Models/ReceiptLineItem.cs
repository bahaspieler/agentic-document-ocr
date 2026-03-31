using System.Text.Json.Serialization;

namespace OcrAgent.Extraction.Models;

/// <summary>
/// One purchased line: description, optional qty/price, line total, flags.
/// </summary>
public sealed class ReceiptLineItem
{
    /// <summary>0-based order in the body list.</summary>
    [JsonPropertyName("index")]
    public int Index { get; set; }

    /// <summary>Product or service text.</summary>
    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    /// <summary>Units bought, when parsed.</summary>
    [JsonPropertyName("quantity")]
    public decimal? Quantity { get; set; }

    /// <summary>Price per unit, when parsed.</summary>
    [JsonPropertyName("unitPrice")]
    public decimal? UnitPrice { get; set; }

    /// <summary>Line amount before receipt-level totals.</summary>
    [JsonPropertyName("lineTotal")]
    public decimal? LineTotal { get; set; }

    /// <summary>Single-letter tax code from OCR, if any.</summary>
    [JsonPropertyName("taxIndicator")]
    public string? TaxIndicator { get; set; }

    /// <summary>True when the line is tagged as a discount.</summary>
    [JsonPropertyName("discountIndicator")]
    public bool DiscountIndicator { get; set; }

    /// <summary>Confidence for this line’s parsing.</summary>
    [JsonPropertyName("confidence")]
    public FieldConfidence Confidence { get; set; } = FieldConfidence.None();
}
