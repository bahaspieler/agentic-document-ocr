using System.Text.Json.Serialization;

namespace OcrAgent.Extraction.Models;

/// <summary>
/// Per-field confidence as a 0–1 value plus an origin label (e.g. rules, OCR).
/// </summary>
public sealed class FieldConfidence
{
    /// <summary>Confidence in the 0–1 range.</summary>
    [JsonPropertyName("value")]
    public double Value { get; set; }

    /// <summary>Where the score came from (e.g. rules, none).</summary>
    [JsonPropertyName("source")]
    public string Source { get; set; } = "rules";

    /// <summary>Fixed high score with optional source label.</summary>
    /// <param name="source">Origin label for serialization.</param>
    /// <returns>Instance with value 0.95.</returns>
    public static FieldConfidence High(string source = "rules") => new() { Value = 0.95, Source = source };

    /// <summary>Fixed medium score with optional source label.</summary>
    /// <param name="source">Origin label for serialization.</param>
    /// <returns>Instance with value 0.7.</returns>
    public static FieldConfidence Medium(string source = "rules") => new() { Value = 0.7, Source = source };

    /// <summary>Fixed low score with optional source label.</summary>
    /// <param name="source">Origin label for serialization.</param>
    /// <returns>Instance with value 0.4.</returns>
    public static FieldConfidence Low(string source = "rules") => new() { Value = 0.4, Source = source };

    /// <summary>Zero confidence, source "none".</summary>
    /// <returns>Empty confidence instance.</returns>
    public static FieldConfidence None() => new() { Value = 0.0, Source = "none" };

    /// <summary>Maps engine percent confidence to 0–1.</summary>
    /// <param name="ocrConfidence">OCR score, typically 0–100.</param>
    /// <param name="source">Origin label for serialization.</param>
    /// <returns>Clamped 0–1 confidence.</returns>
    public static FieldConfidence FromOcr(double ocrConfidence, string source = "rules") =>
        new() { Value = Math.Clamp(ocrConfidence / 100.0, 0.0, 1.0), Source = source };
}
