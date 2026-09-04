namespace OcrAgent.Runs;

/// <summary>Observes completed runs on the current async request without coupling the engine to HTTP.</summary>
public static class RunEventScope
{
    private static readonly AsyncLocal<Action<RunRecord>?> Observer = new();

    /// <summary>Sets the completed-run observer for the current asynchronous flow.</summary>
    /// <param name="observer">Callback invoked when a run completes, or null to suppress reporting.</param>
    /// <returns>A scope that restores the previous observer when disposed.</returns>
    public static IDisposable Enter(Action<RunRecord>? observer)
    {
        var previous = Observer.Value;
        Observer.Value = observer;
        return new RunEventScopeHandle(() => Observer.Value = previous);
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

}
