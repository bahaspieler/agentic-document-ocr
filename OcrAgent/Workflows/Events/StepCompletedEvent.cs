using Microsoft.Agents.AI.Workflows;

namespace OcrAgent.Workflows.Events;

/// <summary>
/// Fired when a workflow tool step finishes (success or failure) for tracing and progress.
/// </summary>
/// <param name="stepId">Workflow step id.</param>
/// <param name="toolName">Tool that ran.</param>
/// <param name="inputRef">Input image reference.</param>
/// <param name="outputRef">Output reference; empty on failure before output.</param>
/// <param name="durationMs">Elapsed milliseconds for the step.</param>
/// <param name="success">True when the tool completed without error.</param>
/// <param name="error">Error message when <paramref name="success"/> is false.</param>
internal sealed class StepCompletedEvent(
    string stepId,
    string toolName,
    string inputRef,
    string outputRef,
    long durationMs,
    bool success,
    string? error) : WorkflowEvent(toolName)
{
    public string StepId { get; } = stepId;
    public string ToolName { get; } = toolName;
    public string InputRef { get; } = inputRef;
    public string OutputRef { get; } = outputRef;
    public long DurationMs { get; } = durationMs;
    public bool Success { get; } = success;
    public string? Error { get; } = error;
}
