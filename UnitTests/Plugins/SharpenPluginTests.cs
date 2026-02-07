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

    
    public string SaveFromFile(string filePath, string? desiredReference = null)
    {
        var reference = desiredReference ?? Guid.NewGuid().ToString("N");
        _meta[reference] = new Dictionary<string, string>();
        return reference;
    }
}
