using System.Text.Json.Serialization;

namespace OcrAgent.Ocr;

/// <summary>
/// Recognized text and confidence from an OCR run, with line and word layout.
/// </summary>
public sealed class OcrResult
{
    /// <summary>Full document text.</summary>
    [JsonPropertyName("text")]
    public string Text { get; set; } = "";

    /// <summary>Mean confidence (0–100) across words or from the page.</summary>
    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }

    /// <summary>Lines in reading order, each with words and boxes.</summary>
    [JsonPropertyName("lines")]
    public List<OcrLine> Lines { get; set; } = new();

    /// <summary>Name of the engine that produced this result.</summary>
    [JsonPropertyName("engineName")]
    public string EngineName { get; set; } = "";
}
