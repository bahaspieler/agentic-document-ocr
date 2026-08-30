using System.Text.Json.Serialization;

namespace OcrAgent.Ocr;

/// <summary>One recognized word with confidence and pixel geometry.</summary>
public sealed class OcrWord
{
    [JsonPropertyName("text")]
    public string Text { get; set; } = "";

    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }

    [JsonPropertyName("boundingBox")]
    public OcrBoundingBox BoundingBox { get; set; } = new();
}
