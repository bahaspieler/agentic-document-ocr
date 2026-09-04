namespace OcrAgent.Plugins.Ocr;

/// <summary>
/// Contains the scoring breakdown for one OCR result.
/// </summary>
public sealed class OcrScore
{
    /// <summary>Gets or sets the total score from zero to 100.</summary>
    public double Score { get; set; }

    /// <summary>Gets or sets the number of recognized words.</summary>
    public int WordCount { get; set; }

    /// <summary>Gets or sets the number of recognized text lines.</summary>
    public int LineCount { get; set; }

    /// <summary>Gets or sets whether decimal amount tokens were found.</summary>
    public bool HasAmounts { get; set; }

    /// <summary>Gets or sets whether a date-like pattern was found.</summary>
    public bool HasDate { get; set; }

    /// <summary>Gets or sets whether a total keyword was found.</summary>
    public bool HasTotal { get; set; }

    /// <summary>Gets or sets the mean recognized-word confidence.</summary>
    public double AvgConfidence { get; set; }

    /// <summary>Gets or sets the average number of characters per line.</summary>
    public double TextDensity { get; set; }

    /// <summary>Gets or sets the binarization variant label.</summary>
    public string Variant { get; set; } = "";

    /// <summary>Gets or sets the page segmentation mode label.</summary>
    public string Psm { get; set; } = "";
}
