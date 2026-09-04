using OcrAgent.Ocr;

namespace OcrAgent.Extraction.RuleBased;

/// <summary>Represents one receipt line after word-level normalization.</summary>
public sealed class NormalizedLine
{
    /// <summary>Gets or sets the full text assembled from normalized words.</summary>
    public string Text { get; set; } = "";

    /// <summary>Gets or sets the source line's OCR confidence.</summary>
    public double Confidence { get; set; }

    /// <summary>Gets or sets the bounding box enclosing the line.</summary>
    public OcrBoundingBox BoundingBox { get; set; } = new();

    /// <summary>Gets or sets the normalized words in left-to-right order.</summary>
    public List<NormalizedWord> Words { get; set; } = new();

    /// <summary>Gets or sets the line's index in the source OCR result.</summary>
    public int OriginalIndex { get; set; }
}
