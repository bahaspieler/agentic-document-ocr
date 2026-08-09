using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Azure.Storage.Blobs;

namespace OcrAgent.Storage;

/// <summary>
/// <see cref="IImageStore"/> backed by an Azure Blob Storage container.
/// </summary>
/// <remarks>
/// Plugins open images from the filesystem, so this store is cache backed rather than purely remote:
/// reads materialise the blob into a local staging directory and return that path, and writes go to
/// the staging directory and through to the container. <see cref="SftpImageStore"/> uses the same
/// approach for the same reason.
/// <para>
/// The active subdirectory is held in an <see cref="AsyncLocal{T}"/> so concurrent runs sharing one
/// store instance cannot overwrite each other's write scope.
/// </para>
/// </remarks>
public sealed class AzureBlobImageStore : IImageStore, IDisposable
{
    private static readonly string[] SupportedExtensions = { "png", "jpg", "jpeg", "tif", "tiff", "bmp" };
    private static readonly JsonSerializerOptions MetadataJsonOptions = new() { WriteIndented = true };

    private readonly BlobContainerClient _container;
    private readonly string _cacheRoot;
    private readonly string _prefix;

    // Blob names this store uploaded itself. Flush uses it to find files that other code
    // (the receipt exporters) wrote straight into the write directory.
    private readonly ConcurrentDictionary<string, byte> _uploaded = new(StringComparer.Ordinal);

    private readonly AsyncLocal<string?> _activeSubdirectory = new();
    private bool _disposed;

    /// <summary>Connects to the container and prepares the local cache directory.</summary>
    /// <param name="config">Connection string, container name, prefix, and cache location.</param>
    public AzureBlobImageStore(AzureBlobStorageConfig config)
    {
        if (config == null) throw new ArgumentNullException(nameof(config));
        config.Validate();

        _container = new BlobContainerClient(config.ConnectionString, config.ContainerName);
        if (config.CreateContainerIfNotExists)
            _container.CreateIfNotExists();

        _prefix = (config.BlobPrefix ?? "").Replace('\\', '/').Trim('/');
        _cacheRoot = Path.GetFullPath(config.CacheRootPath);
        Directory.CreateDirectory(_cacheRoot);
    }

    /// <summary>Container this store reads from and writes to.</summary>
    public string ContainerName => _container.Name;

    /// <inheritdoc />
    public bool Exists(string reference)
    {
        reference = Normalize(reference);
        return TryResolve(reference, out _, out _);
    }

    /// <inheritdoc />
    public string SaveFromFile(string filePath, string? desiredReference = null)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"File not found: {filePath}");

        var extension = Path.GetExtension(filePath).TrimStart('.').ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(extension)) extension = "png";

        var bytes = File.ReadAllBytes(filePath);
        return SaveBytes(bytes, extension, new Dictionary<string, string>
        {
            ["source"] = "file",
            ["originalPath"] = filePath
        }, desiredReference);
    }

    /// <inheritdoc />
    public string SaveBytes(byte[] bytes, string extension, IDictionary<string, string>? metadata = null,
        string? desiredReference = null)
    {
        if (bytes == null || bytes.Length == 0)
            throw new ArgumentException("Bytes cannot be null or empty", nameof(bytes));

        var reference = Normalize(desiredReference ?? ImageReferenceGenerator.NewRef());
        var scope = _activeSubdirectory.Value;

        var imageBlobName = BlobName(scope, $"{reference}.{extension}");
        if (_container.GetBlobClient(imageBlobName).Exists())
            throw new InvalidOperationException($"Reference already exists: {reference}");

        var stagingDirectory = EnsureStagingDirectory(scope);
        var localImagePath = Path.Combine(stagingDirectory, $"{reference}.{extension}");
        File.WriteAllBytes(localImagePath, bytes);
        Upload(imageBlobName, localImagePath);

        var meta = metadata ?? new Dictionary<string, string>();
        meta["reference"] = reference;
        meta["extension"] = extension;
        meta["createdUtc"] = DateTime.UtcNow.ToString("O");
        meta["sizeBytes"] = bytes.Length.ToString();
        if (scope != null)
            meta["workflowDir"] = scope;

        var localMetaPath = Path.Combine(stagingDirectory, $"{reference}.json");
        File.WriteAllText(localMetaPath, JsonSerializer.Serialize(meta, MetadataJsonOptions));
        Upload(BlobName(scope, $"{reference}.json"), localMetaPath);

        return reference;
    }

    /// <inheritdoc />
    /// <remarks>Downloads the blob into the local cache when it is not there yet.</remarks>
    public string GetImagePath(string reference)
    {
        reference = Normalize(reference);

        if (!TryResolve(reference, out var scope, out var extension))
            throw new FileNotFoundException($"No stored image found for reference: {reference}");

        var localPath = Path.Combine(EnsureStagingDirectory(scope), $"{reference}.{extension}");
        if (File.Exists(localPath))
            return localPath;

        _container.GetBlobClient(BlobName(scope, $"{reference}.{extension}")).DownloadTo(localPath);
        return localPath;
    }

    /// <inheritdoc />
    public IDictionary<string, string> GetMetadata(string reference)
    {
        try
        {
            reference = Normalize(reference);

            foreach (var scope in CandidateScopes())
            {
                var localPath = Path.Combine(StagingDirectory(scope), $"{reference}.json");
                if (File.Exists(localPath))
                    return Deserialize(File.ReadAllText(localPath));

                var blob = _container.GetBlobClient(BlobName(scope, $"{reference}.json"));
                if (!blob.Exists()) continue;

                using var stream = new MemoryStream();
                blob.DownloadTo(stream);
                return Deserialize(Encoding.UTF8.GetString(stream.ToArray()));
            }
        }
        catch (Exception)
        {
            // Metadata is optional; callers treat an empty map as "no sidecar".
        }

        return new Dictionary<string, string>();
    }

    /// <inheritdoc />
    /// <remarks>
    /// Uploads anything left in the directory being left behind, which is how artifacts written
    /// directly to <see cref="GetWriteDirectory"/> reach the container.
    /// </remarks>
    public void SetActiveSubdirectory(string? subdirectory)
    {
        Flush(_activeSubdirectory.Value);

        _activeSubdirectory.Value = subdirectory;

        if (subdirectory != null)
            EnsureStagingDirectory(subdirectory);
    }

    /// <inheritdoc />
    public string? GetActiveSubdirectory() => _activeSubdirectory.Value;

    /// <inheritdoc />
    /// <remarks>
    /// Returns the local staging directory for the current scope. Files written here by other code
    /// are uploaded when the scope changes or the store is disposed.
    /// </remarks>
    public string GetWriteDirectory() => EnsureStagingDirectory(_activeSubdirectory.Value);

    /// <summary>Uploads files in a scope's staging directory that this store did not write itself.</summary>
    /// <param name="scope">Subdirectory to flush, or null for the cache root.</param>
    public void Flush(string? scope)
    {
        var directory = StagingDirectory(scope);
        if (!Directory.Exists(directory)) return;

        foreach (var file in Directory.GetFiles(directory, "*", SearchOption.TopDirectoryOnly))
        {
            var blobName = BlobName(scope, Path.GetFileName(file));
            if (_uploaded.ContainsKey(blobName)) continue;

            Upload(blobName, file);
        }
    }

    /// <summary>Flushes the current scope.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            Flush(_activeSubdirectory.Value);
        }
        catch (Exception)
        {
            // Disposal must not throw over a failed best-effort upload.
        }
    }

    /// <summary>Finds where a reference lives, preferring the active scope over the container root.</summary>
    private bool TryResolve(string reference, out string? scope, out string extension)
    {
        foreach (var candidate in CandidateScopes())
        {
            var stagingDirectory = StagingDirectory(candidate);

            foreach (var ext in SupportedExtensions)
            {
                if (File.Exists(Path.Combine(stagingDirectory, $"{reference}.{ext}")))
                {
                    scope = candidate;
                    extension = ext;
                    return true;
                }

                if (_container.GetBlobClient(BlobName(candidate, $"{reference}.{ext}")).Exists())
                {
                    scope = candidate;
                    extension = ext;
                    return true;
                }
            }
        }

        scope = null;
        extension = "";
        return false;
    }

    /// <summary>Active scope first, then the root as fallback, matching the other stores.</summary>
    private IEnumerable<string?> CandidateScopes()
    {
        var scope = _activeSubdirectory.Value;
        yield return scope;

        if (scope != null)
            yield return null;
    }

    private void Upload(string blobName, string localPath)
    {
        using var stream = File.OpenRead(localPath);
        _container.GetBlobClient(blobName).Upload(stream, overwrite: true);
        _uploaded[blobName] = 0;
    }

    private string BlobName(string? scope, string fileName)
    {
        var segments = new List<string>(3);
        if (_prefix.Length > 0) segments.Add(_prefix);
        if (!string.IsNullOrWhiteSpace(scope)) segments.Add(scope.Replace('\\', '/').Trim('/'));
        segments.Add(fileName);
        return string.Join('/', segments);
    }

    private string StagingDirectory(string? scope)
    {
        if (string.IsNullOrWhiteSpace(scope)) return _cacheRoot;

        var relative = scope.Replace('\\', '/').Trim('/').Replace('/', Path.DirectorySeparatorChar);
        return Path.Combine(_cacheRoot, relative);
    }

    private string EnsureStagingDirectory(string? scope)
    {
        var directory = StagingDirectory(scope);
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string Normalize(string reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
            throw new ArgumentException("Reference cannot be null or whitespace", nameof(reference));

        return ImageReferenceGenerator.Normalize(reference);
    }

    private static Dictionary<string, string> Deserialize(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
}
