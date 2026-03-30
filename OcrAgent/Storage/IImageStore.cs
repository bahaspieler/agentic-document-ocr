namespace OcrAgent.Storage;

public interface IImageStore
{
    bool Exists(string reference);
    string SaveFromFile(string filePath, string? desiredReference = null);
    string SaveBytes(byte[] bytes, string extension, IDictionary<string, string>? metadata = null, string? desiredReference = null);
    string GetImagePath(string reference);
    IDictionary<string, string> GetMetadata(string reference);

    /// <summary>
    /// Activates a subdirectory scope. All subsequent writes go into this subdirectory.
    /// Reads fall back to the root if the reference is not found in the subdirectory.
    /// Pass null to clear the scope and return to the root.
    /// </summary>
    void SetActiveSubdirectory(string? subdirectory);

    /// <summary>Returns the currently active subdirectory, or null if at root.</summary>
    string? GetActiveSubdirectory();

    /// <summary>Returns the filesystem directory path for the current write scope.</summary>
    string GetWriteDirectory();
}