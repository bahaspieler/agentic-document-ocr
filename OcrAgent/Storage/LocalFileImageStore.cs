using System.Text.Json;

namespace OcrAgent.Storage;

/// <summary>
/// Stores images and JSON metadata on the local filesystem under a root folder.
/// </summary>
public sealed class LocalFileImageStore : IImageStore
{
    private readonly string _root;
    private string? _activeSubdirectory;

    /// <summary>Creates the store and ensures the root directory exists.</summary>
    /// <param name="rootFolder">Base directory for all stored files.</param>
    public LocalFileImageStore(string rootFolder)
    {
        _root = rootFolder;
        Directory.CreateDirectory(_root);
    }

    /// <inheritdoc />
    public void SetActiveSubdirectory(string? subdirectory)
    {
        _activeSubdirectory = subdirectory;
        if (subdirectory != null)
            Directory.CreateDirectory(Path.Combine(_root, subdirectory));
    }

    /// <inheritdoc />
    public string? GetActiveSubdirectory() => _activeSubdirectory;

    /// <inheritdoc />
    public string GetWriteDirectory() => ActiveRoot;

    private string ActiveRoot => _activeSubdirectory != null
        ? Path.Combine(_root, _activeSubdirectory)
        : _root;

    /// <inheritdoc />
    public bool Exists(string reference)
    {
        reference = ImageReferenceGenerator.Normalize(reference);

        foreach (var ext in new[] { "png", "jpg", "jpeg", "tif", "tiff", "bmp" })
        {
            if (File.Exists(Path.Combine(ActiveRoot, $"{reference}.{ext}"))) return true;
            if (_activeSubdirectory != null && File.Exists(Path.Combine(_root, $"{reference}.{ext}"))) return true;
        }

        return false;
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
    public string SaveBytes(byte[] bytes, string extension, IDictionary<string, string>? metadata = null, string? desiredReference = null)
    {
        var reference = desiredReference ?? ImageReferenceGenerator.NewRef();
        reference = ImageReferenceGenerator.Normalize(reference);

        var imagePath = Path.Combine(ActiveRoot, $"{reference}.{extension}");
        if (File.Exists(imagePath))
            throw new InvalidOperationException($"Reference already exists: {reference}");

        File.WriteAllBytes(imagePath, bytes);

        var metaPath = Path.Combine(ActiveRoot, $"{reference}.json");
        var meta = metadata ?? new Dictionary<string, string>();
        meta["reference"] = reference;
        meta["extension"] = extension;
        meta["createdUtc"] = DateTime.UtcNow.ToString("O");
        if (_activeSubdirectory != null)
            meta["workflowDir"] = _activeSubdirectory;

        File.WriteAllText(metaPath, JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true }));

        return reference;
    }

    /// <inheritdoc />
    public string GetImagePath(string reference) => ResolveExistingImagePath(reference);

    /// <inheritdoc />
    public IDictionary<string, string> GetMetadata(string reference)
    {
        reference = ImageReferenceGenerator.Normalize(reference);

        var metaPath = Path.Combine(ActiveRoot, $"{reference}.json");
        if (!File.Exists(metaPath) && _activeSubdirectory != null)
            metaPath = Path.Combine(_root, $"{reference}.json");
        if (!File.Exists(metaPath)) return new Dictionary<string, string>();

        var json = File.ReadAllText(metaPath);
        return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
    }

    private string ResolveExistingImagePath(string reference)
    {
        reference = ImageReferenceGenerator.Normalize(reference);

        foreach (var ext in new[] { "png", "jpg", "jpeg", "tif", "tiff", "bmp" })
        {
            var path = Path.Combine(ActiveRoot, $"{reference}.{ext}");
            if (File.Exists(path)) return path;
        }

        if (_activeSubdirectory != null)
        {
            foreach (var ext in new[] { "png", "jpg", "jpeg", "tif", "tiff", "bmp" })
            {
                var path = Path.Combine(_root, $"{reference}.{ext}");
                if (File.Exists(path)) return path;
            }
        }

        throw new FileNotFoundException($"No stored image found for reference: {reference}");
    }
}
