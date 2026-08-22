using OcrAgent.Extraction.Models;

namespace OcrAgent.Sql;

public sealed class NullReceiptStore : IReceiptStore
{
    public static NullReceiptStore Instance { get; } = new();
    public bool IsEnabled => false;
    private NullReceiptStore() { }

    public Task SaveAsync(
        ReceiptData receipt, string? runId, string? conversationId,
        CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<ReceiptRecord?> GetAsync(string receiptId, CancellationToken cancellationToken = default) =>
        Task.FromResult<ReceiptRecord?>(null);

    public Task<ReceiptRecord> UpdateAsync(
        string receiptId, ReceiptData corrected, byte[] expectedRowVersion,
        string editorIdentity, string? changeSummary = null,
        bool verified = false,
        CancellationToken cancellationToken = default) =>
        Task.FromException<ReceiptRecord>(new InvalidOperationException("Azure SQL is not configured."));

    public Task<IReadOnlyList<ReceiptRevisionRecord>> ListRevisionsAsync(
        string receiptId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ReceiptRevisionRecord>>([]);

    public Task<IReadOnlyList<ReceiptRecord>> QueryAsync(
        string? merchant = null, DateTime? fromUtc = null, DateTime? toUtc = null,
        int top = 100, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ReceiptRecord>>([]);
}
