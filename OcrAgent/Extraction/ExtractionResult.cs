using OcrAgent.Extraction.Models;

namespace OcrAgent.Extraction;

/// <summary>Parsed receipt data with confidence, extractor identity, and duration.</summary>
public sealed class ExtractionResult
{
    /// <summary>Gets or sets the structured receipt produced by extraction.</summary>
    public ReceiptData Receipt { get; set; } = new();

    /// <summary>Gets or sets the aggregate extraction confidence.</summary>
    public double OverallConfidence { get; set; }

    /// <summary>Gets or sets the name of the extractor implementation.</summary>
    public string ExtractorName { get; set; } = "";

    /// <summary>Gets or sets the extraction duration in milliseconds.</summary>
    public long DurationMs { get; set; }
}
