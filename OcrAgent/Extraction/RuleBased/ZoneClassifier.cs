using System.Text.RegularExpressions;

namespace OcrAgent.Extraction.RuleBased;

/// <summary>
/// Maps each normalized line to header, body, totals, or footer using Y and keywords.
/// </summary>
public static partial class ZoneClassifier
{
    /// <summary>Sorts by Y, finds total anchors, then maps each line to a zone.</summary>
    /// <param name="lines">Output of <see cref="OcrTokenNormalizer.Normalize"/>.</param>
    /// <returns>Same lines in top-to-bottom order with zones.</returns>
    public static List<ZonedLine> Classify(List<NormalizedLine> lines)
    {
        if (lines.Count == 0) return new();

        var sorted = lines.OrderBy(l => l.BoundingBox.Y).ToList();

        int minY = sorted[0].BoundingBox.Y;
        int maxY = sorted[^1].BoundingBox.Y + sorted[^1].BoundingBox.Height;
        int totalHeight = Math.Max(maxY - minY, 1);

        // Find keyword anchors to set adaptive boundaries
        int? firstTotalLineIdx = FindFirstTotalsAnchor(sorted);
        int? lastItemLineIdx = firstTotalLineIdx.HasValue ? firstTotalLineIdx.Value - 1 : null;

        // Compute zone boundaries as Y coordinates
        int headerEndY, totalsStartY, footerStartY;

        if (firstTotalLineIdx.HasValue)
        {
            var totalLine = sorted[firstTotalLineIdx.Value];
            totalsStartY = totalLine.BoundingBox.Y;

            // Header ends at ~20% or at the first line clearly after header patterns
            int headerGuess = minY + (int)(totalHeight * 0.22);
            headerEndY = FindHeaderEnd(sorted, headerGuess, totalsStartY);

            // Footer starts at last non-total line after "TOTAL" or bottom 12%
            footerStartY = FindFooterStart(sorted, firstTotalLineIdx.Value, maxY, minY, totalHeight);
        }
        else
        {
            headerEndY = minY + (int)(totalHeight * 0.22);
            totalsStartY = minY + (int)(totalHeight * 0.70);
            footerStartY = minY + (int)(totalHeight * 0.88);
        }

        var result = new List<ZonedLine>(sorted.Count);
        foreach (var line in sorted)
        {
            int lineY = line.BoundingBox.Y;
            ReceiptZone zone;

            if (lineY < headerEndY)
                zone = ReceiptZone.Header;
            else if (lineY >= footerStartY)
                zone = ReceiptZone.Footer;
            else if (lineY >= totalsStartY)
                zone = ReceiptZone.Totals;
            else
                zone = ReceiptZone.Body;

            result.Add(new ZonedLine { Line = line, Zone = zone });
        }

        return result;
    }

    /// <summary>First line index that looks like subtotal, total, or a late tax line.</summary>
    private static int? FindFirstTotalsAnchor(List<NormalizedLine> sorted)
    {
        // Pass 1: SUBTOTAL (most precise anchor)
        for (int i = 0; i < sorted.Count; i++)
        {
            if (SubtotalKeyword().IsMatch(sorted[i].Text))
                return i;
        }
        // Pass 2: TOTAL / AMOUNT DUE / BALANCE DUE
        for (int i = 0; i < sorted.Count; i++)
        {
            if (TotalsKeyword().IsMatch(sorted[i].Text))
                return i;
        }
        // Pass 3: TAX or BAL near the bottom half (weaker anchors, only in lower portion)
        int halfIdx = sorted.Count / 2;
        for (int i = halfIdx; i < sorted.Count; i++)
        {
            if (FallbackTotalsAnchor().IsMatch(sorted[i].Text))
                return i;
        }
        return null;
    }

    /// <summary>Upper Y bound for the header; capped so it does not cross into totals.</summary>
    private static int FindHeaderEnd(
        List<NormalizedLine> sorted, int defaultEnd, int totalsStartY)
    {
        // Header shouldn't exceed 35% of receipt or cross into totals
        int maxHeader = Math.Min(defaultEnd + (int)((totalsStartY - sorted[0].BoundingBox.Y) * 0.1), totalsStartY);

        // Look for transition indicators: first line with an amount after header area
        foreach (var line in sorted)
        {
            if (line.BoundingBox.Y > defaultEnd && line.BoundingBox.Y < totalsStartY)
            {
                if (HasAmountToken(line))
                    return line.BoundingBox.Y;
            }
        }

        return Math.Min(defaultEnd, maxHeader);
    }

    /// <summary>Y where footer starts: after the last totals-related line.</summary>
    private static int FindFooterStart(
        List<NormalizedLine> sorted, int totalIdx, int maxY, int minY, int totalHeight)
    {
        // Find the last totals-related line, footer starts after it
        int lastTotalIdx = totalIdx;
        for (int i = totalIdx; i < sorted.Count; i++)
        {
            if (TotalsOrPaymentKeyword().IsMatch(sorted[i].Text) || HasAmountToken(sorted[i]))
                lastTotalIdx = i;
        }

        if (lastTotalIdx + 1 < sorted.Count)
            return sorted[lastTotalIdx + 1].BoundingBox.Y;

        // No lines after the last totals-related line -- footer is after all content
        // +1 ensures the last totals line isn't misclassified as footer
        return sorted[lastTotalIdx].BoundingBox.Y + sorted[lastTotalIdx].BoundingBox.Height + 1;
    }

    /// <summary>True if any word matches the amount pattern.</summary>
    private static bool HasAmountToken(NormalizedLine line)
    {
        return line.Words.Any(w => AmountPattern().IsMatch(w.Text));
    }

    [GeneratedRegex(@"\b(SUB\s*TOTAL|SUBTOTAL)\b", RegexOptions.IgnoreCase)]
    private static partial Regex SubtotalKeyword();

    [GeneratedRegex(@"(?<!\bSUB\s?)(?<!\bSUB)\bTOTAL\b|\bAMOUNT\s*DUE\b|\bBALANCE\s*DUE\b", RegexOptions.IgnoreCase)]
    private static partial Regex TotalsKeyword();

    [GeneratedRegex(@"\b(TOTAL|SUBTOTAL|SUB\s*TOTAL|TAX|AMOUNT|PAID|CHANGE|CASH|CREDIT|DEBIT|TIP|DISCOUNT|NET\s*SALES|BALANCE|DUE)\b", RegexOptions.IgnoreCase)]
    private static partial Regex TotalsOrPaymentKeyword();

    [GeneratedRegex(@"\b(TAX|BAL|BALANCE)\b", RegexOptions.IgnoreCase)]
    private static partial Regex FallbackTotalsAnchor();

    [GeneratedRegex(@"[\$€£]?\d{1,6}[.,]\d{2}")]
    private static partial Regex AmountPattern();
}
