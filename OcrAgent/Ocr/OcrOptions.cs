namespace OcrAgent.Ocr;

/// <summary>Language, page segmentation, DPI, and Tesseract overrides for OCR.</summary>
public sealed class OcrOptions
{
    /// <summary>Gets or sets the Tesseract language code.</summary>
    public string Language { get; set; } = "eng";

    /// <summary>Gets or sets the page segmentation strategy.</summary>
    public OcrPageSegMode PageSegMode { get; set; } = OcrPageSegMode.Auto;

    /// <summary>Gets or sets the source image resolution in dots per inch.</summary>
    public int Dpi { get; set; }

    /// <summary>Gets or sets additional Tesseract configuration variables.</summary>
    public Dictionary<string, string> ConfigVars { get; set; } = new();
}
