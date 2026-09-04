namespace OcrAgent.Workflows.Executors;

/// <summary>
/// Describes one tool invocation within a workflow branch.
/// </summary>
/// <param name="StepId">Step identifier used for tracing.</param>
/// <param name="ToolName">Registered tool name.</param>
/// <param name="Args">Argument template for the tool.</param>
/// <param name="Passthrough">Whether to preserve the preceding image reference as output.</param>
/// <param name="Outputs">Optional context keys whose values derive from the tool output.</param>
internal record BranchToolStep(
    string StepId,
    string ToolName,
    Dictionary<string, object?> Args,
    bool Passthrough,
    Dictionary<string, string>? Outputs);
