namespace OcrAgent.Ocr;

/// <summary>
/// Runs OCR on image files and returns structured text and layout.
/// </summary>
public interface IOcrEngine
{
    /// <summary>
    /// Reads the image at <paramref name="imagePath"/> and returns recognition output.
    /// </summary>
    /// <param name="imagePath">Path to the image file.</param>
    /// <param name="options">Language, page mode, DPI, and Tesseract variables; optional.</param>
    /// <returns>Recognized text, confidence, and line/word boxes.</returns>
    Task<OcrResult> RecognizeAsync(string imagePath, OcrOptions? options = null);
}
