using OcrAgent.Extraction.Models;

namespace OcrAgent.Extraction;

/// <summary>Parsed receipt data with confidence, extractor identity, and duration.</summary>
public sealed class ExtractionResult
{
    public ReceiptData Receipt { get; set; } = new();
    public double OverallConfidence { get; set; }
    public string ExtractorName { get; set; } = "";
    public long DurationMs { get; set; }
}
