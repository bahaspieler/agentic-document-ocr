namespace OcrAgent.Storage;

/// <summary>Running statistics for one storage operation name.</summary>
public sealed class HealthMetrics
{
    /// <summary>Logical operation represented by these metrics.</summary>
    public required string OperationName { get; init; }
    /// <summary>Total number of recorded calls.</summary>
    public int TotalCalls { get; set; }
    /// <summary>Number of successful calls.</summary>
    public int SuccessfulCalls { get; set; }
    /// <summary>Number of failed calls.</summary>
    public int FailedCalls { get; set; }
    /// <summary>Combined duration of successful calls, in milliseconds.</summary>
    public long TotalDurationMs { get; set; }
    /// <summary>Shortest successful-call duration, in milliseconds.</summary>
    public long MinDurationMs { get; set; }
    /// <summary>Longest successful-call duration, in milliseconds.</summary>
    public long MaxDurationMs { get; set; }
    /// <summary>UTC time of the most recently recorded call.</summary>
    public DateTime LastCallTime { get; set; }
    /// <summary>Fraction of calls that succeeded.</summary>
    public double SuccessRate => TotalCalls > 0 ? (double)SuccessfulCalls / TotalCalls : 1.0;
    /// <summary>Average duration of successful calls, in milliseconds.</summary>
    public long AverageDurationMs => SuccessfulCalls > 0 ? TotalDurationMs / SuccessfulCalls : 0;
}
