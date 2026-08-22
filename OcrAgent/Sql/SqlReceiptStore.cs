using System.Data;
using System.Globalization;
using System.Text.Json;
using Dapper;
using OcrAgent.Extraction.Models;
using OcrAgent.Extraction.RuleBased;

namespace OcrAgent.Sql;

public sealed class SqlReceiptStore(SqlDatabase database) : IReceiptStore
{
    public bool IsEnabled => true;

    public async Task SaveAsync(
        ReceiptData receipt,
        string? runId,
        string? conversationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        if (string.IsNullOrWhiteSpace(receipt.ReceiptId))
            throw new ArgumentException("ReceiptId is required.", nameof(receipt));

        var transactionUtc = ParseDate(receipt.Transaction.DateTime);
        var rawJson = JsonSerializer.Serialize(receipt);
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(
            IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(false);

        if (await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT COUNT(1) FROM dbo.Receipts WHERE ReceiptId = @receiptId;",
                new { receiptId = receipt.ReceiptId }, transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false) > 0)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        const string receiptSql = """
            INSERT INTO dbo.Receipts
                (ReceiptId, RunId, ConversationId, Merchant, TransactionUtc, Currency,
                 Subtotal, Tax, Total, OcrConfidence, IsValid, ValidationScore, CreatedUtc, RawJson,
                 CurrentJson, UpdatedUtc, ReviewStatus, CurrentRevision)
            VALUES
                (@receiptId, @runId, @conversationId, @merchant, @transactionUtc, @currency,
                 @subtotal, @tax, @total, @ocrConfidence, @isValid, @validationScore, @createdUtc, @rawJson,
                 @rawJson, @createdUtc, @reviewStatus, 0);
            """;
        await connection.ExecuteAsync(new CommandDefinition(
            receiptSql,
            new
            {
                receiptId = receipt.ReceiptId,
                runId,
                conversationId,
                merchant = Limit(receipt.Merchant.Name, 300),
                transactionUtc,
                currency = Limit(receipt.Totals.Currency, 12) ?? "USD",
                subtotal = receipt.Totals.Subtotal,
                tax = receipt.Totals.Tax,
                total = receipt.Totals.Total,
                ocrConfidence = receipt.Source.OcrConfidence,
                isValid = receipt.Validation.IsValid,
                validationScore = receipt.Validation.ValidationScore,
                reviewStatus = receipt.Validation.ReviewStatus,
                createdUtc = DateTime.UtcNow,
                rawJson
            },
            transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        await InsertItemsAsync(
            connection, transaction, receipt, cancellationToken).ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<ReceiptRecord?> GetAsync(
        string receiptId, CancellationToken cancellationToken = default)
    {
        const string sql = "SELECT * FROM dbo.Receipts WHERE ReceiptId = @receiptId;";
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        return await connection.QuerySingleOrDefaultAsync<ReceiptRecord>(new CommandDefinition(
            sql, new { receiptId }, cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public async Task<ReceiptRecord> UpdateAsync(
        string receiptId,
        ReceiptData corrected,
        byte[] expectedRowVersion,
        string editorIdentity,
        string? changeSummary = null,
        bool verified = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(corrected);
        ArgumentNullException.ThrowIfNull(expectedRowVersion);
        ValidateCorrection(corrected, changeSummary);
        corrected.ReceiptId = receiptId;
        corrected.Items = corrected.Items
            .Select((item, index) => { item.Index = index; return item; })
            .ToList();
        corrected.Validation = ReceiptValidator.Validate(corrected);
        corrected.Validation.ReviewStatus = verified ? "user_verified" : "user_corrected";
        var currentJson = JsonSerializer.Serialize(corrected);
        var now = DateTime.UtcNow;

        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(
            IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(false);
        const string updateSql = """
            UPDATE dbo.Receipts
            SET Merchant = @merchant, TransactionUtc = @transactionUtc, Currency = @currency,
                Subtotal = @subtotal, Tax = @tax, Total = @total, OcrConfidence = @ocrConfidence,
                IsValid = @isValid,
                ValidationScore = @validationScore, CurrentJson = @currentJson,
                UpdatedUtc = @now, ReviewStatus = @reviewStatus,
                CurrentRevision = CurrentRevision + 1
            OUTPUT inserted.*
            WHERE ReceiptId = @receiptId AND RowVersion = @expectedRowVersion;
            """;
        var updated = await connection.QuerySingleOrDefaultAsync<ReceiptRecord>(new CommandDefinition(
            updateSql,
            new
            {
                receiptId,
                expectedRowVersion,
                merchant = corrected.Merchant.Name,
                transactionUtc = ParseDate(corrected.Transaction.DateTime),
                currency = corrected.Totals.Currency,
                subtotal = corrected.Totals.Subtotal,
                tax = corrected.Totals.Tax,
                total = corrected.Totals.Total,
                ocrConfidence = corrected.Source.OcrConfidence,
                isValid = corrected.Validation.IsValid,
                validationScore = corrected.Validation.ValidationScore,
                currentJson,
                now,
                reviewStatus = corrected.Validation.ReviewStatus
            },
            transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false);
        if (updated is null)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw new ReceiptConcurrencyException(receiptId);
        }

        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM dbo.ReceiptItems WHERE ReceiptId = @receiptId;",
            new { receiptId }, transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false);
        await InsertItemsAsync(connection, transaction, corrected, cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO dbo.ReceiptRevisions
                (ReceiptId, RevisionNumber, EditorIdentity, CreatedUtc, CorrectedJson, ChangeSummary)
            VALUES
                (@receiptId, @revision, @editorIdentity, @now, @currentJson, @changeSummary);
            """,
            new
            {
                receiptId,
                revision = updated.CurrentRevision,
                editorIdentity,
                now,
                currentJson,
                changeSummary = string.IsNullOrWhiteSpace(changeSummary)
                    ? "Corrected receipt fields and line items."
                    : changeSummary.Trim()
            },
            transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return updated;
    }

    public async Task<IReadOnlyList<ReceiptRevisionRecord>> ListRevisionsAsync(
        string receiptId, CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var rows = await connection.QueryAsync<ReceiptRevisionRecord>(new CommandDefinition(
            """
            SELECT * FROM dbo.ReceiptRevisions
            WHERE ReceiptId = @receiptId ORDER BY RevisionNumber DESC;
            """,
            new { receiptId }, cancellationToken: cancellationToken)).ConfigureAwait(false);
        return rows.AsList();
    }

    public async Task<IReadOnlyList<ReceiptRecord>> QueryAsync(
        string? merchant = null,
        DateTime? fromUtc = null,
        DateTime? toUtc = null,
        int top = 100,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT TOP (@top) *
            FROM dbo.Receipts
            WHERE (@merchant IS NULL OR Merchant LIKE '%' + @merchant + '%')
              AND (@fromUtc IS NULL OR TransactionUtc >= @fromUtc)
              AND (@toUtc IS NULL OR TransactionUtc < @toUtc)
            ORDER BY CreatedUtc DESC;
            """;
        await using var connection = await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var rows = await connection.QueryAsync<ReceiptRecord>(new CommandDefinition(
            sql,
            new
            {
                merchant = string.IsNullOrWhiteSpace(merchant) ? null : merchant.Trim(),
                fromUtc,
                toUtc,
                top = Math.Clamp(top, 1, 200)
            },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
        return rows.AsList();
    }

    private static DateTime? ParseDate(string? value)
    {
        if (!DateTimeOffset.TryParse(
                value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
            return null;
        return parsed.UtcDateTime;
    }

    private static async Task InsertItemsAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        ReceiptData receipt,
        CancellationToken cancellationToken)
    {
        const string itemSql = """
            INSERT INTO dbo.ReceiptItems
                (ReceiptId, ItemIndex, Description, Quantity, UnitPrice, LineTotal)
            VALUES
                (@receiptId, @itemIndex, @description, @quantity, @unitPrice, @lineTotal);
            """;
        foreach (var item in receipt.Items)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                itemSql,
                new
                {
                    receiptId = receipt.ReceiptId,
                    itemIndex = item.Index,
                    description = Limit(item.Description, 1000) ?? "",
                    quantity = item.Quantity,
                    unitPrice = item.UnitPrice,
                    lineTotal = item.LineTotal
                },
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
        }
    }

    private static void ValidateCorrection(ReceiptData receipt, string? changeSummary)
    {
        if (receipt.Merchant is null || receipt.Transaction is null ||
            receipt.Totals is null || receipt.Source is null ||
            receipt.Items is null)
            throw new ArgumentException("Receipt sections cannot be null.", nameof(receipt));
        if (receipt.Merchant.Name?.Length > 300)
            throw new ArgumentException("Merchant name is limited to 300 characters.", nameof(receipt));
        if (string.IsNullOrWhiteSpace(receipt.Totals.Currency) ||
            receipt.Totals.Currency.Length > 12)
            throw new ArgumentException("Currency is required and limited to 12 characters.", nameof(receipt));
        if (receipt.Items.Any(item =>
            item is null || item.Description is null || item.Description.Length > 1000))
            throw new ArgumentException(
                "Every line item needs a description of at most 1000 characters.", nameof(receipt));
        if (changeSummary?.Length > 1000)
            throw new ArgumentException("Change summary is limited to 1000 characters.", nameof(changeSummary));
    }

    private static string? Limit(string? value, int max) =>
        value is null || value.Length <= max ? value : value[..max];
}
