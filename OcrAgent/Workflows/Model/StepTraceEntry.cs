namespace OcrAgent.Workflows.Model;

/// <summary>
/// One row in the workflow trace: step identity, IO refs, args snapshot, timing, and error.
/// </summary>
public sealed class StepTraceEntry
{
    /// <summary>Workflow step id.</summary>
    public string StepId { get; set; } = "";

    /// <summary>Tool name that executed.</summary>
    public string ToolName { get; set; } = "";

    /// <summary>Arguments passed to the tool after resolution.</summary>
    public Dictionary<string, object?> Args { get; set; } = new();

    /// <summary>Image reference fed into the step.</summary>
    public string InputRef { get; set; } = "";

    /// <summary>Image or string reference produced by the step.</summary>
    public string OutputRef { get; set; } = "";

    /// <summary>Step duration in milliseconds.</summary>
    public long DurationMs { get; set; }

    /// <summary>False when the step threw or reported failure.</summary>
    public bool Success { get; set; }

    /// <summary>Error text when <see cref="Success"/> is false.</summary>
    public string? Error { get; set; }
}
