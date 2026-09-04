using OcrAgent.Agent;
using OcrAgent.Runs;
using OcrAgent.Storage;

namespace OcrAgent.Execution;

/// <summary>Discovers files produced by a run independently of its delivery transport.</summary>
public sealed class ArtifactDiscoveryService
{
    private readonly IImageStore _store;

    /// <summary>Creates an artifact discovery service for an image store.</summary>
    /// <param name="store">The store used to locate run outputs.</param>
    public ArtifactDiscoveryService(IImageStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <summary>
    /// Lists a run output directory, falling back to a standalone tool output reference.
    /// </summary>
    public async Task<IReadOnlyList<AgentArtifact>> DiscoverAsync(
        RunRecord run,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(run);

        if (_store is IArtifactStore artifactStore &&
            !string.IsNullOrWhiteSpace(run.OutputDirectory))
        {
            var rows = await artifactStore.ListAsync(
                run.OutputDirectory, cancellationToken).ConfigureAwait(false);
            if (rows.Count > 0)
            {
                return rows.Select(item => new AgentArtifact(
                    run.RunId,
                    item.Name,
                    item.SizeBytes,
                    item.LastModifiedUtc,
                    ContentTypeFor(item.Name),
                    IsPreviewable(item.Name))).ToArray();
            }
        }

        if (string.IsNullOrWhiteSpace(run.OutputReference) ||
            !_store.Exists(run.OutputReference))
            return [];

        var path = _store.GetImagePath(run.OutputReference);
        var file = new FileInfo(path);
        return
        [
            new AgentArtifact(
                run.RunId,
                file.Name,
                file.Exists ? file.Length : 0,
                file.Exists ? file.LastWriteTimeUtc : null,
                ContentTypeFor(path),
                IsPreviewable(path))
        ];
    }

    /// <summary>Determines the MIME content type for an artifact path.</summary>
    /// <param name="path">The artifact path or file name.</param>
    /// <returns>The recognized MIME type, or a binary fallback.</returns>
    public static string ContentTypeFor(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            ".tif" or ".tiff" => "image/tiff",
            ".json" => "application/json",
            ".csv" => "text/csv",
            ".txt" or ".log" => "text/plain",
            _ => "application/octet-stream"
        };

    /// <summary>Determines whether an artifact can be previewed by clients.</summary>
    /// <param name="path">The artifact path or file name.</param>
    /// <returns>True for supported image, JSON, CSV, and text artifacts.</returns>
    public static bool IsPreviewable(string path)
    {
        var contentType = ContentTypeFor(path);
        return contentType.StartsWith("image/", StringComparison.Ordinal) ||
               contentType is "application/json" or "text/csv" or "text/plain";
    }
}
