using OcrAgent.Workflows.Model;

namespace OcrAgent.Agent;

/// <summary>Flows workflow progress from an agent-invoked tool back to the active REST turn.</summary>
public static class AgentProgressScope
{
    private static readonly AsyncLocal<IProgress<WorkflowStepProgress>?> CurrentProgress = new();

    public static IProgress<WorkflowStepProgress>? Current => CurrentProgress.Value;

    public static IDisposable Enter(Action<AgentStreamEvent>? onEvent)
    {
        var previous = CurrentProgress.Value;
        CurrentProgress.Value = onEvent is null
            ? null
            : new Progress<WorkflowStepProgress>(step => onEvent(AgentStreamEvent.WorkflowStep(step)));
        return new Handle(() => CurrentProgress.Value = previous);
    }

    private sealed class Handle(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;
        public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }
}
