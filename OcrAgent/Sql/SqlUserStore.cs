using Dapper;

namespace OcrAgent.Sql;

/// <summary>Azure SQL implementation of authenticated user profile storage.</summary>
public sealed class SqlUserStore : IUserStore
{
    private readonly SqlDatabase _database;

    /// <summary>Initializes a user store backed by the specified SQL database.</summary>
    public SqlUserStore(SqlDatabase database)
    {
        _database = database;
    }

    /// <summary>Gets a value indicating that durable user storage is available.</summary>
    public bool IsEnabled => true;

    /// <summary>Creates or refreshes an authenticated user's profile in Azure SQL.</summary>
    public async Task UpsertAsync(
        string userId,
        string identityProvider,
        string? displayName,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE dbo.Users
            SET IdentityProvider = @identityProvider,
                DisplayName = @displayName,
                LastSeenUtc = @now
            WHERE UserId = @userId;

            IF @@ROWCOUNT = 0
            BEGIN
                INSERT INTO dbo.Users
                    (UserId, IdentityProvider, DisplayName, CreatedUtc, LastSeenUtc)
                VALUES (@userId, @identityProvider, @displayName, @now, @now);
            END;
            """;
        await using var connection = await _database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(
            sql, new { userId, identityProvider, displayName, now = DateTime.UtcNow },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }
}
