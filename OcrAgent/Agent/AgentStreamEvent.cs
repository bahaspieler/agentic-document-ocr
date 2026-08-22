using OcrAgent.Workflows.Model;

namespace OcrAgent.Agent;

/// <summary>Transport-neutral live event emitted while the natural-language agent runs.</summary>
public sealed record AgentStreamEvent(
    string Type,
    string? Text = null,
    string? ToolName = null,
    WorkflowStepProgress? Step = null)
{
    public static AgentStreamEvent Token(string text) => new("token", Text: text);
    public static AgentStreamEvent Tool(string toolName) => new("tool", ToolName: toolName);
    public static AgentStreamEvent WorkflowStep(WorkflowStepProgress step) => new("step", Step: step);
}
