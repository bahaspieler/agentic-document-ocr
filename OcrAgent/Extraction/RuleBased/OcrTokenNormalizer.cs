using System.Text.RegularExpressions;
using OcrAgent.Ocr;

namespace OcrAgent.Extraction.RuleBased;

/// <summary>
/// Cleans OCR words/lines: merge split amounts, trim, normalize currency and spaces.
/// </summary>
public static partial class OcrTokenNormalizer
{
    /// <summary>Builds normalized lines; drops empty lines after word merge.</summary>
    /// <param name="ocrResult">Raw OCR document.</param>
    /// <returns>Lines with merged amounts and collapsed spaces.</returns>
    public static List<NormalizedLine> Normalize(OcrResult ocrResult)
    {
        var lines = new List<NormalizedLine>();

        for (int i = 0; i < ocrResult.Lines.Count; i++)
        {
            var srcLine = ocrResult.Lines[i];
            var normalizedWords = NormalizeWords(srcLine.Words);

            if (normalizedWords.Count == 0)
                continue;

            var lineText = string.Join(" ", normalizedWords.Select(w => w.Text));
            lines.Add(new NormalizedLine
            {
                Text = lineText,
                Confidence = srcLine.Confidence,
                BoundingBox = srcLine.BoundingBox,
                Words = normalizedWords,
                OriginalIndex = i
            });
        }

        return lines;
    }

    /// <summary>Trims, merges split currency amounts, returns word list.</summary>
    private static List<NormalizedWord> NormalizeWords(List<OcrWord> words)
    {
        var result = new List<NormalizedWord>();

        for (int i = 0; i < words.Count; i++)
        {
            var word = words[i];
            var text = NormalizeText(word.Text);

            if (string.IsNullOrWhiteSpace(text))
                continue;

            // Merge split amounts: "$28" + ".28" or "$" + "28.28"
            if (result.Count > 0 && TryMergeAmount(result[^1], text, word, out var merged))
            {
                result[^1] = merged;
                continue;
            }

            result.Add(new NormalizedWord
            {
                Text = text,
                Confidence = word.Confidence,
                BoundingBox = word.BoundingBox
            });
        }

        return result;
    }

    /// <summary>Trim, normalize currency chars, single spaces.</summary>
    private static string NormalizeText(string text)
    {
        text = text.Trim();
        text = NormalizeCurrency(text);
        text = CollapseWhitespace().Replace(text, " ");
        return text;
    }

    /// <summary>Maps full-width and common symbols to ASCII currency.</summary>
    private static string NormalizeCurrency(string text)
    {
        // Common OCR misreads for dollar sign
        text = text.Replace("$", "$").Replace("＄", "$");
        // Euro
        text = text.Replace("€", "€").Replace("£", "£");
        return text;
    }

    /// <summary>Merges "$28" + ".28" or "$" + "28.28" when boxes are close.</summary>
    private static bool TryMergeAmount(
        NormalizedWord prev, string currentText, OcrWord currentWord,
        out NormalizedWord merged)
    {
        merged = prev;

        // Case: prev="$28" current=".28" -> "$28.28"
        if (SplitAmountTail().IsMatch(currentText) && AmountHead().IsMatch(prev.Text))
        {
            int gap = currentWord.BoundingBox.X - (prev.BoundingBox.X + prev.BoundingBox.Width);
            if (gap < prev.BoundingBox.Height) // close horizontally
            {
                merged = new NormalizedWord
                {
                    Text = prev.Text + currentText,
                    Confidence = Math.Min(prev.Confidence, currentWord.Confidence),
                    BoundingBox = MergeBoxes(prev.BoundingBox, currentWord.BoundingBox)
                };
                return true;
            }
        }

        // Case: prev="$" current="28.28" -> "$28.28"
        if (prev.Text is "$" or "€" or "£" && AmountPattern().IsMatch(currentText))
        {
            int gap = currentWord.BoundingBox.X - (prev.BoundingBox.X + prev.BoundingBox.Width);
            if (gap < prev.BoundingBox.Height * 2)
            {
                merged = new NormalizedWord
                {
                    Text = prev.Text + currentText,
                    Confidence = Math.Min(prev.Confidence, currentWord.Confidence),
                    BoundingBox = MergeBoxes(prev.BoundingBox, currentWord.BoundingBox)
                };
                return true;
            }
        }

        return false;
    }

    /// <summary>Axis-aligned union of two boxes.</summary>
    private static OcrBoundingBox MergeBoxes(OcrBoundingBox a, OcrBoundingBox b)
    {
        int x = Math.Min(a.X, b.X);
        int y = Math.Min(a.Y, b.Y);
        int right = Math.Max(a.X + a.Width, b.X + b.Width);
        int bottom = Math.Max(a.Y + a.Height, b.Y + b.Height);
        return new OcrBoundingBox { X = x, Y = y, Width = right - x, Height = bottom - y };
    }

    [GeneratedRegex(@"^\.\d{2}$")]
    private static partial Regex SplitAmountTail();

    [GeneratedRegex(@"[\$€£]?\d+$")]
    private static partial Regex AmountHead();

    [GeneratedRegex(@"^\d{1,6}[.,]\d{2}$")]
    private static partial Regex AmountPattern();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex CollapseWhitespace();
}
