using OcrAgent.Extraction.Models;
using OcrAgent.Ocr;

namespace OcrAgent.Extraction;

/// <summary>
/// Builds structured receipt data from OCR output.
/// </summary>
public interface IReceiptExtractor
{
    /// <param name="ocrResult">Lines and words from the OCR engine.</param>
    /// <param name="imagePath">Optional image path for extractors that read pixels.</param>
    /// <returns>Parsed receipt, confidence, extractor name, and duration.</returns>
    Task<ExtractionResult> ExtractAsync(OcrResult ocrResult, string? imagePath = null);
}

/// <summary>
/// Parsed receipt data with merchant, items, and totals.
/// </summary>
public sealed class ExtractionResult
{
    /// <summary>Structured fields from the extractor.</summary>
    public ReceiptData Receipt { get; set; } = new();

    /// <summary>Aggregate score in the 0–1 range.</summary>
    public double OverallConfidence { get; set; }

    /// <summary>Name of the extractor implementation.</summary>
    public string ExtractorName { get; set; } = "";

    /// <summary>Elapsed time in milliseconds.</summary>
    public long DurationMs { get; set; }
}
