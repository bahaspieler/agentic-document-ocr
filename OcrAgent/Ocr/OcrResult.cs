using System.Text.Json.Serialization;

namespace OcrAgent.Ocr;

/// <summary>
/// Recognized text and confidence from an OCR run, with line and word layout.
/// </summary>
public sealed class OcrResult
{
    /// <summary>Full document text.</summary>
    [JsonPropertyName("text")]
    public string Text { get; set; } = "";

    /// <summary>Mean confidence (0–100) across words or from the page.</summary>
    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }

    /// <summary>Lines in reading order, each with words and boxes.</summary>
    [JsonPropertyName("lines")]
    public List<OcrLine> Lines { get; set; } = new();

    /// <summary>Name of the engine that produced this result.</summary>
    [JsonPropertyName("engineName")]
    public string EngineName { get; set; } = "";
}

/// <summary>
/// One text line: aggregated text, mean confidence, line box, and words.
/// </summary>
public sealed class OcrLine
{
    /// <summary>Line text (words joined with spaces).</summary>
    [JsonPropertyName("text")]
    public string Text { get; set; } = "";

    /// <summary>Mean word confidence for this line.</summary>
    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }

    /// <summary>Axis-aligned box for the whole line.</summary>
    [JsonPropertyName("boundingBox")]
    public OcrBoundingBox BoundingBox { get; set; } = new();

    /// <summary>Words on this line, in order.</summary>
    [JsonPropertyName("words")]
    public List<OcrWord> Words { get; set; } = new();
}

/// <summary>
/// Single word token with confidence and bounding box.
/// </summary>
public sealed class OcrWord
{
    /// <summary>Word text.</summary>
    [JsonPropertyName("text")]
    public string Text { get; set; } = "";

    /// <summary>Tesseract confidence for this word (0–100).</summary>
    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }

    /// <summary>Axis-aligned box for this word.</summary>
    [JsonPropertyName("boundingBox")]
    public OcrBoundingBox BoundingBox { get; set; } = new();
}

/// <summary>
/// Top-left corner and size in image pixels.
/// </summary>
public sealed class OcrBoundingBox
{
    /// <summary>Left edge (pixels).</summary>
    [JsonPropertyName("x")]
    public int X { get; set; }

    /// <summary>Top edge (pixels).</summary>
    [JsonPropertyName("y")]
    public int Y { get; set; }

    /// <summary>Width (pixels).</summary>
    [JsonPropertyName("width")]
    public int Width { get; set; }

    /// <summary>Height (pixels).</summary>
    [JsonPropertyName("height")]
    public int Height { get; set; }
}

/// <summary>
/// Options passed into <see cref="IOcrEngine.RecognizeAsync"/>.
/// </summary>
public sealed class OcrOptions
{
    /// <summary>Tesseract language code (e.g. eng).</summary>
    public string Language { get; set; } = "eng";

    /// <summary>Page segmentation mode passed to Tesseract.</summary>
    public OcrPageSegMode PageSegMode { get; set; } = OcrPageSegMode.Auto;

    /// <summary>0 = Tesseract chooses DPI; &gt; 0 sets X/Y resolution on the loaded image.</summary>
    public int Dpi { get; set; } = 0;

    /// <summary>Tesseract <c>-c key=value</c> overrides (e.g. turn off DAWGs for noisy receipt text).</summary>
    public Dictionary<string, string> ConfigVars { get; set; } = new();
}

/// <summary>
/// Subset of Tesseract <c>PageSegMode</c> values used by this project.
/// </summary>
public enum OcrPageSegMode
{
    /// <summary>Fully automatic page segmentation (PSM 3).</summary>
    Auto = 3,
    /// <summary>Single column of text (PSM 4).</summary>
    SingleColumn = 4,
    /// <summary>Single uniform block (PSM 6).</summary>
    SingleBlock = 6,
    /// <summary>Single text line (PSM 7).</summary>
    SingleLine = 7,
    /// <summary>Single word (PSM 8).</summary>
    SingleWord = 8,
    /// <summary>Single character (PSM 10).</summary>
    SingleChar = 10
}
