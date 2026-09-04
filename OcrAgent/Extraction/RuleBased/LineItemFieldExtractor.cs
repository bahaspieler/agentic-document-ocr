using System.Text.RegularExpressions;
using OcrAgent.Extraction.Models;

namespace OcrAgent.Extraction.RuleBased;

/// <summary>
/// Parses body-zone lines into line items (amount column, qty patterns, continuations).
/// </summary>
public static partial class LineItemFieldExtractor
{
    /// <summary>Body lines only; merges detail and continuation rows.</summary>
    /// <param name="zonedLines">Output of <see cref="ZoneClassifier.Classify"/>.</param>
    /// <returns>Ordered <see cref="ReceiptLineItem"/> list.</returns>
    public static List<ReceiptLineItem> Extract(List<ZonedLine> zonedLines)
    {
        var bodyLines = zonedLines
            .Where(z => z.Zone == ReceiptZone.Body)
            .Select(z => z.Line)
            .ToList();

        if (bodyLines.Count == 0) return new();

        int? amountColumnX = DetectAmountColumn(bodyLines);
        var items = new List<ReceiptLineItem>();
        int itemIndex = 0;

        // First pass: collect raw items and pending detail lines
        var rawItems = new List<(ReceiptLineItem item, int lineIdx, bool isDetailLine)>();

        for (int i = 0; i < bodyLines.Count; i++)
        {
            var line = bodyLines[i];
            var parsed = ParseItemLine(line, amountColumnX);

            if (parsed != null)
            {
                parsed.Confidence = FieldConfidence.FromOcr(line.Confidence);
                bool isDetail = IsWeightOrQtyDetailLine(line.Text);
                rawItems.Add((parsed, i, isDetail));
            }
            else if (line.Text.Trim().Length > 2 && !LooksTotalish(line.Text))
            {
                // Text-only line (no amount). Check if the next line is a weight/qty detail;
                // if so, create a parent item for it (e.g., "BANANAS LOOSE 17KG" + "0.596kg @ 15.99R /kg 9.53")
                if (i + 1 < bodyLines.Count && IsWeightOrQtyDetailLine(bodyLines[i + 1].Text))
                {
                    var parentItem = new ReceiptLineItem
                    {
                        Description = line.Text.Trim(),
                        Confidence = FieldConfidence.FromOcr(line.Confidence),
                    };
                    rawItems.Add((parentItem, i, false));
                }
            }
        }

        // Second pass: merge detail lines and fix phantom items
        for (int ri = 0; ri < rawItems.Count; ri++)
        {
            var (item, lineIdx, isDetail) = rawItems[ri];

            // Weight/qty detail line (e.g. "0.596kg @ 15.99R /kg 9.53 *") -> merge into previous
            if (isDetail && items.Count > 0)
            {
                var prev = items[^1];
                if (item.LineTotal.HasValue && !prev.LineTotal.HasValue)
                    prev.LineTotal = item.LineTotal;
                if (item.Quantity.HasValue)
                    prev.Quantity = item.Quantity;
                if (item.UnitPrice.HasValue)
                    prev.UnitPrice = item.UnitPrice;
                continue;
            }

            // Phantom "(item)" with qty@price -> attach to the next real item
            if (item.Description == "(item)" && item.Quantity.HasValue && item.UnitPrice.HasValue)
            {
                if (ri + 1 < rawItems.Count && !rawItems[ri + 1].isDetailLine)
                {
                    var next = rawItems[ri + 1].item;
                    if (next.Quantity == null) next.Quantity = item.Quantity;
                    if (next.UnitPrice == null) next.UnitPrice = item.UnitPrice;
                    if (next.LineTotal == null && item.LineTotal != null) next.LineTotal = item.LineTotal;
                    continue;
                }
            }

            item.Index = itemIndex++;
            items.Add(item);
        }

        // Third pass: handle text-only body lines (no amount) as continuations
        // only for lines that sit between an item line and the next item line
        var itemLineIndices = new HashSet<int>(rawItems.Select(r => r.lineIdx));
        for (int i = 0; i < bodyLines.Count; i++)
        {
            if (itemLineIndices.Contains(i)) continue;

            var line = bodyLines[i];
            if (string.IsNullOrWhiteSpace(line.Text) || line.Text.Trim().Length <= 1) continue;
            if (!IsMultiLineContinuation(line, amountColumnX)) continue;

            // Find the item immediately before this line
            var ownerItem = items.LastOrDefault(it =>
                rawItems.Any(r => r.item == it && r.lineIdx < i));
            if (ownerItem != null && ownerItem.Description != "(item)")
            {
                // Only append if the previous item line is adjacent or near
                var prevRaw = rawItems.LastOrDefault(r => r.lineIdx < i);
                if (prevRaw.item == ownerItem)
                    ownerItem.Description = (ownerItem.Description + " " + line.Text.Trim()).Trim();
            }
        }

        return items;
    }

    /// <summary>Median X of rightmost amount-like words per line.</summary>
    /// <param name="lines">Body lines with words.</param>
    /// <returns>Column X or null when too few samples.</returns>
    private static int? DetectAmountColumn(List<NormalizedLine> lines)
    {
        var amountXPositions = new List<int>();

        foreach (var line in lines)
        {
            for (int i = line.Words.Count - 1; i >= 0; i--)
            {
                if (TotalsFieldExtractor.TryParseAmount(line.Words[i].Text, out _))
                {
                    amountXPositions.Add(line.Words[i].BoundingBox.X);
                    break;
                }
            }
        }

        if (amountXPositions.Count < 2) return null;

        // Use the median X position as the amount column
        amountXPositions.Sort();
        return amountXPositions[amountXPositions.Count / 2];
    }

    /// <summary>Qty@price, qty×price, or description + trailing amount.</summary>
    private static ReceiptLineItem? ParseItemLine(
        NormalizedLine line, int? amountColumnX)
    {
        // Try quantity pattern first: "2 @ $10.00 ea $20.00" or "2x $10.00"
        var qtyMatch = QtyAtPattern().Match(line.Text);
        if (qtyMatch.Success)
        {
            return ParseQtyPattern(qtyMatch, line);
        }

        var qtyXMatch = QtyXPattern().Match(line.Text);
        if (qtyXMatch.Success)
        {
            return ParseQtyXPattern(qtyXMatch, line);
        }

        // Standard: description on left, amount on right
        decimal? lineTotal = null;
        int amountWordIdx = -1;

        for (int i = line.Words.Count - 1; i >= 0; i--)
        {
            if (TotalsFieldExtractor.TryParseAmount(line.Words[i].Text, out var amt))
            {
                lineTotal = amt;
                amountWordIdx = i;
                break;
            }
        }

        if (lineTotal == null || amountWordIdx <= 0) return null;

        // Description is all words before the amount
        var descWords = line.Words.Take(amountWordIdx).Select(w => w.Text);
        var description = string.Join(" ", descWords).Trim();

        if (description.Length < 1) return null;

        // Skip lines that look like totals
        if (LooksTotalish(description)) return null;

        // Check for a second amount (unit price) just before the line total
        decimal? unitPrice = null;
        decimal? qty = null;
        if (amountWordIdx >= 2 &&
            TotalsFieldExtractor.TryParseAmount(line.Words[amountWordIdx - 1].Text, out var up))
        {
            unitPrice = up;
            descWords = line.Words.Take(amountWordIdx - 1).Select(w => w.Text);
            description = string.Join(" ", descWords).Trim();

            if (unitPrice > 0 && lineTotal > 0)
                qty = Math.Round(lineTotal.Value / unitPrice.Value, 2);
        }

        // Check for leading quantity: "2 Item Name  $10.00"
        var leadingQty = LeadingQtyPattern().Match(description);
        if (leadingQty.Success && qty == null)
        {
            if (decimal.TryParse(leadingQty.Groups[1].Value, out var q) && IsReasonableQuantity(q, leadingQty.Groups[1].Value))
            {
                qty = q;
                description = description[leadingQty.Length..].Trim();
            }
        }

        // Detect tax indicator (common: T, F, A, B at end of line or near amount)
        string? taxIndicator = null;
        if (line.Words.Count > amountWordIdx + 1)
        {
            var suffix = line.Words[amountWordIdx + 1].Text.Trim();
            if (suffix.Length == 1 && char.IsLetter(suffix[0]))
                taxIndicator = suffix;
        }

        return new ReceiptLineItem
        {
            Description = description,
            Quantity = qty,
            UnitPrice = unitPrice,
            LineTotal = lineTotal,
            TaxIndicator = taxIndicator,
        };
    }

    /// <summary>Builds item from <c>qty @ unit [total]</c> regex match.</summary>
    private static ReceiptLineItem? ParseQtyPattern(Match match, NormalizedLine line)
    {
        if (!decimal.TryParse(match.Groups["qty"].Value, out var qty)) return null;
        if (!IsReasonableQuantity(qty, match.Groups["qty"].Value)) return null;

        decimal? unitPrice = null;
        if (TotalsFieldExtractor.TryParseAmount(match.Groups["unit"].Value, out var up))
            unitPrice = up;

        decimal? lineTotal = null;
        if (match.Groups["total"].Success && TotalsFieldExtractor.TryParseAmount(match.Groups["total"].Value, out var lt))
            lineTotal = lt;
        else if (unitPrice.HasValue)
            lineTotal = qty * unitPrice;

        // Description is text before the qty pattern
        var descEnd = match.Index;
        var description = line.Text[..descEnd].Trim();
        if (description.Length == 0)
        {
            // Description may be on the line above; return with empty description
            description = "(item)";
        }

        return new ReceiptLineItem
        {
            Description = description,
            Quantity = qty,
            UnitPrice = unitPrice,
            LineTotal = lineTotal,
        };
    }

    /// <summary>Builds item from <c>qty x price</c> pattern and trailing amounts.</summary>
    private static ReceiptLineItem? ParseQtyXPattern(Match match, NormalizedLine line)
    {
        if (!decimal.TryParse(match.Groups["qty"].Value, out var qty)) return null;
        if (!IsReasonableQuantity(qty, match.Groups["qty"].Value)) return null;

        decimal? unitPrice = null;
        if (match.Groups["price"].Success && TotalsFieldExtractor.TryParseAmount(match.Groups["price"].Value, out var up))
            unitPrice = up;

        // Find line total as last amount on the line
        decimal? lineTotal = null;
        for (int i = line.Words.Count - 1; i >= 0; i--)
        {
            if (TotalsFieldExtractor.TryParseAmount(line.Words[i].Text, out var amt))
            {
                lineTotal = amt;
                break;
            }
        }

        var descStart = match.Index + match.Length;
        var description = line.Text[descStart..].Trim();
        if (description.Length == 0)
            description = line.Text[..match.Index].Trim();
        if (description.Length == 0)
            description = "(item)";

        // Remove trailing amount from description
        description = TrailingAmount().Replace(description, "").Trim();

        return new ReceiptLineItem
        {
            Description = description,
            Quantity = qty,
            UnitPrice = unitPrice,
            LineTotal = lineTotal ?? (unitPrice.HasValue ? qty * unitPrice : null),
        };
    }

    /// <summary>True for weight @ unit price lines merged into the prior item.</summary>
    private static bool IsWeightOrQtyDetailLine(string text)
    {
        return WeightAtRatePattern().IsMatch(text);
    }

    /// <summary>True when a text-only line should append to the previous description.</summary>
    private static bool IsMultiLineContinuation(
        NormalizedLine line, int? amountColumnX)
    {
        var text = line.Text.Trim();
        if (text.Length <= 1) return false;

        bool hasAmount = line.Words.Any(w => TotalsFieldExtractor.TryParseAmount(w.Text, out _));
        if (hasAmount) return false;

        // Don't treat weight/qty detail lines as continuations (handled separately)
        if (IsWeightOrQtyDetailLine(text)) return false;

        // Lines that look like standalone product names (long, starts with uppercase or bullet)
        // should NOT be continuations if they look like they could be real items with misread prices
        if (text.Length > 15 && line.Words.Count >= 3) return false;

        return true;
    }

    /// <summary>Rejects huge integers that look like barcodes, not counts.</summary>
    private static bool IsReasonableQuantity(decimal qty, string rawText)
    {
        if (qty > 100) return false;
        if (rawText.Length > 6 && !rawText.Contains('.') && !rawText.Contains(',')) return false;
        return true;
    }

    /// <summary>True when description text looks like a totals row, not a product.</summary>
    private static bool LooksTotalish(string description)
    {
        var upper = description.ToUpperInvariant();
        return upper.Contains("TOTAL") || upper.Contains("SUBTOTAL") || upper.Contains("TAX") ||
               upper.Contains("CHANGE") || upper.Contains("BALANCE") || upper.Contains("PAID") ||
               upper.Contains("TENDERED") || upper.Contains("NET SALES");
    }

    // "2 @ $10.00 ea $20.00" or "2 @ 10.00"
    [GeneratedRegex(@"(?<qty>\d+)\s*@\s*\$?(?<unit>\d+[.,]\d{2})\s*(ea\s*\$?(?<total>\d+[.,]\d{2}))?", RegexOptions.IgnoreCase)]
    private static partial Regex QtyAtPattern();

    // "2x $10.00" or "QTY 2"
    [GeneratedRegex(@"(?<qty>\d+)\s*[xX]\s*\$?(?<price>\d+[.,]\d{2})?")]
    private static partial Regex QtyXPattern();

    [GeneratedRegex(@"^(?<qty>\d+)\s+")]
    private static partial Regex LeadingQtyPattern();

    [GeneratedRegex(@"\s*\$?\d+[.,]\d{2}\s*$")]
    private static partial Regex TrailingAmount();

    // Matches weight-at-rate patterns: "0.596kg @ 15.99R /kg", "1.08 lb @ 1.99 /1b"
    [GeneratedRegex(@"\d+[.,]\d+\s*(?:kg|lb|oz|g)\s*@\s*\d+", RegexOptions.IgnoreCase)]
    private static partial Regex WeightAtRatePattern();
}
