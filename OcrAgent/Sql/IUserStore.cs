namespace OcrAgent.Sql;

/// <summary>Minimal profile data for authenticated Microsoft identities.</summary>
public interface IUserStore
{
    bool IsEnabled { get; }
    Task UpsertAsync(
        string userId, string identityProvider, string? displayName,
        CancellationToken cancellationToken = default);
}
