namespace OcrAgent.Extraction.RuleBased;

internal sealed class DateCandidate
{
    public string RawDate { get; set; } = "";
    public string? RawTime { get; set; }
    public DateTime Parsed { get; set; }
    public double Confidence { get; set; }
    public double Score { get; set; }
}
