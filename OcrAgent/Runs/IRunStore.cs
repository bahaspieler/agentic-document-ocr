namespace OcrAgent.Runs;

/// <summary>
/// Where executed jobs are recorded and read back.
/// </summary>
/// <remarks>
/// Implementations must not let a bookkeeping failure escape into the job that produced the record:
/// a run that succeeded should still be reported as successful even when its row could not be
/// written.
/// </remarks>
public interface IRunStore
{
    /// <summary>False for the no-op store, so callers can explain why history is empty.</summary>
    bool IsEnabled { get; }

    /// <summary>Writes the record for one finished job.</summary>
    /// <param name="record">Completed run.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SaveAsync(RunRecord record, CancellationToken cancellationToken = default);

    /// <summary>Reads one record back by id.</summary>
    /// <param name="runId">Id returned when the job ran.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The record, or null when no run has that id.</returns>
    Task<RunRecord?> GetAsync(string runId, CancellationToken cancellationToken = default);

    /// <summary>Lists recent records, newest first.</summary>
    /// <param name="top">Maximum number of records to return.</param>
    /// <param name="mode">Optional filter: <c>tool</c> or <c>workflow</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Records in descending start time order.</returns>
    Task<IReadOnlyList<RunRecord>> ListAsync(
        int top = 20, string? mode = null, CancellationToken cancellationToken = default);
}
