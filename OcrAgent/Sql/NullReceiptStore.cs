using OcrAgent.Extraction.Models;

namespace OcrAgent.Sql;

/// <summary>Disabled receipt store used when Azure SQL is not configured.</summary>
public sealed class NullReceiptStore : IReceiptStore
{
    /// <summary>Gets the shared disabled receipt store.</summary>
    public static NullReceiptStore Instance { get; } = new();

    /// <summary>Gets a value indicating that durable receipt storage is unavailable.</summary>
    public bool IsEnabled => false;
    private NullReceiptStore() { }

    /// <summary>Ignores an extracted receipt because durable storage is disabled.</summary>
    public Task SaveAsync(
        ReceiptData receipt, string? runId, string? conversationId,
        CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <summary>Returns no receipt because durable storage is disabled.</summary>
    public Task<ReceiptRecord?> GetAsync(string receiptId, CancellationToken cancellationToken = default) =>
        Task.FromResult<ReceiptRecord?>(null);

    /// <summary>Rejects a receipt correction because durable storage is disabled.</summary>
    public Task<ReceiptRecord> UpdateAsync(
        string receiptId, ReceiptData corrected, byte[] expectedRowVersion,
        string editorIdentity, string? changeSummary = null,
        bool verified = false,
        CancellationToken cancellationToken = default) =>
        Task.FromException<ReceiptRecord>(new InvalidOperationException("Azure SQL is not configured."));

    /// <summary>Returns no receipt revisions because durable storage is disabled.</summary>
    public Task<IReadOnlyList<ReceiptRevisionRecord>> ListRevisionsAsync(
        string receiptId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ReceiptRevisionRecord>>([]);

    /// <summary>Returns no receipts because durable storage is disabled.</summary>
    public Task<IReadOnlyList<ReceiptRecord>> QueryAsync(
        string? merchant = null, DateTime? fromUtc = null, DateTime? toUtc = null,
        int top = 100, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ReceiptRecord>>([]);
}
