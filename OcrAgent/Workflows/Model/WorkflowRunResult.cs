namespace OcrAgent.Workflows.Model;

public sealed class WorkflowRunResult
{
    public string WorkflowId { get; set; } = "";
    public string Version { get; set; } = "";
    public string InputImageRef { get; set; } = "";
    public string OutputImageRef { get; set; } = "";
    public List<StepTraceEntry> Trace { get; set; } = new();
    public long TotalDurationMs { get; set; }
    public bool Success { get; set; }
    public string? Error { get; set; }
}
