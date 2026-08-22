namespace OcrAgent.Sql;

public sealed class NullUserStore : IUserStore
{
    public static NullUserStore Instance { get; } = new();
    public bool IsEnabled => false;
    private NullUserStore() { }

    public Task UpsertAsync(
        string userId, string identityProvider, string? displayName,
        CancellationToken cancellationToken = default) => Task.CompletedTask;
}
