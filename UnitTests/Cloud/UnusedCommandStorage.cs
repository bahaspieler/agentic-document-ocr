using OcrAgent.Worker.Contracts;
using OcrAgent.Worker.Azure;

namespace UnitTests.Cloud;

/// <summary>Storage test double that fails if an empty command reaches persistence.</summary>
internal sealed class UnusedCommandStorage : IStorageProvider, IQueueCommandStorage
{
    /// <inheritdoc />
    public Task<ResolvedQueueImage> ResolveImageAsync(
        string command,
        string? exactBlobName = null) =>
        throw new AssertFailedException(
            "An empty command must fail before image resolution.");

    /// <inheritdoc />
    public Task<(string DatasetName, IReadOnlyList<QueueImage> Images)>
        ListDatasetImagesAsync(string command, int maximumImages) =>
        throw new AssertFailedException(
            "An empty command must fail before dataset resolution.");

    /// <inheritdoc />
    public Task EnqueueBulkItemsAsync(IEnumerable<BulkQueueMessage> messages) =>
        throw new NotSupportedException();

    /// <inheritdoc />
    public Task<ExperimentResult?> GetResultAsync(string rowKey) =>
        throw new NotSupportedException();

    /// <inheritdoc />
    public Task SaveBatchPlanAsync(ExperimentResult result) =>
        throw new NotSupportedException();

    /// <inheritdoc />
    public Task MoveToPoisonAsync(
        ExperimentRequestMessage request,
        string error) =>
        throw new NotSupportedException();

    /// <inheritdoc />
    public IExperimentRequest ReceiveExperimentRequestAsync(CancellationToken token) =>
        throw new NotSupportedException();

    /// <inheritdoc />
    public Task<string> DownloadInputAsync(string fileName) =>
        throw new NotSupportedException();

    /// <inheritdoc />
    public Task UploadResultAsync(string experimentName, IExperimentResult result) =>
        throw new NotSupportedException();

    /// <inheritdoc />
    public Task CommitRequestAsync(IExperimentRequest request) =>
        throw new NotSupportedException();
}
