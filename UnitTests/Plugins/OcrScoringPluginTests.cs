using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OcrAgent.Ocr;
using OcrAgent.Plugins.Ocr;

namespace UnitTests.Plugins;

/// <summary>
/// Tests OcrScoringPlugin scoring from OCR JSON and edge cases.
/// </summary>
[TestClass]
public class OcrScoringPluginTests
{
    [TestMethod]
    public void ComputeScore_InvalidJson_ReturnsDefaultScore()
    {
        var score = OcrScoringPlugin.ComputeScore("not json", "natural", "Auto");

        Assert.AreEqual(0, score.Score);
        Assert.AreEqual("natural", score.Variant);
        Assert.AreEqual("Auto", score.Psm);
        Assert.AreEqual(0, score.WordCount);
    }

    [TestMethod]
    public void ComputeScore_NullDeserialization_ReturnsDefaultScore()
    {
        var score = OcrScoringPlugin.ComputeScore("null", "v1", "p1");

        Assert.AreEqual(0, score.Score);
        Assert.AreEqual("v1", score.Variant);
        Assert.AreEqual("p1", score.Psm);
    }

    [TestMethod]
    public void ComputeScore_EmptyResult_ReturnsZeroScore()
    {
        var result = new OcrResult { Text = "", Lines = new(), EngineName = "Tesseract" };
        var json = JsonSerializer.Serialize(result);

        var score = OcrScoringPlugin.ComputeScore(json);

        Assert.AreEqual(0, score.Score);
        Assert.AreEqual(0, score.WordCount);
        Assert.AreEqual(0, score.LineCount);
        Assert.AreEqual(0, score.AvgConfidence);
    }

    [TestMethod]
    public void ComputeScore_ReceiptLikeText_DetectsAmountsDateTotal()
    {
        var result = BuildReceiptResult(
            "SUPERMARKET\n01/15/2024\nMILK 3.99\nBREAD 2.50\nTOTAL 6.49",
            confidence: 85);
        var json = JsonSerializer.Serialize(result);

        var score = OcrScoringPlugin.ComputeScore(json, "natural", "SingleBlock");

        Assert.IsTrue(score.HasAmounts, "Should detect amounts (3.99, 2.50, 6.49)");
        Assert.IsTrue(score.HasDate, "Should detect date (01/15/2024)");
        Assert.IsTrue(score.HasTotal, "Should detect TOTAL keyword");
        Assert.IsTrue(score.Score > 30, $"Receipt-like text should score well, got {score.Score}");
        Assert.AreEqual("natural", score.Variant);
        Assert.AreEqual("SingleBlock", score.Psm);
    }

    [TestMethod]
    public void ComputeScore_HighConfidence_ContributesToScore()
    {
        var result = BuildReceiptResult("Word1 Word2 Word3", confidence: 95);
        var json = JsonSerializer.Serialize(result);

        var score = OcrScoringPlugin.ComputeScore(json);

        Assert.IsTrue(score.AvgConfidence >= 90, $"Should preserve avg confidence, got {score.AvgConfidence}");
        Assert.IsTrue(score.Score > 0);
    }

    [TestMethod]
    public void ComputeScore_ManyWords_GetsWordLineBonus()
    {
        var fewWords = BuildReceiptResult("One Two", confidence: 70, lineCount: 1);
        var fewJson = JsonSerializer.Serialize(fewWords);
        var fewScore = OcrScoringPlugin.ComputeScore(fewJson);

        var words = Enumerable.Range(1, 15).Select(i => $"Word{i}");
        var text = string.Join(" ", words);
        var result = BuildReceiptResult(text, confidence: 70, lineCount: 6);
        var json = JsonSerializer.Serialize(result);

        var score = OcrScoringPlugin.ComputeScore(json);

        Assert.IsTrue(score.WordCount >= 10, $"Word count should be >= 10, got {score.WordCount}");
        Assert.IsTrue(score.LineCount >= 5, $"Line count should be >= 5, got {score.LineCount}");
        Assert.IsTrue(score.Score > fewScore.Score, $"Many-word score ({score.Score}) should exceed few-word score ({fewScore.Score})");
    }

    [TestMethod]
    public void ComputeScore_ScoreClampedTo100()
    {
        var result = BuildReceiptResult(
            "SUPERMARKET\n01/15/2024\nMILK 3.99\nBREAD 2.50\nCHEESE 5.99\n" +
            "BUTTER 4.50\nEGGS 3.00\nJUICE 2.99\nRICE 1.50\nSALT 0.99\n" +
            "TOTAL 25.46",
            confidence: 98, lineCount: 11);
        var json = JsonSerializer.Serialize(result);

        var score = OcrScoringPlugin.ComputeScore(json);

        Assert.IsTrue(score.Score <= 100, $"Score should not exceed 100, got {score.Score}");
        Assert.IsTrue(score.Score >= 0, $"Score should not be negative, got {score.Score}");
    }

    [TestMethod]
    public void ComputeScore_OnlyAmounts_NoDateNoTotal()
    {
        var result = BuildReceiptResult("MILK 3.99\nBREAD 2.50", confidence: 80);
        var json = JsonSerializer.Serialize(result);

        var score = OcrScoringPlugin.ComputeScore(json);

        Assert.IsTrue(score.HasAmounts);
        Assert.IsFalse(score.HasDate);
        Assert.IsFalse(score.HasTotal);
    }

    [TestMethod]
    public void ComputeScore_DateFormats_AllDetected()
    {
        foreach (var dateStr in new[] { "01/15/2024", "15-01-24", "1.12.2025" })
        {
            var result = BuildReceiptResult($"Store\n{dateStr}\nItem 1.00", confidence: 80);
            var json = JsonSerializer.Serialize(result);

            var score = OcrScoringPlugin.ComputeScore(json);

            Assert.IsTrue(score.HasDate, $"Should detect date format: {dateStr}");
        }
    }

    [TestMethod]
    public void ComputeScore_TextDensity_NormalRange()
    {
        var result = BuildReceiptResult("Normal length line here with words", confidence: 80);
        var json = JsonSerializer.Serialize(result);

        var score = OcrScoringPlugin.ComputeScore(json);

        Assert.IsTrue(score.TextDensity > 0, "Text density should be positive for non-empty text");
    }

    [TestMethod]
    public void ComputeScore_ZeroConfidenceWords_Excluded()
    {
        var result = new OcrResult
        {
            Text = "Hello World",
            Lines = new List<OcrLine>
            {
                new()
                {
                    Text = "Hello World",
                    Words = new List<OcrWord>
                    {
                        new() { Text = "Hello", Confidence = 90 },
                        new() { Text = "World", Confidence = 0 },
                    }
                }
            }
        };
        var json = JsonSerializer.Serialize(result);

        var score = OcrScoringPlugin.ComputeScore(json);

        Assert.AreEqual(90, score.AvgConfidence, 0.1, "Zero-confidence words should be excluded from avg");
    }

    [TestMethod]
    public async Task ScoreOcrResult_ReturnsValidJson()
    {
        var plugin = new OcrScoringPlugin();
        var ocrResult = BuildReceiptResult("TOTAL 5.99", confidence: 85);
        var ocrJson = JsonSerializer.Serialize(ocrResult);

        var resultJson = await plugin.ScoreOcrResult(ocrJson, "otsu", "SingleColumn");

        var score = JsonSerializer.Deserialize<OcrScoringPlugin.OcrScore>(resultJson);
        Assert.IsNotNull(score);
        Assert.AreEqual("otsu", score.Variant);
        Assert.AreEqual("SingleColumn", score.Psm);
        Assert.IsTrue(score.Score > 0);
    }

    [TestMethod]
    public void ComputeScore_TotalCaseInsensitive()
    {
        var result = BuildReceiptResult("total 5.99", confidence: 80);
        var json = JsonSerializer.Serialize(result);

        var score = OcrScoringPlugin.ComputeScore(json);

        Assert.IsTrue(score.HasTotal, "TOTAL pattern should be case-insensitive");
    }

    private static OcrResult BuildReceiptResult(string text, double confidence, int lineCount = 0)
    {
        var lines = text.Split('\n');
        if (lineCount > 0 && lineCount > lines.Length)
        {
            var padded = new List<string>(lines);
            while (padded.Count < lineCount)
                padded.Add($"Extra line {padded.Count}");
            lines = padded.ToArray();
        }

        return new OcrResult
        {
            Text = text,
            EngineName = "Tesseract",
            Lines = lines.Select(line => new OcrLine
            {
                Text = line,
                Confidence = confidence,
                Words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Select(w => new OcrWord { Text = w, Confidence = confidence })
                    .ToList()
            }).ToList()
        };
    }
}
