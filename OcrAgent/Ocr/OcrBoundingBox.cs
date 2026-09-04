using System.Text.Json.Serialization;

namespace OcrAgent.Ocr;

/// <summary>Top-left corner and size in image pixels.</summary>
public sealed class OcrBoundingBox
{
    /// <summary>Gets or sets the left edge in pixels.</summary>
    [JsonPropertyName("x")]
    public int X { get; set; }

    /// <summary>Gets or sets the top edge in pixels.</summary>
    [JsonPropertyName("y")]
    public int Y { get; set; }

    /// <summary>Gets or sets the width in pixels.</summary>
    [JsonPropertyName("width")]
    public int Width { get; set; }

    /// <summary>Gets or sets the height in pixels.</summary>
    [JsonPropertyName("height")]
    public int Height { get; set; }
}
