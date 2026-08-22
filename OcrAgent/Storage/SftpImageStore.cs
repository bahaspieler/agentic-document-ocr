using System.Text.Json;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace OcrAgent.Storage;

/// <summary>
/// <see cref="IImageStore"/> backed by remote files over SFTP, with retries and optional debug uploads.
/// </summary>
/// <remarks>
/// The active subdirectory is held in an <see cref="AsyncLocal{T}"/> so concurrent runs sharing one
/// store instance cannot overwrite each other's write scope, matching <see cref="AzureBlobImageStore"/>.
/// <para>
/// Writes return a reference qualified with the scope they landed in (see
/// <see cref="ImageReferenceGenerator"/>), and reads take the scope back out of it, so an image is
/// reachable by the reference alone rather than only while the scope that produced it is still active.
/// </para>
/// </remarks>
public sealed class SftpImageStore : IImageStore, IArtifactStore, IDisposable
{
    private readonly SftpStorageConfig _config;
    private readonly SftpConnectionPool _connectionPool;
    private readonly string[] _supportedExtensions = { "png", "jpg", "jpeg", "tif", "tiff", "bmp" };
    private readonly AsyncLocal<string?> _activeSubdirectory = new();
    private bool _disposed = false;

    /// <summary>Builds the store and opens the shared connection pool from config.</summary>
    /// <param name="config">Host, paths, pool, and retry settings.</param>
    public SftpImageStore(SftpStorageConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _config.Validate();

        _connectionPool = new SftpConnectionPool(config);
    }

    #region IImageStore Implementation

    /// <inheritdoc />
    public bool Exists(string reference)
    {
        return ExistsAsync(reference).GetAwaiter().GetResult();
    }

    /// <summary>Async variant of <see cref="Exists"/>.</summary>
    /// <param name="reference">Stored image id.</param>
    /// <returns>True if a remote image file exists.</returns>
    public async Task<bool> ExistsAsync(string reference)
    {
        var (explicitScope, leaf) = ImageReferenceGenerator.Split(reference);

        return await ExecuteWithRetryAsync(async client =>
        {
            foreach (var scope in CandidateScopes(explicitScope))
            {
                foreach (var ext in _supportedExtensions)
                {
                    if (await Task.Run(() => client.Exists(RemotePath(scope, $"{leaf}.{ext}"))))
                        return true;
                }
            }

            return false;
        });
    }

    /// <inheritdoc />
    public string SaveFromFile(string filePath, string? desiredReference = null)
    {
        return SaveFromFileAsync(filePath, desiredReference).GetAwaiter().GetResult();
    }

    /// <summary>Async variant of <see cref="SaveFromFile"/>.</summary>
    /// <param name="filePath">Path to the source file.</param>
    /// <param name="desiredReference">Optional id; a new one is generated if null.</param>
    /// <returns>The stored reference string.</returns>
    public async Task<string> SaveFromFileAsync(string filePath, string? desiredReference = null)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"File not found: {filePath}");

        var ext = Path.GetExtension(filePath).TrimStart('.').ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(ext)) ext = "png";

        var bytes = await File.ReadAllBytesAsync(filePath);
        return await SaveBytesAsync(bytes, ext, new Dictionary<string, string>
        {
            ["source"] = "file",
            ["originalPath"] = filePath
        }, desiredReference);
    }

    /// <inheritdoc />
    public string SaveBytes(byte[] bytes, string extension, IDictionary<string, string>? metadata = null,
        string? desiredReference = null)
    {
        return SaveBytesAsync(bytes, extension, metadata, desiredReference).GetAwaiter().GetResult();
    }

    /// <summary>Async variant of <see cref="SaveBytes"/>.</summary>
    /// <param name="bytes">Image file contents.</param>
    /// <param name="extension">File extension without dot.</param>
    /// <param name="metadata">Optional sidecar fields.</param>
    /// <param name="desiredReference">Optional id; a new one is generated if null.</param>
    /// <returns>The stored reference string.</returns>
    public async Task<string> SaveBytesAsync(byte[] bytes, string extension,
        IDictionary<string, string>? metadata = null, string? desiredReference = null)
    {
        if (bytes == null || bytes.Length == 0)
            throw new ArgumentException("Bytes cannot be null or empty", nameof(bytes));

        var leaf = LeafOf(desiredReference);
        var scope = _activeSubdirectory.Value;
        var reference = ImageReferenceGenerator.Qualify(scope, leaf);

        await ExecuteWithRetryAsync(async client =>
        {
            var remotePath = RemotePath(scope, $"{leaf}.{extension}");

            if (await Task.Run(() => client.Exists(remotePath)))
                throw new InvalidOperationException($"Reference already exists: {reference}");

            EnsureRemoteDirectoryExists(client, Path.GetDirectoryName(remotePath)!.Replace("\\", "/"));

            using var stream = new MemoryStream(bytes);
            await Task.Run(() => client.UploadFile(stream, remotePath, true));

            var meta = metadata ?? new Dictionary<string, string>();
            meta["reference"] = reference;
            meta["extension"] = extension;
            meta["createdUtc"] = DateTime.UtcNow.ToString("O");
            meta["sizeBytes"] = bytes.Length.ToString();
            if (scope != null)
                meta["workflowDir"] = scope;

            await SaveMetadataAsync(client, RemotePath(scope, $"{leaf}.json"), meta);

            return true;
        });

        return reference;
    }

    /// <inheritdoc />
    public string GetImagePath(string reference)
    {
        return GetImagePathAsync(reference).GetAwaiter().GetResult();
    }

    /// <summary>Downloads the image to a temp file and returns that local path.</summary>
    /// <param name="reference">Stored image id.</param>
    /// <returns>Path under the system temp directory.</returns>
    public async Task<string> GetImagePathAsync(string reference)
    {
        var (explicitScope, leaf) = ImageReferenceGenerator.Split(reference);

        return await ExecuteWithRetryAsync(async client =>
        {
            string? remotePath = null;
            string? foundExtension = null;
            string? foundScope = null;

            foreach (var scope in CandidateScopes(explicitScope))
            {
                foreach (var ext in _supportedExtensions)
                {
                    var path = RemotePath(scope, $"{leaf}.{ext}");
                    if (await Task.Run(() => client.Exists(path)))
                    {
                        remotePath = path;
                        foundExtension = ext;
                        foundScope = scope;
                        break;
                    }
                }

                if (remotePath != null)
                    break;
            }

            if (remotePath == null)
                throw new FileNotFoundException($"No stored image found for reference: {reference}");

            // Flattened because the temp directory has no scope subdirectories, and scope-qualified
            // because two runs can produce the same leaf name under different scopes.
            var tempName = ImageReferenceGenerator.Qualify(foundScope, leaf)
                .Replace(ImageReferenceGenerator.ScopeSeparator, '_');
            var tempPath = Path.Combine(Path.GetTempPath(), $"{tempName}.{foundExtension}");

            // The temp name comes from the reference, so two runs given the same input land on
            // the same file. Truncating it in place would hand the other run an empty or partial
            // image, which decodes to nothing rather than failing. Download privately, then
            // replace in one step.
            var partialPath = $"{tempPath}.{Guid.NewGuid():N}.partial";
            try
            {
                using (var fileStream = File.Create(partialPath))
                    await Task.Run(() => client.DownloadFile(remotePath, fileStream));

                File.Move(partialPath, tempPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(partialPath))
                    File.Delete(partialPath);
            }

            return tempPath;
        });
    }

    /// <inheritdoc />
    public IDictionary<string, string> GetMetadata(string reference)
    {
        return GetMetadataAsync(reference).GetAwaiter().GetResult();
    }

    /// <summary>Loads remote JSON metadata; on failure returns an empty dictionary.</summary>
    /// <param name="reference">Stored image id.</param>
    /// <returns>Deserialized metadata or empty map.</returns>
    public async Task<IDictionary<string, string>> GetMetadataAsync(string reference)
    {
        try
        {
            var (explicitScope, leaf) = ImageReferenceGenerator.Split(reference);

            return await ExecuteWithRetryAsync(async client =>
            {
                foreach (var scope in CandidateScopes(explicitScope))
                {
                    var metaPath = RemotePath(scope, $"{leaf}.json");
                    if (!await Task.Run(() => client.Exists(metaPath)))
                        continue;

                    using var stream = new MemoryStream();
                    await Task.Run(() => client.DownloadFile(metaPath, stream));
                    stream.Position = 0;

                    using var reader = new StreamReader(stream);
                    var json = await reader.ReadToEndAsync();

                    return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                        ?? new Dictionary<string, string>();
                }

                return new Dictionary<string, string>();
            });
        }
        catch (Exception)
        {
            return new Dictionary<string, string>();
        }
    }

    #endregion

    #region Debug / Diagnostics

    /// <summary>Uploads a debug artifact under the configured debug folder when enabled.</summary>
    /// <param name="parentReference">Reference grouping debug files.</param>
    /// <param name="debugFolderName">Top-level debug directory name on the server.</param>
    /// <param name="filename">Remote file name.</param>
    /// <param name="imageBytes">File contents to upload.</param>
    public async Task SaveDebugImageAsync(string parentReference, string debugFolderName,
        string filename, byte[] imageBytes)
    {
        if (!_config.Behavior.EnableDebugFolders)
            return;

        var parentLeaf = ImageReferenceGenerator.Split(parentReference).Leaf;

        try
        {
            await ExecuteWithRetryAsync(async client =>
            {
                var debugPath = $"{NormalizeBasePath()}/{debugFolderName}/{parentLeaf}/{filename}";
                var debugDir = Path.GetDirectoryName(debugPath)!.Replace("\\", "/");

                EnsureRemoteDirectoryExists(client, debugDir);

                using var stream = new MemoryStream(imageBytes);
                await Task.Run(() => client.UploadFile(stream, debugPath, true));

                return true;
            });
        }
        catch (Exception)
        {
            // Debug image save failures are non-critical
        }
    }

    #endregion

    #region SFTP Operations

    private async Task<T> ExecuteWithRetryAsync<T>(Func<SftpClient, Task<T>> operation)
    {
        int attempt = 0;
        Exception? lastException = null;

        while (attempt <= _config.Retry.MaxRetries)
        {
            try
            {
                // Runs the delegate with a pooled SFTP client (borrow on enter, return in finally).
                return await _connectionPool.ExecuteAsync(operation);
            }
            catch (Exception ex) when (IsRetryableException(ex) && attempt < _config.Retry.MaxRetries)
            {
                lastException = ex;
                attempt++;

                var delay = CalculateBackoffDelay(attempt);
                await Task.Delay(delay);
            }
        }

        throw lastException ?? new InvalidOperationException("Operation failed with no exception captured");
    }

    private bool IsRetryableException(Exception ex)
    {
        var exceptionTypeName = ex.GetType().Name;

        if (_config.Retry.RetryableExceptions.Contains(exceptionTypeName))
            return true;

        return ex is SshConnectionException or SshOperationTimeoutException or SftpPathNotFoundException;
    }

    private int CalculateBackoffDelay(int attempt)
    {
        var delay = (int)(_config.Retry.InitialDelayMs * Math.Pow(_config.Retry.BackoffMultiplier, attempt - 1));
        return Math.Min(delay, _config.Retry.MaxDelayMs);
    }

    private void EnsureRemoteDirectoryExists(SftpClient client, string remotePath)
    {
        if (string.IsNullOrWhiteSpace(remotePath)) return;

        remotePath = remotePath.Replace("\\", "/").TrimEnd('/');
        if (remotePath == "" || remotePath == "/") return;

        if (client.Exists(remotePath)) return;

        var parts = remotePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var currentPath = "";

        foreach (var part in parts)
        {
            currentPath += "/" + part;
            try
            {
                if (!client.Exists(currentPath))
                {
                    client.CreateDirectory(currentPath);
                }
            }
            catch (SftpPermissionDeniedException)
            {
                if (!client.Exists(currentPath))
                    throw;
            }
        }
    }

    #endregion

    #region Metadata

    private async Task SaveMetadataAsync(SftpClient client, string metaPath, IDictionary<string, string> metadata)
    {
        var json = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);

        using var stream = new MemoryStream(bytes);
        await Task.Run(() => client.UploadFile(stream, metaPath, true));
    }

    #endregion

    #region IImageStore Implementation

    /// <inheritdoc />
    public void SetActiveSubdirectory(string? subdirectory)
    {
        _activeSubdirectory.Value = subdirectory;
    }

    /// <inheritdoc />
    public string? GetActiveSubdirectory() => _activeSubdirectory.Value;

    /// <inheritdoc />
    public string GetWriteDirectory() => ScopeBasePath(_activeSubdirectory.Value);

    public Task<IReadOnlyList<StoredArtifact>> ListAsync(
        string scope, CancellationToken cancellationToken = default)
    {
        var normalizedScope = ArtifactPath.Scope(scope);
        return ExecuteWithRetryAsync<IReadOnlyList<StoredArtifact>>(async client =>
        {
            var directory = ScopeBasePath(normalizedScope);
            if (!await Task.Run(() => client.Exists(directory), cancellationToken))
                return [];
            return (await Task.Run(() => client.ListDirectory(directory), cancellationToken))
                .Where(item => !item.IsDirectory && item.Name is not "." and not "..")
                .Select(item => new StoredArtifact(
                    item.Name, item.Attributes.Size, item.Attributes.LastWriteTimeUtc))
                .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        });
    }

    public Task<Stream> OpenReadAsync(
        string scope, string name, CancellationToken cancellationToken = default)
    {
        var normalizedScope = ArtifactPath.Scope(scope);
        var normalizedName = ArtifactPath.Name(name);
        return ExecuteWithRetryAsync<Stream>(async client =>
        {
            var path = RemotePath(normalizedScope, normalizedName);
            if (!await Task.Run(() => client.Exists(path), cancellationToken))
                throw new FileNotFoundException("Artifact not found.", normalizedName);
            var stream = new MemoryStream();
            await Task.Run(() => client.DownloadFile(path, stream), cancellationToken);
            stream.Position = 0;
            return stream;
        });
    }

    #endregion

    #region SFTP Operations

    private string NormalizeBasePath()
    {
        var basePath = _config.RemoteBasePath?.Trim().TrimEnd('/').Replace("\\", "/");
        if (string.IsNullOrEmpty(basePath)) basePath = "/";
        if (!basePath.StartsWith('/')) basePath = "/" + basePath;
        return basePath;
    }

    private string ScopeBasePath(string? scope)
    {
        var basePath = NormalizeBasePath();
        if (string.IsNullOrWhiteSpace(scope)) return basePath;

        return $"{basePath}/{scope.Replace('\\', '/').Trim('/')}";
    }

    private string RemotePath(string? scope, string fileName) => $"{ScopeBasePath(scope)}/{fileName}";

    /// <summary>
    /// Where to look for a reference: the scope it names, or the active scope then the root.
    /// </summary>
    /// <remarks>
    /// A reference that names its own scope needs no search, which is what keeps an image produced by
    /// a workflow reachable once that run has released the scope it was written under.
    /// </remarks>
    private IEnumerable<string?> CandidateScopes(string? explicitScope)
    {
        if (explicitScope != null)
        {
            yield return explicitScope;
            yield break;
        }

        var scope = _activeSubdirectory.Value;
        yield return scope;

        if (scope != null)
            yield return null;
    }

    /// <summary>
    /// The file name a write should use, which is the requested reference stripped of any scope.
    /// </summary>
    /// <remarks>
    /// Plugins derive an output name by appending to the reference they were given, and that reference
    /// is itself scope-qualified whenever it came from an earlier step. Stripping here rather than in
    /// twenty plugins is what stops the scope being prefixed twice.
    /// <para>
    /// Traversal is not a concern the way it was when this store rejected separators outright:
    /// <see cref="ImageReferenceGenerator.Normalize"/> allows no <c>.</c> in a segment, so no
    /// reference can name a parent directory.
    /// </para>
    /// </remarks>
    private static string LeafOf(string? desiredReference) =>
        desiredReference == null
            ? ImageReferenceGenerator.NewRef()
            : ImageReferenceGenerator.Split(desiredReference).Leaf;

    #endregion

    #region IImageStore Implementation

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        _connectionPool?.Dispose();
    }

    #endregion
}
