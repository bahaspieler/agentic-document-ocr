namespace OcrAgent.Workflows.Model;

public sealed class WorkflowRunResult
{
    public string WorkflowId { get; set; } = "";
    public string Version { get; set; } = "";
    public string InputImageRef { get; set; } = "";
    /// <summary>UUID v7 (time-ordered) generated per workflow execution for chronological sorting and SQL Server compatibility.</summary>
    public string RunId { get; set; } = "";
    public string OutputImageRef { get; set; } = "";
    public string OutputDirectory { get; set; } = "";
    public string? FinalOutput { get; set; }
    public Dictionary<string, string> Context { get; set; } = new();
    public List<StepTraceEntry> Trace { get; set; } = new();
    public long TotalDurationMs { get; set; }
    public bool Success { get; set; }
    public string? Error { get; set; }
}
