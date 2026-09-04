namespace OcrAgent.Runs;

/// <summary>Releases one ambient run scope exactly once.</summary>
internal sealed class RunScopeHandle(Action release) : IDisposable
{
    private bool _released;

    /// <summary>Restores the ambient state captured by the scope.</summary>
    public void Dispose()
    {
        if (_released)
            return;

        _released = true;
        release();
    }
}
