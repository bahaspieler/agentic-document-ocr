namespace OcrAgent.Storage;

/// <summary>Running statistics for one storage operation name.</summary>
public sealed class HealthMetrics
{
    public required string OperationName { get; init; }
    public int TotalCalls { get; set; }
    public int SuccessfulCalls { get; set; }
    public int FailedCalls { get; set; }
    public long TotalDurationMs { get; set; }
    public long MinDurationMs { get; set; }
    public long MaxDurationMs { get; set; }
    public DateTime LastCallTime { get; set; }
    public double SuccessRate => TotalCalls > 0 ? (double)SuccessfulCalls / TotalCalls : 1.0;
    public long AverageDurationMs => SuccessfulCalls > 0 ? TotalDurationMs / SuccessfulCalls : 0;
}
