namespace OcrAgent.Ocr;

/// <summary>Language, page segmentation, DPI, and Tesseract overrides for OCR.</summary>
public sealed class OcrOptions
{
    public string Language { get; set; } = "eng";
    public OcrPageSegMode PageSegMode { get; set; } = OcrPageSegMode.Auto;
    public int Dpi { get; set; }
    public Dictionary<string, string> ConfigVars { get; set; } = new();
}
