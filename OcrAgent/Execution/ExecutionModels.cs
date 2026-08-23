using System.Text.Json;
using OcrAgent.Agent;
using OcrAgent.Runs;
using OcrAgent.Workflows.Model;

namespace OcrAgent.Execution;

/// <summary>The kind of engine operation requested by a transport.</summary>
public enum ExecutionMode
{
    Agent,
    Tool,
    Workflow,
}

/// <summary>
/// Transport-neutral description of one isolated agent, tool, or workflow execution.
/// </summary>
public sealed record ExecutionRequest(
    ExecutionMode Mode,
    string? Name = null,
    string? ImageReference = null,
    string? OverridesJson = null,
    IReadOnlyDictionary<string, JsonElement>? Arguments = null,
    string? Message = null,
    string Source = "queue",
    string? ConversationId = null);

/// <summary>Complete execution outcome, including observable activity and produced files.</summary>
public sealed record ExecutionResult(
    ExecutionMode Mode,
    string Output,
    WorkflowRunResult? Workflow,
    IReadOnlyList<AgentStreamEvent> Events,
    IReadOnlyList<RunRecord> Runs,
    RunRecord? PrimaryRun,
    IReadOnlyList<AgentArtifact> Artifacts);
