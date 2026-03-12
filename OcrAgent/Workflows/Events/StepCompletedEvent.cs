using Microsoft.Agents.AI.Workflows;

namespace OcrAgent.Workflows.Events;

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
