using System.Text.Json;

namespace OcrAgent.Storage;

/// <summary>
/// Stores images and JSON metadata on the local filesystem under a root folder.
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
public sealed class LocalFileImageStore : IImageStore, IArtifactStore
{
    private static readonly string[] SupportedExtensions = { "png", "jpg", "jpeg", "tif", "tiff", "bmp" };

    private readonly string _root;
    private readonly AsyncLocal<string?> _activeSubdirectory = new();

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
        _activeSubdirectory.Value = subdirectory;
        if (subdirectory != null)
            Directory.CreateDirectory(ScopeDirectory(subdirectory));
    }

    /// <inheritdoc />
    public string? GetActiveSubdirectory() => _activeSubdirectory.Value;

    /// <inheritdoc />
    public string GetWriteDirectory() => ScopeDirectory(_activeSubdirectory.Value);

    /// <summary>Lists files stored directly within a local artifact scope.</summary>
    /// <param name="scope">Relative artifact scope to inspect.</param>
    /// <param name="cancellationToken">Token accepted for interface compatibility.</param>
    /// <returns>Artifact metadata ordered by file name.</returns>
    public Task<IReadOnlyList<StoredArtifact>> ListAsync(
        string scope, CancellationToken cancellationToken = default)
    {
        var directory = ScopeDirectory(ArtifactPath.Scope(scope));
        IReadOnlyList<StoredArtifact> result = Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
                .Select(path =>
                {
                    var info = new FileInfo(path);
                    return new StoredArtifact(info.Name, info.Length, info.LastWriteTimeUtc);
                })
                .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : [];
        return Task.FromResult(result);
    }

    /// <summary>Opens a local artifact for reading.</summary>
    /// <param name="scope">Relative scope containing the artifact.</param>
    /// <param name="name">Artifact file name.</param>
    /// <param name="cancellationToken">Token accepted for interface compatibility.</param>
    /// <returns>A readable file stream.</returns>
    public Task<Stream> OpenReadAsync(
        string scope, string name, CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(
            ScopeDirectory(ArtifactPath.Scope(scope)), ArtifactPath.Name(name));
        if (!File.Exists(path))
            throw new FileNotFoundException("Artifact not found.", name);
        return Task.FromResult<Stream>(File.OpenRead(path));
    }

    /// <inheritdoc />
    public bool Exists(string reference) => TryResolve(reference, out _, out _, out _);

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
        var leaf = LeafOf(desiredReference);
        var scope = _activeSubdirectory.Value;
        var reference = ImageReferenceGenerator.Qualify(scope, leaf);

        var writeDirectory = ScopeDirectory(scope);
        Directory.CreateDirectory(writeDirectory);

        var imagePath = Path.Combine(writeDirectory, $"{leaf}.{extension}");
        if (File.Exists(imagePath))
            throw new InvalidOperationException($"Reference already exists: {reference}");

        File.WriteAllBytes(imagePath, bytes);

        var meta = metadata ?? new Dictionary<string, string>();
        meta["reference"] = reference;
        meta["extension"] = extension;
        meta["createdUtc"] = DateTime.UtcNow.ToString("O");
        if (scope != null)
            meta["workflowDir"] = scope;

        var metaPath = Path.Combine(writeDirectory, $"{leaf}.json");
        File.WriteAllText(metaPath, JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true }));

        return reference;
    }

    /// <inheritdoc />
    public string GetImagePath(string reference)
    {
        if (!TryResolve(reference, out var scope, out var leaf, out var extension))
            throw new FileNotFoundException($"No stored image found for reference: {reference}");

        return Path.Combine(ScopeDirectory(scope), $"{leaf}.{extension}");
    }

    /// <inheritdoc />
    public IDictionary<string, string> GetMetadata(string reference)
    {
        var (explicitScope, leaf) = ImageReferenceGenerator.Split(reference);

        foreach (var scope in CandidateScopes(explicitScope))
        {
            var metaPath = Path.Combine(ScopeDirectory(scope), $"{leaf}.json");
            if (File.Exists(metaPath))
            {
                var json = File.ReadAllText(metaPath);
                return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
            }
        }

        return new Dictionary<string, string>();
    }

    /// <summary>Finds where a reference lives, preferring the active scope over the store root.</summary>
    private bool TryResolve(string reference, out string? scope, out string leaf, out string extension)
    {
        var (explicitScope, name) = ImageReferenceGenerator.Split(reference);
        leaf = name;

        foreach (var candidate in CandidateScopes(explicitScope))
        {
            var directory = ScopeDirectory(candidate);

            foreach (var ext in SupportedExtensions)
            {
                if (File.Exists(Path.Combine(directory, $"{leaf}.{ext}")))
                {
                    scope = candidate;
                    extension = ext;
                    return true;
                }
            }
        }

        scope = null;
        extension = "";
        return false;
    }

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

    private string ScopeDirectory(string? scope)
    {
        if (string.IsNullOrWhiteSpace(scope)) return _root;

        var relative = scope.Replace('\\', '/').Trim('/').Replace('/', Path.DirectorySeparatorChar);
        return Path.Combine(_root, relative);
    }

    /// <summary>
    /// The file name a write should use, which is the requested reference stripped of any scope.
    /// </summary>
    /// <remarks>
    /// Plugins derive an output name by appending to the reference they were given, and that reference
    /// is itself scope-qualified whenever it came from an earlier step. Stripping here rather than in
    /// twenty plugins is what stops the scope being prefixed twice.
    /// </remarks>
    private static string LeafOf(string? desiredReference) =>
        desiredReference == null
            ? ImageReferenceGenerator.NewRef()
            : ImageReferenceGenerator.Split(desiredReference).Leaf;
}
