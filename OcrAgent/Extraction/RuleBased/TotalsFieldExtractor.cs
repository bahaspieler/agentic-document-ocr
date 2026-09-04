using System.Text.RegularExpressions;
using OcrAgent.Extraction.Models;

namespace OcrAgent.Extraction.RuleBased;

/// <summary>
/// Totals and payment lines: keyword match plus nearest amount; currency from symbols.
/// </summary>
public static partial class TotalsFieldExtractor
{
    private static readonly (string[] keywords, Action<TotalsInfo, decimal, double> setter)[] FieldMappings =
    {
        (new[] { "SUBTOTAL", "SUB TOTAL", "SUB-TOTAL" }, (t, v, c) => { t.Subtotal = v; t.SubtotalConfidence = FieldConfidence.FromOcr(c); }),
        (new[] { "TAX", "SALES TAX", "HST", "GST", "VAT" }, (t, v, c) => { t.Tax = v; t.TaxConfidence = FieldConfidence.FromOcr(c); }),
        (new[] { "TOTAL", "GRAND TOTAL", "BALANCE DUE", "BALANCE", "BAL", "AMOUNT DUE" }, (t, v, c) => { t.Total = v; t.TotalConfidence = FieldConfidence.FromOcr(c); }),
        (new[] { "NET SALES", "NET" }, (t, v, _) => t.NetSales = v),
        (new[] { "DISCOUNT", "SAVINGS", "YOU SAVED" }, (t, v, _) => t.Discount = v),
        (new[] { "TIP", "GRATUITY" }, (t, v, _) => t.Tip = v),
        (new[] { "AMOUNT PAID", "PAID", "TENDERED" }, (t, v, _) => t.Paid = v),
        (new[] { "CHANGE", "CHANGE DUE" }, (t, v, _) => t.Change = v),
    };

    private static readonly (string[] keywords, string method)[] PaymentMethods =
    {
        (new[] { "VISA", "MASTERCARD", "MASTER CARD", "AMEX", "DISCOVER", "CREDIT", "DEBIT" }, "card"),
        (new[] { "CASH" }, "cash"),
        (new[] { "APPLE PAY", "GOOGLE PAY", "SAMSUNG PAY", "MOBILE" }, "mobile"),
        (new[] { "CHECK", "CHEQUE" }, "check"),
    };

    /// <summary>Scans totals and body lines for labeled amounts and payment type.</summary>
    /// <param name="zonedLines">Classifier output.</param>
    /// <returns>Filled totals; missing fields stay null.</returns>
    public static TotalsInfo Extract(List<ZonedLine> zonedLines)
    {
        var info = new TotalsInfo();

        var totalsLines = zonedLines
            .Where(z => z.Zone is ReceiptZone.Totals or ReceiptZone.Body)
            .Select(z => z.Line)
            .ToList();

        var assignedFields = new HashSet<int>();

        for (int li = 0; li < totalsLines.Count; li++)
        {
            var line = totalsLines[li];
            var upperText = line.Text.ToUpperInvariant();

            for (int fi = 0; fi < FieldMappings.Length; fi++)
            {
                if (assignedFields.Contains(fi)) continue;

                var (keywords, setter) = FieldMappings[fi];
                var matchedKw = FindMatchingKeyword(upperText, keywords);
                if (matchedKw == null) continue;

                var amount = FindAmountNearKeyword(line, matchedKw);

                // If keyword line has no amount, check the next line
                if (!amount.HasValue && li + 1 < totalsLines.Count)
                    amount = FindAmount(totalsLines[li + 1]);

                if (amount.HasValue)
                {
                    setter(info, amount.Value, line.Confidence);
                    assignedFields.Add(fi);
                }
            }
        }

        // Detect currency from all lines
        info.Currency = DetectCurrency(zonedLines);

        // Detect payment method
        foreach (var line in totalsLines)
        {
            var upper = line.Text.ToUpperInvariant();
            foreach (var (keywords, method) in PaymentMethods)
            {
                if (keywords.Any(k => upper.Contains(k)))
                {
                    info.PaymentMethod = method;
                    break;
                }
            }
            if (info.PaymentMethod != null) break;
        }

        // Disambiguation: if "TOTAL" matched before "SUBTOTAL", and subtotal is null
        // but total has a value, try to find a second amount line for the actual total.
        EnsureTotalVsSubtotal(info, totalsLines);

        return info;
    }

    /// <summary>True when <paramref name="upperText"/> contains any keyword.</summary>
    private static bool MatchesKeyword(string upperText, string[] keywords)
        => FindMatchingKeyword(upperText, keywords) != null;

    /// <summary>Rightmost parseable amount on the line.</summary>
    private static decimal? FindAmount(NormalizedLine line)
    {
        // Search right-to-left for amounts (right-aligned column)
        for (int i = line.Words.Count - 1; i >= 0; i--)
        {
            var word = line.Words[i];
            if (TryParseAmount(word.Text, out var amount))
                return amount;
        }
        return null;
    }

    /// <summary>First amount after the keyword word; else rightmost amount on the line.</summary>
    private static decimal? FindAmountNearKeyword(NormalizedLine line, string keyword)
    {
        int kwWordIdx = -1;
        for (int i = 0; i < line.Words.Count; i++)
        {
            if (line.Words[i].Text.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                kwWordIdx = i;
                break;
            }
        }

        if (kwWordIdx >= 0)
        {
            // Find the first amount after the keyword
            for (int i = kwWordIdx + 1; i < line.Words.Count; i++)
            {
                if (TryParseAmount(line.Words[i].Text, out var amount))
                    return amount;
            }
        }

        return FindAmount(line);
    }

    /// <summary>First matching keyword token, or null; skips SUBTOTAL when matching TOTAL.</summary>
    private static string? FindMatchingKeyword(string upperText, string[] keywords)
    {
        foreach (var kw in keywords)
        {
            if (upperText.Contains(kw))
            {
                if (kw == "TOTAL")
                {
                    if (upperText.Contains("SUBTOTAL") || upperText.Contains("SUB TOTAL"))
                        continue;
                    if (upperText.Contains("TOTAL TAX") || upperText.Contains("TOTAL NUMBER") ||
                        upperText.Contains("TOTAL ITEMS") || upperText.Contains("TOTAL SAVINGS"))
                        continue;
                }
                if (kw == "TAX")
                {
                    if (upperText.Contains("TOTAL TAX"))
                        continue;
                }
                return kw;
            }
        }
        return null;
    }

    /// <summary>Strips currency, fixes OCR digit quirks, parses invariant decimal.</summary>
    /// <param name="text">Single token or short fragment.</param>
    /// <param name="amount">Parsed value when true.</param>
    /// <returns>True when the text is a strict amount pattern.</returns>
    public static bool TryParseAmount(string text, out decimal amount)
    {
        amount = 0;
        text = text.Trim().TrimStart('$', '€', '£');

        bool negative = false;
        if (text.StartsWith('-') || text.StartsWith('('))
        {
            negative = true;
            text = text.Trim('-', '(', ')');
        }

        text = NormalizeOcrDigitErrors(text);
        text = NormalizeDecimalSeparator(text);

        // Handle leading-dot amounts: ".93" -> "0.93"
        if (text.StartsWith('.') && text.Length >= 3 && char.IsDigit(text[1]))
            text = "0" + text;

        if (AmountStrict().IsMatch(text) && decimal.TryParse(text, System.Globalization.NumberStyles.Number,
            System.Globalization.CultureInfo.InvariantCulture, out amount))
        {
            if (negative) amount = -amount;
            return true;
        }
        return false;
    }

    /// <summary>Colon/degree as decimal, g→8, l/I→1, O→0 in digit context.</summary>
    internal static string NormalizeOcrDigitErrors(string text)
    {
        // Colon as decimal separator: "21:99" -> "21.99"
        if (text.Contains(':') && ColonAmount().IsMatch(text))
            text = text.Replace(':', '.');

        // Degree symbol as decimal separator: "3°99" -> "3.99"
        text = text.Replace('°', '.');

        // Leading 'g' or 'G' followed by .NN or ,NN -> '8' (Tesseract 8/g confusion)
        if (text.Length >= 3 && (text[0] == 'g' || text[0] == 'G') &&
            (text[1] == '.' || text[1] == ',') && char.IsDigit(text[2]))
            text = '8' + text[1..];

        // Leading 'l' or 'I' followed by digits -> '1'
        if (text.Length >= 2 && (text[0] == 'l' || text[0] == 'I') &&
            (char.IsDigit(text[1]) || text[1] == '.' || text[1] == ','))
            text = '1' + text[1..];

        // 'O' or 'o' in digit context -> '0'
        if (text.Length >= 3 && text.Any(char.IsDigit))
        {
            var chars = text.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if ((chars[i] == 'O' || chars[i] == 'o') &&
                    ((i > 0 && char.IsDigit(chars[i - 1])) ||
                     (i < chars.Length - 1 && char.IsDigit(chars[i + 1]))))
                    chars[i] = '0';
            }
            text = new string(chars);
        }

        return text;
    }

    /// <summary>European and mixed comma/dot amounts to invariant <c>.</c> decimal.</summary>
    internal static string NormalizeDecimalSeparator(string text)
    {
        bool hasDot = text.Contains('.');
        bool hasComma = text.Contains(',');

        if (hasComma && !hasDot)
        {
            int lastComma = text.LastIndexOf(',');
            string afterComma = text[(lastComma + 1)..];
            if (afterComma.Length <= 2)
                return text[..lastComma].Replace(",", "") + "." + afterComma;
            return text.Replace(",", "");
        }

        if (hasComma && hasDot)
        {
            int lastDot = text.LastIndexOf('.');
            int lastComma = text.LastIndexOf(',');
            if (lastComma > lastDot)
            {
                string afterComma = text[(lastComma + 1)..];
                if (afterComma.Length <= 2)
                    return text[..lastComma].Replace(",", "").Replace(".", "") + "." + afterComma;
            }
            return text.Replace(",", "");
        }

        return text;
    }

    /// <summary>USD, EUR, or GBP from first symbol in words.</summary>
    private static string DetectCurrency(List<ZonedLine> lines)
    {
        foreach (var zl in lines)
        {
            foreach (var w in zl.Line.Words)
            {
                if (w.Text.Contains('$')) return "USD";
                if (w.Text.Contains('€')) return "EUR";
                if (w.Text.Contains('£')) return "GBP";
            }
        }
        return "USD";
    }

    /// <summary>Infers subtotal from total minus tax when subtotal missing.</summary>
    private static void EnsureTotalVsSubtotal(TotalsInfo info, List<NormalizedLine> lines)
    {
        if (info.Total == null || info.Subtotal != null) return;

        // If we have tax, calculate subtotal = total - tax
        if (info.Tax.HasValue)
        {
            info.Subtotal = info.Total - info.Tax;
            info.SubtotalConfidence = FieldConfidence.Low("rules");
        }
    }

    [GeneratedRegex(@"^\d{1,6}\.\d{1,2}$")]
    private static partial Regex AmountStrict();

    [GeneratedRegex(@"^\d{1,6}:\d{2}$")]
    private static partial Regex ColonAmount();
}
