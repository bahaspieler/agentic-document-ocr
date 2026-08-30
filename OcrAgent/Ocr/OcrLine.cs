using System.Text.Json.Serialization;

namespace OcrAgent.Ocr;

/// <summary>One recognized text line with confidence, geometry, and words.</summary>
public sealed class OcrLine
{
    [JsonPropertyName("text")]
    public string Text { get; set; } = "";

    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }

    [JsonPropertyName("boundingBox")]
    public OcrBoundingBox BoundingBox { get; set; } = new();

    [JsonPropertyName("words")]
    public List<OcrWord> Words { get; set; } = new();
}
