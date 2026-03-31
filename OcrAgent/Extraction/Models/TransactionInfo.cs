using System.Text.Json.Serialization;

namespace OcrAgent.Extraction.Models;

/// <summary>
/// When the sale happened and optional register or receipt numbers.
/// </summary>
public sealed class TransactionInfo
{
    /// <summary>ISO 8601 date-time string when parsed.</summary>
    [JsonPropertyName("dateTime")]
    public string? DateTime { get; set; }

    /// <summary>Confidence for <see cref="DateTime"/>.</summary>
    [JsonPropertyName("dateTimeConfidence")]
    public FieldConfidence DateTimeConfidence { get; set; } = FieldConfidence.None();

    /// <summary>Receipt or order number from keywords.</summary>
    [JsonPropertyName("receiptNumber")]
    public string? ReceiptNumber { get; set; }

    /// <summary>Store id if present on the slip.</summary>
    [JsonPropertyName("storeId")]
    public string? StoreId { get; set; }

    /// <summary>Register or terminal id.</summary>
    [JsonPropertyName("registerId")]
    public string? RegisterId { get; set; }

    /// <summary>Cashier name or id if printed.</summary>
    [JsonPropertyName("cashier")]
    public string? Cashier { get; set; }
}
