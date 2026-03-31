using System.Globalization;
using System.Text.RegularExpressions;
using OcrAgent.Extraction.Models;

namespace OcrAgent.Extraction.RuleBased;

/// <summary>
/// Parses transaction date/time and receipt number from OCR text (US, EU, ISO patterns).
/// </summary>
public static partial class DateTimeFieldExtractor
{
    private sealed class DateCandidate
    {
        public string RawDate { get; set; } = "";
        public string? RawTime { get; set; }
        public DateTime Parsed { get; set; }
        public double Confidence { get; set; }
        public double Score { get; set; }
    }

    /// <summary>Best date/time candidate plus receipt number scan.</summary>
    /// <param name="allLines">Zoned lines from the classifier.</param>
    /// <returns>Transaction fields; empty when no date found.</returns>
    public static TransactionInfo Extract(List<ZoneClassifier.ZonedLine> allLines)
    {
        var info = new TransactionInfo();
        var candidates = new List<DateCandidate>();

        foreach (var zl in allLines)
        {
            var line = zl.Line;
            FindDates(line.Text, line.Confidence, zl.Zone, candidates);
        }

        if (candidates.Count == 0) return info;

        // Also look for times near dates
        var timeMap = BuildTimeMap(allLines);

        foreach (var c in candidates)
        {
            // Boost score if time is found on the same or adjacent line
            c.Score = c.Confidence;
            if (c.RawTime != null)
                c.Score += 20;
        }

        var best = candidates.OrderByDescending(c => c.Score).First();

        try
        {
            var dt = best.Parsed;
            if (best.RawTime != null && TryParseTime(best.RawTime, out var time))
                dt = dt.Date + time;

            info.DateTime = dt.ToString("o", CultureInfo.InvariantCulture);
            info.DateTimeConfidence = FieldConfidence.FromOcr(best.Confidence);
        }
        catch
        {
            info.DateTime = best.RawDate;
            info.DateTimeConfidence = FieldConfidence.Low();
        }

        // Extract receipt/transaction number
        foreach (var zl in allLines)
        {
            var rnMatch = ReceiptNumberPattern().Match(zl.Line.Text);
            if (rnMatch.Success)
            {
                info.ReceiptNumber = rnMatch.Groups[1].Value.Trim();
                break;
            }
        }

        return info;
    }

    /// <summary>Runs date regexes; adds sane-range hits to <paramref name="candidates"/>.</summary>
    private static void FindDates(string text, double confidence, ReceiptZone zone, List<DateCandidate> candidates)
    {
        // Zone bonus: dates in header/footer are more likely correct
        double zoneBonus = zone is ReceiptZone.Header or ReceiptZone.Footer ? 10 : 0;

        var patterns = new (Regex pattern, string[] formats)[]
        {
            (UsDate(), new[] { "MM/dd/yyyy", "M/d/yyyy", "MM/dd/yy", "M/d/yy" }),
            (EuDate(), new[] { "dd-MM-yyyy", "dd.MM.yyyy", "d-M-yyyy", "d.M.yyyy", "dd-MM-yy", "dd.MM.yy" }),
            (IsoDate(), new[] { "yyyy-MM-dd" }),
            (UsDateDash(), new[] { "MM-dd-yyyy", "MM-dd-yy", "M-d-yyyy" }),
        };

        foreach (var (pattern, formats) in patterns)
        {
            foreach (Match match in pattern.Matches(text))
            {
                if (DateTime.TryParseExact(match.Value, formats,
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                {
                    // Sanity: date should be in a reasonable range
                    if (parsed.Year < 2000 || parsed > DateTime.Now.AddDays(30)) continue;

                    var timeMatch = TimePattern().Match(text);
                    candidates.Add(new DateCandidate
                    {
                        RawDate = match.Value,
                        RawTime = timeMatch.Success ? timeMatch.Value : null,
                        Parsed = parsed,
                        Confidence = confidence + zoneBonus,
                    });
                }
            }
        }
    }

    /// <summary>Maps original line index to first time match on that line.</summary>
    private static Dictionary<int, string> BuildTimeMap(List<ZoneClassifier.ZonedLine> allLines)
    {
        var map = new Dictionary<int, string>();
        foreach (var zl in allLines)
        {
            var m = TimePattern().Match(zl.Line.Text);
            if (m.Success)
                map[zl.Line.OriginalIndex] = m.Value;
        }
        return map;
    }

    /// <summary>Parses 12h/24h time strings into <paramref name="time"/>.</summary>
    private static bool TryParseTime(string raw, out TimeSpan time)
    {
        time = default;
        raw = raw.Trim();

        string[] formats = { "hh:mm:ss tt", "h:mm:ss tt", "HH:mm:ss", "hh:mm tt", "h:mm tt", "HH:mm", "H:mm" };
        if (DateTime.TryParseExact(raw, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
        {
            time = dt.TimeOfDay;
            return true;
        }
        return false;
    }

    [GeneratedRegex(@"\b(\d{1,2})/(\d{1,2})/(\d{2,4})\b")]
    private static partial Regex UsDate();

    [GeneratedRegex(@"\b(\d{1,2})[.-](\d{1,2})[.-](\d{2,4})\b")]
    private static partial Regex EuDate();

    [GeneratedRegex(@"\b(\d{4})-(\d{1,2})-(\d{1,2})\b")]
    private static partial Regex IsoDate();

    [GeneratedRegex(@"\b(\d{1,2})-(\d{1,2})-(\d{2,4})\b")]
    private static partial Regex UsDateDash();

    [GeneratedRegex(@"\b(\d{1,2}:\d{2}(:\d{2})?\s*(AM|PM|am|pm)?)\b")]
    private static partial Regex TimePattern();

    [GeneratedRegex(@"(?:receipt|trans(?:action)?|order|check|ref)\s*#?\s*:?\s*(\S+)", RegexOptions.IgnoreCase)]
    private static partial Regex ReceiptNumberPattern();
}
