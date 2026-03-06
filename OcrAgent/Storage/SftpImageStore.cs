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

    }