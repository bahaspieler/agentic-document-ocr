using System.Text.Json.Serialization;

namespace OcrAgent.Ocr;

/// <summary>One recognized text line with confidence, geometry, and words.</summary>
public sealed class OcrLine
{
    /// <summary>Gets or sets the recognized line text.</summary>
    [JsonPropertyName("text")]
    public string Text { get; set; } = "";

    /// <summary>Gets or sets the OCR confidence for the line.</summary>
    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }

    /// <summary>Gets or sets the bounding box enclosing the line.</summary>
    [JsonPropertyName("boundingBox")]
    public OcrBoundingBox BoundingBox { get; set; } = new();

    /// <summary>Gets or sets the recognized words in the line.</summary>
    [JsonPropertyName("words")]
    public List<OcrWord> Words { get; set; } = new();
}
