using OcrAgent.Ocr;

namespace OcrAgent.Extraction.RuleBased;

/// <summary>Represents one OCR word after token normalization.</summary>
public sealed class NormalizedWord
{
    /// <summary>Gets or sets the normalized word text.</summary>
    public string Text { get; set; } = "";

    /// <summary>Gets or sets the source word's OCR confidence.</summary>
    public double Confidence { get; set; }

    /// <summary>Gets or sets the word's bounding box.</summary>
    public OcrBoundingBox BoundingBox { get; set; } = new();
}
