using System.Text.Json.Serialization;

namespace OcrAgent.Ocr;

/// <summary>Top-left corner and size in image pixels.</summary>
public sealed class OcrBoundingBox
{
    [JsonPropertyName("x")]
    public int X { get; set; }

    [JsonPropertyName("y")]
    public int Y { get; set; }

    [JsonPropertyName("width")]
    public int Width { get; set; }

    [JsonPropertyName("height")]
    public int Height { get; set; }
}
