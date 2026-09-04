namespace MyExperiment;

/// <summary>Provides queue-specific Blob, Queue, and Table operations to the worker.</summary>
public interface IQueueCommandStorage
{
    /// <summary>Finds and downloads the image named by a command or exact child message.</summary>
    /// <param name="command">Queue command that names a training image.</param>
    /// <param name="exactBlobName">Exact Blob selected by a trusted internal item message.</param>
    /// <returns>The resolved reference, Blob name, and temporary local path.</returns>
    Task<ResolvedQueueImage> ResolveImageAsync(
        string command,
        string? exactBlobName = null);

    /// <summary>Lists supported images in the uniquely named dataset folder from a command.</summary>
    /// <param name="command">Bulk command containing a dataset name.</param>
    /// <param name="maximumImages">Maximum number of images accepted.</param>
    /// <returns>Dataset name and deterministic Blob list.</returns>
    Task<(string DatasetName, IReadOnlyList<QueueImage> Images)> ListDatasetImagesAsync(
        string command,
        int maximumImages);

    /// <summary>Enqueues one internal message for each selected batch image.</summary>
    /// <param name="messages">Deterministic child messages.</param>
    Task EnqueueBulkItemsAsync(IEnumerable<BulkQueueMessage> messages);

    /// <summary>Gets a previously persisted batch or item row.</summary>
    /// <param name="rowKey">Result row key.</param>
    /// <returns>The existing row, or null.</returns>
    Task<ExperimentResult?> GetResultAsync(string rowKey);

    /// <summary>Persists a batch plan before child messages are dispatched.</summary>
    /// <param name="result">Parent batch row.</param>
    Task SaveBatchPlanAsync(ExperimentResult result);

    /// <summary>Moves a repeatedly failing message to the configured poison queue.</summary>
    /// <param name="request">Received message metadata and content.</param>
    /// <param name="error">Terminal failure detail.</param>
    Task MoveToPoisonAsync(ExerimentRequestMessage request, string error);
}
