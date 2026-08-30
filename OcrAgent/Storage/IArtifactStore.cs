namespace OcrAgent.Storage;

/// <summary>Lists and opens non-image and image artifacts within one recorded run scope.</summary>
public interface IArtifactStore
{
    Task<IReadOnlyList<StoredArtifact>> ListAsync(
        string scope, CancellationToken cancellationToken = default);
    Task<Stream> OpenReadAsync(
        string scope, string name, CancellationToken cancellationToken = default);
}
