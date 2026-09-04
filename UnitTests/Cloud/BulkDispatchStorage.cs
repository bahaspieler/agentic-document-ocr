using MyCloudProject.Common;
using MyExperiment;

namespace UnitTests.Cloud;

/// <summary>In-memory storage test double for deterministic bulk dispatch tests.</summary>
internal sealed class BulkDispatchStorage : IStorageProvider, IQueueCommandStorage
{
    private ExperimentResult? _saved;

    /// <summary>Gets child messages submitted by the experiment.</summary>
    public List<BulkQueueMessage> Messages { get; } = [];

    /// <summary>Gets whether a plan existed before each enqueue call.</summary>
    public bool PlanWasSavedBeforeEveryEnqueue { get; private set; } = true;

    /// <inheritdoc />
    public Task<(string DatasetName, IReadOnlyList<QueueImage> Images)>
        ListDatasetImagesAsync(string command, int maximumImages) =>
        Task.FromResult<(string, IReadOnlyList<QueueImage>)>((
            "cord-smoke",
            [
                new QueueImage("images/cord-smoke/train-001.png", "train-001.png"),
                new QueueImage("images/cord-smoke/test-001.png", "test-001.png")
            ]));

    /// <inheritdoc />
    public Task EnqueueBulkItemsAsync(IEnumerable<BulkQueueMessage> messages)
    {
        PlanWasSavedBeforeEveryEnqueue &= _saved is not null;
        Messages.AddRange(messages);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<ExperimentResult?> GetResultAsync(string rowKey) =>
        Task.FromResult(_saved?.RowKey == rowKey ? _saved : null);

    /// <inheritdoc />
    public Task SaveBatchPlanAsync(ExperimentResult result)
    {
        _saved = result;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<ResolvedQueueImage> ResolveImageAsync(
        string command,
        string? exactBlobName = null) =>
        throw new NotSupportedException();

    /// <inheritdoc />
    public Task MoveToPoisonAsync(
        ExerimentRequestMessage request,
        string error) =>
        throw new NotSupportedException();

    /// <inheritdoc />
    public IExerimentRequest ReceiveExperimentRequestAsync(CancellationToken token) =>
        throw new NotSupportedException();

    /// <inheritdoc />
    public Task<string> DownloadInputAsync(string fileName) =>
        throw new NotSupportedException();

    /// <inheritdoc />
    public Task UploadResultAsync(string experimentName, IExperimentResult result) =>
        throw new NotSupportedException();

    /// <inheritdoc />
    public Task CommitRequestAsync(IExerimentRequest request) =>
        throw new NotSupportedException();
}
