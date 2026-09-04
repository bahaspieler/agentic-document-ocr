using OcrAgent.Storage;

namespace UnitTests.Plugins.Enhancement;

/// <summary>In-memory index backed by temporary files for denoise tests.</summary>
internal sealed class DenoiseTestImageStore : IImageStore
{
    private readonly string _root;
    private readonly Dictionary<string, string> _paths = [];
    private readonly Dictionary<string, IDictionary<string, string>> _metadata = [];

    /// <summary>Initializes the store below a temporary root.</summary>
    public DenoiseTestImageStore(string root)
    {
        _root = root;
    }

    /// <summary>Registers an existing image path for a test reference.</summary>
    public void RegisterExisting(string reference, string path)
    {
        _paths[reference] = path;
        _metadata[reference] = new Dictionary<string, string>();
    }

    /// <inheritdoc />
    public bool Exists(string reference) => _paths.ContainsKey(reference);

    /// <inheritdoc />
    public string GetImagePath(string reference)
    {
        if (!_paths.TryGetValue(reference, out var path))
            throw new FileNotFoundException(reference);
        return path;
    }

    /// <inheritdoc />
    public IDictionary<string, string> GetMetadata(string reference) =>
        _metadata.TryGetValue(reference, out var metadata)
            ? metadata
            : new Dictionary<string, string>();

    /// <inheritdoc />
    public string SaveFromFile(string path, string? desiredReference)
    {
        var extension = Path.GetExtension(path);
        var reference = desiredReference ?? $"img_{Guid.NewGuid():N}";
        var destination = Path.Combine(_root, reference + extension);
        File.Copy(path, destination, overwrite: true);
        _paths[reference] = destination;
        _metadata[reference] = new Dictionary<string, string>();
        return reference;
    }

    /// <inheritdoc />
    public string SaveBytes(
        byte[] bytes,
        string extension,
        IDictionary<string, string>? metadata = null,
        string? desiredReference = null)
    {
        var reference = desiredReference ?? $"img_{Guid.NewGuid():N}";
        var destination = Path.Combine(_root, $"{reference}.{extension}");
        File.WriteAllBytes(destination, bytes);
        _paths[reference] = destination;
        _metadata[reference] = metadata ?? new Dictionary<string, string>();
        return reference;
    }

    /// <inheritdoc />
    public void SetActiveSubdirectory(string? subdirectory)
    {
    }

    /// <inheritdoc />
    public string? GetActiveSubdirectory() => null;

    /// <inheritdoc />
    public string GetWriteDirectory() => _root;
}
