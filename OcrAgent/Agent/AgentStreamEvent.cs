using OcrAgent.Workflows.Model;
using OcrAgent.Runs;

namespace OcrAgent.Agent;

/// <summary>Transport-neutral live event emitted while the natural-language agent runs.</summary>
public sealed record AgentStreamEvent(
    string Type,
    string? Text = null,
    string? ToolName = null,
    WorkflowStepProgress? Step = null,
    RunRecord? Run = null,
    AgentArtifact? Artifact = null)
{
    /// <summary>Creates an event containing a streamed response token.</summary>
    /// <param name="text">The token text.</param>
    /// <returns>A token event.</returns>
    public static AgentStreamEvent Token(string text) => new("token", Text: text);

    /// <summary>Creates an event identifying a tool invocation.</summary>
    /// <param name="toolName">The invoked tool's name.</param>
    /// <returns>A tool event.</returns>
    public static AgentStreamEvent Tool(string toolName) => new("tool", ToolName: toolName);

    /// <summary>Creates an event containing workflow step progress.</summary>
    /// <param name="step">The workflow step progress.</param>
    /// <returns>A workflow step event.</returns>
    public static AgentStreamEvent WorkflowStep(WorkflowStepProgress step) => new("step", Step: step);

    /// <summary>Creates an event containing a completed run.</summary>
    /// <param name="run">The completed run record.</param>
    /// <returns>A run completion event.</returns>
    public static AgentStreamEvent RunCompleted(RunRecord run) => new("run", Run: run);

    /// <summary>Creates an event containing a discovered artifact.</summary>
    /// <param name="artifact">The available artifact.</param>
    /// <returns>An artifact event.</returns>
    public static AgentStreamEvent ArtifactAvailable(AgentArtifact artifact) =>
        new("artifact", Artifact: artifact);
}
