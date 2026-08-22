using Dapper;

namespace OcrAgent.Sql;

public sealed class SqlUserStore(SqlDatabase database) : IUserStore
{
    public bool IsEnabled => true;

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
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(
            sql, new { userId, identityProvider, displayName, now = DateTime.UtcNow },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }
}
