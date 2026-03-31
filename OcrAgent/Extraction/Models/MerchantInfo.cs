using System.Text.Json.Serialization;

namespace OcrAgent.Extraction.Models;

/// <summary>
/// Merchant name, phone, and address parts from the receipt header.
/// </summary>
public sealed class MerchantInfo
{
    /// <summary>Store or business name.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>Confidence for <see cref="Name"/>.</summary>
    [JsonPropertyName("nameConfidence")]
    public FieldConfidence NameConfidence { get; set; } = FieldConfidence.None();

    /// <summary>Contact phone if detected.</summary>
    [JsonPropertyName("phone")]
    public string? Phone { get; set; }

    /// <summary>Confidence for <see cref="Phone"/>.</summary>
    [JsonPropertyName("phoneConfidence")]
    public FieldConfidence PhoneConfidence { get; set; } = FieldConfidence.None();

    /// <summary>Raw address lines in reading order.</summary>
    [JsonPropertyName("addressLines")]
    public List<string> AddressLines { get; set; } = new();

    /// <summary>City parsed from address, when found.</summary>
    [JsonPropertyName("city")]
    public string? City { get; set; }

    /// <summary>State or region code.</summary>
    [JsonPropertyName("stateOrRegion")]
    public string? StateOrRegion { get; set; }

    /// <summary>Postal or ZIP code.</summary>
    [JsonPropertyName("postalCode")]
    public string? PostalCode { get; set; }

    /// <summary>Country name or code if present.</summary>
    [JsonPropertyName("country")]
    public string? Country { get; set; }

    /// <summary>Confidence for the address block.</summary>
    [JsonPropertyName("addressConfidence")]
    public FieldConfidence AddressConfidence { get; set; } = FieldConfidence.None();
}
