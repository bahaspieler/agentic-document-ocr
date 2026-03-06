using System.Security;
using System.Text.Json;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace OcrAgent.Storage;

public sealed class SftpImageStore : IImageStore, IDisposable
{
    private readonly SftpStorageConfig _config;
    private readonly SftpConnectionPool _connectionPool;
    private readonly string[] _supportedExtensions = { "png", "jpg", "jpeg", "tif", "tiff", "bmp" };
    private bool _disposed = false;

    public SftpImageStore(SftpStorageConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _config.Validate();

        _connectionPool = new SftpConnectionPool(config);
    }

    public bool Exists(string reference)
    {
        return ExistsAsync(reference).GetAwaiter().GetResult();
    }

    public async Task<bool> ExistsAsync(string reference)
    {
        ValidateReference(reference);
        reference = ImageReferenceGenerator.Normalize(reference);

        try
        {
            return await ExecuteWithRetryAsync(async client =>
            {
                foreach (var ext in _supportedExtensions)
                {
                    var remotePath = GetRemotePath(reference, ext);
                    if (await Task.Run(() => client.Exists(remotePath)))
                    {
                        return true;
                    }
                }
                return false;
            });
        }
        catch (Exception)
        {
            throw;
        }
    }

    public string SaveFromFile(string filePath, string? desiredReference = null)
    {
        return SaveFromFileAsync(filePath, desiredReference).GetAwaiter().GetResult();
    }

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

    public string SaveBytes(byte[] bytes, string extension, IDictionary<string, string>? metadata = null,
        string? desiredReference = null)
    {
        return SaveBytesAsync(bytes, extension, metadata, desiredReference).GetAwaiter().GetResult();
    }

    public async Task<string> SaveBytesAsync(byte[] bytes, string extension,
        IDictionary<string, string>? metadata = null, string? desiredReference = null)
    {
        if (bytes == null || bytes.Length == 0)
            throw new ArgumentException("Bytes cannot be null or empty", nameof(bytes));

        var reference = desiredReference ?? ImageReferenceGenerator.NewRef();
        reference = ImageReferenceGenerator.Normalize(reference);
        ValidateReference(reference);

        try
        {
            await ExecuteWithRetryAsync(async client =>
            {
                var remotePath = GetRemotePath(reference, extension);

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

                await SaveMetadataAsync(client, reference, meta);

                return true;
            });

            return reference;
        }
        catch (Exception)
        {
            throw;
        }
    }

    public string GetImagePath(string reference)
    {
        return GetImagePathAsync(reference).GetAwaiter().GetResult();
    }

    public async Task<string> GetImagePathAsync(string reference)
    {
        ValidateReference(reference);
        reference = ImageReferenceGenerator.Normalize(reference);

        try
        {
            return await ExecuteWithRetryAsync(async client =>
            {
                string? remotePath = null;
                string? foundExtension = null;

                foreach (var ext in _supportedExtensions)
                {
                    var path = GetRemotePath(reference, ext);
                    if (await Task.Run(() => client.Exists(path)))
                    {
                        remotePath = path;
                        foundExtension = ext;
                        break;
                    }
                }

                if (remotePath == null)
                    throw new FileNotFoundException($"No stored image found for reference: {reference}");

                var tempPath = Path.Combine(Path.GetTempPath(), $"{reference}.{foundExtension}");

                using var fileStream = File.Create(tempPath);
                await Task.Run(() => client.DownloadFile(remotePath, fileStream));

                return tempPath;
            });
        }
        catch (Exception)
        {
            throw;
        }
    }

    public IDictionary<string, string> GetMetadata(string reference)
    {
        return GetMetadataAsync(reference).GetAwaiter().GetResult();
    }

    public async Task<IDictionary<string, string>> GetMetadataAsync(string reference)
    {
        ValidateReference(reference);
        reference = ImageReferenceGenerator.Normalize(reference);

        try
        {
            return await ExecuteWithRetryAsync(async client =>
            {
                var metaPath = GetMetadataPath(reference);

                if (!await Task.Run(() => client.Exists(metaPath)))
                    return new Dictionary<string, string>();

                using var stream = new MemoryStream();
                await Task.Run(() => client.DownloadFile(metaPath, stream));
                stream.Position = 0;

                using var reader = new StreamReader(stream);
                var json = await reader.ReadToEndAsync();

                return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                    ?? new Dictionary<string, string>();
            });
        }
        catch (Exception)
        {
            return new Dictionary<string, string>();
        }
    }

    public async Task SaveDebugImageAsync(string parentReference, string debugFolderName,
        string filename, byte[] imageBytes)
    {
        if (!_config.Behavior.EnableDebugFolders)
            return;

        ValidateReference(parentReference);

        try
        {
            await ExecuteWithRetryAsync(async client =>
            {
                var debugPath = $"{NormalizeBasePath()}/{debugFolderName}/{parentReference}/{filename}";
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

    private async Task<T> ExecuteWithRetryAsync<T>(Func<SftpClient, Task<T>> operation)
    {
        int attempt = 0;
        Exception? lastException = null;

        while (attempt <= _config.Retry.MaxRetries)
        {
            try
            {
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

    private async Task SaveMetadataAsync(SftpClient client, string reference, IDictionary<string, string> metadata)
    {
        var metaPath = GetMetadataPath(reference);
        var json = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);

        using var stream = new MemoryStream(bytes);
        await Task.Run(() => client.UploadFile(stream, metaPath, true));
    }

    private string NormalizeBasePath()
    {
        var basePath = _config.RemoteBasePath?.Trim().TrimEnd('/').Replace("\\", "/");
        if (string.IsNullOrEmpty(basePath)) basePath = "/";
        if (!basePath.StartsWith('/')) basePath = "/" + basePath;
        return basePath;
    }

    private string GetRemotePath(string reference, string extension)
    {
        return $"{NormalizeBasePath()}/{reference}.{extension}";
    }

    private string GetMetadataPath(string reference)
    {
        return $"{NormalizeBasePath()}/{reference}.json";
    }

    private void ValidateReference(string reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
            throw new ArgumentException("Reference cannot be null or whitespace", nameof(reference));

        if (reference.Contains("..") || reference.Contains("/") || reference.Contains("\\"))
            throw new SecurityException($"Invalid reference: potential directory traversal - {reference}");
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        _connectionPool?.Dispose();
    }
}
