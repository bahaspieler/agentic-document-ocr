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


            }