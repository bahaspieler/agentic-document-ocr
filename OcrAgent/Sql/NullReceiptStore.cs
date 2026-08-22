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

    public Task<IReadOnlyList<ReceiptRecord>> QueryAsync(
        string? merchant = null, DateTime? fromUtc = null, DateTime? toUtc = null,
        int top = 100, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ReceiptRecord>>([]);
}
