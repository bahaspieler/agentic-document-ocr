using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using System.ComponentModel;
using OcrAgent.Ocr;

namespace OcrAgent.Plugins.Ocr;

/// <summary>
/// Scores OCR results on a 0-100 scale using confidence, receipt-likeness signals,
/// and text density heuristics. Used by OcrPlugin to rank variant candidates.
/// </summary>
public sealed partial class OcrScoringPlugin : IImageProcessingTool
{
    /// <summary>Breakdown of a single OCR run used for ranking variants.</summary>
    public sealed class OcrScore
    {
        /// <summary>Total score on 0–100 after clamping.</summary>
        public double Score { get; set; }
        /// <summary>Word count from all lines.</summary>
        public int WordCount { get; set; }
        /// <summary>Number of text lines.</summary>
        public int LineCount { get; set; }
        /// <summary>True if decimal money-like tokens were found.</summary>
        public bool HasAmounts { get; set; }
        /// <summary>True if a date-like pattern matched.</summary>
        public bool HasDate { get; set; }
        /// <summary>True if a TOTAL keyword matched.</summary>
        public bool HasTotal { get; set; }
        /// <summary>Mean word confidence where confidence is in (0,100].</summary>
        public double AvgConfidence { get; set; }
        /// <summary>Average characters per line (same value used for density scoring).</summary>
        public double TextDensity { get; set; }
        /// <summary>Binarization variant label for logging.</summary>
        public string Variant { get; set; } = "";
        /// <summary>PSM label for logging.</summary>
        public string Psm { get; set; } = "";
    }

    /// <summary>
    /// Deserializes OCR JSON, computes a 0–100 score, and returns JSON of <see cref="OcrScore"/>.
    /// </summary>
    /// <param name="ocrJson">Serialized <see cref="OcrResult"/>.</param>
    /// <param name="variant">Variant name stored on the result.</param>
    /// <param name="psm">PSM name stored on the result.</param>
    /// <returns>JSON for the computed <see cref="OcrScore"/>.</returns>
    [Description("Scores an OCR result JSON for quality (0-100). Higher = more text, better confidence, more receipt-like.")]
    public Task<string> ScoreOcrResult(
        [Description("The OCR result JSON from RunOcr.")] string ocrJson,
        [Description("Label for this variant (for logging).")] string variant = "",
        [Description("PSM used (for logging).")] string psm = "")
    {
        var score = ComputeScore(ocrJson, variant, psm);
        return Task.FromResult(JsonSerializer.Serialize(score));
    }

    /// <summary>
    /// Builds an <see cref="OcrScore"/> from OCR JSON using fixed weights for confidence, structure, receipt cues, and line density.
    /// </summary>
    /// <param name="ocrJson">Serialized <see cref="OcrResult"/>.</param>
    /// <param name="variant">Variant label on the output.</param>
    /// <param name="psm">PSM label on the output.</param>
    /// <returns>Score DTO; empty score if JSON is invalid.</returns>
    public static OcrScore ComputeScore(string ocrJson, string variant = "", string psm = "")
    {
        OcrResult? result;
        try { result = JsonSerializer.Deserialize<OcrResult>(ocrJson); }
        catch (Exception ex)
        {
            Debug.WriteLine($"[WARN] deserialize OCR JSON for scoring: {ex.Message}");
            return new OcrScore { Variant = variant, Psm = psm };
        }

        if (result == null) return new OcrScore { Variant = variant, Psm = psm };

        var text = result.Text ?? "";
        var lines = result.Lines;
        int wordCount = lines.SelectMany(l => l.Words).Count();
        int lineCount = lines.Count;

        var allConf = lines
            .SelectMany(l => l.Words)
            .Where(w => w.Confidence > 0 && w.Confidence <= 100)
            .Select(w => w.Confidence)
            .ToList();
        double avgConf = allConf.Count > 0 ? allConf.Average() : 0;

        bool hasAmounts = AmountPattern().IsMatch(text);
        bool hasDate = DatePattern().IsMatch(text);
        bool hasTotal = TotalPattern().IsMatch(text);

        double charsPerLine = lineCount > 0 ? text.Length / (double)lineCount : 0;

        // Scoring weights
        double confScore = Math.Min(avgConf, 100.0) * 0.30; // 30% of mean word confidence (capped at 100)

        double wordLineScore = 0;
        if (wordCount >= 10) wordLineScore += 7.5;
        else if (wordCount >= 3) wordLineScore += wordCount * 0.75; // word tier: cap +7.5 or linear below 10 words
        if (lineCount >= 5) wordLineScore += 7.5;
        else if (lineCount >= 2) wordLineScore += lineCount * 1.5; // line tier: cap +7.5 or linear below 5 lines

        double receiptScore = 0; // receipt keyword bonus: amount / date / total pattern hits
        if (hasAmounts) receiptScore += 10;
        if (hasDate) receiptScore += 8;
        if (hasTotal) receiptScore += 12;

        double densityScore = 0; // density factor from chars per line
        if (charsPerLine >= 10 && charsPerLine <= 80) densityScore = 10;
        else if (charsPerLine > 80) densityScore = 5;

        double total = confScore + wordLineScore + receiptScore + densityScore;
        total = Math.Clamp(total, 0, 100);

        return new OcrScore
        {
            Score = Math.Round(total, 1),
            WordCount = wordCount,
            LineCount = lineCount,
            HasAmounts = hasAmounts,
            HasDate = hasDate,
            HasTotal = hasTotal,
            AvgConfidence = Math.Round(avgConf, 1),
            TextDensity = Math.Round(charsPerLine, 1),
            Variant = variant,
            Psm = psm,
        };
    }

    [GeneratedRegex(@"\d+[.,]\d{2}")]
    private static partial Regex AmountPattern();

    [GeneratedRegex(@"\d{1,2}[/\-\.]\d{1,2}[/\-\.]\d{2,4}")]
    private static partial Regex DatePattern();

    [GeneratedRegex(@"\bTOTAL\b", RegexOptions.IgnoreCase)]
    private static partial Regex TotalPattern();

    /// <summary>Exposes <see cref="ScoreOcrResult"/> as an AI-callable tool.</summary>
    /// <returns>Sequence containing the ScoreOcrResult tool.</returns>
    public IEnumerable<AITool> GetAITools()
    {
        yield return AIFunctionFactory.Create(this.ScoreOcrResult);
    }
}
