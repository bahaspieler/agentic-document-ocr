using OcrAgent.Ocr;

namespace OcrAgent.Extraction;

/// <summary>
/// Builds structured receipt data from OCR output.
/// </summary>
public interface IReceiptExtractor
{
    /// <summary>Extracts structured receipt fields from OCR output.</summary>
    /// <param name="ocrResult">Lines and words from the OCR engine.</param>
    /// <param name="imagePath">Optional image path for extractors that read pixels.</param>
    /// <returns>Parsed receipt, confidence, extractor name, and duration.</returns>
    Task<ExtractionResult> ExtractAsync(OcrResult ocrResult, string? imagePath = null);
}
