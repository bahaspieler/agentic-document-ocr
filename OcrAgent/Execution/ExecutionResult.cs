using OcrAgent.Agent;
using OcrAgent.Runs;
using OcrAgent.Workflows.Model;

namespace OcrAgent.Execution;

/// <summary>Complete execution outcome, including observable activity and produced files.</summary>
public sealed record ExecutionResult(
    ExecutionMode Mode,
    string Output,
    WorkflowRunResult? Workflow,
    IReadOnlyList<AgentStreamEvent> Events,
    IReadOnlyList<RunRecord> Runs,
    RunRecord? PrimaryRun,
    IReadOnlyList<AgentArtifact> Artifacts);
