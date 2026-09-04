using OcrAgent.Workflows.Model;
using OcrAgent.Execution;

namespace OcrAgent.Agent;

/// <summary>Flows workflow progress from an agent-invoked tool back to the active REST turn.</summary>
public static class AgentProgressScope
{
    private static readonly AsyncLocal<IProgress<WorkflowStepProgress>?> CurrentProgress = new();

    /// <summary>Gets the workflow progress sink active in the current asynchronous context.</summary>
    public static IProgress<WorkflowStepProgress>? Current => CurrentProgress.Value;

    /// <summary>Starts a scope that forwards workflow progress as agent stream events.</summary>
    /// <param name="onEvent">Callback that receives workflow progress events, or null to disable forwarding.</param>
    /// <returns>A handle that restores the previous progress sink when disposed.</returns>
    public static IDisposable Enter(Action<AgentStreamEvent>? onEvent)
    {
        var previous = CurrentProgress.Value;
        CurrentProgress.Value = onEvent is null
            ? null
            : new InlineProgress<WorkflowStepProgress>(
                step => onEvent(AgentStreamEvent.WorkflowStep(step)));
        return new ScopeHandle(() => CurrentProgress.Value = previous);
    }
}
