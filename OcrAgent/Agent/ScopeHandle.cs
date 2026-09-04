namespace OcrAgent.Agent;

internal sealed class ScopeHandle(Action dispose) : IDisposable
{
    private Action? _dispose = dispose;

    public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
}
