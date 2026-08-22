namespace OcrAgent.Storage;

/// <summary>
/// Reads and writes stored images and sidecar metadata, with optional subdirectory scoping for writes.
/// </summary>
/// <remarks>
/// A write returns a reference qualified with the scope it landed in, and a read takes the scope back
/// out of the reference it is given (see <see cref="ImageReferenceGenerator"/>). A reference is
/// therefore enough on its own to find an image, including after the run that produced it has released
/// its write scope. A reference naming no scope is looked for in the active scope, then at the root.
/// </remarks>
public interface IImageStore
{
    /// <summary>Whether an image file exists for the reference (any supported extension).</summary>
    /// <param name="reference">Stored image id, optionally scope-qualified.</param>
    /// <returns>True if a matching file exists in the scope the reference resolves to.</returns>
    bool Exists(string reference);

    /// <summary>Copies a file from disk into the store and returns its reference.</summary>
    /// <param name="filePath">Path to the source file.</param>
    /// <param name="desiredReference">Optional id; a new one is generated if null. Any scope on it is replaced by the active one.</param>
    /// <returns>The stored reference, qualified with the scope it was written to.</returns>
    string SaveFromFile(string filePath, string? desiredReference = null);

    /// <summary>Writes image bytes and JSON metadata next to the image.</summary>
    /// <param name="bytes">Image file contents.</param>
    /// <param name="extension">File extension without dot (e.g. png).</param>
    /// <param name="metadata">Optional key/value pairs merged into the sidecar.</param>
    /// <param name="desiredReference">Optional id; a new one is generated if null. Any scope on it is replaced by the active one.</param>
    /// <returns>The stored reference, qualified with the scope it was written to.</returns>
    string SaveBytes(byte[] bytes, string extension, IDictionary<string, string>? metadata = null, string? desiredReference = null);

    /// <summary>Resolves the full path to the image file for the reference.</summary>
    /// <param name="reference">Stored image id, optionally scope-qualified.</param>
    /// <returns>Absolute or rooted path suitable for opening the file.</returns>
    string GetImagePath(string reference);

    /// <summary>Loads sidecar metadata for the reference, or an empty dictionary if missing.</summary>
    /// <param name="reference">Stored image id, optionally scope-qualified.</param>
    /// <returns>Deserialized metadata key/value pairs.</returns>
    IDictionary<string, string> GetMetadata(string reference);

    /// <summary>
    /// Sets the write subdirectory. Reads still fall back to root when the file is not under the subdirectory.
    /// Pass null to write at the store root again.
    /// </summary>
    /// <param name="subdirectory">Relative folder name, or null for root.</param>
    void SetActiveSubdirectory(string? subdirectory);

    /// <summary>Current write subdirectory, or null at root.</summary>
    /// <returns>Subdirectory name or null.</returns>
    string? GetActiveSubdirectory();

    /// <summary>Directory path used for new writes in the current scope.</summary>
    /// <returns>Filesystem path for the active write root.</returns>
    string GetWriteDirectory();
}
