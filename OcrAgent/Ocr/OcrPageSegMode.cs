namespace OcrAgent.Ocr;

/// <summary>Subset of Tesseract page-segmentation modes used by the engine.</summary>
public enum OcrPageSegMode
{
    Auto = 3,
    SingleColumn = 4,
    SingleBlock = 6,
    SingleLine = 7,
    SingleWord = 8,
    SingleChar = 10,
}
