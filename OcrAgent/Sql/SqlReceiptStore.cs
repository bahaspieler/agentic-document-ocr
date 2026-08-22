using System.Data;
using System.Globalization;
using System.Text.Json;
using Dapper;
using OcrAgent.Extraction.Models;

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

        const string receiptSql = """
            UPDATE dbo.Receipts
            SET RunId = @runId,
                ConversationId = @conversationId,
                Merchant = @merchant,
                TransactionUtc = @transactionUtc,
                Currency = @currency,
                Subtotal = @subtotal,
                Tax = @tax,
                Total = @total,
                IsValid = @isValid,
                ValidationScore = @validationScore,
                CreatedUtc = @createdUtc,
                RawJson = @rawJson
            WHERE ReceiptId = @receiptId;

            IF @@ROWCOUNT = 0
            BEGIN
                INSERT INTO dbo.Receipts
                    (ReceiptId, RunId, ConversationId, Merchant, TransactionUtc, Currency,
                     Subtotal, Tax, Total, IsValid, ValidationScore, CreatedUtc, RawJson)
                VALUES
                    (@receiptId, @runId, @conversationId, @merchant, @transactionUtc, @currency,
                     @subtotal, @tax, @total, @isValid, @validationScore, @createdUtc, @rawJson);
            END;
            """;
        await connection.ExecuteAsync(new CommandDefinition(
            receiptSql,
            new
            {
                receiptId = receipt.ReceiptId,
                runId,
                conversationId,
                merchant = receipt.Merchant.Name,
                transactionUtc,
                currency = receipt.Totals.Currency,
                subtotal = receipt.Totals.Subtotal,
                tax = receipt.Totals.Tax,
                total = receipt.Totals.Total,
                isValid = receipt.Validation.IsValid,
                validationScore = receipt.Validation.ValidationScore,
                createdUtc = DateTime.UtcNow,
                rawJson
            },
            transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM dbo.ReceiptItems WHERE ReceiptId = @receiptId;",
            new { receiptId = receipt.ReceiptId },
            transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false);

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
                    description = item.Description,
                    quantity = item.Quantity,
                    unitPrice = item.UnitPrice,
                    lineTotal = item.LineTotal
                },
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
        }

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
}
