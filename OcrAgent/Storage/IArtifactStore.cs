namespace OcrAgent.Storage;

/// <summary>Lists and opens non-image and image artifacts within one recorded run scope.</summary>
public interface IArtifactStore
{
    /// <summary>Lists the files stored directly within an artifact scope.</summary>
    /// <param name="scope">Relative artifact scope to inspect.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>Artifact metadata ordered by file name.</returns>
    Task<IReadOnlyList<StoredArtifact>> ListAsync(
        string scope, CancellationToken cancellationToken = default);

    /// <summary>Opens a stored artifact for reading.</summary>
    /// <param name="scope">Relative scope containing the artifact.</param>
    /// <param name="name">Artifact file name.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A readable stream positioned at the start of the artifact.</returns>
    Task<Stream> OpenReadAsync(
        string scope, string name, CancellationToken cancellationToken = default);
}
