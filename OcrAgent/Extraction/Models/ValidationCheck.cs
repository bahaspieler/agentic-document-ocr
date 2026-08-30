using System.Text.Json.Serialization;

namespace OcrAgent.Extraction.Models;

/// <summary>One validation rule with a pass flag and optional detail.</summary>
public sealed class ValidationCheck
{
    /// <summary>Stable identifier for the rule.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>Whether the check passed.</summary>
    [JsonPropertyName("passed")]
    public bool Passed { get; set; }

    /// <summary>Human-readable values or explanation.</summary>
    [JsonPropertyName("detail")]
    public string? Detail { get; set; }
}
