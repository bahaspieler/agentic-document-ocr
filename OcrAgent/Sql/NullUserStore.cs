namespace OcrAgent.Sql;

/// <summary>Disabled user store used when Azure SQL is not configured.</summary>
public sealed class NullUserStore : IUserStore
{
    /// <summary>Gets the shared disabled user store.</summary>
    public static NullUserStore Instance { get; } = new();

    /// <summary>Gets a value indicating that durable user storage is unavailable.</summary>
    public bool IsEnabled => false;
    private NullUserStore() { }

    /// <summary>Ignores a profile update because durable storage is disabled.</summary>
    public Task UpsertAsync(
        string userId, string identityProvider, string? displayName,
        CancellationToken cancellationToken = default) => Task.CompletedTask;
}
