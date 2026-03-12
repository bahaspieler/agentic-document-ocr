namespace OcrAgent.Workflows.Model;

public sealed class StepTraceEntry
{
    public string StepId { get; set; } = "";
    public string ToolName { get; set; } = "";
    public Dictionary<string, object?> Args { get; set; } = new();
    public string InputRef { get; set; } = "";
    public string OutputRef { get; set; } = "";
    public long DurationMs { get; set; }
    public bool Success { get; set; }
    public string? Error { get; set; }
}
