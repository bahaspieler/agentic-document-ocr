using System.Threading.Tasks;

namespace MyExperiment;

/// <summary>Resolves the training image named by a natural-language queue command.</summary>
public interface IQueueCommandStorage
{
    /// <summary>Finds and downloads the image reference contained in a command.</summary>
    /// <param name="command">Queue command that names a training image.</param>
    /// <returns>The resolved reference, Blob name, and temporary local path.</returns>
    Task<ResolvedQueueImage> ResolveImageAsync(string command);
}

/// <summary>Identifies a queue image in Azure Blob Storage and on local temporary storage.</summary>
/// <param name="Reference">Image reference used by OCR tools.</param>
/// <param name="BlobName">Source name in the training container.</param>
/// <param name="LocalPath">Temporary path containing the downloaded image.</param>
public sealed record ResolvedQueueImage(
    string Reference,
    string BlobName,
    string LocalPath);
