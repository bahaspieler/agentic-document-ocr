using System.Text.Json.Serialization;

namespace OcrAgent.Extraction.Models;

/// <summary>
/// Money summary: subtotal, tax, total, payment, and related confidence.
/// </summary>
public sealed class TotalsInfo
{
    /// <summary>Pre-tax subtotal when detected.</summary>
    [JsonPropertyName("subtotal")]
    public decimal? Subtotal { get; set; }

    /// <summary>Sales or VAT line.</summary>
    [JsonPropertyName("tax")]
    public decimal? Tax { get; set; }

    /// <summary>Amount due or grand total.</summary>
    [JsonPropertyName("total")]
    public decimal? Total { get; set; }

    /// <summary>Net sales when labeled separately.</summary>
    [JsonPropertyName("netSales")]
    public decimal? NetSales { get; set; }

    /// <summary>Discount or savings amount.</summary>
    [JsonPropertyName("discount")]
    public decimal? Discount { get; set; }

    /// <summary>Tip or gratuity.</summary>
    [JsonPropertyName("tip")]
    public decimal? Tip { get; set; }

    /// <summary>Amount tendered or paid.</summary>
    [JsonPropertyName("paid")]
    public decimal? Paid { get; set; }

    /// <summary>Change returned.</summary>
    [JsonPropertyName("change")]
    public decimal? Change { get; set; }

    /// <summary>ISO-style currency code (default USD).</summary>
    [JsonPropertyName("currency")]
    public string Currency { get; set; } = "USD";

    /// <summary>Normalized payment type (card, cash, etc.).</summary>
    [JsonPropertyName("paymentMethod")]
    public string? PaymentMethod { get; set; }

    /// <summary>Confidence for <see cref="Total"/>.</summary>
    [JsonPropertyName("totalConfidence")]
    public FieldConfidence TotalConfidence { get; set; } = FieldConfidence.None();

    /// <summary>Confidence for <see cref="Subtotal"/>.</summary>
    [JsonPropertyName("subtotalConfidence")]
    public FieldConfidence SubtotalConfidence { get; set; } = FieldConfidence.None();

    /// <summary>Confidence for <see cref="Tax"/>.</summary>
    [JsonPropertyName("taxConfidence")]
    public FieldConfidence TaxConfidence { get; set; } = FieldConfidence.None();
}
