namespace OcrAgent.Ocr;

/// <summary>Subset of Tesseract page-segmentation modes used by the engine.</summary>
public enum OcrPageSegMode
{
    /// <summary>Automatically selects a page layout.</summary>
    Auto = 3,

    /// <summary>Treats the image as one text column.</summary>
    SingleColumn = 4,

    /// <summary>Treats the image as one uniform text block.</summary>
    SingleBlock = 6,

    /// <summary>Treats the image as one text line.</summary>
    SingleLine = 7,

    /// <summary>Treats the image as one word.</summary>
    SingleWord = 8,

    /// <summary>Treats the image as one character.</summary>
    SingleChar = 10,
}
