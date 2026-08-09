namespace OcrAgent.Runs;

/// <summary>
/// Run store that keeps nothing, used when no run store is configured.
/// </summary>
/// <remarks>
/// Recording is an addition to the engine rather than a requirement of it, so a developer with no
/// storage account still gets a working CLI, chat session, and MCP server. Callers check
/// <see cref="IsEnabled"/> when they want to say why history is empty.
/// </remarks>
public sealed class NullRunStore : IRunStore
{
    /// <summary>Shared instance; the type holds no state.</summary>
    public static readonly NullRunStore Instance = new();

    /// <inheritdoc />
    public bool IsEnabled => false;

    /// <inheritdoc />
    public Task SaveAsync(RunRecord record, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    /// <inheritdoc />
    public Task<RunRecord?> GetAsync(string runId, CancellationToken cancellationToken = default) =>
        Task.FromResult<RunRecord?>(null);

    /// <inheritdoc />
    public Task<IReadOnlyList<RunRecord>> ListAsync(
        int top = 20, string? mode = null, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<RunRecord>>(Array.Empty<RunRecord>());
}
