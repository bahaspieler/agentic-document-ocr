namespace OcrAgent.Sql;

/// <summary>Minimal profile data for authenticated Microsoft identities.</summary>
public interface IUserStore
{
    /// <summary>Gets whether durable user storage is available.</summary>
    bool IsEnabled { get; }

    /// <summary>Creates or refreshes an authenticated user's profile.</summary>
    Task UpsertAsync(
        string userId, string identityProvider, string? displayName,
        CancellationToken cancellationToken = default);
}
