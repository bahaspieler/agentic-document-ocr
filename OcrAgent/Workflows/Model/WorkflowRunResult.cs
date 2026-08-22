namespace OcrAgent.Workflows.Model;

/// <summary>
/// Outcome of one workflow execution: ids, paths, trace, context snapshot, timing, and error state.
/// </summary>
public sealed class WorkflowRunResult
{
    /// <summary>Workflow definition id that ran.</summary>
    public string WorkflowId { get; set; } = "";

    /// <summary>Definition version string at run time.</summary>
    public string Version { get; set; } = "";

    /// <summary>Input image reference passed to the workflow.</summary>
    public string InputImageRef { get; set; } = "";

    /// <summary>UUID v7 per run for ordering and storage keys.</summary>
    public string RunId { get; set; } = "";

    /// <summary>Final image reference produced by the last step.</summary>
    public string OutputImageRef { get; set; } = "";

    /// <summary>Directory where outputs were written (if applicable).</summary>
    public string OutputDirectory { get; set; } = "";

    /// <summary>Non-image result text (e.g. OCR JSON) when different from <see cref="OutputImageRef"/>.</summary>
    public string? FinalOutput { get; set; }

    /// <summary>Structured receipt id when the final output is a receipt export.</summary>
    public string? ReceiptId { get; set; }

    /// <summary>String snapshot of workflow context after the run.</summary>
    public Dictionary<string, string> Context { get; set; } = new();

    /// <summary>Per-step timing and success records.</summary>
    public List<StepTraceEntry> Trace { get; set; } = new();

    /// <summary>Wall-clock duration of the full run in milliseconds.</summary>
    public long TotalDurationMs { get; set; }

    /// <summary>True when no fatal workflow error occurred.</summary>
    public bool Success { get; set; }

    /// <summary>Error message when <see cref="Success"/> is false.</summary>
    public string? Error { get; set; }

    /// <summary>Stable REST-friendly status label.</summary>
    public string Status => Success ? "succeeded" : "failed";
}
