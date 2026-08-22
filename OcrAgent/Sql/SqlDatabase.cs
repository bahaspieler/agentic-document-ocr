using Dapper;
using Microsoft.Data.SqlClient;

namespace OcrAgent.Sql;

/// <summary>Opens SQL connections and installs the additive Phase 7 schema.</summary>
public sealed class SqlDatabase
{
    private readonly string _connectionString;

    public SqlDatabase(SqlConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        configuration.Validate();
        _connectionString = configuration.ConnectionString;
    }

    /// <summary>Returns an open connection owned by the caller.</summary>
    public async Task<SqlConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }

    /// <summary>
    /// Creates tables and indexes with guarded DDL. It is safe for every replica to call at startup.
    /// </summary>
    public async Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(SchemaSql, cancellationToken: cancellationToken))
            .ConfigureAwait(false);
    }

    private const string SchemaSql = """
        IF OBJECT_ID(N'dbo.Users', N'U') IS NULL
        BEGIN
            CREATE TABLE dbo.Users (
                UserId nvarchar(200) NOT NULL CONSTRAINT PK_Users PRIMARY KEY,
                IdentityProvider nvarchar(100) NOT NULL,
                DisplayName nvarchar(256) NULL,
                CreatedUtc datetime2(7) NOT NULL,
                LastSeenUtc datetime2(7) NOT NULL
            );
        END;

        IF OBJECT_ID(N'dbo.Conversations', N'U') IS NULL
        BEGIN
            CREATE TABLE dbo.Conversations (
                Id nvarchar(64) NOT NULL CONSTRAINT PK_Conversations PRIMARY KEY,
                OwnerUserId nvarchar(200) NULL,
                Title nvarchar(200) NOT NULL,
                CreatedUtc datetime2(7) NOT NULL,
                LastActiveUtc datetime2(7) NOT NULL,
                TurnCount int NOT NULL CONSTRAINT DF_Conversations_TurnCount DEFAULT 0,
                SchemaVersion int NOT NULL CONSTRAINT DF_Conversations_SchemaVersion DEFAULT 1,
                PayloadJson nvarchar(max) NULL,
                RowVersion rowversion NOT NULL,
                CONSTRAINT FK_Conversations_Users FOREIGN KEY (OwnerUserId)
                    REFERENCES dbo.Users(UserId) ON DELETE SET NULL
            );
            CREATE INDEX IX_Conversations_Owner_LastActive
                ON dbo.Conversations(OwnerUserId, LastActiveUtc DESC);
        END;

        IF OBJECT_ID(N'dbo.ConversationTurns', N'U') IS NULL
        BEGIN
            CREATE TABLE dbo.ConversationTurns (
                ConversationId nvarchar(64) NOT NULL,
                IdempotencyKey nvarchar(100) NOT NULL,
                TurnIndex int NOT NULL,
                CreatedUtc datetime2(7) NOT NULL,
                UserMessage nvarchar(max) NOT NULL,
                Answer nvarchar(max) NOT NULL,
                CONSTRAINT PK_ConversationTurns PRIMARY KEY (ConversationId, IdempotencyKey),
                CONSTRAINT FK_ConversationTurns_Conversations FOREIGN KEY (ConversationId)
                    REFERENCES dbo.Conversations(Id) ON DELETE CASCADE
            );
            CREATE UNIQUE INDEX UX_ConversationTurns_Index
                ON dbo.ConversationTurns(ConversationId, TurnIndex);
        END;

        IF OBJECT_ID(N'dbo.Receipts', N'U') IS NULL
        BEGIN
            CREATE TABLE dbo.Receipts (
                ReceiptId nvarchar(200) NOT NULL CONSTRAINT PK_Receipts PRIMARY KEY,
                RunId nvarchar(64) NULL,
                ConversationId nvarchar(64) NULL,
                Merchant nvarchar(300) NULL,
                TransactionUtc datetime2(7) NULL,
                Currency nvarchar(12) NOT NULL,
                Subtotal decimal(19,4) NULL,
                Tax decimal(19,4) NULL,
                Total decimal(19,4) NULL,
                IsValid bit NOT NULL,
                ValidationScore int NOT NULL,
                CreatedUtc datetime2(7) NOT NULL,
                RawJson nvarchar(max) NOT NULL,
                CONSTRAINT FK_Receipts_Conversations FOREIGN KEY (ConversationId)
                    REFERENCES dbo.Conversations(Id) ON DELETE SET NULL
                -- Future work: add an embedding vector column and similarity index.
            );
            CREATE INDEX IX_Receipts_Merchant_TransactionUtc
                ON dbo.Receipts(Merchant, TransactionUtc DESC);
            CREATE INDEX IX_Receipts_RunId ON dbo.Receipts(RunId);
        END;

        IF OBJECT_ID(N'dbo.ReceiptItems', N'U') IS NULL
        BEGIN
            CREATE TABLE dbo.ReceiptItems (
                Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_ReceiptItems PRIMARY KEY,
                ReceiptId nvarchar(200) NOT NULL,
                ItemIndex int NOT NULL,
                Description nvarchar(1000) NOT NULL,
                Quantity decimal(19,4) NULL,
                UnitPrice decimal(19,4) NULL,
                LineTotal decimal(19,4) NULL,
                CONSTRAINT FK_ReceiptItems_Receipts FOREIGN KEY (ReceiptId)
                    REFERENCES dbo.Receipts(ReceiptId) ON DELETE CASCADE
            );
            CREATE UNIQUE INDEX UX_ReceiptItems_Receipt_Index
                ON dbo.ReceiptItems(ReceiptId, ItemIndex);
        END;
        """;
}
