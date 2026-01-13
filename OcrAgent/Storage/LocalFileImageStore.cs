using System.Text.Json;

namespace OcrAgent.Storage;

public sealed class LocalFileImageStore : IImageStore
{
    private readonly string _root;

    public LocalFileImageStore(string rootFolder)
    {
        _root = rootFolder;
        Directory.CreateDirectory(_root);
    }

    // resolves an existing file and throws if none exist.
    public bool Exists(string reference)
    {
        reference = ImageReferenceGenerator.Normalize(reference);

        foreach (var ext in new[] { "png", "jpg", "jpeg", "tif", "tiff", "bmp" })
        {
            var path = Path.Combine(_root, $"{reference}.{ext}");
            if (File.Exists(path)) return true;
        }

        return false;
    }

    public string SaveFromFile(string filePath, string? desiredReference = null)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"File not found: {filePath}");

        var ext = Path.GetExtension(filePath).TrimStart('.').ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(ext)) ext = "png";

        var bytes = File.ReadAllBytes(filePath);
        return SaveBytes(bytes, ext, new Dictionary<string, string>
        {
            ["source"] = "file",
            ["originalPath"] = filePath
        }, desiredReference);
    }
