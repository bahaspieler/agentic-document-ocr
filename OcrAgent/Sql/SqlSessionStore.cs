using System.Data;
using Dapper;

namespace OcrAgent.Sql;

/// <summary>Azure SQL implementation of durable conversations.</summary>
public sealed class SqlSessionStore(SqlDatabase database) : ISessionStore
{
    public bool IsEnabled => true;

    #region Conversation Operations

    public async Task<ConversationRecord> CreateAsync(
        string conversationId,
        string? ownerUserId,
        string title,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            INSERT INTO dbo.Conversations
                (Id, OwnerUserId, Title, CreatedUtc, LastActiveUtc, TurnCount, SchemaVersion)
            OUTPUT inserted.*
            VALUES (@conversationId, @ownerUserId, @title, @now, @now, 0, 1);
            """;
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        return await connection.QuerySingleAsync<ConversationRecord>(new CommandDefinition(
            sql,
            new { conversationId, ownerUserId, title = NormalizeTitle(title), now = DateTime.UtcNow },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public async Task<ConversationRecord?> GetAsync(
        string conversationId, CancellationToken cancellationToken = default)
    {
        const string sql = "SELECT * FROM dbo.Conversations WHERE Id = @conversationId;";
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        return await connection.QuerySingleOrDefaultAsync<ConversationRecord>(new CommandDefinition(
            sql, new { conversationId }, cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ConversationRecord>> ListAsync(
        string ownerUserId, int top = 100, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT TOP (@top) *
            FROM dbo.Conversations
            WHERE OwnerUserId = @ownerUserId
            ORDER BY LastActiveUtc DESC;
            """;
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var rows = await connection.QueryAsync<ConversationRecord>(new CommandDefinition(
            sql, new { ownerUserId, top = Math.Clamp(top, 1, 200) },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
        return rows.AsList();
    }

    public async Task RenameAsync(
        string conversationId,
        string title,
        string? ownerUserId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE dbo.Conversations
            SET Title = @title, LastActiveUtc = @now
            WHERE Id = @conversationId
              AND ((@ownerUserId IS NULL AND OwnerUserId IS NULL) OR OwnerUserId = @ownerUserId);
            """;
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var changed = await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new { conversationId, title = NormalizeTitle(title), ownerUserId, now = DateTime.UtcNow },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
        if (changed == 0)
            throw new KeyNotFoundException($"Conversation '{conversationId}' was not found.");
    }

    public async Task DeleteAsync(
        string conversationId, string? ownerUserId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            DELETE FROM dbo.Conversations
            WHERE Id = @conversationId
              AND ((@ownerUserId IS NULL AND OwnerUserId IS NULL) OR OwnerUserId = @ownerUserId);
            """;
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(
            sql, new { conversationId, ownerUserId }, cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public async Task ClaimAsync(
        IEnumerable<string> conversationIds,
        string ownerUserId,
        CancellationToken cancellationToken = default)
    {
        var ids = conversationIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().Take(100).ToArray();
        if (ids.Length == 0)
            return;

        const string sql = """
            UPDATE dbo.Conversations
            SET OwnerUserId = @ownerUserId, LastActiveUtc = @now
            WHERE Id IN @ids AND OwnerUserId IS NULL;
            """;
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(
            sql, new { ids, ownerUserId, now = DateTime.UtcNow },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    #endregion

    #region Turn Operations

    public async Task<ConversationTurnRecord?> GetTurnAsync(
        string conversationId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT * FROM dbo.ConversationTurns
            WHERE ConversationId = @conversationId AND IdempotencyKey = @idempotencyKey;
            """;
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        return await connection.QuerySingleOrDefaultAsync<ConversationTurnRecord>(new CommandDefinition(
            sql, new { conversationId, idempotencyKey },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ConversationTurnRecord>> ListTurnsAsync(
        string conversationId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT * FROM dbo.ConversationTurns
            WHERE ConversationId = @conversationId
            ORDER BY TurnIndex;
            """;
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var rows = await connection.QueryAsync<ConversationTurnRecord>(new CommandDefinition(
            sql, new { conversationId }, cancellationToken: cancellationToken)).ConfigureAwait(false);
        return rows.AsList();
    }

    public async Task<ConversationRecord> SaveTurnAsync(
        ConversationRecord expected,
        string payloadJson,
        string idempotencyKey,
        string userMessage,
        string answer,
        string eventsJson,
        string runIdsJson,
        string attachmentsJson,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expected);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(
            IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(false);

        // The rowversion check and turn insert commit together, so concurrent requests cannot
        // overwrite a newer serialized model session.
        const string updateSql = """
            UPDATE dbo.Conversations
            SET PayloadJson = @payloadJson,
                LastActiveUtc = @now,
                TurnCount = TurnCount + 1
            OUTPUT inserted.*
            WHERE Id = @id AND RowVersion = @rowVersion;
            """;
        var now = DateTime.UtcNow;
        var updated = await connection.QuerySingleOrDefaultAsync<ConversationRecord>(new CommandDefinition(
            updateSql,
            new { payloadJson, now, id = expected.Id, rowVersion = expected.RowVersion },
            transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        if (updated is null)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw new ConversationConcurrencyException(expected.Id);
        }

        const string insertSql = """
            INSERT INTO dbo.ConversationTurns
                (ConversationId, IdempotencyKey, TurnIndex, CreatedUtc, UserMessage, Answer,
                 EventsJson, RunIdsJson, AttachmentsJson)
            VALUES (@conversationId, @idempotencyKey, @turnIndex, @now, @userMessage, @answer,
                    @eventsJson, @runIdsJson, @attachmentsJson);
            """;
        await connection.ExecuteAsync(new CommandDefinition(
            insertSql,
            new
            {
                conversationId = expected.Id,
                idempotencyKey,
                turnIndex = updated.TurnCount,
                now,
                userMessage,
                answer,
                eventsJson,
                runIdsJson,
                attachmentsJson
            },
            transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return updated;
    }

    #endregion

    #region Mapping Helpers

    private static string NormalizeTitle(string title)
    {
        var normalized = string.IsNullOrWhiteSpace(title) ? "New conversation" : title.Trim();
        return normalized.Length <= 200 ? normalized : normalized[..200];
    }

    #endregion
}
