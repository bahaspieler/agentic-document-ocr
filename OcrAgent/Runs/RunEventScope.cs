namespace OcrAgent.Runs;

/// <summary>Observes completed runs on the current async request without coupling the engine to HTTP.</summary>
public static class RunEventScope
{
    private static readonly AsyncLocal<Action<RunRecord>?> Observer = new();

    public static IDisposable Enter(Action<RunRecord>? observer)
    {
        var previous = Observer.Value;
        Observer.Value = observer;
        return new Handle(() => Observer.Value = previous);
    }

    internal static void Report(RunRecord record)
    {
        try
        {
            Observer.Value?.Invoke(record);
        }
        catch
        {
            // Live UI reporting is bookkeeping and cannot fail a completed OCR job.
        }
    }

    private sealed class Handle(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;
        public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }
}
