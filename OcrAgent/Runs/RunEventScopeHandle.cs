namespace OcrAgent.Runs;

/// <summary>Restores a previous run-event observer when its scope ends.</summary>
internal sealed class RunEventScopeHandle(Action dispose) : IDisposable
{
    private Action? _dispose = dispose;

    /// <summary>Restores the previous observer once.</summary>
    public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
}
