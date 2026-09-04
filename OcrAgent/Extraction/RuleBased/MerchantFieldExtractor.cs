using System.Text.RegularExpressions;
using OcrAgent.Extraction.Models;

namespace OcrAgent.Extraction.RuleBased;

/// <summary>
/// Header lines: store name (uppercase heuristic), phone regex, street/city patterns.
/// </summary>
public static partial class MerchantFieldExtractor
{
    /// <summary>Reads header-zone lines for name, phone, and address.</summary>
    /// <param name="headerLines">All zoned lines; only <see cref="ReceiptZone.Header"/> are used.</param>
    /// <returns>Filled <see cref="MerchantInfo"/>; empty when no header lines.</returns>
    public static MerchantInfo Extract(List<ZonedLine> headerLines)
    {
        var info = new MerchantInfo();
        if (headerLines.Count == 0) return info;

        var lines = headerLines
            .Where(z => z.Zone == ReceiptZone.Header)
            .Select(z => z.Line)
            .ToList();

        if (lines.Count == 0) return info;

        ExtractMerchantName(lines, info);
        ExtractPhone(lines, info);
        ExtractAddress(lines, info);

        return info;
    }

    /// <summary>Picks first strong uppercase line or falls back to first long line.</summary>
    private static void ExtractMerchantName(
        List<NormalizedLine> lines, MerchantInfo info)
    {
        // Merchant name is typically the first strong, mostly-uppercase line
        foreach (var line in lines)
        {
            var text = line.Text.Trim();
            if (text.Length < 3) continue;
            if (PhonePattern().IsMatch(text)) continue;
            if (IsLikelyAddress(text)) continue;

            double uppercaseRatio = text.Count(char.IsUpper) / (double)Math.Max(text.Count(char.IsLetter), 1);
            bool isStrong = uppercaseRatio > 0.5 || line.Confidence > 70;

            if (isStrong)
            {
                info.Name = text;
                info.NameConfidence = FieldConfidence.FromOcr(line.Confidence);
                return;
            }
        }

        // Fallback: take the first non-empty line
        var first = lines.FirstOrDefault(l => l.Text.Trim().Length > 2);
        if (first != null)
        {
            info.Name = first.Text.Trim();
            info.NameConfidence = FieldConfidence.Low();
        }
    }

    /// <summary>First phone-shaped match in header lines.</summary>
    private static void ExtractPhone(
        List<NormalizedLine> lines, MerchantInfo info)
    {
        foreach (var line in lines)
        {
            var match = PhonePattern().Match(line.Text);
            if (match.Success)
            {
                info.Phone = match.Value;
                info.PhoneConfidence = FieldConfidence.FromOcr(line.Confidence);
                return;
            }
        }
    }

    /// <summary>Collects street/city lines and parses city, state, ZIP when possible.</summary>
    private static void ExtractAddress(
        List<NormalizedLine> lines, MerchantInfo info)
    {
        var addressLines = new List<string>();
        double avgConfidence = 0;
        int count = 0;

        foreach (var line in lines)
        {
            var text = line.Text.Trim();
            if (text == info.Name || text == info.Phone) continue;
            if (text.Length < 3) continue;

            if (IsLikelyAddress(text))
            {
                addressLines.Add(text);
                avgConfidence += line.Confidence;
                count++;

                var cityMatch = CityStateZip().Match(text);
                if (cityMatch.Success)
                {
                    info.City = cityMatch.Groups["city"].Value.Trim().TrimEnd(',');
                    info.StateOrRegion = cityMatch.Groups["state"].Value.Trim();
                    info.PostalCode = cityMatch.Groups["zip"].Value.Trim();
                }
            }
            else if (StreetPattern().IsMatch(text))
            {
                addressLines.Add(text);
                avgConfidence += line.Confidence;
                count++;
            }
        }

        if (addressLines.Count > 0)
        {
            info.AddressLines = addressLines;
            info.AddressConfidence = FieldConfidence.FromOcr(avgConfidence / count);
        }
    }

    /// <summary>True when text matches street or city/state/ZIP patterns.</summary>
    private static bool IsLikelyAddress(string text)
    {
        return StreetPattern().IsMatch(text) || CityStateZip().IsMatch(text);
    }

    [GeneratedRegex(@"\(?\d{3}\)?[-.\s]?\d{3}[-.\s]?\d{4}")]
    private static partial Regex PhonePattern();

    [GeneratedRegex(@"\d+\s+\w+\s+(st|street|ave|avenue|rd|road|blvd|boulevard|dr|drive|way|ln|lane|ct|court|pl|place|hwy|highway|pkwy|parkway|ste|suite|apt|unit|#)\b", RegexOptions.IgnoreCase)]
    private static partial Regex StreetPattern();

    [GeneratedRegex(@"(?<city>[A-Za-z\s]+),?\s+(?<state>[A-Z]{2})\s+(?<zip>\d{5}(-\d{4})?)")]
    private static partial Regex CityStateZip();
}
