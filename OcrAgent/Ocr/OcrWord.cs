using System.Text.Json.Serialization;

namespace OcrAgent.Ocr;

/// <summary>One recognized word with confidence and pixel geometry.</summary>
public sealed class OcrWord
{
    /// <summary>Gets or sets the recognized word text.</summary>
    [JsonPropertyName("text")]
    public string Text { get; set; } = "";

    /// <summary>Gets or sets the OCR confidence for the word.</summary>
    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }

    /// <summary>Gets or sets the bounding box enclosing the word.</summary>
    [JsonPropertyName("boundingBox")]
    public OcrBoundingBox BoundingBox { get; set; } = new();
}
