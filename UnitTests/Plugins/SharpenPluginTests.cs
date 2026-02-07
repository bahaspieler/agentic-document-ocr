using OcrAgent.Storage;
using System.Collections.Concurrent;

namespace OcrAgent.Tests.Fakes;

public sealed class FakeImageStore : IImageStore
{
    private readonly string _baseDir;
    private readonly ConcurrentDictionary<string, Dictionary<string, string>> _meta = new();

    public FakeImageStore(string baseDir)
    {
        _baseDir = baseDir;
        Directory.CreateDirectory(_baseDir);
    }

    public string GetImagePath(string reference)
        => Path.Combine(_baseDir, $"{reference}.png");

    public bool Exists(string reference)
        => File.Exists(GetImagePath(reference));

    public IDictionary<string, string> GetMetadata(string reference)
        => _meta.TryGetValue(reference, out var m)
            ? m
            : new Dictionary<string, string>();

    // Save raw bytes as a file and store metadata
    public string SaveBytes(
        byte[] bytes,
        string extension,
        IDictionary<string, string>? metadata = null,
        string? desiredReference = null)
    {
        var reference = desiredReference ?? Guid.NewGuid().ToString("N");
        var path = Path.Combine(_baseDir, $"{reference}.{extension}");

        File.WriteAllBytes(path, bytes);

        _meta[reference] = metadata != null
            ? new Dictionary<string, string>(metadata)
            : new Dictionary<string, string>();

        return reference;
    }

    // Save an existing file into the fake store
    public string SaveFromFile(string filePath, string? desiredReference = null)
    {
        var reference = desiredReference ?? Guid.NewGuid().ToString("N");
        var ext = Path.GetExtension(filePath).TrimStart('.');
        var destPath = Path.Combine(_baseDir, $"{reference}.{ext}");

        File.Copy(filePath, destPath, overwrite: true);
        _meta[reference] = new Dictionary<string, string>();

        return reference;
    }
}
