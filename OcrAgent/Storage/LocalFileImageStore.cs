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
    public string SaveBytes(byte[] bytes, string extension, IDictionary<string, string>? metadata = null, string? desiredReference = null)
    {
        var reference = desiredReference ?? ImageReferenceGenerator.NewRef();
        reference = ImageReferenceGenerator.Normalize(reference);

        var imagePath = GetImagePath(reference, extension);
        if (File.Exists(imagePath))
            throw new InvalidOperationException($"Reference already exists: {reference}");

        File.WriteAllBytes(imagePath, bytes);

        // sidecar metadata
        var metaPath = GetMetaPath(reference);
        var meta = metadata ?? new Dictionary<string, string>();
        meta["reference"] = reference;
        meta["extension"] = extension;
        meta["createdUtc"] = DateTime.UtcNow.ToString("O");

        File.WriteAllText(metaPath, JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true }));

        return reference;
    }

    public string GetImagePath(string reference) => ResolveExistingImagePath(reference);

    public IDictionary<string, string> GetMetadata(string reference)
    {
        var metaPath = GetMetaPath(reference);
        if (!File.Exists(metaPath)) return new Dictionary<string, string>();

        var json = File.ReadAllText(metaPath);
        return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
    }

    //  helpers

    private string ResolveExistingImagePath(string reference)
    {
        reference = ImageReferenceGenerator.Normalize(reference);

        foreach (var ext in new[] { "png", "jpg", "jpeg", "tif", "tiff", "bmp" })
        {
            var path = GetImagePath(reference, ext);
            if (File.Exists(path)) return path;
        }

        throw new FileNotFoundException($"No stored image found for reference: {reference}");
    }

    private string GetImagePath(string reference, string extension)
        => Path.Combine(_root, $"{reference}.{extension}");

    private string GetMetaPath(string reference)
        => Path.Combine(_root, $"{reference}.json");
}