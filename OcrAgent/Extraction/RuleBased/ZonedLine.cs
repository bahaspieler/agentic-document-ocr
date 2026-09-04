namespace OcrAgent.Extraction.RuleBased;

/// <summary>Pairs a normalized OCR line with its assigned receipt zone.</summary>
public sealed class ZonedLine
{
    /// <summary>Gets or sets the normalized line content and geometry.</summary>
    public NormalizedLine Line { get; set; } = null!;

    /// <summary>Gets or sets the line's receipt zone.</summary>
    public ReceiptZone Zone { get; set; }
}
