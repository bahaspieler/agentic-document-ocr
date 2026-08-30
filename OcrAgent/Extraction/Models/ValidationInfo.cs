using System.Text.Json.Serialization;

namespace OcrAgent.Extraction.Models;

/// <summary>
/// Result of post-extraction checks: pass flag, score, checks, warnings.
/// </summary>
public sealed class ValidationInfo
{
    /// <summary>True when <see cref="ValidationScore"/> meets the threshold.</summary>
    [JsonPropertyName("isValid")]
    public bool IsValid { get; set; }

    /// <summary>0–100 aggregate score from fields and checks.</summary>
    [JsonPropertyName("validationScore")]
    public int ValidationScore { get; set; }

    /// <summary>Human-review state derived from consistency, OCR, and field confidence.</summary>
    [JsonPropertyName("reviewStatus")]
    public string ReviewStatus { get; set; } = "needs_review";

    /// <summary>Individual rule outcomes.</summary>
    [JsonPropertyName("checks")]
    public List<ValidationCheck> Checks { get; set; } = new();

    /// <summary>Non-fatal issues (missing fields, etc.).</summary>
    [JsonPropertyName("warnings")]
    public List<string> Warnings { get; set; } = new();
}
